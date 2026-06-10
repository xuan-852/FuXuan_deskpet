using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 桌面宠物 — 窗口透明化与置顶管理
/// 
/// 功能：
/// 1. 移除窗口边框与标题栏
/// 2. 窗口始终置顶 (WS_EX_TOPMOST)
/// 3. 色键透明：将 #00FF00 亮绿色背景挖空
/// 4. 点击穿透：鼠标事件透过窗口传递到桌面 (WS_EX_TRANSPARENT)
/// 5. 隐藏任务栏图标 (WS_EX_TOOLWINDOW)
/// 
/// 用法：
/// 1. 将此脚本挂到场景中任意 GameObject 上
/// 2. 设置 Main Camera 的 Background 为 #00FF00 (Alpha=255)
/// 3. 构建运行即可看到透明窗口
/// 
/// 注意：仅在 Windows 平台下生效，编辑器模式下无效。
/// </summary>
public class WindowOverlay : MonoBehaviour
{
    #region Win32 API 声明

    [Flags]
    private enum SetWindowPosFlags : uint
    {
        SWP_NOSIZE = 0x0001,
        SWP_NOMOVE = 0x0002,
        SWP_NOZORDER = 0x0004,
        SWP_NOACTIVATE = 0x0010,
        SWP_SHOWWINDOW = 0x0040,
        SWP_FRAMECHANGED = 0x0020,
    }

    private enum WindowLongFlags : int
    {
        GWL_STYLE = -16,
        GWL_EXSTYLE = -20,
    }

    // 窗口样式
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CAPTION = 0x00C00000;   // 标题栏
    private const uint WS_THICKFRAME = 0x00040000; // 可调整边框
    private const uint WS_SYSMENU = 0x00080000;
    private const uint WS_MINIMIZEBOX = 0x00020000;
    private const uint WS_MAXIMIZEBOX = 0x00010000;

    // 扩展样式
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_APPWINDOW = 0x00040000;

    // 分层窗口属性
    private const uint LWA_COLORKEY = 0x00000001;
    private const uint LWA_ALPHA = 0x00000002;

    // 移除边框的样式掩码
    private const uint WS_BORDER_MASK = WS_CAPTION | WS_THICKFRAME | WS_SYSMENU |
                                        WS_MINIMIZEBOX | WS_MAXIMIZEBOX;

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
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd,
        uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth,
        int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // 用于置顶的常量句柄
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;

    #endregion

    #region 配置参数

    [Header("透明窗口设置")]
    [Tooltip("屏幕宽度（像素），0 表示使用当前窗口宽度")]
    public int windowWidth = 512;

    [Tooltip("屏幕高度（像素），0 表示使用当前窗口高度")]
    public int windowHeight = 512;

    [Tooltip("色键颜色 (RGB)。默认 #00FF00 (亮绿) 将被设为透明")]
    public Color32 colorKey = new Color32(0, 255, 0, 255);

    [Tooltip("透明窗口启用后才禁用点击穿透，允许鼠标点击")]
    public bool enableClickThrough = true;

    [Tooltip("是否隐藏任务栏图标")]
    public bool hideFromTaskbar = true;

    [Tooltip("启动时自动设置窗口透明")]
    public bool applyOnStart = true;

    [Tooltip("置顶检查间隔（秒），小于等于0则不检查")]
    public float topmostCheckInterval = 1.0f;

    [Header("调试")]
    [Tooltip("显示调试日志")]
    public bool debugLog = false;

    #endregion

    #region 内部状态

    private IntPtr _hwnd = IntPtr.Zero;
    private bool _isApplied = false;
    private float _topmostTimer = 0f;

    // 保存原窗口位置用于闪屏恢复
    private int _origX = 0;
    private int _origY = 0;
    private int _origW = 0;
    private int _origH = 0;

    #endregion

    #region Unity 生命周期

    private void Start()
    {
        if (!Application.isEditor && applyOnStart)
        {
            ApplyWindowSettings();
        }
        else if (Application.isEditor)
        {
            Log("编辑器模式：透明窗口仅在构建后生效");
        }
    }

    private void Update()
    {
        if (_hwnd == IntPtr.Zero || !_isApplied)
            return;

        // 定期检查置顶状态，防止被其他窗口遮挡
        if (topmostCheckInterval > 0f)
        {
            _topmostTimer += Time.deltaTime;
            if (_topmostTimer >= topmostCheckInterval)
            {
                _topmostTimer = 0f;
                RefreshTopmost();
            }
        }
    }

    private void OnApplicationQuit()
    {
        RestoreWindow();
    }

    #endregion

    #region 公开方法

    /// <summary>
    /// 手动触发窗口透明化设置
    /// </summary>
    public void ApplyWindowSettings()
    {
        FindUnityWindow();
        if (_hwnd == IntPtr.Zero)
        {
            LogError("未找到 Unity 窗口句柄");
            return;
        }
        SetupTransparentWindow();
    }

    /// <summary>
    /// 恢复窗口为正常状态
    /// </summary>
    public void RestoreWindow()
    {
        if (_hwnd == IntPtr.Zero || !_isApplied)
            return;

        Log("恢复窗口样式...");
        // 恢复原有样式
        uint oldStyle = GetWindowLong(_hwnd, (int)WindowLongFlags.GWL_STYLE);
        uint oldExStyle = GetWindowLong(_hwnd, (int)WindowLongFlags.GWL_EXSTYLE);

        // 恢复标题栏边框
        oldStyle |= WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX;
        SetWindowLong(_hwnd, (int)WindowLongFlags.GWL_STYLE, oldStyle);

        // 移除透明相关样式
        oldExStyle &= ~(WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
        SetWindowLong(_hwnd, (int)WindowLongFlags.GWL_EXSTYLE, oldExStyle);

        // 恢复原有窗口位置大小
        SetWindowPos(_hwnd, HWND_NOTOPMOST, _origX, _origY, _origW, _origH,
            (uint)(SetWindowPosFlags.SWP_FRAMECHANGED | SetWindowPosFlags.SWP_NOACTIVATE |
                   SetWindowPosFlags.SWP_SHOWWINDOW));

        _isApplied = false;
        Log("窗口已恢复");
    }

    /// <summary>
    /// 切换点击穿透模式
    /// </summary>
    public void SetClickThrough(bool enabled)
    {
        if (_hwnd == IntPtr.Zero || !_isApplied)
            return;

        uint exStyle = GetWindowLong(_hwnd, (int)WindowLongFlags.GWL_EXSTYLE);
        if (enabled)
        {
            exStyle |= WS_EX_TRANSPARENT;
        }
        else
        {
            exStyle &= ~WS_EX_TRANSPARENT;
        }
        SetWindowLong(_hwnd, (int)WindowLongFlags.GWL_EXSTYLE, exStyle);
        enableClickThrough = enabled;
        Log($"点击穿透: {(enabled ? "开启" : "关闭")}");
    }

    #endregion

    #region 内部实现

    /// <summary>
    /// 查找 Unity 主窗口句柄
    /// </summary>
    private void FindUnityWindow()
    {
        string title = Application.productName;
        Log($"查找窗口: {title}");

        _hwnd = FindWindow(null, title);

        if (_hwnd == IntPtr.Zero)
        {
            // 尝试通过进程查找
            try
            {
                Process currentProcess = Process.GetCurrentProcess();
                string processName = currentProcess.ProcessName;
                Log($"通过进程名查找: {processName}");
                // 给 Unity 一点时间更新窗口标题
                _hwnd = FindWindow(null, title);
            }
            catch (Exception e)
            {
                LogError($"查找窗口异常: {e.Message}");
            }
        }

        if (_hwnd != IntPtr.Zero)
        {
            Log($"找到窗口句柄: {_hwnd.ToInt64():X8}");

            // 记录原始窗口状态
            if (GetWindowRect(_hwnd, out RECT rect))
            {
                _origX = rect.Left;
                _origY = rect.Top;
                _origW = rect.Right - rect.Left;
                _origH = rect.Bottom - rect.Top;
                Log($"原始窗口: {_origW}x{_origH} @ ({_origX},{_origY})");
            }
        }
    }

    /// <summary>
    /// 应用透明窗口设置
    /// </summary>
    private void SetupTransparentWindow()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        Log("应用透明窗口设置...");

        // 1. 移除标题栏、边框、系统菜单等
        uint style = GetWindowLong(_hwnd, (int)WindowLongFlags.GWL_STYLE);
        style &= ~WS_BORDER_MASK;
        style |= WS_POPUP | WS_VISIBLE;
        SetWindowLong(_hwnd, (int)WindowLongFlags.GWL_STYLE, style);

        // 2. 设置扩展样式：分层 + 置顶 + 点透 + 隐藏任务栏
        uint exStyle = GetWindowLong(_hwnd, (int)WindowLongFlags.GWL_EXSTYLE);
        exStyle |= WS_EX_LAYERED | WS_EX_TOPMOST;
        exStyle &= ~WS_EX_APPWINDOW; // 避免出现在任务栏

        if (hideFromTaskbar)
        {
            exStyle |= WS_EX_TOOLWINDOW;
        }

        if (enableClickThrough)
        {
            exStyle |= WS_EX_TRANSPARENT;
        }

        SetWindowLong(_hwnd, (int)WindowLongFlags.GWL_EXSTYLE, exStyle);

        // 3. 应用色键透明 — 亮点：提取 Color32 转 RGB
        uint colorKeyRGB = (uint)(colorKey.r << 0)  |  // R
                           (uint)(colorKey.g << 8)  |  // G
                           (uint)(colorKey.b << 16);   // B
        SetLayeredWindowAttributes(_hwnd, colorKeyRGB, 0, LWA_COLORKEY);

        // 4. 设置窗口大小
        int w = windowWidth > 0 ? windowWidth : _origW;
        int h = windowHeight > 0 ? windowHeight : _origH;
        SetWindowPos(_hwnd, HWND_TOPMOST, _origX, _origY, w, h,
            (uint)(SetWindowPosFlags.SWP_FRAMECHANGED | SetWindowPosFlags.SWP_SHOWWINDOW));

        _isApplied = true;
        Log($"透明窗口已就绪: {w}x{h} @ ({_origX},{_origY}), 色键: #{colorKey.r:X2}{colorKey.g:X2}{colorKey.b:X2}");
    }

    /// <summary>
    /// 刷新置顶状态
    /// </summary>
    private void RefreshTopmost()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        // 检查是否仍在最前
        IntPtr foreground = GetForegroundWindow();
        if (foreground != _hwnd)
        {
            // 检查是否被某些置顶窗口挡住了
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                (uint)(SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE |
                       SetWindowPosFlags.SWP_NOACTIVATE));
        }
    }

    /// <summary>
    /// 移动窗口到指定位置
    /// </summary>
    public void MoveTo(int x, int y)
    {
        if (_hwnd == IntPtr.Zero || !_isApplied)
            return;

        int w = windowWidth > 0 ? windowWidth : _origW;
        int h = windowHeight > 0 ? windowHeight : _origH;
        MoveWindow(_hwnd, x, y, w, h, true);
    }

    #endregion

    #region 辅助

    private void Log(string msg)
    {
        if (debugLog)
            UnityEngine.Debug.Log($"[WindowOverlay] {msg}");
    }

    private void LogError(string msg)
    {
        UnityEngine.Debug.LogError($"[WindowOverlay] {msg}");
    }

    #endregion
}
