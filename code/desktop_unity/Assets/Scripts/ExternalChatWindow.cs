using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

/// <summary>
/// 聊天独立窗口（2026-08-15 大工程 Phase 1）— QQ 式普通窗口，桌宠本体保持置顶不变。
///
/// 架构：原生 Win32 窗口（非置顶、可被遮挡、可拖动）运行在后台线程；
/// Unity 把聊天面板渲染成 BGRA 像素流（RenderTexture → ReadPixels）经 SetBuffer 推给窗口显示；
/// 输入用**原生 EDIT 控件**（可靠，不注入 IMGUI 键盘事件）：底部输入框 + 发送按钮，
/// Enter/按钮 → 回调 → MainThreadDispatcher 送回 Unity 主线程 → ChatManager。
///
/// 渲染为纯显示（聊天历史），输入为原生控件——Phase 1 闭环"独立窗口显示 + 可输入发送"。
/// </summary>
public static class ExternalChatWindow
{
    // ─── 用户事件（Unity 侧订阅） ───
    /// <summary>用户在独立窗口点「发送」（主线程回调）</summary>
    public static event Action<string> OnSendText;
    /// <summary>用户点窗口 ✕（主线程回调，用于收起面板）</summary>
    public static event Action OnClosed;
    /// <summary>用户在面板区点击（主线程回调，坐标=客户区/面板内坐标，双击标志）</summary>
    public static event Action<float, float, bool> OnPanelClick;
    /// <summary>外置窗口鼠标移动（坐标=客户区/面板内坐标；离开窗口时为负值）</summary>
    public static event Action<float, float> OnPanelMouseMove;

    // ─── 状态 ───
    public static bool IsCreated { get; private set; }
    public static bool IsVisible { get; private set; }
    private static int _width = 640, _height = 480;
    private static int _startX = 200, _startY = 200;
    private static bool _posRestored;
    private static string PosPrefKey => "ExtPanel_Pos_" + UnityEngine.Screen.width + "x" + UnityEngine.Screen.height;

    // ─── 像素缓冲（Unity → 窗口线程） ───
    private static readonly object _bufLock = new object();
    private static byte[] _buffer;       // BGRA32
    private static int _bufW, _bufH;

    // ─── Win32 常量 ───
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_TABSTOP = 0x00010000;
    private const uint WS_BORDER = 0x00800000;
    private const uint ES_AUTOHSCROLL = 0x0080;
    private const uint WS_EX_CLIENTEDGE = 0x00000200;
    private const uint WS_EX_TOOLWINDOW = 0x00000080; // 无任务栏按钮（面板非主窗口）
    private const uint CS_DBLCLKS = 0x0008; // 窗口类接收 WM_*BUTTONDBLCLK
    private const int WM_DESTROY = 0x0002;
    private const int WM_PAINT = 0x000F;
    private const int WM_SIZE = 0x0005;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KILLFOCUS = 0x0008;
    private const int WM_COMMAND = 0x0111;
    private const int WM_CLOSE = 0x0010;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_MOUSELEAVE = 0x02A3;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_CTLCOLOREDIT = 0x0133;
    private const int WM_SETCURSOR = 0x0020;
    private const int WM_APP_FOCUS_INPUT = 0x8000 + 1; // 自定义：请求窗口线程聚焦输入框
    private const int WM_APP_SHUTDOWN = 0x8000 + 2;    // 自定义：由窗口线程自己销毁窗口并退出消息循环
    private const int HTCAPTION = 2;
    private const int HTCLIENT = 1;
    private const int HTBOTTOMRIGHT = 17;
    private const int HTNOWHERE = 0;
    private const int VK_RETURN = 0x0D;
    private const int BN_CLICKED = 0;
    private const int IDC_EDIT = 101;
    private const int IDC_SEND = 102;
    private const int IDC_ARROW = 32512;
    private const int IDC_IBEAM = 32513;

    // ★ 无边框窗口：使用面板自身标题行作为拖动带，不再额外绘制“独立面板”标题栏。
    public const int TITLE_BAR_H = 54;
    // ★ 右下角缩放手柄尺寸（逻辑像素）
    private const int RESIZE_GRIP = 20;
    // ★ 右上角按钮区宽度（逻辑像素，最小化/关闭按钮统一命中区）
    public const int BTN_AREA_W = 68;

    private static IntPtr _hwnd, _edit, _sendBtn, _hInst;
    private static IntPtr _arrowCursor, _ibeamCursor;
    private static volatile bool _inputFocusActive;
    private static bool _closeNotificationSent;
    private static WndProcDelegate _wndProcDelegate; // 防止被 GC
    private static EditWndProcDelegate _editWndProcDelegate; // 防止被 GC（EDIT 子类化）
    private static IntPtr _origEditProc;             // 原 EDIT 窗口过程
    private static Thread _windowThread;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr EditWndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASS
    {
        public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern int GetMessageW(ref MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string name);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr cursorName);
    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowTextW(IntPtr hWnd, string text);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmdShow);
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("user32.dll")]
    private static extern bool ValidateRect(IntPtr hWnd, IntPtr rect);
    [DllImport("gdi32.dll")]
    private static extern int SetDIBitsToDevice(IntPtr hdc, int xDest, int yDest, int w, int h,
        int xSrc, int ySrc, int startScan, int scanLines, byte[] bits, ref BITMAPINFO bmi, uint colorUse);
    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT tme);
    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProcW(IntPtr prevWndProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    // ★ 2026-08-17 修复：32 位进程没有 SetWindowLongPtrW/GetWindowLongPtrW（EntryPointNotFoundException 杀窗口线程）。
    //   统一用 SetWindowLongW/GetWindowLongW（32 位下与指针同宽，兼容 x86 构建）。
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int fnObject);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    private static extern bool AdjustWindowRectEx(ref RECT rect, uint style, bool menu, uint exStyle);
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int GWLP_WNDPROC = -4;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>保存窗口位置（WM_EXITSIZEMOVE / 关闭时）</summary>
    private static void SavePos()
    {
        if (_hwnd == IntPtr.Zero) return;
        RECT r;
        if (GetWindowRect(_hwnd, out r))
            UnityEngine.PlayerPrefs.SetString(PosPrefKey, $"{r.Left},{r.Top}");
    }

    // ══════════════════════════════════════════════════════════════
    //  DPI 坐标统一转换（2026-08-17 验收 P1-1 修复）
    //  所有「物理像素 ⇄ 面板逻辑像素」换算只走这里，禁止在 WndProc 各处
    //  分别计算缩放比例（144 DPI 下 GetWindowRect/GetClientRect/lParam
    //  返回值尺度不一致曾导致按钮/输入区命中错位）。
    //  约定：_width/_height = 面板逻辑尺寸（客户区 1:1）；窗口物理像素
    //  = 逻辑 × DPI 比例。lParam 鼠标坐标为物理像素。
    // ══════════════════════════════════════════════════════════════

    /// <summary>物理客户区坐标 → 面板逻辑坐标（鼠标消息统一入口）</summary>
    private static void ClientToLogical(int physX, int physY, out float lx, out float ly)
    {
        RECT cr;
        GetClientRect(_hwnd, out cr);
        int cw = Math.Max(1, cr.Right - cr.Left);
        int ch = Math.Max(1, cr.Bottom - cr.Top);
        lx = physX * (float)_width / cw;
        ly = physY * (float)_height / ch;
    }

    /// <summary>面板逻辑坐标 → 物理客户区坐标（原生控件布局统一入口）</summary>
    private static void LogicalToClient(float lx, float ly, out int physX, out int physY)
    {
        RECT cr;
        GetClientRect(_hwnd, out cr);
        int cw = Math.Max(1, cr.Right - cr.Left);
        int ch = Math.Max(1, cr.Bottom - cr.Top);
        physX = (int)(lx * cw / (float)_width);
        physY = (int)(ly * ch / (float)_height);
    }

    /// <summary>面板逻辑尺寸 → 物理客户区尺寸（原生控件宽高统一入口）</summary>
    private static void LogicalToClientSize(float lw, float lh, out int physW, out int physH)
    {
        RECT cr;
        GetClientRect(_hwnd, out cr);
        int cw = Math.Max(1, cr.Right - cr.Left);
        int ch = Math.Max(1, cr.Bottom - cr.Top);
        physW = Math.Max(1, (int)(lw * cw / (float)_width));
        physH = Math.Max(1, (int)(lh * ch / (float)_height));
    }

    /// <summary>诊断：打印当前窗口/客户区/DPI 关系（144 DPI 验收用）</summary>
    private static void LogDpiDiagnostics(string tag)
    {
        if (_hwnd == IntPtr.Zero) return;
        RECT wr, cr;
        GetWindowRect(_hwnd, out wr);
        GetClientRect(_hwnd, out cr);
        float scaleX = (cr.Right - cr.Left) > 0 ? (float)_width / (cr.Right - cr.Left) : 1f;
        float scaleY = (cr.Bottom - cr.Top) > 0 ? (float)_height / (cr.Bottom - cr.Top) : 1f;
        Debug.Log($"[ExternalChat] {tag} 窗口物理=({wr.Right - wr.Left}x{wr.Bottom - wr.Top}) " +
                  $"客户区物理=({cr.Right - cr.Left}x{cr.Bottom - cr.Top}) " +
                  $"逻辑={_width}x{_height} 比例=({scaleX:F3},{scaleY:F3})");
    }

    private static System.ValueTuple<int, int>? GetSavedPos()
    {
        string s = UnityEngine.PlayerPrefs.GetString(PosPrefKey, "");
        if (string.IsNullOrEmpty(s)) return null;
        var parts = s.Split(',');
        if (parts.Length != 2) return null;
        int x, y;
        if (int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y))
        {
            // 屏幕外校正：至少保留 40px 可见
            int sw = UnityEngine.Screen.width, sh = UnityEngine.Screen.height;
            x = Mathf.Clamp(x, -_width + 40, Mathf.Max(sw - 40, 40));
            y = Mathf.Clamp(y, -_height + 40, Mathf.Max(sh - 40, 40));
            return (x, y);
        }
        return null;
    }

    // ──────────────────────────────────────────────
    //  生命周期（Unity 侧调用）
    // ──────────────────────────────────────────────

    /// <summary>创建窗口线程与原生窗口（幂等）</summary>
    public static void EnsureCreated()
    {
        if (IsCreated) return;
        _windowThread = new Thread(WindowThreadMain) { IsBackground = true, Name = "FuXuanChatWindow" };
        _windowThread.SetApartmentState(ApartmentState.STA);
        _windowThread.Start();
        // 等待创建完成
        for (int i = 0; i < 200 && !IsCreated; i++) Thread.Sleep(10);
        if (!IsCreated) Debug.LogError("[ExternalChat] 窗口线程创建超时");
    }

    /// <summary>显示窗口（若未创建则先创建）</summary>
    public static void Show(int width, int height)
    {
        // ★ 先定尺寸再创建：EnsureCreated 的窗口线程按当前 _width/_height 建窗，
        //   若后赋值会丢失高度（输入栏 44px 被截掉）
        _width = Mathf.Max(320, width);
        _height = Mathf.Max(200, height);
        EnsureCreated();
        if (IsCreated && !IsVisible)
        {
            // 恢复记忆位置（仅首次显示）
            if (!_posRestored)
            {
                _posRestored = true;
                var saved = GetSavedPos();
                if (saved != null)
                {
                    _startX = saved.Value.Item1;
                    _startY = saved.Value.Item2;
                }
            }
            // 应用客户区尺寸（含边框补偿）
            ApplyClientSize(_width, _height);
            PostMessageW(_hwnd, WM_SIZE, IntPtr.Zero, IntPtr.Zero); // 触发布局
            ShowWindow(_hwnd, 5 /*SW_SHOW*/);
            _closeNotificationSent = false;
            IsVisible = true;
        }
    }

    /// <summary>运行期调整客户区尺寸（Unity 侧面板尺寸变化时调用，含边框补偿）</summary>
    public static void SetSize(int clientW, int clientH)
    {
        _width = Mathf.Max(320, clientW);
        _height = Mathf.Max(200, clientH);
        if (IsCreated && IsVisible)
            ApplyClientSize(_width, _height);
    }

    /// <summary>把客户区尺寸换算成窗口尺寸并 SetWindowPos（无边框：客户区=窗口区，RT 1:1）</summary>
    private static void ApplyClientSize(int clientW, int clientH)
    {
        if (_hwnd == IntPtr.Zero || !IsWindow(_hwnd)) return;
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, clientW, clientH,
            SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>关闭窗口线程（Unity OnDestroy 时调用；★ 优雅退出避免窗口线程与引擎
    ///   D3D 设备销毁竞态 → destroyTJDevice 崩溃）</summary>
    public static void Shutdown()
    {
        if (!IsCreated) return;
        // DestroyWindow 必须由创建该窗口的线程调用。此前 Unity 主线程直接调用
        // DestroyWindow，窗口线程仍可能在 WM_PAINT 访问像素缓冲，存在退出竞态，
        // 也是 destroyTJDevice 崩溃风险的一部分。改为投递自定义消息，让窗口线程
        // 在自己的消息循环中销毁窗口并 PostQuitMessage。
        IntPtr hwnd = _hwnd;
        if (hwnd != IntPtr.Zero)
        {
            PostMessageW(hwnd, WM_APP_SHUTDOWN, IntPtr.Zero, IntPtr.Zero);
        }
        // 等窗口线程退出（最多 1s）；不要在超时后强行伪造 IsCreated=false，
        // 否则仍在运行的窗口线程会继续使用已释放的 Unity 资源。
        if (_windowThread != null && _windowThread != Thread.CurrentThread)
            _windowThread.Join(1000);
        if (IsCreated)
            Debug.LogWarning("[ExternalChat] 窗口线程退出超时，保留线程状态等待自然退出");
    }

    /// <summary>隐藏窗口</summary>
    public static void Hide()
    {
        if (IsCreated && _hwnd != IntPtr.Zero)
        {
            ShowWindow(_hwnd, 0 /*SW_HIDE*/);
            IsVisible = false;
            _inputFocusActive = false;
        }
    }

    /// <summary>最小化窗口（自绘标题栏「—」按钮调用）</summary>
    public static void Minimize()
    {
        if (IsCreated && IsVisible)
            ShowWindow(_hwnd, 6 /*SW_MINIMIZE*/);
    }

    /// <summary>关闭窗口（自绘标题栏「✕」按钮调用 → 隐藏 + 通知 Unity 退出外置）</summary>
    public static void RequestClose()
    {
        if (!IsCreated) return;
        PostMessageW(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>把聊天面板渲染的 BGRA 像素推给窗口显示（Unity 主线程调用，节流后）</summary>
    public static void SetBuffer(byte[] bgra, int w, int h)
    {
        if (!IsCreated || !IsVisible) return;
        lock (_bufLock)
        {
            int need = w * h * 4;
            if (_buffer == null || _buffer.Length != need) _buffer = new byte[need];
            Buffer.BlockCopy(bgra, 0, _buffer, 0, need);
            _bufW = w; _bufH = h;
        }
        InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    /// <summary>把 BGRA 像素推给窗口显示（AsyncGPUReadback 回调版，NativeArray 输入）
    /// ★ 消除 ToArray 高频大分配（6.7MB/次 → GC 停顿卡顿）：预分配 _buffer + CopyTo 零分配</summary>
    public static void SetBuffer(Unity.Collections.NativeArray<byte> bgra, int w, int h)
    {
        if (!IsCreated || !IsVisible) return;
        lock (_bufLock)
        {
            int need = w * h * 4;
            if (_buffer == null || _buffer.Length != need) _buffer = new byte[need];
            // NativeArray → 托管数组：CopyTo 目标已预分配，零新分配
            bgra.CopyTo(_buffer);
            _bufW = w; _bufH = h;
        }
        InvalidateRect(_hwnd, IntPtr.Zero, false);
    }

    /// <summary>向输入框追加文本（主线程调用，用于测试注入）</summary>
    public static void SetInputText(string text)
    {
        if (IsCreated) SetWindowTextW(_edit, text);
    }

    // ──────────────────────────────────────────────
    //  窗口线程
    // ──────────────────────────────────────────────

    private static void WindowThreadMain()
    {
        try
        {
            _hInst = GetModuleHandleW(null);
            var wc = new WNDCLASS
            {
                // 必须注册双击类样式，否则 Windows 只派发两次单击，永远不会产生
                // WM_LBUTTONDBLCLK，会话列表无法实现“单击保留、双击进入聊天”。
                style = CS_DBLCLKS,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate = WndProc),
                hInstance = _hInst,
                hCursor = _arrowCursor = LoadCursorW(IntPtr.Zero, new IntPtr(IDC_ARROW)),
                lpszClassName = "FuXuanChatWindowClass"
            };
            _ibeamCursor = LoadCursorW(IntPtr.Zero, new IntPtr(IDC_IBEAM));
            if (RegisterClassW(ref wc) == 0)
            {
                Debug.LogError("[ExternalChat] RegisterClassW 失败");
                return;
            }

            _hwnd = CreateWindowExW(WS_EX_TOOLWINDOW, "FuXuanChatWindowClass", "符玄 · 太卜司",
                WS_POPUP | WS_VISIBLE, _startX, _startY, _width, _height, IntPtr.Zero, IntPtr.Zero, _hInst, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                Debug.LogError("[ExternalChat] CreateWindowExW 失败");
                return;
            }
            // 无边框窗口：客户区 = 窗口区（WS_POPUP 无系统边框），直接 SetWindowPos 定尺寸
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, _width, _height, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);

            // 原生输入控件 — ★ 透明样式（无白底/无边框，覆盖在 IMGUI 星空输入框位置作为隐形输入通道；
            //   用户反馈白框输入框突兀 + 原生控件遮挡点击）。位置由 SetInputRect 从 Unity 侧同步。
            // EDIT 仅作为键盘输入通道；发送按钮不参与交互，输入通道自身也使用
            // 透明扩展样式，避免原生控件背景覆盖 Unity 绘制的输入栏。
            _edit = CreateWindowExW(0x00000020 /* WS_EX_TRANSPARENT */, "EDIT", "",
                WS_CHILD | WS_TABSTOP | ES_AUTOHSCROLL,
                8, 0, 100, 30, _hwnd, (IntPtr)IDC_EDIT, _hInst, IntPtr.Zero);
            _sendBtn = CreateWindowExW(0, "BUTTON", "发送", WS_CHILD | WS_TABSTOP,
                8, 0, 68, 30, _hwnd, (IntPtr)IDC_SEND, _hInst, IntPtr.Zero);
            // ★ 2026-08-17 修复回车发送：子类化 EDIT——真实用户按键时焦点在 edit，
            //   WM_KEYDOWN 由 edit 默认过程消费，不会冒泡到父窗口；子类化拦截 VK_RETURN → DoSend。
            _editWndProcDelegate = EditProc;
            // ★ SetWindowLongW 返回值 = 原窗口过程指针（32 位 int 位宽足够）；GetWindowLong 是 int 版
            _origEditProc = SetWindowLong(_edit, GWLP_WNDPROC,
                Marshal.GetFunctionPointerForDelegate(_editWndProcDelegate));
            if (_origEditProc == IntPtr.Zero)
                Debug.LogWarning($"[ExternalChat] EDIT 子类化失败（SetWindowLongW 返回 0，lastError={Marshal.GetLastWin32Error()}）—— 回车发送将不可用");
            else
                Debug.Log($"[ExternalChat] EDIT 子类化成功 原过程=0x{_origEditProc.ToInt64():X}");
            ShowWindow(_edit, 0); // 默认隐藏，点击输入框区域才显示（透明覆盖）
            HideNativeSendButton();

            IsCreated = true;
            Debug.Log("[ExternalChat] 独立窗口已创建");
            LogDpiDiagnostics("创建时");

            // 消息循环
            MSG msg = new MSG();
            while (GetMessageW(ref msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
            IsCreated = false;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ExternalChat] 窗口线程异常: {e}");
            IsCreated = false;
        }
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
            {
                // 无边框窗口命中测试：顶部标题栏→拖动(HTCAPTION)，右下角→缩放(HTBOTTOMRIGHT)
                // ★ 统一 DPI：屏幕物理坐标 → 客户区物理 → 逻辑（只走 ClientToLogical）
                // ★ 2026-08-17 带符号修复：lParam 屏幕坐标可为负（窗口拖到屏幕左/上缘外），
                //   & 0xFFFF 会把负坐标变成 65536-|x| 的大正数 → 命中判定错乱（拖动时灵时不灵根因）。
                int sx = (short)(lParam.ToInt32() & 0xFFFF);
                int sy = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                RECT wr;
                if (GetWindowRect(hWnd, out wr))
                {
                    int cx = sx - wr.Left;   // 窗口内物理坐标（客户区=窗口区，无边框）
                    int cy = sy - wr.Top;
                    float lx, ly;
                    ClientToLogical(cx, cy, out lx, out ly);
                    // 右下角缩放手柄（逻辑 20px 区）
                    if (lx >= _width - RESIZE_GRIP && ly >= _height - RESIZE_GRIP)
                        return new IntPtr(HTBOTTOMRIGHT);
                    // 顶部标题栏（逻辑 44px 区，全宽）
                    if (ly <= TITLE_BAR_H)
                    {
                        // 面板标题行上的返回、字体、最小化、关闭按钮必须交给 Unity 命中表；
                        // 中间空白区域继续作为原生拖动带。
                        if (lx < 54 || lx >= _width - 220)
                            return new IntPtr(HTCLIENT);
                        return new IntPtr(HTCAPTION);
                    }
                }
                return new IntPtr(HTCLIENT);
            }
            case WM_APP_FOCUS_INPUT:
            {
                // 输入聚焦状态机（2026-08-17 验收 P1 修复，codex 建议 5.2）：
                //   hit(Unity 命中) → shown(控件显示) → focused(SetFocus) → send(回车/按钮)
                //   每步留痕，144 DPI 验收可直接看日志定位断点。
                ShowWindow(_edit, 5);
                // 发送按钮由 Unity 位图和外置命中表绘制/处理；不要显示原生 BUTTON，
                // 否则它会以黑色控件覆盖输入栏右侧。
                HideNativeSendButton();
                LayoutChildren();
                MoveEditOffscreen();
                SetFocus(_edit);
                _inputFocusActive = true;
                LogInputState("focused");
                return IntPtr.Zero;
            }
            case WM_APP_SHUTDOWN:
                // 该消息只由 Shutdown 投递，当前 WndProc 就运行在窗口创建线程上。
                ShowWindow(hWnd, 0 /*SW_HIDE*/);
                IsVisible = false;
                DestroyWindow(hWnd);
                return IntPtr.Zero;
            case WM_CLOSE:
                // ✕ = 隐藏（窗口生命周期归 Unity 管），先记忆位置
                SavePos();
                ShowWindow(hWnd, 0);
                IsVisible = false;
                _inputFocusActive = false;
                NotifyClosedOnce();
                return IntPtr.Zero;
            case WM_SETCURSOR:
            {
                // 透明且移出屏幕的 EDIT 聚焦后不能提供系统光标，明确设置箭头/I-beam，避免鼠标消失。
                IntPtr cursor = _arrowCursor;
                if (_inputFocusActive && _inputRectSet && GetCursorPos(out POINT screen) && GetWindowRect(hWnd, out RECT window))
                {
                    int px = screen.X - window.Left;
                    int py = screen.Y - window.Top;
                    if (px >= _inputX && px < _inputX + _inputW
                        && py >= _inputY && py < _inputY + _inputH)
                        cursor = _ibeamCursor;
                }
                SetCursor(cursor != IntPtr.Zero ? cursor : LoadCursorW(IntPtr.Zero, new IntPtr(IDC_ARROW)));
                return new IntPtr(1);
            }
            case WM_COMMAND:
                if (wParam.ToInt32() == IDC_SEND) { DoSend(); return IntPtr.Zero; }
                break;
            case WM_KEYDOWN:
                // ★ 2026-08-17 修复：回车的实际处理在 EDIT 子类化过程（EditProc）里拦截——
                //   真实用户按键焦点在 edit，WM_KEYDOWN 发给 edit 不会冒泡到父窗口 WndProc。
                //   这里仅保留父窗口兜底（PostMessage 直接发给父窗口的场景，如测试注入）。
                if (wParam.ToInt32() == VK_RETURN && GetFocus() == _edit) { DoSend(); return IntPtr.Zero; }
                break;
            case WM_NCLBUTTONDOWN:
            {
                // ★ 标题栏拖动：显式转发 DefWindowProc 启动 MoveLoop。
                //   无边框窗口必须处理此消息，否则系统可能不进入拖动模式
                //   （用户实测「拖动着拖不动了」）。
                if (wParam.ToInt32() == HTCAPTION || wParam.ToInt32() == HTBOTTOMRIGHT)
                    return DefWindowProcW(hWnd, msg, wParam, lParam);
                break;
            }
            case WM_LBUTTONDOWN:
            case WM_LBUTTONDBLCLK:
                // 面板区点击 → 物理客户区坐标 → 逻辑面板坐标（统一 DPI 转换）→ 主线程命中表
            {
                // ★ 带符号解析（客户区坐标通常非负，但保持与 WM_NCHITTEST 一致防御负值）
                int x = (short)(lParam.ToInt32() & 0xFFFF);
                int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                if (x < 0) x = 0;
                if (y < 0) y = 0;
                float lx, ly;
                ClientToLogical(x, y, out lx, out ly);
                bool dbl = msg == WM_LBUTTONDBLCLK;
                // ★ 标题栏点击一律走命中表（含最小化/关闭按钮）——WM_NCHITTEST 已返回
                //   HTCAPTION 的区域系统会走 WM_NCLBUTTONDOWN 拖动，到这里的都是
                //   HTCLIENT 区域；不做拖动兜底（兜底会吞掉标题栏按钮点击）
                MainThreadDispatcher.Run(() => OnPanelClick?.Invoke(lx, ly, dbl));
                return IntPtr.Zero;
            }
            case WM_MOUSEMOVE:
            {
                int x = (short)(lParam.ToInt32() & 0xFFFF);
                int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
                if (x >= 0 && y >= 0)
                {
                    float lx, ly;
                    ClientToLogical(x, y, out lx, out ly);
                    TrackMouseLeave(hWnd);
                    MainThreadDispatcher.Run(() => OnPanelMouseMove?.Invoke(lx, ly));
                }
                return IntPtr.Zero;
            }
            case WM_MOUSELEAVE:
                MainThreadDispatcher.Run(() => OnPanelMouseMove?.Invoke(-1f, -1f));
                return IntPtr.Zero;
            case WM_SIZE:
                LayoutChildren();
                return IntPtr.Zero;
            case WM_CTLCOLOREDIT:
            {
                // ★ 透明 EDIT：白字 + 透明背景（融合星空面板，无白框）
                IntPtr hdcEdit = wParam;
                SetTextColor(hdcEdit, 0x00D8CCFF);      // 白紫字
                SetBkMode(hdcEdit, 1 /*TRANSPARENT*/);
                return GetStockObject(5 /*NULL_BRUSH*/);
            }
            case WM_EXITSIZEMOVE:
                // 拖动/缩放结束 → 记忆位置
                SavePos();
                return IntPtr.Zero;
            case WM_PAINT:
            {
                // 渲染最新像素流（GetDC 方式，避免 PAINTSTRUCT 封送）
                IntPtr hdc = GetDC(hWnd);
                lock (_bufLock)
                {
                    if (_buffer != null && _bufW > 0 && _bufH > 0)
                    {
                        var bmi = new BITMAPINFO();
                        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                        bmi.bmiHeader.biWidth = _bufW;
                        bmi.bmiHeader.biHeight = _bufH; // ★ 正数=bottom-up：Unity 纹理数组第一行是视觉底部，方向正确（负数 top-down 会上下颠倒）
                        bmi.bmiHeader.biPlanes = 1;
                        bmi.bmiHeader.biBitCount = 32;
                        bmi.bmiHeader.biCompression = 0; // BI_RGB
                        SetDIBitsToDevice(hdc, 0, 0, _bufW, _bufH, 0, 0, 0, _bufH, _buffer, ref bmi, 0);
                    }
                }
                ReleaseDC(hWnd, hdc);
                ValidateRect(hWnd, IntPtr.Zero); // 标记已绘制，避免 WM_PAINT 风暴
                return IntPtr.Zero;
            }
            case WM_DESTROY:
                if (_hwnd == hWnd) _hwnd = IntPtr.Zero;
                // WM_QUIT 不能通过 PostMessage 投递到窗口；必须使用
                // PostQuitMessage 才能让本窗口线程的 GetMessage 循环退出。
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>EDIT 子类化窗口过程（2026-08-17 修复回车发送）：
    /// 拦截 VK_RETURN → DoSend（清空 + 回调 Unity）；其余消息转原过程。
    /// 真实用户输入时焦点在 edit，回车必须先在这里拦截，否则被 edit 默认过程消费。</summary>
    private static IntPtr EditProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_KILLFOCUS)
            _inputFocusActive = false;
        if (msg == WM_KEYDOWN)
            Debug.Log($"[ExternalChat] EditProc WM_KEYDOWN vk=0x{wParam.ToInt32():X} (VK_RETURN=0x{VK_RETURN:X})");
        if (msg == WM_KEYDOWN && wParam.ToInt32() == VK_RETURN)
        {
            // 单行 EDIT：回车不换行，直接触发发送
            DoSend();
            return IntPtr.Zero;
        }
        return CallWindowProcW(_origEditProc, hWnd, msg, wParam, lParam);
    }

    private static void DoSend()
    {
        var sb = new System.Text.StringBuilder(1024);
        GetWindowTextW(_edit, sb, 1024);
        string text = sb.ToString().Trim();
        if (text.Length == 0) return;
        SetWindowTextW(_edit, "");
        // 发送留痕（不记录真实内容，防敏感信息入日志——codex 建议 5.2）
        LogInputState($"send length={text.Length}");
        MainThreadDispatcher.Run(() => OnSendText?.Invoke(text));
    }

    /// <summary>控制原生输入框显隐（★ 透明覆盖在 IMGUI 输入框上：无白底无边框，视觉融合）
    /// 位置由 SetInputRect 从 Unity 侧同步（IMGUI 输入框的逻辑矩形）</summary>
    public static void ShowInputBar(bool show)
    {
        if (!IsCreated) return;
        if (!show) _inputFocusActive = false;
        ShowWindow(_edit, show ? 5 : 0);
        // 原生发送按钮不参与外置模式交互，始终隐藏，避免黑色控件覆盖 Unity 发送图标。
        HideNativeSendButton();
        if (show) LayoutChildren();
    }

    /// <summary>设置原生输入框位置（Unity 主线程调用，坐标为面板逻辑像素）
    /// ★ 2026-08-17 统一 DPI：逻辑 → 物理客户区转换后才 SetWindowPos（144 DPI 下 Edit 位置正确）</summary>
    public static void SetInputRect(int x, int y, int w, int h)
    {
        if (!IsCreated) return;
        int px, py, pw, ph;
        LogicalToClient(x, y, out px, out py);
        LogicalToClientSize(w, h, out pw, out ph);
        // 原生 EDIT 只作为键盘通道，不再覆盖 Unity 绘制的输入框。
        // 显示它会造成黑色原生背景与 IMGUI 输入框交替闪烁；保持 1x1 隐藏位置，
        // 仍可获得焦点并通过 GetWindowTextW 同步文字到 Unity RT。
        _inputRectSet = true;
        _inputX = px; _inputY = py; _inputW = pw; _inputH = ph;
        MoveEditOffscreen();
        LogInputState("hit+rect");
    }

    /// <summary>读取隐形原生输入通道的当前文字，由 Unity 主线程同步到 IMGUI。</summary>
    public static string GetInputText()
    {
        if (!IsCreated || _edit == IntPtr.Zero) return string.Empty;
        var sb = new System.Text.StringBuilder(1024);
        GetWindowTextW(_edit, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>外置窗口输入通道是否聚焦，用于 RT 中绘制可见插入光标。</summary>
    public static bool IsInputFocused => _inputFocusActive;

    /// <summary>轮询外置窗口的真实鼠标位置，统一转换为面板逻辑坐标。</summary>
    public static bool TryGetMousePosition(out float lx, out float ly)
    {
        lx = -1f;
        ly = -1f;
        if (!IsCreated || !IsVisible || _hwnd == IntPtr.Zero) return false;

        POINT cursor;
        RECT window;
        if (!GetCursorPos(out cursor) || !GetWindowRect(_hwnd, out window)) return false;
        if (cursor.X < window.Left || cursor.X >= window.Right
            || cursor.Y < window.Top || cursor.Y >= window.Bottom)
            return false;

        ClientToLogical(cursor.X - window.Left, cursor.Y - window.Top, out lx, out ly);
        return true;
    }

    /// <summary>输入聚焦状态机日志（2026-08-17）：每步留痕，144 DPI 验收定位断点用</summary>
    private static void LogInputState(string step)
    {
        IntPtr focus = GetFocus();
        bool editVisible = IsWindowVisible(_edit);
        Debug.Log($"[ExternalChat] input {step} | edit可见={editVisible} focus=0x{focus.ToInt64():X} (edit=0x{_edit.ToInt64():X})");
    }

    /// <summary>请求窗口线程聚焦输入框（★ PostMessage 跨线程：SetFocus 必须由窗口线程自己执行，
    ///   否则跨线程 SetFocus 失败 →「点击输入框无法输入」）</summary>
    public static void FocusInput()
    {
        if (!IsCreated) return;
        PostMessageW(_hwnd, WM_APP_FOCUS_INPUT, IntPtr.Zero, IntPtr.Zero);
    }

    private static void LayoutChildren()
    {
        if (_edit == IntPtr.Zero || _sendBtn == IntPtr.Zero) return;
        // 输入框位置由 Unity 侧 SetInputRect 同步（已统一 DPI 转换）；按钮仅作参考（透明样式下隐藏）
        RECT rc; GetClientRect(_hwnd, out rc);
        int barH = 44;
        if (_inputRectSet)
            MoveEditOffscreen();
        else
            SetWindowPos_Edit(8, rc.Bottom - barH + 6, rc.Right - 90, 30);
        SetWindowPos_Button(rc.Right - 76, rc.Bottom - barH + 6, 68, 30);
    }

    private static bool _inputRectSet;
    private static int _inputX, _inputY, _inputW, _inputH;
    private static void SetWindowPos_Edit(int x, int y, int w, int h)
    {
        _inputRectSet = true; _inputX = x; _inputY = y; _inputW = w; _inputH = h;
        SetWindowPos(_edit, IntPtr.Zero, x, y, w, h, 0x0004 /*SWP_NOZORDER*/);
    }

    private static void MoveEditOffscreen()
    {
        if (_edit == IntPtr.Zero) return;
        SetWindowPos(_edit, IntPtr.Zero, -4, -4, 1, 1,
            0x0004 /*SWP_NOZORDER*/ | 0x0010 /*SWP_NOACTIVATE*/);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    private static void SetWindowPos_Button(int x, int y, int w, int h) => SetWindowPos(_sendBtn, IntPtr.Zero, x, y, w, h, 0x0004);

    private static void HideNativeSendButton()
    {
        if (_sendBtn == IntPtr.Zero) return;
        // 除了隐藏，再移到客户区外并缩成 1x1，防止某些 DWM/主题在刷新子控件
        // 时留下旧的黑色 invalidated 区域。
        ShowWindow(_sendBtn, 0 /* SW_HIDE */);
        SetWindowPos(_sendBtn, IntPtr.Zero, -2, -2, 1, 1,
            0x0004 /* SWP_NOZORDER */ | 0x0010 /* SWP_NOACTIVATE */);
    }

    private static void NotifyClosedOnce()
    {
        if (_closeNotificationSent) return;
        _closeNotificationSent = true;
        MainThreadDispatcher.Run(() => OnClosed?.Invoke());
    }

    private static void TrackMouseLeave(IntPtr hWnd)
    {
        var tme = new TRACKMOUSEEVENT
        {
            cbSize = (uint)Marshal.SizeOf(typeof(TRACKMOUSEEVENT)),
            dwFlags = 0x00000002, // TME_LEAVE
            hwndTrack = hWnd,
            dwHoverTime = 0
        };
        TrackMouseEvent(ref tme);
    }
}
