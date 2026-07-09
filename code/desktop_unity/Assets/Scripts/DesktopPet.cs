using System;
using System.Threading;
using System.Runtime.InteropServices;
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
    private const string MutexName = "DesktopPet_Unity_SingleInstance";

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

    // 时间间隙检测：睡眠唤醒后 Time.realtimeSinceStartup 跳变 >10s 则触发重建
    private float _lastUpdateRealtime = 0f;

    #endregion

    #region Unity 生命周期

    // ===== 崩溃日志 & 内存监控 =====
    private const string CrashLogPath = "crash_log.txt";
    private const long CRASH_LOG_MAX_BYTES = 1024L * 1024L;  // crash_log.txt 超过1MB自动截断
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
    private const long PLAYER_LOG_MAX_BYTES = 2L * 1024L * 1024L; // Player.log 超过 2MB 自动删除重开

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
            catch { }
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
            catch { }
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
                    catch { }
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
        catch { }
    }

    /// <summary>清理过期日志文件：crash_log.txt 超1MB截断 + 删除7天前的 build_log*.txt</summary>
    private void CleanupLogFiles()
    {
        try
        {
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

            // 2) 删除 7 天前的 build_log*.txt
            string dataDir = System.IO.Directory.GetCurrentDirectory();
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

            // 3) Player.log 超过 2MB 时删除重建（Unity 会自动重建）
            string playerLogPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Low", "DefaultCompany", "desktop pet", "Player.log");
            var playerLogInfo = new System.IO.FileInfo(playerLogPath);
            if (playerLogInfo.Exists && playerLogInfo.Length > PLAYER_LOG_MAX_BYTES)
            {
                // 先备份文件名，删除当前日志文件
                System.IO.File.Delete(playerLogPath);
                Debug.Log($"[DesktopPet] Player.log 超过 2MB，已删除释放空间");
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
    private const int MAX_CRASHES_BEFORE_SAFE_MODE = 2;

    private void Awake()
    {
        // ---- 崩溃看门狗 ----
#if !UNITY_EDITOR
        Application.logMessageReceivedThreaded += CaptureCrashLog;
#endif
        // ★ 看门狗逻辑：检查上次是否正常退出（方式1: clean_exit 标记）
        bool previousCleanExit = PlayerPrefs.GetInt(PREF_CLEAN_EXIT, 0) == 1;
        PlayerPrefs.DeleteKey(PREF_CLEAN_EXIT); // 清除标记，等正常退出时重新设置
        PlayerPrefs.Save();

        if (!previousCleanExit)
        {
            // 上次异常退出 → 崩溃计数+1
            int crashCount = PlayerPrefs.GetInt(PREF_CRASH_COUNT, 0) + 1;
            PlayerPrefs.SetInt(PREF_CRASH_COUNT, crashCount);
            PlayerPrefs.Save();
            Debug.LogWarning($"[DesktopPet] ⚠ 上次异常退出（崩溃计数={crashCount}/{MAX_CRASHES_BEFORE_SAFE_MODE}）");

            if (crashCount >= MAX_CRASHES_BEFORE_SAFE_MODE)
            {
                // 连续多次崩溃 → 下一次唤醒跳过 DWM 重建（黑色背景不透明，但程序稳定运行）
                PlayerPrefs.SetString("_skip_dwm_rebuild", "1");
                PlayerPrefs.Save();
                Debug.LogWarning("[DesktopPet] 🛡 连续崩溃超过阈值，下次睡眠唤醒跳过 DWM 玻璃层重建（安全模式）");
            }
        }
        else
        {
            // 上次正常退出 → 重置崩溃计数 + 清除安全模式
            PlayerPrefs.SetInt(PREF_CRASH_COUNT, 0);
            PlayerPrefs.DeleteKey("_skip_dwm_rebuild");
            PlayerPrefs.Save();
        }

        // ---- 单例互斥锁：防止 Build and Run 产生多个实例 ----
        // ★ 注意: Editor 下跳过互斥锁检查，因为命名 Mutex 在域重载后不释放，
        //   会导致第二次 Play 时误判"已有实例"而退出 Play Mode。
#if !UNITY_EDITOR
        try
        {
            bool createdNew;
            _instanceMutex = new Mutex(true, MutexName, out createdNew);

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
            // 方式2: 互斥锁遗弃也视为崩溃
            int crashCount = PlayerPrefs.GetInt(PREF_CRASH_COUNT, 0) + 1;
            PlayerPrefs.SetInt(PREF_CRASH_COUNT, crashCount);
            if (crashCount >= MAX_CRASHES_BEFORE_SAFE_MODE)
            {
                PlayerPrefs.SetString("_skip_dwm_rebuild", "1");
                Debug.LogWarning("[DesktopPet] 🛡 连续崩溃超过阈值，下次睡眠唤醒跳过 DWM 玻璃层重建（安全模式）");
            }
            PlayerPrefs.Save();
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
        // ---- 启动时清理上次 Player.log（防止旧日志堆积）----
#if !UNITY_EDITOR
        try
        {
            string playerLogPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "Low", "DefaultCompany", "desktop pet", "Player.log");
            var fi = new System.IO.FileInfo(playerLogPath);
            if (fi.Exists)
            {
                fi.Delete();
                Debug.Log("[DesktopPet] 启动时已重置 Player.log，释放磁盘空间");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[DesktopPet] 启动重置 Player.log 失败（无害）: {ex.Message}");
        }
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

        // 自动确保底部输入栏存在
        if (GetComponent<BottomInputBar>() == null)
        {
            gameObject.AddComponent<BottomInputBar>();
            Debug.Log("[DesktopPet] 自动添加了 BottomInputBar 组件");
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

        // 自动确保 ServerPollService 存在（连接本地课表服务）
        if (GetComponent<ServerPollService>() == null)
        {
            gameObject.AddComponent<ServerPollService>();
            Debug.Log("[DesktopPet] 自动添加了 ServerPollService 组件");
        }

        // 自动确保 FloatingBall 存在（悬浮球 + 辐射菜单）
        if (GetComponent<FloatingBall>() == null)
        {
            gameObject.AddComponent<FloatingBall>();
            Debug.Log("[DesktopPet] 自动添加了 FloatingBall 组件");
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

        // 物理步进
        StepPet();

        // 地面状态机更新
        if (onGround && !isPaused)
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
        OnDestroy(); // 释放互斥锁
        Application.Quit();
    }

    private void OnApplicationQuit()
    {
        // 标记正常退出（看门狗用）
        PlayerPrefs.SetInt(PREF_CLEAN_EXIT, 1);
        PlayerPrefs.Save();
        ReleaseMutex();
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
                catch { }
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
        ReleaseMutex();
    }

    private void ReleaseMutex()
    {
        if (_instanceMutex != null)
        {
            try { _instanceMutex.ReleaseMutex(); } catch { }
            try { _instanceMutex.Dispose(); } catch { }
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
