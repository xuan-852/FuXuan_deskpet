using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// 桌面宠物 — 窗口透明化与置顶管理
///
/// 使用 DWM 玻璃层扩展 (DwmExtendFrameIntoClientArea) 使黑色像素透明，
/// 实现透明窗口效果，支持置顶、无边框、点击穿透。
///
/// 相比色键抠图 (Color Key) 方案，此方案不会产生绿色残留问题，
/// 因为 Live2D 模型的半透明/抗锯齿像素是黑色系的，不会混入绿色。
///
/// 用法：
/// 1. 挂到任意 GameObject
/// 2. Main Camera 的 Background 设为纯黑 (R=0,G=0,B=0)
/// 3. 构建运行
/// 4. 需在 Project Settings 中：Graphics API = D3D11，取消勾选 DXGI flip model
/// </summary>
public class WindowOverlay : MonoBehaviour
{
    #region Win32 API

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_SYSMENU = 0x00080000;
    private const uint WS_MINIMIZEBOX = 0x00020000;
    private const uint WS_MAXIMIZEBOX = 0x00010000;

    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_APPWINDOW = 0x00040000;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    // 要移除的样式：标题栏、边框、系统菜单、最小/最大按钮
    private const uint STYLE_TO_REMOVE = WS_CAPTION | WS_THICKFRAME | WS_SYSMENU |
                                          WS_MINIMIZEBOX | WS_MAXIMIZEBOX;

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOZORDER = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_SHOWNA = 8;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("Dwmapi.dll", SetLastError = true)]
    private static extern uint DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS margins);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const uint SPI_GETWORKAREA = 0x0030;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left; public int Top;
        public int Right; public int Bottom;
    }

    #endregion

    [Header("窗口设置")]
    [Tooltip("窗口宽度（像素），0=全屏宽度")]
    public int width = 0;  // 0=全屏

    [Tooltip("窗口高度（像素），0=全屏高度")]
    public int height = 0; // 0=全屏

    [Tooltip("点击穿透：鼠标事件透过窗口传到桌面（默认 true=安全启动，DragHandler 每帧动态管理）")]
    public bool clickThrough = true;

    [Tooltip("重试次数")]
    public int maxRetries = 5;

    [Header("恢复设置")]
    [Tooltip("透明窗口设置失败后，后台重试间隔（秒），0=不重试")]
    public float backgroundRetryInterval = 30f;

    [Header("调试")]
    public bool debugLog = true;

    private IntPtr _hwnd = IntPtr.Zero;
    public System.IntPtr WindowHandle => _hwnd;
    private bool _applied = false;
    private bool _suspended = false;  // 睡眠挂起标记，阻止危险 Win32 调用
    private bool _rebuildPending = false; // 延迟重建已调度，防重复
    private int _origW;
    private int _origH;

    private void Start()
    {
        // 强制相机设置
        ForceCameraSettings();

        if (!Application.isEditor)
        {
            StartCoroutine(RetryApply());

            // ★ 后台自动恢复：初始重试失败后，每 30s 再试一次
            if (backgroundRetryInterval > 0f)
                StartCoroutine(BackgroundRetryLoop());
        }
    }

    /// <summary>
    /// 后台定时检测透明窗口状态，失败时自动恢复
    /// DWM 在驱动重置、系统负载高时可能暂时不可用，需持续重试
    /// </summary>
    private System.Collections.IEnumerator BackgroundRetryLoop()
    {
        // 先等初始重试完成
        yield return new WaitForSeconds(backgroundRetryInterval);

        while (backgroundRetryInterval > 0f)
        {
            // 已成功就绪，且窗口句柄有效 → 不需要恢复
            if (_applied && _hwnd != IntPtr.Zero && IsWindow(_hwnd))
            {
                yield return new WaitForSeconds(backgroundRetryInterval);
                continue;
            }

            // 重新查找窗口句柄（可能因为 DWM 重启、快速启动等原因窗口重建了句柄）
            IntPtr newHwnd = FindUnityWindow();
            if (newHwnd != IntPtr.Zero)
            {
                _hwnd = newHwnd;
                bool ok = ApplyNow();
                if (ok)
                {
                    Log($"✅ 后台自动恢复透明窗口成功 (句柄={_hwnd.ToInt64():X8})");
                }
                else
                {
                    Log($"⚠️ 后台自动恢复第尝试失败，{backgroundRetryInterval}s 后重试");
                }
            }
            else
            {
                Log($"⚠️ 后台恢复：未找到 Unity 窗口，{backgroundRetryInterval}s 后重试");
            }

            yield return new WaitForSeconds(backgroundRetryInterval);
        }
    }

    private void ForceCameraSettings()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // 纯黑透明背景
            cam.allowHDR = false;
            cam.allowMSAA = false;
            Log("已强制相机背景为纯黑+关HDR");
        }
    }

    private System.Collections.IEnumerator RetryApply()
    {
        for (int i = 0; i < startupDelayFrames; i++)
            yield return null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            Log($"第 {attempt}/{maxRetries} 次尝试...");

            // 每次重试前重新找窗口（窗口可能在延迟后才有正确标题）
            _hwnd = FindUnityWindow();

            if (_hwnd != IntPtr.Zero)
            {
                bool ok = ApplyNow();
                if (ok)
                {
                    Log($"✅ 第 {attempt} 次尝试成功");
                    yield break;
                }
            }

            // 等待一帧再重试
            yield return null;
        }

        LogError($"❌ 经过 {maxRetries} 次尝试仍无法完成透明窗口设置");
    }

    private int _startupDelayFrames = 5;
    private int startupDelayFrames => _startupDelayFrames;

    /// <summary>
    /// 是否启用多显示器虚拟桌面模式
    /// </summary>
    public bool isMultiMonitor { get; private set; } = false;

    /// <summary>
    /// 虚拟桌面原点 X（主屏不一定是左起时可能为负）
    /// </summary>
    public int virtualScreenX { get; private set; } = 0;
    public int virtualScreenY { get; private set; } = 0;

    /// <summary>
    /// 主显示器尺寸（始终以 (0,0) 为原点）
    /// </summary>
    public int primaryScreenWidth { get; private set; } = 0;
    public int primaryScreenHeight { get; private set; } = 0;

    /// <summary>
    /// 获取全屏尺寸 — 只在主显示器范围内（多显示器时窗口不跨屏）
    /// 使用 SM_CXSCREEN / SM_CYSCREEN 而非虚拟桌面尺寸，确保 Screen.width/height 始终等于主屏大小，
    /// 所有 UI 组件（悬浮球、气泡、面板等）自然定位正确，无需逐个适配多显示器。
    /// </summary>
    private void GetFullScreenSize(out int w, out int h, out int originX, out int originY)
    {
        w = GetSystemMetrics(SM_CXSCREEN);
        h = GetSystemMetrics(SM_CYSCREEN);
        originX = 0;
        originY = 0;

        primaryScreenWidth = w;
        primaryScreenHeight = h;
        isMultiMonitor = false;
        virtualScreenX = 0;
        virtualScreenY = 0;

        Log($"主屏尺寸: {w}x{h}");
    }

    /// <summary>
    /// 应用透明窗口设置
    /// </summary>
    public bool ApplyNow()
    {
        if (_hwnd == IntPtr.Zero)
        {
            LogError("ApplyNow: 窗口句柄为空");
            return false;
        }

        // 验证句柄有效性 — 避免操作 DWM 重启后的失效句柄导致系统崩溃
        if (!IsWindow(_hwnd))
        {
            LogError("ApplyNow: 句柄已失效，跳过");
            _applied = false;
            return false;
        }

        // ★ 整个操作包裹 try-catch：任何 Win32/DWM 操作失败不崩系统
        try
        {

        int screenW, screenH, screenX, screenY;
        GetFullScreenSize(out screenW, out screenH, out screenX, out screenY);

        // 读取窗口标题做诊断
        StringBuilder titleSb = new StringBuilder(256);
        int titleLen = (int)GetWindowTextW(_hwnd, titleSb, titleSb.Capacity);
        string title = titleLen > 0 ? titleSb.ToString().Trim() : "(空标题)";
        Log($"目标窗口: '{title}' ({_hwnd.ToInt64():X8})");

        if (GetWindowRect(_hwnd, out RECT rect))
        {
            _origW = rect.Right - rect.Left;
            _origH = rect.Bottom - rect.Top;
            Log($"原始窗口大小: {_origW}x{_origH}");
        }

        // ---- 步骤1: 设置扩展样式（必须先做！因为 WS_EX_LAYERED 必须存在） ----
        uint exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        Log($"旧扩展样式: 0x{exStyle:X8}");

        exStyle |= WS_EX_LAYERED | WS_EX_TOPMOST;
        exStyle &= ~WS_EX_APPWINDOW;
        exStyle |= WS_EX_TOOLWINDOW;

        int setResult = SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
        if (setResult == 0)
        {
            int err = Marshal.GetLastWin32Error();
            LogError($"SetWindowLong(扩展样式) 失败, error={err}");
        }
        else
        {
            Log($"已设置扩展样式: 0x{exStyle:X8}");
        }

        // ---- 步骤2: 移除标题栏和边框 ----
        uint style = GetWindowLong(_hwnd, GWL_STYLE);
        Log($"旧窗口样式: 0x{style:X8}");

        style &= ~STYLE_TO_REMOVE;
        style |= WS_POPUP | WS_VISIBLE;

        setResult = SetWindowLong(_hwnd, GWL_STYLE, style);
        if (setResult == 0)
        {
            int err = Marshal.GetLastWin32Error();
            LogError($"SetWindowLong(样式) 失败, error={err}");
        }
        else
        {
            Log($"已移除标题栏和边框: 0x{style:X8}");
        }

        // ---- 步骤3: 用 SetWindowPos 刷新窗口 ----
        int w = width > 0 ? width : screenW;
        int h = height > 0 ? height : screenH;

        bool posResult = SetWindowPos(_hwnd, HWND_TOPMOST,
            screenX, screenY, w, h,
            SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOACTIVATE);
        if (!posResult)
        {
            int err = Marshal.GetLastWin32Error();
            LogError($"SetWindowPos 失败, error={err}");
        }
        else
        {
            Log($"窗口已刷新: {w}x{h}");
        }

        // ---- 步骤4: 重新显示窗口，确保新样式生效 ----
        ShowWindow(_hwnd, SW_SHOWNA);

        // ---- 步骤5: DWM 玻璃层扩展（黑色=透明） ----
        // 关键：将整个客户区扩展为 DWM 玻璃区域，使纯黑 (0,0,0) 变透明
        MARGINS margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = 0, cyTopHeight = 0, cyBottomHeight = 0 };
        uint dwmResult = DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        if (dwmResult != 0)
        {
            LogError($"DwmExtendFrameIntoClientArea 失败, error={dwmResult}");
        }
        else
        {
            Log("已应用 DWM 玻璃层透明（黑色=透明）");
        }

        // 刷新窗口使 DWM 生效
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, w, h, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);

        _applied = true;

        // ★ 启动时开启穿透，让桌面点击正常通过。
        SetClickThrough(true);

        // ★ 初始化收纳盘拖放接收（窗口就绪后方可挂载 WndProc）
        if (_hwnd != IntPtr.Zero)
        {
            DockDropHandler.Initialize(_hwnd);
        }

        // ★ 通知 DragHandler 强制重设穿透缓存
        DragHandler dragHandler = GetComponent<DragHandler>();
        if (dragHandler != null)
            dragHandler.ResetClickState();

        // ★★★ 确保窗口能接收输入事件
        ShowWindow(_hwnd, 5);
        SetForegroundWindow(_hwnd);
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, w, h,
            SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

        Log($"✅ 透明窗口已就绪: {w}x{h}, 句柄={_hwnd.ToInt64():X8}, 标题='{title}'");
        return true;

        }
        catch (Exception ex)
        {
            LogError($"ApplyNow 异常（已安全捕获，跳过窗口重建）: {ex.GetType().Name}: {ex.Message}");
            _applied = false;
            return false;
        }
    }

    /// <summary>
    /// 查找 Unity 主窗口句柄
    /// </summary>
    private IntPtr FindUnityWindow()
    {
        uint currentPid = (uint)Process.GetCurrentProcess().Id;
        string productName = Application.productName;

        Log($"本进程 PID={currentPid}, productName='{productName}'");

        // ★ 方法1: 用 Process.MainWindowHandle — 最简单可靠
        try
        {
            IntPtr mainHwnd = Process.GetCurrentProcess().MainWindowHandle;
            if (mainHwnd != IntPtr.Zero)
            {
                StringBuilder sb = new StringBuilder(256);
                int len = (int)GetWindowTextW(mainHwnd, sb, sb.Capacity);
                string title = len > 0 ? sb.ToString().Trim() : "(空标题)";
                Log($"Process.MainWindowHandle → '{title}' ({mainHwnd.ToInt64():X8})");
                if (len > 0)
                {
                    Log($"匹配窗口: '{title}' ({mainHwnd.ToInt64():X8})");
                    return mainHwnd;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Process.MainWindowHandle 失败: {ex.Message}");
        }

        // ★ 方法2: 枚举窗口，但更严格地跳过非主窗口
        Log($"回退: 枚举进程窗口...");
        IntPtr found = IntPtr.Zero;

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == currentPid)
            {
                StringBuilder sb = new StringBuilder(256);
                int len = (int)GetWindowTextW(hWnd, sb, sb.Capacity);
                string title = len > 0 ? sb.ToString().Trim() : "(空标题)";

                Log($"  窗口: {hWnd.ToInt64():X8} 标题='{title}'");

                if (len > 0 && !string.IsNullOrEmpty(title))
                {
                    // 跳过所有已知的内部窗口 — 尤其警惕单字符标题！
                    if (title.StartsWith("Unity_") || title.StartsWith("UMP_") ||
                        title.StartsWith("D3D") || title.Contains("GfxPlugin") ||
                        title == "UnityWindowClass" || title == "UnityChildWindow" ||
                        title.Length <= 2)  // ★ 单/双字符标题几乎肯定是内部窗口
                        return true;

                    // 优先精确匹配 productName
                    if (!string.IsNullOrEmpty(productName) &&
                        title.Equals(productName, StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"  → 精确匹配 productName");
                        found = hWnd;
                        return false;
                    }

                    // 包含 productName
                    if (!string.IsNullOrEmpty(productName) &&
                        title.IndexOf(productName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Log($"  → 包含 productName");
                        found = hWnd;
                        return false;
                    }

                    // 特征匹配 — 必须有明确的关键词
                    if (title.Contains("Unity") || title.Contains("Player") ||
                        title.IndexOf("desktop", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Log($"  → 特征匹配");
                        found = hWnd;
                        return false;
                    }
                }
            }
            return true;
        }, IntPtr.Zero);

        if (found != IntPtr.Zero)
        {
            StringBuilder sb = new StringBuilder(256);
            int len = (int)GetWindowTextW(found, sb, sb.Capacity);
            Log($"匹配窗口: '{sb.ToString().Trim()}' ({found.ToInt64():X8})");
        }

        return found;
    }

    public void SetClickThrough(bool enabled)
    {
        if (_hwnd == IntPtr.Zero || !_applied || _suspended) return;
        // ★★★ 运行时句柄有效性验证：睡眠唤醒后句柄可能已失效（DWM 重启），
        //     操作失效句柄可致 DWM 崩溃进而系统重启。
        if (!IsWindow(_hwnd))
        {
            UnityEngine.Debug.LogWarning("[WindowOverlay] SetClickThrough: 句柄已失效，跳过");
            _applied = false;
            _hwnd = IntPtr.Zero;
            return;
        }
        // ★ 睡眠后不再调 SetWindowLong/SetWindowPos — DWM 可能未就绪，操作失效句柄可致系统崩溃
        uint ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        if (enabled) ex |= WS_EX_TRANSPARENT;
        else ex &= ~WS_EX_TRANSPARENT;
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
        clickThrough = enabled;

        // ★★★ 关键：SetWindowLong 改完 WS_EX_TRANSPARENT 后，DWM 可能不会立即重新命中测试。
        //     连续两次 SetWindowPos 强制 DWM 刷新窗口区域和命中测试状态。
        //     第一次刷帧，第二次确保生效（有用户反馈单次 SWP_FRAMECHANGED 在某些 Win11 版本不够）。
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    /// <summary>
    /// 系统挂起（睡眠/Sleep）时标记，阻止危险 Win32 调用。
    /// 唤醒后延迟重建窗口透明设置（避免 DWM/GPU 未恢复时直接操作导致系统崩溃）。
    /// </summary>
    private void OnApplicationPause(bool pause)
    {
        if (pause)
        {
            _suspended = true;
            UnityEngine.Debug.Log("[WindowOverlay] ⏸ 系统挂起（睡眠），暂停 Win32 操作");
        }
        else
        {
            UnityEngine.Debug.Log("[WindowOverlay] ▶ 系统唤醒（OnApplicationPause），延迟2s重建窗口");

            // ★ 安全模式检查：连续崩溃后跳过 DWM 重建
            if (UnityEngine.PlayerPrefs.GetString("_skip_dwm_rebuild", "") == "1")
            {
                UnityEngine.Debug.Log("[WindowOverlay] 🛡 安全模式：跳过 DWM 玻璃层重建（唤醒）");
                _suspended = false;
                return;
            }

            // ★ 不立即恢复，走延迟重建路径（与 OnResumeFromSleep 一致）
            //   保持 _suspended=true 防止 SetClickThrough 等在 DWM 未就绪时调用 Win32 API
            RebuildAfterDelay();
        }
    }

    /// <summary>
    /// 延迟 2 秒重建窗口设置（DWM 玻璃层 + 样式），等待 DWM/GPU 驱动完全恢复。
    /// 重建完成后恢复 _suspended = false，允许正常 Win32 调用。
    /// _rebuildPending 防止并发调度双重重建。
    /// </summary>
    private void RebuildAfterDelay()
    {
        if (_rebuildPending)
        {
            UnityEngine.Debug.Log("[WindowOverlay] 延迟重建已在调度中，跳过重复请求");
            return;
        }
        _rebuildPending = true;

        if (_hwnd != IntPtr.Zero)
        {
            RunAfterDelay(2f, () =>
            {
                _rebuildPending = false;
                if (_hwnd == IntPtr.Zero) return;
                UnityEngine.Debug.Log("[WindowOverlay] 延迟重建：验证窗口句柄");
                if (!IsWindow(_hwnd))
                {
                    _hwnd = FindUnityWindow();
                    if (_hwnd == IntPtr.Zero)
                    {
                        UnityEngine.Debug.LogError("[WindowOverlay] 重建时无法找到窗口");
                        _applied = false;
                        _suspended = false;
                        return;
                    }
                }
                ApplyNow(); // 重建 DWM 玻璃层
                _suspended = false;

                // ★ 恢复 DesktopPet 运行（只恢复暂停，不强制重置落地状态）
                //   注意：DesktopPet.OnApplicationPause(false) 或 OnResumeFromSleep
                //   已经（或即将）处理 onGround=false 和行走任务链。
                //   这里如果强制 onGround=false 会打断已经开始的行走，导致唤醒后不走动。
                DesktopPet pet = GetComponent<DesktopPet>();
                if (pet != null)
                {
                    pet.isPaused = false;
                    UnityEngine.Debug.Log("[WindowOverlay] ✅ 已恢复 DesktopPet 运行");
                }

                UnityEngine.Debug.Log("[WindowOverlay] ✅ 唤醒恢复完成，Win32 调用已恢复");
            });
        }
        else
        {
            _rebuildPending = false;
            _applied = false;
            _suspended = false;
        }
    }

    /// <summary>
    /// 外部调用的唤醒恢复入口（由时间间隙检测触发，弥补 OnApplicationPause 不可靠问题）。
    /// 委托给 RebuildAfterDelay 做延迟重建。
    /// </summary>
    public void OnResumeFromSleep()
    {
        if (!_suspended) return; // 可能已被 OnApplicationPause 处理过

        // ★ 安全模式：连续崩溃后跳过 DWM 玻璃层重建（窗口黑底但不崩系统）
        if (UnityEngine.PlayerPrefs.GetString("_skip_dwm_rebuild", "") == "1")
        {
            UnityEngine.Debug.Log("[WindowOverlay] 🛡 安全模式：跳过 DWM 玻璃层重建");
            _suspended = false;
            return;
        }

        UnityEngine.Debug.Log("[WindowOverlay] ▶ 时间间隙触发唤醒恢复，委托 RebuildAfterDelay");
        RebuildAfterDelay();
    }

    /// <summary>
    /// 协程延迟执行（Unity 主线程安全）
    /// </summary>
    private void RunAfterDelay(float seconds, Action action)
    {
        StartCoroutine(_DelayedAction(seconds, action));
    }

    private System.Collections.IEnumerator _DelayedAction(float seconds, Action action)
    {
        yield return new UnityEngine.WaitForSecondsRealtime(seconds);
        action?.Invoke();
    }

    private void Log(string msg)
    {
        if (debugLog)
            UnityEngine.Debug.Log($"[WindowOverlay] {msg}");
    }

    private void LogError(string msg)
    {
        UnityEngine.Debug.LogError($"[WindowOverlay] {msg}");
    }

    /// <summary>
    /// 销毁时清理收纳盘拖放接收（还原窗口过程）
    /// </summary>
    private void OnDestroy()
    {
        DockDropHandler.Shutdown();
    }
}
