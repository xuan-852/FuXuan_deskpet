using System;
using System.Threading;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// 桌面宠物 — 主控脚本
///
/// 职责：
/// 1. 管理宠物的物理状态 (位置、速度、尺寸)
/// 2. 驱动物理步进（重力、碰撞、落地检测）
/// 3. 管理地面状态机（行走、停止等任务）
/// 4. 协调 DragHandler、TouchController 等交互模块
///
/// v2 架构设计：
/// - 分离交互逻辑到独立的 Handler 脚本
/// - 用 IPetRenderer 接口抽象渲染层
/// - 状态机可扩展，方便添加新行为
/// </summary>
public class DesktopPet : MonoBehaviour
{
    // 单例互斥锁：防止多个实例同时运行
    private static Mutex _instanceMutex = null;
    private const string MutexNamePrefix = "DesktopPet_Unity_SingleInstance_";

    /// <summary>
    /// 为每个数据根目录建立独立的单实例作用域。
    /// 生产目录仍保持单实例；隔离测试目录可以与生产实例并行运行，避免测试脚本强杀用户桌宠。
    /// </summary>
    private static string GetInstanceMutexName()
    {
        try
        {
            string dataRoot = System.IO.Path.GetFullPath(DataPathConfig.DataRoot)
                .TrimEnd('\\', '/')
                .ToUpperInvariant();
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(dataRoot));
                var suffix = new StringBuilder(16);
                for (int i = 0; i < 8; i++)
                    suffix.Append(digest[i].ToString("x2"));
                return MutexNamePrefix + suffix;
            }
        }
        catch
        {
            // 数据目录异常时回退到稳定锁名，优先保证不会产生多个不受控实例。
            return MutexNamePrefix + "default";
        }
    }

    // Win32 API
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private const int VK_ESCAPE = 0x1B;

    // ——— 系统总内存查询 ———
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // ================ 可调参数（改这里）================
    const int GROUND_Y_MARGIN     = 0;       // 地面距屏幕底部距离（像素），负数=往下调，正数=往上调
    const float WALK_SPEED_FACTOR = 24f;     // 移动速度（像素/秒，帧率无关）
    // ==================================================

    #region 物理状态

    [Header("物理属性")]
    [Tooltip("宠物初始X位置")]
    public int startX = 50;

    [Tooltip("宠物初始Y位置，-1=屏幕底部")]
    public int startY = -1;

    [Tooltip("重力加速度（像素/帧²）")]
    public int gravity = 1;

    [Tooltip("最大下落速度（像素/帧）")]
    public int maxFallSpeed = 8;

    [Tooltip("水平速度范围")]
    public int maxHorizontalSpeed = 12;

    // 宠物物理状态（仿 v1 PetState）
    [System.NonSerialized]
    public int petX;
    [System.NonSerialized]
    public int petY;
    [System.NonSerialized]
    public int petVx;
    [System.NonSerialized]
    public int petVy;
    [System.NonSerialized]
    public int petWidth;
    [System.NonSerialized]
    public int petHeight;

    [System.NonSerialized]
    public bool onGround = false;

    [System.NonSerialized]
    public bool isPaused = false;

    [System.NonSerialized]
    public bool isDragging = false;

    // 强制动作的物理移动锁。它独立于 isPaused，防止其他模块调用 Resume()
    // 后绕过 Live2DRenderer 的动作锁，重新启动地面行走任务。
    private bool _actionMovementLocked = false;

    // 屏幕尺寸（动态获取，不缓存）
    private int _screenWidth => Screen.width;
    private int _screenHeight => Screen.height;

    #endregion

    #region 地面任务状态机

    /// <summary>
    /// 地面行为枚举（与 v1 GroundTask 对应）
    /// </summary>
    public enum GroundTask
    {
        None,
        MoveLeftEdge,      // 向左走到边缘
        MoveRightEdge,     // 向右走到边缘
        MoveLeftTime,      // 向左走固定时长
        MoveRightTime,     // 向右走固定时长
        StopTime           // 停止固定时长
    }

    [Header("地面任务配置")]
    [Tooltip("向左走到边缘权重")]
    public int taskWeightMoveLeftEdge = 1;
    [Tooltip("向右走到边缘权重")]
    public int taskWeightMoveRightEdge = 1;
    [Tooltip("向左走定时权重")]
    public int taskWeightMoveLeftTime = 1;
    [Tooltip("向右走定时权重")]
    public int taskWeightMoveRightTime = 1;
    [Tooltip("停止定时权重")]
    public int taskWeightStopTime = 6;

    [Tooltip("地面任务移动最短时间（毫秒）")]
    public int taskMoveTimeMinMs = 2000;

    [Tooltip("地面任务移动最长时间（毫秒）")]
    public int taskMoveTimeMaxMs = 4000;

    [Tooltip("停止最短时间（毫秒）")]
    public int taskStopTimeMinMs = 6000;

    [Tooltip("停止最长时间（毫秒）")]
    public int taskStopTimeMaxMs = 15000;

    [System.NonSerialized]
    public GroundTask currentTask = GroundTask.None;

    [System.NonSerialized]
    public GroundTask lastTask = GroundTask.None;

    private float _taskEndTime = 0f;

    #endregion

    #region 组件引用

    private WindowOverlay _windowOverlay;
    private IPetRenderer _renderer;
    private SystemTrayManager _trayManager;
    private PerformanceMonitor _perfMonitor;
    private bool _pendingEscToTray = false;  // ESC 按下时托盘未就绪，等就绪后自动隐藏
    // 退出入口可能同时来自托盘、测试命令、Unity OnApplicationQuit 和 OnDestroy。
    // 所有路径必须共用幂等清理，不能手动调用 Unity 生命周期方法。
    private bool _shutdownStarted = false;

    // 时间间隙检测：睡眠唤醒后 Time.realtimeSinceStartup 跳变 >10s 则触发重建
    private float _lastUpdateRealtime = 0f;
    private float _sessionStartedRealtime = 0f;
    private bool _stableSessionMarked = false;
    private bool _crashWatchdogEnabled = true;

    #endregion

    #region Unity 生命周期

    // ===== 崩溃日志 & 内存监控 =====
    // 统一日志目录：DataPathConfig.LogsDir（与持久化数据同根，不依赖启动工作目录）
    private static readonly string CrashLogDir = DataPathConfig.LogsDir;
    private static readonly string CrashLogPath = System.IO.Path.Combine(DataPathConfig.LogsDir, "crash_log.txt");
    private const long CRASH_LOG_MAX_BYTES = 1024L * 1024L;  // crash_log.txt 超过1MB自动截断
    // ★ 全量日志镜像：所有 Debug.Log/Warning/Error 追加到 player_log.txt（每次写入即刷盘，运行中可实时查看）
    private static readonly string FullLogPath = System.IO.Path.Combine(DataPathConfig.LogsDir, "player_log.txt");
    private const long FULL_LOG_MAX_BYTES = 10L * 1024L * 1024L; // player_log.txt 超过10MB自动截断
    private static readonly object _logLock = new object(); // 日志镜像写锁（可能来自任意线程）
    private float _memoryCheckInterval = 30f;  // 每30秒检查一次内存
    private float _memoryCheckTimer = 0f;
    private const long MEMORY_WARNING_MB = 800L;  // 超过800MB触发GC
    private const long MEMORY_CRITICAL_MB = 1200L; // 超过1.2GB记录警告

    // ===== 系统总内存监控（防止 VS Code + 桌面宠物抢内存导致被系统杀进程） =====
    private const float SYS_MEM_WARN_PCT = 85f;   // 系统总内存 > 85% → 预警 + 主动GC
    private const float SYS_MEM_DANGER_PCT = 93f; // 系统总内存 > 93% → 紧急降质
    private float _sysMemWarnCooldown = 0f;       // 防刷屏冷却
    private float _cleanupTimer = 0f;
    private const float CLEANUP_INTERVAL = 600f;  // 每10分钟清理一次旧日志

    /// <summary>监听 Unity 日志，捕获崩溃前最后一刻的痕迹</summary>
    private void CaptureCrashLog(string logString, string stackTrace, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error)
        {
            try
            {
                string msg = $"[{DateTime.Now:HH:mm:ss}] {type}: {logString}\n{stackTrace}\n";
                System.IO.File.AppendAllText(CrashLogPath, msg);
            }
            catch
            {
                // 崩溃处理器中不能抛异常，否则会覆盖原始崩溃信息
            }
        }
    }

    /// <summary>
    /// ★ 全量日志镜像：所有日志（含 Debug.Log/Warning）追加到 player_log.txt，每次写入即刷盘。
    /// 解决 Unity Player.log 在运行中被删除句柄后永远 0 B 的问题，运行中可实时 tail 查看调试。
    /// </summary>
    private void MirrorAllLogs(string logString, string stackTrace, LogType type)
    {
        try
        {
            string msg = $"[{DateTime.Now:HH:mm:ss.fff}] {type}: {logString}\n";
            if (type == LogType.Exception || type == LogType.Error)
            {
                msg += stackTrace + "\n";
            }
            lock (_logLock)
            {
                System.IO.File.AppendAllText(FullLogPath, msg);
            }
        }
        catch
        {
            // 日志镜像失败绝不影响主程序
        }
    }

    /// <summary>检查内存压力，必要时强制 GC</summary>
    private void CheckMemoryPressure()
    {
        long memMB = System.GC.GetTotalMemory(false) / (1024L * 1024L);
        if (memMB > MEMORY_CRITICAL_MB)
        {
            Debug.LogWarning($"[DesktopPet] ⚠ 内存占用 {memMB}MB，超过临界值 {MEMORY_CRITICAL_MB}MB");
            try
            {
                string msg = $"[{DateTime.Now:HH:mm:ss}] MEMORY_HIGH: {memMB}MB\n";
                System.IO.File.AppendAllText(CrashLogPath, msg);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[DesktopPet] 写入崩溃日志失败（无害）: {ex.Message}");
            }
        }
        if (memMB > MEMORY_WARNING_MB)
        {
            Debug.Log($"[DesktopPet] 内存 {memMB}MB，强制 GC");
            Resources.UnloadUnusedAssets();
            System.GC.Collect();
        }
    }

    /// <summary>
    /// 检查系统总内存占用，防止 VS Code + Unity 抢内存导致被杀进程。
    /// 通过 GlobalMemoryStatusEx 获取真实物理内存占用率。
    /// </summary>
    private void CheckSystemMemory()
    {
        try
        {
            var memStatus = new MEMORYSTATUSEX();
            memStatus.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref memStatus)) return;

            float usedPct = memStatus.dwMemoryLoad;
            ulong totalMB = memStatus.ullTotalPhys / (1024UL * 1024UL);
            ulong availMB = memStatus.ullAvailPhys / (1024UL * 1024UL);

            // 冷却计时（防刷屏）
            _sysMemWarnCooldown -= _memoryCheckInterval;
            if (_sysMemWarnCooldown < 0f) _sysMemWarnCooldown = 0f;

            if (usedPct >= SYS_MEM_DANGER_PCT)
            {
                // 93%+ → 紧急：强制 GC + 降性能档
                if (_sysMemWarnCooldown <= 0f)
                {
                    Debug.LogWarning($"[DesktopPet] 🚨 系统内存 {usedPct:F0}%（剩余 {availMB}MB/{totalMB}MB），紧急降质保命");
                    try
                    {
                        string msg = $"[{DateTime.Now:HH:mm:ss}] SYS_MEM_DANGER: {usedPct:F0}% ({availMB}MB left)\n";
                        System.IO.File.AppendAllText(CrashLogPath, msg);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[DesktopPet] 写入内存预警日志失败: {ex.Message}");
                    }
                    Resources.UnloadUnusedAssets();
                    System.GC.Collect();
                    _sysMemWarnCooldown = 120f; // 每2分钟才报一次

                    // 通知 PerformanceMonitor 降档
                    if (_perfMonitor != null && _perfMonitor.currentTier > PerformanceTier.Low)
                    {
                        _perfMonitor.ForceDowngrade();
                    }
                }
            }
            else if (usedPct >= SYS_MEM_WARN_PCT)
            {
                // 85%+ → 预警 GC
                if (_sysMemWarnCooldown <= 0f)
                {
                    Debug.Log($"[DesktopPet] ⚠ 系统内存 {usedPct:F0}%（剩余 {availMB}MB/{totalMB}MB），主动 GC");
                    Resources.UnloadUnusedAssets();
                    System.GC.Collect();
                    _sysMemWarnCooldown = 60f; // 每1分钟报一次
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[DesktopPet] CheckSystemMemory 异常（无害）: {ex.Message}");
        }
    }

    /// <summary>清理过期日志文件：crash_log.txt 超1MB截断 + 删除7天前的 build_log*.txt</summary>
    private void CleanupLogFiles()
    {
        try
        {
            // 0) 确保日志目录存在
            System.IO.Directory.CreateDirectory(CrashLogDir);

            // 1) crash_log.txt 超过 1MB 时截断，保留最后 2000 行
            var crashInfo = new System.IO.FileInfo(CrashLogPath);
            if (crashInfo.Exists && crashInfo.Length > CRASH_LOG_MAX_BYTES)
            {
                string[] lines = System.IO.File.ReadAllLines(CrashLogPath);
                if (lines.Length > 2000)
                {
                    string tail = string.Join("\n", lines, lines.Length - 2000, 2000);
                    System.IO.File.WriteAllText(CrashLogPath, 
                        "=== 日志已截断（保留最近2000行）===\n" + tail);
                    Debug.Log($"[DesktopPet] 已截断 {CrashLogPath} → 保留2000行");
                }
            }

            // 2) 删除 7 天前的 build_log*.txt（统一日志目录内）
            string dataDir = CrashLogDir;
            string[] buildLogs = System.IO.Directory.GetFiles(dataDir, "build_log*.txt");
            var cutoff = System.DateTime.Now.AddDays(-7);
            foreach (string path in buildLogs)
            {
                var fi = new System.IO.FileInfo(path);
                if (fi.LastWriteTime < cutoff)
                {
                    System.IO.File.Delete(path);
                    Debug.Log($"[DesktopPet] 已删除过期日志: {fi.Name}");
                }
            }

            // 3) ★ 不再删除 Unity 的 Player.log：进程已持有该文件句柄，删除后 Unity 继续写
            //    "已删除句柄"，文件系统里只剩 0 B 空壳，运行日志全部丢失。
            //    改为截断全量日志镜像 player_log.txt（超过 10MB 保留尾部 3000 行）
            var fullLogInfo = new System.IO.FileInfo(FullLogPath);
            if (fullLogInfo.Exists && fullLogInfo.Length > FULL_LOG_MAX_BYTES)
            {
                string[] lines = System.IO.File.ReadAllLines(FullLogPath);
                if (lines.Length > 3000)
                {
                    string tail = string.Join("\n", lines, lines.Length - 3000, 3000);
                    System.IO.File.WriteAllText(FullLogPath,
                        "=== player_log 已截断（保留最近3000行）===\n" + tail);
                    Debug.Log($"[DesktopPet] 已截断 {FullLogPath} → 保留3000行");
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[DesktopPet] 日志清理异常（无害）: {ex.Message}");
        }
    }

    // ===== 崩溃看门狗 =====
    private const string PREF_CRASH_COUNT = "_crash_count";
    private const string PREF_CLEAN_EXIT = "_clean_exit";
    private const string PREF_STABLE_SESSION = "_stable_session";
    private const int MAX_CRASHES_BEFORE_SAFE_MODE = 2;
    private const float STABLE_SESSION_SECONDS = 60f;

    // PlayerPrefs 是启动辅助状态，不应成为桌宠主流程的硬依赖。
    // 沙盒权限、注册表损坏或测试隔离环境均可能让 Unity PlayerPrefs 抛异常；
    // 此时保留默认行为即可，不能阻断 RightPanel/ChatManager 初始化。
    private static int SafePrefsGetInt(string key, int fallback)
    {
        try { return PlayerPrefs.GetInt(key, fallback); }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DesktopPet] PlayerPrefs 读取失败，使用默认值: {ex.Message}");
            return fallback;
        }
    }

    private static void SafePrefsDeleteKey(string key)
    {
        try { PlayerPrefs.DeleteKey(key); }
        catch (Exception ex) { Debug.LogWarning($"[DesktopPet] PlayerPrefs 删除失败（无害）: {ex.Message}"); }
    }

    private static void SafePrefsSetInt(string key, int value)
    {
        try { PlayerPrefs.SetInt(key, value); }
        catch (Exception ex) { Debug.LogWarning($"[DesktopPet] PlayerPrefs 写入失败（无害）: {ex.Message}"); }
    }

    private static void SafePrefsSetString(string key, string value)
    {
        try { PlayerPrefs.SetString(key, value); }
        catch (Exception ex) { Debug.LogWarning($"[DesktopPet] PlayerPrefs 写入失败（无害）: {ex.Message}"); }
    }

    private static void SafePrefsSave()
    {
        try { PlayerPrefs.Save(); }
        catch (Exception ex) { Debug.LogWarning($"[DesktopPet] PlayerPrefs 保存失败（无害）: {ex.Message}"); }
    }

    private void Awake()
    {
        // ---- 确保统一日志目录存在（崩溃日志写入前提）----
        if (!DataPathConfig.EnsureDataRoot(out var dataRootError))
            Debug.LogWarning($"[DesktopPet] 数据目录不可用: {DataPathConfig.DataRoot}，{dataRootError}");
        try { System.IO.Directory.CreateDirectory(CrashLogDir); } catch { }

        // ---- 崩溃看门狗 ----
#if !UNITY_EDITOR
        Application.logMessageReceivedThreaded += CaptureCrashLog;
        // ★ 全量日志镜像（运行中实时可看 player_log.txt）
        Application.logMessageReceivedThreaded += MirrorAllLogs;
#endif
        _sessionStartedRealtime = Time.realtimeSinceStartup;
        _crashWatchdogEnabled = !System.IO.File.Exists(DataPathConfig.TestModeFile);

        if (_crashWatchdogEnabled)
        {
            // 看门狗只统计连续异常会话。稳定运行标记与正常退出标记都能
            // 结束一次异常启动链，避免构建/测试强杀后永久累加计数。
            bool previousCleanExit = SafePrefsGetInt(PREF_CLEAN_EXIT, 0) == 1;
            bool previousStableSession = SafePrefsGetInt(PREF_STABLE_SESSION, 0) == 1;
            SafePrefsDeleteKey(PREF_CLEAN_EXIT);
            SafePrefsDeleteKey(PREF_STABLE_SESSION);
            SafePrefsSave();

            if (!previousCleanExit && !previousStableSession)
            {
                int crashCount = SafePrefsGetInt(PREF_CRASH_COUNT, 0) + 1;
                SafePrefsSetInt(PREF_CRASH_COUNT, crashCount);
                SafePrefsSave();
                Debug.LogWarning($"[DesktopPet] ⚠ 上次异常退出（连续异常计数={crashCount}/{MAX_CRASHES_BEFORE_SAFE_MODE}）");

                if (crashCount >= MAX_CRASHES_BEFORE_SAFE_MODE)
                {
                    SafePrefsSetString("_skip_dwm_rebuild", "1");
                    SafePrefsSave();
                    Debug.LogWarning("[DesktopPet] 🛡 连续异常启动达到阈值，唤醒时启用 DWM 恢复保护");
                }
            }
            else
            {
                SafePrefsSetInt(PREF_CRASH_COUNT, 0);
                SafePrefsDeleteKey("_skip_dwm_rebuild");
                SafePrefsSave();
                Debug.Log("[DesktopPet] ✅ 上次会话已正常结束或稳定运行，重置异常计数");
            }
        }
        else
        {
            // 测试数据目录与 PlayerPrefs 并非同一隔离层；测试模式必须完全
            // 跳过生产崩溃计数，避免运行时验收污染用户的安全模式状态。
            Debug.Log("[DesktopPet] 🧪 测试模式：跳过生产崩溃计数与 DWM 安全模式状态");
        }

        // ---- 单例互斥锁：防止 Build and Run 产生多个实例 ----
        // ★ 注意: Editor 下跳过互斥锁检查，因为命名 Mutex 在域重载后不释放，
        //   会导致第二次 Play 时误判"已有实例"而退出 Play Mode。
#if !UNITY_EDITOR
        try
        {
            bool createdNew;
            _instanceMutex = new Mutex(true, GetInstanceMutexName(), out createdNew);

            if (!createdNew)
            {
                // 已有实例在运行 → 把现有窗口唤起到前台后退出
                Debug.LogWarning("[DesktopPet] 检测到已有实例在运行，唤醒既有窗口并退出");
                System.Environment.Exit(0);
                return;
            }
        }
        catch (System.Threading.AbandonedMutexException)
        {
            // 上一个实例崩溃了，Mutex 被系统遗弃 — 我们获得了所有权，正常启动
            Debug.LogWarning("[DesktopPet] 检测到上一个实例异常崩溃，已接管互斥锁，正常启动");
            // 启动阶段已经根据 clean/stable 标记统计过一次，不能在这里重复累加。
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[DesktopPet] 互斥锁创建失败（通常无害）: {ex.Message}");
        }
#endif

        // 防重复：如果已经有一个 DesktopPet 了，这个自毁
        DesktopPet[] all = FindObjectsOfType<DesktopPet>();
        if (all.Length > 1)
        {
            Debug.LogWarning("[DesktopPet] 检测到多个实例，自毁中");
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        // ---- 启动时初始化全量日志镜像 ----
        // ★ 不删除 Unity 的 Player.log：进程已持有该文件句柄，删除后 Unity 继续写
        //   "已删除句柄"，文件系统里只剩 0 B 空壳，日志全部丢失。
        //   改用 player_log.txt 镜像（每次追加即刷盘，运行中可实时查看调试）。
#if !UNITY_EDITOR
        try
        {
            System.IO.Directory.CreateDirectory(CrashLogDir);
            System.IO.File.AppendAllText(FullLogPath, $"\n===== [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 桌宠启动 =====\n");
        }
        catch { }
#endif

        // ---- 智能性能监控（根据系统负载自适应帧率/分辨率）----
        _perfMonitor = GetComponent<PerformanceMonitor>();
        if (_perfMonitor == null)
        {
            _perfMonitor = gameObject.AddComponent<PerformanceMonitor>();
            Debug.Log("[DesktopPet] 自动添加了 PerformanceMonitor 组件");
        }
        // 性能档位变化 → 同步刷新渲染分辨率
        _perfMonitor.OnTierChanged += OnPerformanceTierChanged;

        // ---- 自动挂载演武心经（MotionMemoryManager）----
        if (MotionMemoryManager.Instance == null)
        {
            gameObject.AddComponent<MotionMemoryManager>();
            Debug.Log("[DesktopPet] 自动挂载了 MotionMemoryManager（演武心经）组件");
        }

        QualitySettings.vSyncCount = 0;
#if !UNITY_EDITOR
        Debug.Log($"[DesktopPet] 启动性能监控，当前档位: {_perfMonitor.currentTier}");
#endif

        // 初始化物理状态
        petX = startX;
        int groundFloor = _screenHeight + GROUND_Y_MARGIN;
        petY = startY >= 0 ? startY : (groundFloor - petHeight);
        petVx = 0;
        petVy = 0;
        petWidth = 100;
        petHeight = 170;

        // 强制落地检测
        if (petY + petHeight >= groundFloor)
        {
            petY = groundFloor - petHeight;
            onGround = true;
        }

        // 自动确保 WindowOverlay 存在
        _windowOverlay = GetComponent<WindowOverlay>();
        if (_windowOverlay == null)
        {
            _windowOverlay = gameObject.AddComponent<WindowOverlay>();
            Debug.Log("[DesktopPet] 自动添加了 WindowOverlay 组件");
        }

        // 自动确保 MainThreadDispatcher 存在（后台线程 → 主线程回调调度，独立聊天窗口依赖）
        if (GetComponent<MainThreadDispatcher>() == null)
        {
            gameObject.AddComponent<MainThreadDispatcher>();
            Debug.Log("[DesktopPet] 自动添加了 MainThreadDispatcher 组件");
        }

        // 初始化系统托盘管理器
        _trayManager = GetComponent<SystemTrayManager>();
        if (_trayManager == null)
        {
            _trayManager = gameObject.AddComponent<SystemTrayManager>();
            Debug.Log("[DesktopPet] 自动添加了 SystemTrayManager 组件");
            // 首次添加时读取开机自启状态
        }

        // 监听托盘退出请求
        _trayManager.OnQuitRequested += OnTrayQuitRequested;

        // 自动确保 DragHandler 存在
        if (GetComponent<DragHandler>() == null)
        {
            gameObject.AddComponent<DragHandler>();
            Debug.Log("[DesktopPet] 自动添加了 DragHandler 组件");
        }

        // 自动确保 HybridRenderer 存在
        if (GetComponent<HybridRenderer>() == null)
        {
            gameObject.AddComponent<HybridRenderer>();
            Debug.Log("[DesktopPet] 自动添加了 HybridRenderer 组件");
        }

        // 自动确保右侧面板存在（取代旧的底部输入栏）
        if (GetComponent<RightPanel>() == null)
        {
            gameObject.AddComponent<RightPanel>();
            Debug.Log("[DesktopPet] 自动添加了 RightPanel 组件");
        }

        // 自动确保 TimeWeatherController 存在
        if (GetComponent<TimeWeatherController>() == null)
        {
            gameObject.AddComponent<TimeWeatherController>();
            Debug.Log("[DesktopPet] 自动添加了 TimeWeatherController 组件");
        }

        // 自动确保 ReminderManager 存在
        if (GetComponent<ReminderManager>() == null)
        {
            gameObject.AddComponent<ReminderManager>();
            Debug.Log("[DesktopPet] 自动添加了 ReminderManager 组件");
        }

        // 自动确保偏好与任务模板管理器存在，避免对应工具已注册但运行时单例未初始化。
        if (PreferencesManager.Instance == null)
        {
            gameObject.AddComponent<PreferencesManager>();
            Debug.Log("[DesktopPet] 自动添加了 PreferencesManager（心之所向）组件");
        }
        if (TaskTemplateManager.Instance == null)
        {
            gameObject.AddComponent<TaskTemplateManager>();
            Debug.Log("[DesktopPet] 自动添加了 TaskTemplateManager（太卜阵法图）组件");
        }

        // 自动确保 ServerPollService 存在（连接本地课表服务）
        if (GetComponent<ServerPollService>() == null)
        {
            gameObject.AddComponent<ServerPollService>();
            Debug.Log("[DesktopPet] 自动添加了 ServerPollService 组件");
        }

        // 旧 BallPanel 已由 RightPanel 的内嵌子视图取代，不再自动创建，
        // 避免遗留的左下角“符玄·太卜司”系统面板偶发出现。
        // 场景升级/旧 prefab 可能把 BallPanel 挂在独立对象上，不能只处理 DesktopPet 自身的组件；
        // 统一关闭并禁用所有遗留实例，避免热键偶发在左下角重新绘制旧系统面板。
        var legacyBallPanels = FindObjectsOfType<BallPanel>();
        for (int i = 0; i < legacyBallPanels.Length; i++)
        {
            legacyBallPanels[i].Close();
            legacyBallPanels[i].enabled = false;
        }
        if (legacyBallPanels.Length > 0)
            Debug.Log($"[DesktopPet] 已禁用遗留 BallPanel 实例: {legacyBallPanels.Length}");

        // 自动确保 ChatManager 存在（AI 对话核心）
        if (GetComponent<ChatManager>() == null)
        {
            gameObject.AddComponent<ChatManager>();
            Debug.Log("[DesktopPet] 自动添加了 ChatManager 组件");
        }

        // 自动确保 ToolCallInvoker 存在（符玄法阵 — 工具调用执行器）
        if (GetComponent<ToolCallInvoker>() == null)
        {
            gameObject.AddComponent<ToolCallInvoker>();
            Debug.Log("[DesktopPet] 自动添加了 ToolCallInvoker 组件");
        }

        // 将 ToolCallInvoker 注入 ChatManager
        var chat = GetComponent<ChatManager>();
        if (chat != null && chat.toolInvoker == null)
        {
            chat.toolInvoker = GetComponent<ToolCallInvoker>();
        }

        // ★ MotionAgent：常驻自主动作决策（本地 LLM 驱动）
        if (GetComponent<MotionAgent>() == null)
        {
            gameObject.AddComponent<MotionAgent>();
            Debug.Log("[DesktopPet] 自动添加了 MotionAgent（分神化身）组件");
        }

        // ★ DualModelValidator：动作镜鉴（单 GLM-4V 视觉评分，Qwen-VL 已移除）
        if (GetComponent<DualModelValidator>() == null)
        {
            gameObject.AddComponent<DualModelValidator>();
            Debug.Log("[DesktopPet] 自动添加了 DualModelValidator（镜鉴）组件");
        }

        // 自动确保 AutoChat 存在（AI 回复气泡 + 定时问候 + 互动事件）
        if (GetComponent<AutoChat>() == null)
        {
            gameObject.AddComponent<AutoChat>();
            Debug.Log("[DesktopPet] 自动添加了 AutoChat 组件");
        }

        // ★ ProactiveMessageScheduler：零 LLM 成本的主动关心调度器
        if (GetComponent<ProactiveMessageScheduler>() == null)
        {
            gameObject.AddComponent<ProactiveMessageScheduler>();
            Debug.Log("[DesktopPet] 自动添加了 ProactiveMessageScheduler（体贴天机）组件");
        }

        // ★ VisualHeartbeat：零 LLM 成本的屏幕变化感知（法眼）
        if (GetComponent<VisualHeartbeat>() == null)
        {
            gameObject.AddComponent<VisualHeartbeat>();
            Debug.Log("[DesktopPet] 自动添加了 VisualHeartbeat（法眼）组件");
        }

        // 获取渲染器引用：优先使用 HybridRenderer
        var hybrid = GetComponent<HybridRenderer>();
        if (hybrid != null)
        {
            _renderer = hybrid;
            Debug.Log("[DesktopPet] 使用 HybridRenderer（Live2D + 3D 混合）");
        }
        else
        {
            // 降级：单独使用 Live2DRenderer
            var live2d = GetComponent<Live2DRenderer>();
            if (live2d != null)
            {
                _renderer = live2d;
                Debug.Log("[DesktopPet] 使用 Live2DRenderer（降级模式）");
            }
            else
            {
                Debug.LogError("[DesktopPet] 未找到任何渲染器组件 (HybridRenderer/Live2DRenderer)");
                enabled = false;
            }
        }

        // ——— 应用持久化配置（覆盖 Inspector 默认值）———
        if (PetConfig.Instance != null)
        {
            PetConfig.Instance.ApplyAll();
            Debug.Log("[DesktopPet] 已应用 PetConfig 持久化配置");
        }

        Debug.Log($"[DesktopPet] 初始化完成 @ ({petX},{petY}), 屏幕: {Screen.width}x{Screen.height}");

        // 在非编辑器模式下，等待 WindowOverlay 找到句柄后初始化托盘
        if (!Application.isEditor)
        {
            StartCoroutine(WaitForWindowAndInitTray());
        }
    }

    private System.Collections.IEnumerator WaitForWindowAndInitTray()
    {
        // 等待 WindowOverlay 找到窗口句柄（最多等 10 秒）
        float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (_windowOverlay != null && _windowOverlay.WindowHandle != IntPtr.Zero)
            {
                Debug.Log($"[DesktopPet] 窗口句柄已就绪，初始化托盘管理器");
                if (_trayManager != null)
                {
                    _trayManager.Initialize(_windowOverlay.WindowHandle);
                    // ★ 首次运行自动设置开机自启（写入 HKCU\\Run）
                    // 这样下次重启后程序会自动启动
                    if (!_trayManager.AutoStartEnabled)
                    {
                        _trayManager.SetAutoStart(true);
                        Debug.Log("[DesktopPet] 首次运行，已自动设置开机自启");
                    }

                    // ★ ESC 待处理 — 托盘就绪后立即隐藏
                    if (_pendingEscToTray)
                    {
                        Debug.Log("[DesktopPet] 处理待处理的 ESC，隐藏到托盘");
                        _trayManager.MinimizeToTray();
                        _pendingEscToTray = false;
                    }

                    // ★ 不自动隐藏 — 让用户先看到窗口
                    // 用户可通过托盘右键菜单隐藏
                }
                yield break;
            }
            yield return new WaitForSeconds(0.5f);
            elapsed += 0.5f;
        }
        Debug.LogWarning("[DesktopPet] 等待窗口句柄超时，托盘管理器未初始化");
    }

    private void Update()
    {
        // ★★★ 时间间隙检测：Windows 睡眠后 Update() 停止，唤醒后 Time.realtimeSinceStartup 有巨大跳变。
        //     这是最可靠的睡眠检测方式。必须在最顶部执行，因为 Win32 操作（在 DragHandler.Update 等中）
        //     可能在稍后触发处于 DWM 恢复期的窗口操作，导致 DWM 崩溃。
        float now = Time.realtimeSinceStartup;
        if (_crashWatchdogEnabled && !_stableSessionMarked && _sessionStartedRealtime > 0f
            && now - _sessionStartedRealtime >= STABLE_SESSION_SECONDS)
        {
            _stableSessionMarked = true;
            SafePrefsSetInt(PREF_CRASH_COUNT, 0);
            SafePrefsSetInt(PREF_STABLE_SESSION, 1);
            SafePrefsDeleteKey("_skip_dwm_rebuild");
            SafePrefsSave();
            Debug.Log("[DesktopPet] ✅ 会话已稳定运行 60 秒，清除异常计数与 DWM 安全模式");
        }

        if (_lastUpdateRealtime > 0f && (now - _lastUpdateRealtime) > 10f)
        {
            Debug.Log($"[DesktopPet] ⚠ 检测到时间间隙 {(now - _lastUpdateRealtime):F0}s，系统可能刚从睡眠恢复");
            // 清空问候/闲话缓存，防止使用睡眠前生成的过时问候语
            var idleGen = GetComponent<IdleChatGenerator>();
            if (idleGen != null) idleGen.ClearCache();
            // 通知 WindowOverlay 暂停 Win32 操作
            var overlay = GetComponent<WindowOverlay>();
            if (overlay != null)
            {
                overlay.OnResumeFromSleep();
            }
            // 暂停物理和渲染，等待重建完成
            isPaused = true;
        }
        _lastUpdateRealtime = now;

        // ★ ESC → 隐藏到系统托盘（非编辑器模式）
#if !UNITY_EDITOR
        bool escDown = (GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0;
        if (escDown)
        {
            // ⚠️ 若有危险工具待确认，ESC = 拒绝执行（而不是隐藏窗口）
            if (ToolConfirmManager.HasPending)
            {
                Debug.Log("[DesktopPet] ESC 按下，拒绝危险工具执行");
                ToolConfirmManager.Resolve(false);
                return;
            }

            if (_trayManager != null && _trayManager.IsReady)
            {
                Debug.Log("[DesktopPet] ESC 按下，隐藏到系统托盘");
                _trayManager.MinimizeToTray();
                return;
            }
            // 托盘未就绪，标记等待 — 等就绪后自动隐藏
            if (_trayManager != null && !_trayManager.IsReady)
            {
                Debug.Log("[DesktopPet] ESC 按下但托盘未就绪，标记待处理");
                _pendingEscToTray = true;
                return;
            }
            // 没有托盘管理器 — 忽略
            Debug.Log("[DesktopPet] ESC 忽略：无托盘管理器");
            return;
        }
#endif

        // 暂停时不更新物理
        if (isPaused)
            return;

        // ========== v1 行为：拖拽时完全冻结物理 ==========
        if (isDragging)
        {
            // ★ 拖拽时仍要通知渲染器切换挣扎动画（物理不更新）
            if (_renderer != null)
                _renderer.OnPetUpdate(petX, petY, petWidth, petHeight,
                    petVx, petVy, onGround, isDragging, isPaused);
            return;
        }

        // 通知渲染器更新状态
        if (_renderer != null)
        {
            _renderer.OnPetUpdate(petX, petY, petWidth, petHeight,
                petVx, petVy, onGround, isDragging, isPaused);
        }

        // ---- 内存压力检查 + 日志清理（每30秒内存 / 每10分钟清理）----
#if !UNITY_EDITOR
        _memoryCheckTimer += Time.deltaTime;
        _cleanupTimer += Time.deltaTime;
        if (_memoryCheckTimer >= _memoryCheckInterval)
        {
            _memoryCheckTimer = 0f;
            CheckMemoryPressure();
            CheckSystemMemory();
        }
        if (_cleanupTimer >= CLEANUP_INTERVAL)
        {
            _cleanupTimer = 0f;
            CleanupLogFiles();
        }
#endif

        // 强制动作期间即使某个外部路径误调用 Resume()，也不能恢复水平移动或地面任务。
        if (_actionMovementLocked)
        {
            petVx = 0;
            currentTask = GroundTask.StopTime;
            _taskEndTime = 0f;
        }

        // 物理步进
        StepPet();

        // 地面状态机更新
        if (onGround && !isPaused && !_actionMovementLocked)
        {
            UpdateGroundTask();
        }
    }

    #endregion

    #region 物理步进

    private float _walkSpeedAccum = 0f; // 速度系数累加器（小数部分）

    /// <summary>
    /// 物理步进：位置更新、重力、边界碰撞、落地检测
    /// </summary>
    private void StepPet()
    {
        // 1. 应用速度（deltaTime 归一化，帧率无关）
        float moveDelta = petVx * WALK_SPEED_FACTOR * Time.deltaTime;
        _walkSpeedAccum += moveDelta;
        int deltaX = Mathf.RoundToInt(_walkSpeedAccum);
        _walkSpeedAccum -= deltaX;
        petX += deltaX;
        petY += petVy;

        // 2. 重力（空中时）
        if (!onGround)
        {
            petVy += gravity;
            if (petVy > maxFallSpeed)
                petVy = maxFallSpeed;
        }

        // 3. 左右边界碰撞
        if (petX <= 0)
        {
            petX = 0;
            if (petVx < 0)
            {
                if (onGround && currentTask == GroundTask.MoveLeftEdge)
                {
                    petVx = 0;
                }
                else
                {
                    petVx = -petVx;
                    // 碰撞反弹动画
                    if (_renderer != null) _renderer.ShowWallHitPose(-1);
                }
            }
        }
        else if (petX + petWidth >= _screenWidth)
        {
            petX = _screenWidth - petWidth;
            if (petVx > 0)
            {
                if (onGround && currentTask == GroundTask.MoveRightEdge)
                {
                    petVx = 0;
                }
                else
                {
                    petVx = -petVx;
                    // 碰撞反弹动画
                    if (_renderer != null) _renderer.ShowWallHitPose(1);
                }
            }
        }

        // 4. 顶部边界
        if (petY <= 0)
        {
            petY = 0;
            if (petVy < 0)
                petVy = -petVy;
        }

        // 5. 底部落地检测（地面位置 = 屏幕底部 + GROUND_Y_MARGIN）
        int groundFloor = _screenHeight + GROUND_Y_MARGIN;
        if (petY + petHeight >= groundFloor)
        {
            petY = groundFloor - petHeight;
            if (petVy > 0)
            {
                petVy = 0;
                onGround = true;
                OnLand();
            }
        }
        else
        {
            onGround = false;
        }
    }

    /// <summary>
    /// 落地回调
    /// </summary>
    private void OnLand()
    {
        Debug.Log("[DesktopPet] 落地");

        // 通知渲染器显示落地姿势
        if (_renderer != null)
            _renderer.ShowLandPose();

        // 落地后开始地面任务
        StartNextGroundTask();
    }

    #endregion

    #region 地面状态机

    /// <summary>
    /// 选择下一个地面任务 — 用权重随机选取
    /// </summary>
    private GroundTask PickNextGroundTask()
    {
        int wLeftEdge = taskWeightMoveLeftEdge;
        int wRightEdge = taskWeightMoveRightEdge;
        int wLeftTime = taskWeightMoveLeftTime;
        int wRightTime = taskWeightMoveRightTime;
        int wStop = taskWeightStopTime;

        int total = wLeftEdge + wRightEdge + wLeftTime + wRightTime + wStop;
        if (total <= 0) return GroundTask.StopTime; // 安全保底

        int roll = UnityEngine.Random.Range(0, total);

        if (roll < wLeftEdge) return GroundTask.MoveLeftEdge;
        roll -= wLeftEdge;

        if (roll < wRightEdge) return GroundTask.MoveRightEdge;
        roll -= wRightEdge;

        if (roll < wLeftTime) return GroundTask.MoveLeftTime;
        roll -= wLeftTime;

        if (roll < wRightTime) return GroundTask.MoveRightTime;

        return GroundTask.StopTime;
    }

    private GroundTask PickNextFromLeftEdge()
    {
        int wEdge = taskWeightMoveRightEdge;
        int wTime = taskWeightMoveRightTime;
        int wStop = taskWeightStopTime;
        int total = wEdge + wTime + wStop;
        if (total <= 0) return GroundTask.StopTime;
        int roll = UnityEngine.Random.Range(0, total);
        if (roll < wEdge) return GroundTask.MoveRightEdge;
        roll -= wEdge;
        if (roll < wTime) return GroundTask.MoveRightTime;
        return GroundTask.StopTime;
    }

    private GroundTask PickNextFromRightEdge()
    {
        int wEdge = taskWeightMoveLeftEdge;
        int wTime = taskWeightMoveLeftTime;
        int wStop = taskWeightStopTime;
        int total = wEdge + wTime + wStop;
        if (total <= 0) return GroundTask.StopTime;
        int roll = UnityEngine.Random.Range(0, total);
        if (roll < wEdge) return GroundTask.MoveLeftEdge;
        roll -= wEdge;
        if (roll < wTime) return GroundTask.MoveLeftTime;
        return GroundTask.StopTime;
    }

    /// <summary>
    /// 启动一个地面任务
    /// </summary>
    public void StartGroundTask(GroundTask task)
    {
        if (_actionMovementLocked)
        {
            petVx = 0;
            currentTask = GroundTask.StopTime;
            _taskEndTime = 0f;
            return;
        }

        currentTask = task;
        lastTask = task;
        _taskEndTime = 0f;

        switch (task)
        {
            case GroundTask.MoveLeftEdge:
            case GroundTask.MoveLeftTime:
                petVx = -1;
                break;
            case GroundTask.MoveRightEdge:
            case GroundTask.MoveRightTime:
                petVx = 1;
                break;
            case GroundTask.StopTime:
                petVx = 0;
                break;
        }

        switch (task)
        {
            case GroundTask.MoveLeftEdge:
            case GroundTask.MoveRightEdge:
            case GroundTask.MoveLeftTime:
            case GroundTask.MoveRightTime:
                if (_renderer != null) _renderer.ShowWalkPose();
                float moveDuration = UnityEngine.Random.Range(taskMoveTimeMinMs, taskMoveTimeMaxMs + 1);
                _taskEndTime = Time.time + moveDuration / 1000f;
                break;
            case GroundTask.StopTime:
                if (_renderer != null) _renderer.ShowStopPose(0f); // 不锁定姿势，让空闲动作播放
                float stopDuration = UnityEngine.Random.Range(taskStopTimeMinMs, taskStopTimeMaxMs + 1);
                _taskEndTime = Time.time + stopDuration / 1000f;
                break;
        }
    }

    public void StartNextGroundTask()
    {
        if (_actionMovementLocked)
        {
            petVx = 0;
            currentTask = GroundTask.StopTime;
            _taskEndTime = 0f;
            return;
        }
        StartGroundTask(PickNextGroundTask());
    }

    private void StartNextFromLeftEdge()
    {
        StartGroundTask(PickNextFromLeftEdge());
    }

    private void StartNextFromRightEdge()
    {
        StartGroundTask(PickNextFromRightEdge());
    }

    /// <summary>
    /// 每帧检查当前地面任务是否需要切换
    /// </summary>
    private void UpdateGroundTask()
    {
        if (currentTask == GroundTask.None)
        {
            StartNextGroundTask();
            return;
        }

        switch (currentTask)
        {
            case GroundTask.MoveLeftEdge:
                if (petX <= 0)
                    StartNextFromLeftEdge();
                break;

            case GroundTask.MoveRightEdge:
                if (petX + petWidth >= _screenWidth)
                    StartNextFromRightEdge();
                break;

            case GroundTask.MoveLeftTime:
            case GroundTask.MoveRightTime:
            case GroundTask.StopTime:
                if (_taskEndTime > 0f && Time.time >= _taskEndTime)
                    StartNextGroundTask();
                break;
        }
    }

    #endregion

    private void OnPerformanceTierChanged(PerformanceTier tier)
    {
        var renderer = GetComponent<Live2DRenderer>();
        if (renderer != null)
        {
            renderer.OnPerformanceTierChanged(tier);
        }
    }

    public PerformanceMonitor GetPerformanceMonitor() => _perfMonitor;

    // Live2DRenderer 负责所有渲染，无需 OnGUI

    private void OnTrayQuitRequested()
    {
        Debug.Log("[DesktopPet] 托盘管理器请求退出程序");
        BeginShutdown("托盘");
        Application.Quit();
    }

    /// <summary>
    /// 测试模式完整退出入口（@@test:quit，2026-08-17 验收 P10）：
    /// 与托盘「退出」走同一清理链（ExternalChatWindow.Shutdown 已由调用方执行，
    /// 此处负责互斥体释放 + Application.Quit），供终端链路验收完整退出。
    /// </summary>
    public void QuitFromTestCommand()
    {
        Debug.Log("[DesktopPet] 测试命令请求完整退出");
        BeginShutdown("测试命令");
        Application.Quit();
    }

    private void OnApplicationQuit()
    {
        // 标记正常退出（看门狗用）
        PlayerPrefs.SetInt(PREF_CLEAN_EXIT, 1);
        PlayerPrefs.Save();

        BeginShutdown("Unity 生命周期");
    }

    /// <summary>
    /// 系统挂起（睡眠/休眠）时释放 D3D 资源和 Win32 句柄关联。
    /// 唤醒后一切由 WindowOverlay.OnApplicationPause(false) 自动重建。
    /// </summary>
    private void OnApplicationPause(bool pause)
    {
        if (pause)
        {
            Debug.Log("[DesktopPet] ⏸ 系统挂起（睡眠），暂停活动");
            isPaused = true;
            // 记下暂停时间戳，唤醒后根据耗时判断是否真的睡了
            PlayerPrefs.SetString("_suspend_time", DateTime.Now.ToString("O"));
            PlayerPrefs.Save();
        }
        else
        {
            // 检查是否真的经历了睡眠（还是只是焦点切换）
            string saved = PlayerPrefs.GetString("_suspend_time", "");
            bool wasSleep = true;
            if (!string.IsNullOrEmpty(saved))
            {
                try
                {
                    var suspendTime = DateTime.Parse(saved);
                    if ((DateTime.Now - suspendTime).TotalSeconds < 30)
                        wasSleep = false; // <30s 可能是临时焦点切换，不是睡眠唤醒
                }
                catch
                {
                    // 保存的字符串格式异常，视为睡眠唤醒（安全侧）
                }
            }
            if (wasSleep)
            {
                Debug.Log("[DesktopPet] ▶ 系统唤醒（睡眠后恢复）");
                // 清空问候/闲话缓存，防止使用睡眠前生成的过时问候语
                var idleGen = GetComponent<IdleChatGenerator>();
                if (idleGen != null) idleGen.ClearCache();
                // 唤醒后 Unity 会重建 D3D 设备和渲染上下文，所有老资源已释放
                // WindowOverlay.OnApplicationPause(false) 已自动重建窗口样式/DWM 玻璃
                // 这里只需要恢复物理状态
                isPaused = false;
                onGround = false; // 强制重新找地面
            }
            else
            {
                Debug.Log("[DesktopPet] ▶ 焦点恢复（短时切换，非睡眠）");
                isPaused = false;
            }
        }
    }

    private void OnDestroy()
    {
        BeginShutdown("对象销毁");
    }

    /// <summary>
    /// 统一的退出/销毁清理入口。Unity 不保证多个组件 OnDestroy 的先后顺序，
    /// 因此必须在 OnApplicationQuit 阶段主动停止外置窗口线程，早于 D3D/RT 资源回收。
    /// </summary>
    private void BeginShutdown(string source)
    {
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        Debug.Log($"[DesktopPet] 开始退出清理（来源: {source}）");

        if (_trayManager != null)
            _trayManager.OnQuitRequested -= OnTrayQuitRequested;

        // 外置窗口线程可能仍在读取 RT/NativeArray；先让它在自身线程销毁窗口，
        // 再进入 Unity 组件的 OnDestroy，避免 destroyTJDevice 竞态。
        if (ExternalChatWindow.IsCreated)
            ExternalChatWindow.Shutdown();

        // 退出传播取消：避免桌宠退出后 OpenClaw 继续后台运行任务。
        if (OpenClawBridge.IsBusy && !string.IsNullOrEmpty(OpenClawBridge.LastTaskId))
        {
            Debug.Log($"[DesktopPet] 🚫 退出时取消在途任务: {OpenClawBridge.LastTaskId}");
            OpenClawBridge.RequestTaskCancellation();
            _ = OpenClawBridge.CancelTaskAsync(OpenClawBridge.LastTaskId);
        }

#if !UNITY_EDITOR
        Application.logMessageReceivedThreaded -= MirrorAllLogs;
#endif
        ReleaseMutex();
    }

    private void ReleaseMutex()
    {
        if (_instanceMutex != null)
        {
            try { _instanceMutex.ReleaseMutex(); } catch { /* 进程退出时 Mutex 可能已被 OS 释放 */ }
            try { _instanceMutex.Dispose(); } catch { /* 同上 */ }
            _instanceMutex = null;
        }
    }

    #region 交互接口

    /// <summary>
    /// 从拖拽释放设置初速度（v1 行为：保证向下速度）
    /// </summary>
    public void ApplyDragVelocity(int vx, int vy)
    {
        // v1 行为：释放后如果 vy <= 0，强制设 vy = 2 保证下落
        if (vy <= 0) vy = 2;
        petVx = Mathf.Clamp(vx, -maxHorizontalSpeed, maxHorizontalSpeed);
        petVy = Mathf.Clamp(vy, -maxFallSpeed, maxFallSpeed);
        onGround = false;
        currentTask = GroundTask.None;
        Debug.Log($"[DesktopPet] 拖拽释放: vx={petVx}, vy={petVy}");
    }

    /// <summary>
    /// 暂停宠物运动
    /// </summary>
    public void Pause(float durationSeconds)
    {
        isPaused = true;
        if (durationSeconds > 0)
        {
            Invoke(nameof(Resume), durationSeconds);
        }
    }

    /// <summary>
    /// 恢复宠物运动
    /// </summary>
    public void Resume()
    {
        isPaused = false;
        if (onGround)
        {
            if (currentTask == GroundTask.None || currentTask == GroundTask.StopTime)
            {
                StartNextGroundTask();
            }
        }
    }

    /// <summary>
    /// 锁定/解除强制动作的物理移动。与 isPaused 分离，避免普通 Resume 路径绕过动作锁。
    /// </summary>
    public void SetActionMovementLock(bool locked)
    {
        _actionMovementLocked = locked;
        if (locked)
        {
            petVx = 0;
            currentTask = GroundTask.StopTime;
            _taskEndTime = 0f;
        }
    }

    public bool IsActionMovementLocked => _actionMovementLocked;

    /// <summary>
    /// 重置宠物位置
    /// </summary>
    public void TeleportTo(int x, int y)
    {
        petX = x;
        petY = y;
    }

    /// <summary>
    /// 强制停止走路（被右键菜单调用）
    /// </summary>
    public void ForceStop()
    {
        // 如果当前是边缘任务，强制转为自由走路
        if (currentTask == GroundTask.MoveLeftEdge)
            currentTask = GroundTask.MoveLeftTime;
        else if (currentTask == GroundTask.MoveRightEdge)
            currentTask = GroundTask.MoveRightTime;

        // 立即结束当前任务
        if (currentTask != GroundTask.None && currentTask != GroundTask.StopTime)
        {
            petVx = 0;
            if (_renderer != null)
            {
                _renderer.ShowStopPose(0f);
                _renderer.OnPetUpdate(petX, petY, petWidth, petHeight,
                    petVx, petVy, onGround, isDragging, isPaused);
            }
            _taskEndTime = 0f;
            StartGroundTask(GroundTask.StopTime);
        }
    }

    #endregion
}
