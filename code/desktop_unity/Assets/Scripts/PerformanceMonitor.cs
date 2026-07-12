using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 智能性能监控器 — 以 FPS 为主、CPU/GPU 占用为辅，自适应帧率/分辨率
///
/// 核心策略：
///   1. 降档只看 FPS — 持续跑不满目标帧率才降（宠物真的在卡）
///   2. CPU/GPU 仅用于拦住升档 — 系统还在忙就别升回去
///   3. 紧急情况：CPU/GPU > 90% 长时间持续，即使 FPS 还行也预降
///
/// 三档:
///   High   (60fps, RT 100%)
///   Normal (40fps, RT 75%)
///   Low    (20fps, RT 50%)
/// </summary>
public class PerformanceMonitor : MonoBehaviour
{
    [Header("当前状态（只读）")]
    public PerformanceTier currentTier = PerformanceTier.High;
    public float currentFPS = 60f;
    public float targetFPS = 60f;
    [Tooltip("最近一次采样的 CPU 占用率 0-100")]
    public float cpuUsagePercent = 0f;
    [Tooltip("最近一次采样的 GPU 占用率 0-100，-1 表示不可用")]
    public float gpuUsagePercent = -1f;

    [Header("FPS 降档参数")]
    [Tooltip("低于目标帧率多少比例算跑不满")]
    public float fpsThresholdRatio = 0.7f;
    [Tooltip("连续跑不满多少秒后降档")]
    public float downgradeAfterSeconds = 5f;

    [Header("CPU/GPU 升档阻拦")]
    [Tooltip("峰值占用高于此值不允许升档")]
    public float upgradeBlockThreshold = 80f;
    [Tooltip("峰值占用高于此值→紧急降 Low（即使 FPS 还行）")]
    public float emergencyThreshold = 92f;
    [Tooltip("紧急降档前需持续超阈值多少秒")]
    public float emergencyAfterSeconds = 8f;

    [Header("渲染参数（Live2DRenderer 读取）")]
    [System.NonSerialized] public float rtResolutionScale = 1f;

    // ── FPS 滚动统计 ──
    private const int FPS_SAMPLE_COUNT = 90;
    private float[] _frameTimeSamples = new float[FPS_SAMPLE_COUNT];
    private int _sampleIndex = 0;
    private int _sampleCount = 0;

    // ── 降档计时 ──
    private float _lowFpsTimer = 0f;
    private float _stableTimer = 0f;

    // ── 紧急降档 ──
    private float _emergencyTimer = 0f;

    // ── 稳定计时（防止频繁跳变）──
    private float _timeSinceLastChange = 0f;
    private const float MIN_CHANGE_INTERVAL = 8f;

    // ── 事件 ──
    public System.Action<PerformanceTier> OnTierChanged;

    // ═══════════════════════════════════════
    //  CPU: GetSystemTimes (kernel32)
    // ═══════════════════════════════════════
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint dwLowDateTime; public uint dwHighDateTime; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    private ulong _prevIdleTicks = 0;
    private ulong _prevTotalTicks = 0;
    private bool _cpuFirstSample = true;
    private float _loadPollTimer = 0f;
    private const float LOAD_POLL_INTERVAL = 2f;

    // ═══════════════════════════════════════
    //  GPU: NVML (NVIDIA)
    // ═══════════════════════════════════════
    private static class NVML
    {
        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
        public static extern int nvmlInit();
        [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
        public static extern int nvmlShutdown();
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        public static extern int nvmlDeviceGetHandleByIndex(uint index, out System.IntPtr handle);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
        public static extern int nvmlDeviceGetUtilizationRates(System.IntPtr handle, out nvmlUtilization_t util);

        public struct nvmlUtilization_t { public uint gpuUtil; public uint memoryUtil; }
        public const int NVML_SUCCESS = 0;
    }

    private System.IntPtr _nvmlDevice = System.IntPtr.Zero;
    private bool _nvmlInitialized = false;
    private bool _nvmlAttempted = false;
    private float _nvmlRetryCooldown = 0f;     // 休眠恢复后冷却重试

    void Start()
    {
        SetTier(PerformanceTier.High);
    }

    void OnDestroy()
    {
        if (_nvmlInitialized) { NVML.nvmlShutdown(); _nvmlInitialized = false; }
    }

    void Update()
    {
        // 1) FPS 采样 + 滚动平均
        float dt = Time.deltaTime;
        _frameTimeSamples[_sampleIndex] = dt;
        _sampleIndex = (_sampleIndex + 1) % FPS_SAMPLE_COUNT;
        if (_sampleCount < FPS_SAMPLE_COUNT) _sampleCount++;
        float sum = 0f;
        for (int i = 0; i < _sampleCount; i++) sum += _frameTimeSamples[i];
        float avgFrameTime = sum / Mathf.Max(1, _sampleCount);
        currentFPS = avgFrameTime > 0f ? 1f / avgFrameTime : 999f;

        _timeSinceLastChange += Time.deltaTime;

        // 2) 定时采样 CPU+GPU
        _loadPollTimer += Time.deltaTime;
        if (_loadPollTimer >= LOAD_POLL_INTERVAL)
        {
            _loadPollTimer = 0f;
            SampleSystemLoad();
        }

        // 3) FPS 降档判定（主信号）
        float threshold = GetTargetFPS(currentTier) * fpsThresholdRatio;
        bool fpsStruggling = currentFPS < threshold && _sampleCount >= 30;

        if (fpsStruggling)
        {
            _lowFpsTimer += Time.deltaTime;
            _stableTimer = 0f;
        }
        else
        {
            _lowFpsTimer = Mathf.Max(0f, _lowFpsTimer - Time.deltaTime * 0.3f);
            _stableTimer += Time.deltaTime;
        }

        // 降档
        if (_lowFpsTimer >= downgradeAfterSeconds
            && currentTier > PerformanceTier.Low
            && _timeSinceLastChange > MIN_CHANGE_INTERVAL)
        {
            SetTier(currentTier - 1);
            Debug.Log($"[PerformanceMonitor] ⬇ FPS 降档至 {currentTier}（{_lowFpsTimer:F1}s < {threshold:F0}fps）");
            return;
        }

        // 4) 紧急降档（CPU/GPU 备用信号 — 系统被其他程序压满）
        float peak = GetPeakLoad();
        if (peak >= emergencyThreshold)
        {
            _emergencyTimer += Time.deltaTime;
            if (_emergencyTimer >= emergencyAfterSeconds
                && currentTier > PerformanceTier.Low
                && _timeSinceLastChange > MIN_CHANGE_INTERVAL)
            {
                SetTier(PerformanceTier.Low);
                Debug.Log($"[PerformanceMonitor] ⬇⬇ 紧急降 Low（峰值 {peak:F0}% 持续 {_emergencyTimer:F1}s）");
                return;
            }
        }
        else
        {
            _emergencyTimer = Mathf.Max(0f, _emergencyTimer - Time.deltaTime);
        }

        // 5) 升档判定 — FPS 达标足够久 + 系统不忙
        float upgradeTarget = GetTargetFPS((PerformanceTier)((int)currentTier + 1));
        bool canUpgrade = currentTier < PerformanceTier.High
            && _stableTimer >= 15f
            && currentFPS >= upgradeTarget * 0.9f
            && peak < upgradeBlockThreshold
            && _timeSinceLastChange > MIN_CHANGE_INTERVAL;

        if (canUpgrade)
        {
            SetTier(currentTier + 1);
            Debug.Log($"[PerformanceMonitor] ⬆ 升档至 {currentTier}");
        }
    }

    private void SampleSystemLoad()
    {
        cpuUsagePercent = SampleCPU();
        gpuUsagePercent = SampleGPU();
    }

    /// <summary>取 CPU/GPU 中的峰值占用</summary>
    private float GetPeakLoad()
    {
        float peak = cpuUsagePercent;
        if (gpuUsagePercent >= 0f && gpuUsagePercent > peak)
            peak = gpuUsagePercent;
        return peak;
    }

    private float SampleCPU()
    {
        if (!GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user))
            return 0f;

        ulong idleT  = ((ulong)idle.dwHighDateTime  << 32) | idle.dwLowDateTime;
        ulong kerT   = ((ulong)kernel.dwHighDateTime << 32) | kernel.dwLowDateTime;
        ulong userT  = ((ulong)user.dwHighDateTime  << 32) | user.dwLowDateTime;
        ulong totalT = kerT + userT;

        if (_cpuFirstSample)
        {
            _cpuFirstSample = false;
            _prevIdleTicks = idleT;
            _prevTotalTicks = totalT;
            return 0f;
        }

        ulong idleDelta  = idleT - _prevIdleTicks;
        ulong totalDelta = totalT - _prevTotalTicks;
        _prevIdleTicks = idleT;
        _prevTotalTicks = totalT;

        if (totalDelta == 0) return 0f;
        return Mathf.Clamp01(1f - (float)((double)idleDelta / totalDelta)) * 100f;
    }

    private float SampleGPU()
    {
        if (!_nvmlAttempted)
        {
            _nvmlAttempted = true;
            try
            {
                int ret = NVML.nvmlInit();
                if (ret == NVML.NVML_SUCCESS)
                {
                    ret = NVML.nvmlDeviceGetHandleByIndex(0, out _nvmlDevice);
                    if (ret == NVML.NVML_SUCCESS && _nvmlDevice != System.IntPtr.Zero)
                        _nvmlInitialized = true;
                    else
                        NVML.nvmlShutdown();
                }
            }
            catch (System.DllNotFoundException) { }
            catch
            {
                // NVML 初始化失败（非 NVIDIA GPU 或无驱动），跳过 GPU 监控
            }
        }
        if (!_nvmlInitialized || _nvmlDevice == System.IntPtr.Zero) return -1f;
        // 休眠恢复后冷却期内不查，避免打在失效句柄上导致显卡驱动崩溃
        if (_nvmlRetryCooldown > 0f)
        {
            _nvmlRetryCooldown -= LOAD_POLL_INTERVAL;
            return -1f;
        }
        try
        {
            int ret = NVML.nvmlDeviceGetUtilizationRates(_nvmlDevice, out NVML.nvmlUtilization_t util);
            if (ret == NVML.NVML_SUCCESS) return Mathf.Clamp(util.gpuUtil, 0, 100);
            // 句柄失效（休眠/驱动重置后）→ 冷却 60s 后重试初始化
            Debug.LogWarning("[PerformanceMonitor] NVML 查询失败，驱动可能已重置，进入冷却重试");
            _nvmlInitialized = false;
            _nvmlAttempted = false;
            _nvmlDevice = System.IntPtr.Zero;
            _nvmlRetryCooldown = 60f;
        }
        catch
        {
            // NVML P/Invoke 异常（如 AccessViolation），冷却后重试
        }
        return -1f;
    }

    private void SetTier(PerformanceTier newTier)
    {
        if (newTier == currentTier) return;
        currentTier = newTier;
        _timeSinceLastChange = 0f;
        _lowFpsTimer = 0f;
        _stableTimer = 0f;
        _emergencyTimer = 0f;

        targetFPS = GetTargetFPS(newTier);
        rtResolutionScale = GetResolutionScale(newTier);
        Application.targetFrameRate = (int)targetFPS;

        Debug.Log($"[PerformanceMonitor] ⚡ {newTier}: {targetFPS}fps, RT {rtResolutionScale*100:F0}%");
        OnTierChanged?.Invoke(currentTier);
    }

    /// <summary>
    /// 外部调用的强制降档（系统内存不足时由 DesktopPet 调用）。
    /// 忽略 MIN_CHANGE_INTERVAL 冷却，直接降到 Low。
    /// </summary>
    public void ForceDowngrade()
    {
        if (currentTier <= PerformanceTier.Low) return;
        SetTier(PerformanceTier.Low);
        Debug.Log($"[PerformanceMonitor] ⬇⬇⬇ 外部强制降档至 Low");
    }

    private static float GetTargetFPS(PerformanceTier t) => t switch
    {
        PerformanceTier.High => 60f, PerformanceTier.Normal => 40f, PerformanceTier.Low => 20f, _ => 60f
    };

    private static float GetResolutionScale(PerformanceTier t) => 1.0f; // 始终全分辨率，防放大马赛克
}

public enum PerformanceTier { High = 2, Normal = 1, Low = 0 }