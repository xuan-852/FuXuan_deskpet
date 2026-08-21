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

    // 鼠标消息来自独立窗口线程，不能每条 WM_MOUSEMOVE 都塞入 Unity 主线程队列。
    // 只保留最新坐标，并限制为约 30Hz；否则 MainThreadDispatcher 会被无界队列拖满。
    private const int MOUSE_MOVE_INTERVAL_MS = 33;
    private static int _mouseMoveDispatchPending;
    private static int _lastMouseMovePostTick = int.MinValue;
    private static volatile float _pendingMouseX;
    private static volatile float _pendingMouseY;

    // ─── 状态 ───
    public static bool IsCreated { get; private set; }
    public static bool IsVisible { get; private set; }
    private static int _width = 640, _height = 480;
    // 由窗口线程在 WM_SIZE 中更新；Unity 主线程只读取缓存，不跨线程 GetClientRect。
    private static volatile int _clientWidth = 640, _clientHeight = 480;
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
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA = 0x00000002;
    private const uint CS_DBLCLKS = 0x0008; // 窗口类接收 WM_*BUTTONDBLCLK
    private const int WM_DESTROY = 0x0002;
    private const int WM_SETFOCUS = 0x0007;
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_SETREDRAW = 0x000B;
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
    private const int WM_IME_STARTCOMPOSITION = 0x010D;
    private const int WM_IME_COMPOSITION = 0x010F;
    private const int WM_IME_ENDCOMPOSITION = 0x010E;
    private const int WM_IME_SETCONTEXT = 0x0281;
    private const int WM_IME_NOTIFY = 0x0282;
    private const int WM_CHAR = 0x0102;
    private const int WM_PASTE = 0x0302;
    private const int WM_CUT = 0x0300;
    private const int WM_CLEAR = 0x0303;
    private const int WM_SETTEXT = 0x000C;
    private const int WM_APP_FOCUS_INPUT = 0x8000 + 1; // 自定义：请求窗口线程聚焦输入框
    private const int WM_APP_SHUTDOWN = 0x8000 + 2;    // 自定义：由窗口线程自己销毁窗口并退出消息循环
    private const int WM_APP_ACTIVATE = 0x8000 + 3;    // 自定义：热键唤出时恢复并带到前台
    private const int HTCAPTION = 2;
    private const int HTCLIENT = 1;
    private const int HTBOTTOMRIGHT = 17;
    private const int HTNOWHERE = 0;
    private const int VK_RETURN = 0x0D;
    private const int VK_BACK = 0x08;
    private const int VK_DELETE = 0x2E;
    private const int BN_CLICKED = 0;
    private const int IDC_EDIT = 101;
    private const int IDC_SEND = 102;
    private const int IDC_ARROW = 32512;
    private const int WM_APP_POSITION_IME = 0x8000 + 4;
    private const int IDC_IBEAM = 32513;
    private const int SW_RESTORE = 9;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint GCS_COMPSTR = 0x0008;
    private const uint GCS_RESULTSTR = 0x0800;
    private const int EM_REPLACESEL = 0x00C2;
    private const int CFS_POINT = 0x0002;
    private const int CFS_FORCE_POSITION = 0x0020;
    private const int CFS_CANDIDATEPOS = 0x0040;
    private const int CFS_EXCLUDE = 0x0080;
    // WM_IME_SETCONTEXT 的 lParam 标志：默认组合窗口由输入法绘制。
    // 本项目已经在 Unity 输入栏内绘制组合文本，因此只关闭这一项，保留候选窗口。
    private const long ISC_SHOWUICOMPOSITIONWINDOW = unchecked((long)0x80000000L);

    // ★ 无边框窗口：使用面板自身标题行作为拖动带，不再额外绘制“独立面板”标题栏。
    public const int TITLE_BAR_H = 54;
    // ★ 右下角缩放手柄尺寸（逻辑像素）
    private const int RESIZE_GRIP = 20;
    // ★ 右上角按钮区宽度（逻辑像素，最小化/关闭按钮统一命中区）
    public const int BTN_AREA_W = 68;

    private static IntPtr _hwnd, _edit, _sendBtn, _hInst;
    private static IntPtr _arrowCursor, _ibeamCursor;
    private static volatile bool _inputFocusActive;
    private static volatile string _imeCompositionText = string.Empty;
    // 原生 EDIT 只作为持久的 IME 宿主。显示状态只在输入栏生命周期切换时改变，
    // 不在每个字符到达时反复 ShowWindow，避免 DWM/IME 产生可见闪帧。
    private static bool _editHostShown;
    private static int _imePositionPending;
    // Unity 主线程不能每帧对另一个线程创建的 EDIT 调用 GetWindowTextW。
    // 文本在 EDIT 所属线程中更新，主线程只读取这个快照，避免点击/输入时被 Win32 同步调用卡住。
    private static volatile string _inputTextCache = string.Empty;
    // EDIT 线程写入、Unity 主线程读取的输入快照版本。只有版本变化时 Unity 才复制字符串和触发重绘。
    private static int _inputTextVersion;
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
    private struct COMPOSITIONFORM
    {
        public int dwStyle;
        public POINT ptCurrentPos;
        public RECT rcArea;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CANDIDATEFORM
    {
        public int dwIndex;
        public int dwStyle;
        public POINT ptCurrentPos;
        public RECT rcArea;
    }
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
    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
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
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam,
        [MarshalAs(UnmanagedType.LPWStr)] string lParam);
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
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);
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
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT paint);
    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT paint);
    [DllImport("gdi32.dll")]
    private static extern int SetDIBitsToDevice(IntPtr hdc, int xDest, int yDest, int w, int h,
        int xSrc, int ySrc, int startScan, int scanLines, byte[] bits, ref BITMAPINFO bmi, uint colorUse);
    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);
    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
    [DllImport("imm32.dll")]
    private static extern int ImmGetCompositionStringW(IntPtr hIMC, uint dwIndex, IntPtr lpBuf, int dwBufLen);
    [DllImport("imm32.dll")]
    private static extern bool ImmSetCompositionWindow(IntPtr hIMC, ref COMPOSITIONFORM lpCompForm);
    [DllImport("imm32.dll")]
    private static extern bool ImmSetCandidateWindow(IntPtr hIMC, ref CANDIDATEFORM lpCandidateForm);
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
        int cw = Math.Max(1, _clientWidth);
        int ch = Math.Max(1, _clientHeight);
        lx = physX * (float)_width / cw;
        ly = physY * (float)_height / ch;
    }

    /// <summary>面板逻辑坐标 → 物理客户区坐标（原生控件布局统一入口）</summary>
    private static void LogicalToClient(float lx, float ly, out int physX, out int physY)
    {
        int cw = Math.Max(1, _clientWidth);
        int ch = Math.Max(1, _clientHeight);
        physX = (int)(lx * cw / (float)_width);
        physY = (int)(ly * ch / (float)_height);
    }

    /// <summary>面板逻辑尺寸 → 物理客户区尺寸（原生控件宽高统一入口）</summary>
    private static void LogicalToClientSize(float lw, float lh, out int physW, out int physH)
    {
        int cw = Math.Max(1, _clientWidth);
        int ch = Math.Max(1, _clientHeight);
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
            ActivateWindow();
        }
    }

    /// <summary>
    /// 将普通外置窗口恢复到当前普通窗口层级的最前方。
    /// 不使用 HWND_TOPMOST：唤出瞬间显示在其他窗口上方，但随后仍允许被新激活的窗口遮挡。
    /// </summary>
    public static void ActivateWindow()
    {
        if (!IsCreated || !IsVisible || _hwnd == IntPtr.Zero) return;
        PostMessageW(_hwnd, WM_APP_ACTIVATE, IntPtr.Zero, IntPtr.Zero);
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
            ClearBuffer();
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

    /// <summary>隐藏或关闭外置窗口时清掉上一帧，避免窗口残留时继续显示旧画面。</summary>
    private static void ClearBuffer()
    {
        lock (_bufLock)
        {
            if (_buffer != null)
                Array.Clear(_buffer, 0, _buffer.Length);
            _bufW = 0;
            _bufH = 0;
        }
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
                WS_POPUP, _startX, _startY, _width, _height, IntPtr.Zero, IntPtr.Zero, _hInst, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                Debug.LogError("[ExternalChat] CreateWindowExW 失败");
                return;
            }
            // 无边框窗口：客户区 = 窗口区（WS_POPUP 无系统边框），直接 SetWindowPos 定尺寸
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, _width, _height, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);

            // 原生输入控件 — 透明无边框，仅作为屏外键盘/中文输入法桥接。
            // 可见背景、文字和光标均由 Unity 绘制，避免 Win32 EDIT 客户区留下黑色矩形。
            _edit = CreateWindowExW(0x00000020 /* WS_EX_TRANSPARENT */ | WS_EX_LAYERED, "EDIT", "",
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
            // EDIT 只作为键盘/中文输入法桥接，始终不参与可见界面绘制。
            ShowWindow(_edit, 0);
            SendMessageW(_edit, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            MakeEditVisuallyHidden();
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
                // EDIT 仅作为输入法宿主，真正隐藏；Unity 负责绘制可见文字和光标。
                EnsureEditHostShown();
                MakeEditVisuallyHidden();
                // 仅在获得焦点时同步一次 IME 宿主矩形；后续字符输入不再重复布局。
                PositionEditImeHost();
                // 发送按钮由 Unity 位图和外置命中表绘制/处理；不要显示原生 BUTTON，
                // 否则它会以黑色控件覆盖输入栏右侧。
                HideNativeSendButton();
                SetFocus(_edit);
                _imeCompositionText = string.Empty;
                _inputFocusActive = true;
                LogInputState("focused");
                // 焦点消息返回后再定位一次候选框。不能在这里直接调用 IMM，
                // 但也不能等到第一字符后才定位，否则候选栏会先出现在屏幕左上角。
                RequestImePosition();
                return IntPtr.Zero;
            }
            case WM_APP_POSITION_IME:
                // 点击输入框期间不触碰 IMM；仅在真正组词时异步定位，
                // 避免部分中文输入法在焦点切换期间同步阻塞窗口线程。
                Volatile.Write(ref _imePositionPending, 0);
                if (_inputFocusActive && GetFocus() == _edit)
                {
                    UpdateImeCompositionText();
                    PositionImeWindow();
                }
                return IntPtr.Zero;
            case WM_APP_ACTIVATE:
            {
                // 由窗口创建线程执行，避免跨线程激活/置前不稳定。
                // Windows 前台切换限制可能让 BringWindowToTop/SetForegroundWindow
                // 返回成功但窗口仍被其他窗口遮挡；用一次性 TOPMOST 提升保证用户能看到，
                // 随后立即恢复 NOTOPMOST，保持普通窗口的日常行为。
                ShowWindow(hWnd, SW_RESTORE);
                uint promoteFlags = SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW;
                SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, promoteFlags);
                bool raised = BringWindowToTop(hWnd);
                bool foreground = SetForegroundWindow(hWnd);
                SetWindowPos(hWnd, HWND_NOTOPMOST, 0, 0, 0, 0,
                    promoteFlags | SWP_NOACTIVATE);
                Debug.Log($"[ExternalChat] 热键唤出：临时置顶并恢复普通层级 raised={raised} foreground={foreground}");
                return IntPtr.Zero;
            }
            case WM_APP_SHUTDOWN:
                // 该消息只由 Shutdown 投递，当前 WndProc 就运行在窗口创建线程上。
                ShowWindow(hWnd, 0 /*SW_HIDE*/);
                IsVisible = false;
                ClearBuffer();
                DestroyWindow(hWnd);
                return IntPtr.Zero;
            case WM_CLOSE:
                // ✕ = 隐藏（窗口生命周期归 Unity 管），先记忆位置
                SavePos();
                ShowWindow(hWnd, 0);
                IsVisible = false;
                _inputFocusActive = false;
                ClearBuffer();
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
            case WM_IME_SETCONTEXT:
            {
                // 父窗口也可能收到 WM_IME_SETCONTEXT（取决于当前 IME/Windows 版本）。
                // 关闭默认组合窗口，组合文字由 Unity 输入栏绘制；候选窗口仍保留。
                long contextFlags = lParam.ToInt64() & ~ISC_SHOWUICOMPOSITIONWINDOW;
                // 不把默认组合窗口交给 DefWindowProc；否则部分 Windows 中文输入法即使清掉
                // ISC_SHOWUICOMPOSITIONWINDOW 仍会在屏幕左上角创建白色组词框。
                DefWindowProcW(hWnd, msg, wParam, new IntPtr(contextFlags));
                return IntPtr.Zero;
            }
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
                    QueueMouseMove(lx, ly, false);
                }
                return IntPtr.Zero;
            }
            case WM_MOUSELEAVE:
                QueueMouseMove(-1f, -1f, true);
                return IntPtr.Zero;
            case WM_SIZE:
                // Unity 每帧会提交同一个逻辑输入框矩形。窗口尺寸变化后允许重新换算一次 DPI/客户区坐标。
                _logicalInputRectSet = false;
                if (GetClientRect(hWnd, out RECT client))
                {
                    _clientWidth = Math.Max(1, client.Right - client.Left);
                    _clientHeight = Math.Max(1, client.Bottom - client.Top);
                }
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
                // 使用标准 BeginPaint/EndPaint 清除无效区域；GetDC + ValidateRect
                // 会让焦点切换后的 WM_PAINT 反复重入，最终拖死外置窗口线程。
                PAINTSTRUCT paint;
                IntPtr hdc = BeginPaint(hWnd, out paint);
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
                EndPaint(hWnd, ref paint);
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
        // EDIT 覆盖在 Unity 已绘制的输入背景上，禁止 Windows 默认擦除客户区，
        // 否则透明控件会在圆角输入框内部留下黑色矩形。
        if (msg == WM_ERASEBKGND)
            return new IntPtr(1);
        if (msg == WM_PAINT)
        {
            // EDIT 只作为键盘/IME 桥接，禁止原生控件绘制白底、文字和系统光标。
            // 可见内容和光标由 Unity RT 绘制，但 EDIT 保留真实输入栏坐标供 IME 使用。
            PAINTSTRUCT paint;
            BeginPaint(hWnd, out paint);
            EndPaint(hWnd, ref paint);
            return IntPtr.Zero;
        }
        if (msg == WM_SETREDRAW)
        {
            // EDIT 只作为输入法宿主，禁止原生文本、背景和光标绘制。
            return CallWindowProcW(_origEditProc, hWnd, msg, wParam, lParam);
        }
        if (msg == WM_IME_SETCONTEXT)
        {
            // 原生 EDIT 仅负责接收键盘/IME 消息；组合文字由 Unity 绘制。
            // 保留候选窗口，避免输入法失去候选选择能力。
            long contextFlags = lParam.ToInt64() & ~ISC_SHOWUICOMPOSITIONWINDOW;
            CallWindowProcW(_origEditProc, hWnd, msg, wParam, new IntPtr(contextFlags));
            return IntPtr.Zero;
        }
        if (msg == WM_SETFOCUS)
        {
            _inputFocusActive = true;
            RequestImePosition();
        }
        if (msg == WM_KILLFOCUS)
        {
            _inputFocusActive = false;
            _imeCompositionText = string.Empty;
        }
        // WM_IME_NOTIFY 可能在刚获得焦点时被输入法发送；它不包含组词文本，
        // 不应因为一次点击就触发 IMM 查询。开始组词和组词变化则需要异步定位。
        // 只拦截 Win32 默认的组合字层；候选栏仍由 Windows 原生 IME 绘制。
        if (msg == WM_IME_STARTCOMPOSITION)
        {
            _imeCompositionText = string.Empty;
            RequestImePosition();
            return IntPtr.Zero;
        }
        if (msg == WM_IME_COMPOSITION)
        {
            HandleImeCompositionMessage(hWnd, lParam);
            return IntPtr.Zero;
        }
        if (msg == WM_IME_ENDCOMPOSITION)
        {
            _imeCompositionText = string.Empty;
            RequestImePosition();
            return IntPtr.Zero;
        }
        if (msg == WM_IME_STARTCOMPOSITION || msg == WM_IME_COMPOSITION)
        {
            // 输入法回调期间不调用任何 IMM 查询/定位 API。
            // 某些中文输入法在焦点切换阶段会同步等待 IME 窗口，
            // 在这里调用 ImmGetContext 也会把外置窗口线程卡住。
            // 等当前回调返回后再合并处理读取和定位。
            RequestImePosition();
        }
        if (msg == WM_IME_ENDCOMPOSITION)
            _imeCompositionText = string.Empty;
        if (msg == WM_KEYDOWN)
            Debug.Log($"[ExternalChat] EditProc WM_KEYDOWN vk=0x{wParam.ToInt32():X} (VK_RETURN=0x{VK_RETURN:X})");
        if (msg == WM_KEYDOWN && wParam.ToInt32() == VK_RETURN)
        {
            // 单行 EDIT：回车不换行，直接触发发送
            DoSend();
            return IntPtr.Zero;
        }
        IntPtr result = CallWindowProcW(_origEditProc, hWnd, msg, wParam, lParam);
        // 字符输入走 WM_CHAR，但退格/删除只会走 WM_KEYDOWN；漏掉后两者会让 Unity 字层
        // 等到下一次字符输入才同步，表现为“输入有延迟/删除不及时”。必须在默认 EDIT
        // 过程完成后再读取，确保拿到已经修改后的文本。
        int vk = wParam.ToInt32();
        if (msg == WM_CHAR || msg == WM_PASTE || msg == WM_CUT || msg == WM_CLEAR || msg == WM_SETTEXT
            || (msg == WM_KEYDOWN && (vk == VK_BACK || vk == VK_DELETE)))
            UpdateInputTextCache();
        return result;
    }

    // 只在 EDIT 所属窗口线程中调用。不要从 Unity 主线程调用 GetWindowTextW。
    private static void HandleImeCompositionMessage(IntPtr hWnd, IntPtr lParam)
    {
        long flags = lParam.ToInt64();
        if ((flags & GCS_RESULTSTR) != 0)
        {
            string resultText = ReadImeString(GCS_RESULTSTR);
            if (!string.IsNullOrEmpty(resultText))
            {
                // EDIT 不再接收 WM_IME_COMPOSITION 默认处理，因此由我们把已提交文本插入当前光标处。
                SendMessageW(hWnd, EM_REPLACESEL, new IntPtr(1), resultText);
                UpdateInputTextCache();
            }
        }

        // 只读取 IME 状态，不调用 ImmSet*；候选栏位置通过消息队列异步更新，避免回调重入卡死。
        UpdateImeCompositionText();
        RequestImePosition();
    }

    private static string ReadImeString(uint index)
    {
        if (_edit == IntPtr.Zero) return string.Empty;
        IntPtr imc = ImmGetContext(_edit);
        if (imc == IntPtr.Zero) return string.Empty;
        try
        {
            int bytes = ImmGetCompositionStringW(imc, index, IntPtr.Zero, 0);
            if (bytes <= 0) return string.Empty;
            IntPtr buffer = Marshal.AllocHGlobal(bytes + sizeof(char));
            try
            {
                int actualBytes = ImmGetCompositionStringW(imc, index, buffer, bytes);
                if (actualBytes <= 0) return string.Empty;
                return Marshal.PtrToStringUni(buffer, actualBytes / sizeof(char)) ?? string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ImmReleaseContext(_edit, imc);
        }
    }

    private static void UpdateInputTextCache()
    {
        if (_edit == IntPtr.Zero) return;
        var sb = new System.Text.StringBuilder(1024);
        GetWindowTextW(_edit, sb, sb.Capacity);
        string next = sb.ToString();
        if (!string.Equals(_inputTextCache, next, StringComparison.Ordinal))
        {
            _inputTextCache = next;
            Interlocked.Increment(ref _inputTextVersion);
        }
    }

    /// <summary>
    /// 将鼠标位置合并后再投递给 Unity。原生窗口线程可能在一帧内收到数百条 WM_MOUSEMOVE，
    /// 不能让每条消息都进入 MainThreadDispatcher 的无界队列。
    /// </summary>
    private static void QueueMouseMove(float x, float y, bool force)
    {
        _pendingMouseX = x;
        _pendingMouseY = y;

        int now = Environment.TickCount;
        int elapsed = unchecked(now - _lastMouseMovePostTick);
        if (!force && elapsed >= 0 && elapsed < MOUSE_MOVE_INTERVAL_MS)
            return;
        if (Interlocked.CompareExchange(ref _mouseMoveDispatchPending, 1, 0) != 0)
            return;

        _lastMouseMovePostTick = now;
        MainThreadDispatcher.Run(() =>
        {
            try
            {
                OnPanelMouseMove?.Invoke(_pendingMouseX, _pendingMouseY);
            }
            finally
            {
                Volatile.Write(ref _mouseMoveDispatchPending, 0);
            }
        });
    }

    /// <summary>
    /// 请求在当前 IMM 回调返回后重新定位候选框。
    /// 不能在 EditProc 的 WM_IME_* 分支里直接调用 ImmSet*Window，
    /// 否则部分输入法会回发 WM_IME_* 造成递归。
    /// </summary>
    private static void RequestImePosition()
    {
        if (_hwnd == IntPtr.Zero || !_inputRectSet) return;
        if (Interlocked.CompareExchange(ref _imePositionPending, 1, 0) != 0)
            return;
        if (!PostMessageW(_hwnd, WM_APP_POSITION_IME, IntPtr.Zero, IntPtr.Zero))
            Volatile.Write(ref _imePositionPending, 0);
    }

    private static void PositionImeWindow()
    {
        if (_edit == IntPtr.Zero || !_inputRectSet) return;
        IntPtr imc = ImmGetContext(_edit);
        if (imc == IntPtr.Zero)
        {
            Debug.LogWarning("[ExternalChat] IMM context unavailable for hidden EDIT");
            return;
        }
        try
        {
            // IMM 的坐标是「关联窗口 _edit 的客户区坐标」，不是父窗口坐标。
            // _edit 为避免覆盖 Unity 输入栏而位于 (-4,-4)，必须经过：
            // 父窗口客户区 → 屏幕 → 隐藏 EDIT 客户区，才能得到正确的屏幕锚点。
            int anchorX = _caretRectSet ? _caretX : _inputX + 8;
            int anchorY = _caretRectSet ? _caretY : _inputY + _inputH - 4;
            int anchorW = _caretRectSet ? Math.Max(1, _caretW) : Math.Max(1, _inputW);
            int anchorH = _caretRectSet ? Math.Max(1, _caretH) : Math.Max(1, _inputH);
            POINT areaTopLeft = new POINT { X = anchorX, Y = anchorY };
            POINT areaBottomRight = new POINT
            {
                X = anchorX + anchorW,
                Y = anchorY + anchorH
            };
            if (!ClientToScreen(_hwnd, ref areaTopLeft)
                || !ClientToScreen(_hwnd, ref areaBottomRight)
                || !ScreenToClient(_edit, ref areaTopLeft)
                || !ScreenToClient(_edit, ref areaBottomRight))
                return;

            POINT anchor = new POINT
            {
                X = areaTopLeft.X,
                Y = areaBottomRight.Y
            };
            RECT area = new RECT
            {
                Left = areaTopLeft.X,
                Top = areaTopLeft.Y,
                Right = areaBottomRight.X,
                Bottom = areaBottomRight.Y
            };
            COMPOSITIONFORM composition = new COMPOSITIONFORM
            {
                dwStyle = CFS_POINT | CFS_FORCE_POSITION,
                ptCurrentPos = anchor,
                rcArea = area
            };
            ImmSetCompositionWindow(imc, ref composition);
            CANDIDATEFORM candidate = new CANDIDATEFORM
            {
                dwIndex = 0,
                // CFS_EXCLUDE 让原生候选栏从 Unity 光标矩形下方展开，而不是覆盖输入文字。
                dwStyle = CFS_EXCLUDE,
                ptCurrentPos = anchor,
                rcArea = area
            };
            ImmSetCandidateWindow(imc, ref candidate);
        }
        finally
        {
            ImmReleaseContext(_edit, imc);
        }
    }

    private static void UpdateImeCompositionText()
    {
        if (_edit == IntPtr.Zero) return;
        IntPtr imc = ImmGetContext(_edit);
        if (imc == IntPtr.Zero) return;
        try
        {
            int bytes = ImmGetCompositionStringW(imc, GCS_COMPSTR, IntPtr.Zero, 0);
            if (bytes <= 0)
            {
                _imeCompositionText = string.Empty;
                return;
            }

            // ImmGetCompositionStringW 的 dwBufLen/返回值单位是“字节”，而不是字符。
            // 不能把这个长度直接交给 StringBuilder，否则 UTF-16 缓冲区会出现长度
            // 不匹配，组合文本末尾可能泄漏出类似 A/Ä 的伪字符。
            IntPtr buffer = Marshal.AllocHGlobal(bytes + sizeof(char));
            try
            {
                int actualBytes = ImmGetCompositionStringW(imc, GCS_COMPSTR, buffer, bytes);
                if (actualBytes <= 0)
                {
                    _imeCompositionText = string.Empty;
                    return;
                }

                // 返回值仍是 UTF-16 字节数；PtrToStringUni 的长度单位是字符数。
                int charCount = actualBytes / sizeof(char);
                _imeCompositionText = charCount > 0
                    ? Marshal.PtrToStringUni(buffer, charCount)
                    : string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ImmReleaseContext(_edit, imc);
        }
    }

    public static string GetInputComposition()
    {
        return _imeCompositionText ?? string.Empty;
    }

    private static void DoSend()
    {
        var sb = new System.Text.StringBuilder(1024);
        GetWindowTextW(_edit, sb, 1024);
        string text = sb.ToString().Trim();
        if (text.Length == 0) return;
        SetWindowTextW(_edit, "");
        // 清空后立即发布新版本，否则 Unity 字层可能残留上一条消息直到下一次按键。
        UpdateInputTextCache();
        // 发送留痕（不记录真实内容，防敏感信息入日志——codex 建议 5.2）
        LogInputState($"send length={text.Length}");
        MainThreadDispatcher.Run(() => OnSendText?.Invoke(text));
    }

    /// <summary>控制原生输入桥显隐（可见输入框完全由 Unity 绘制）
    /// SetInputRect 仍同步逻辑矩形，用于保留 DPI/命中链路记录</summary>
    public static void ShowInputBar(bool show)
    {
        if (!IsCreated) return;
        if (!show)
        {
            _inputFocusActive = false;
            if (_editHostShown)
            {
                ShowWindow(_edit, 0 /* SW_HIDE */);
                _editHostShown = false;
            }
        }
        else
        {
            EnsureEditHostShown();
            MakeEditVisuallyHidden();
        }

        // 原生发送按钮不参与外置模式交互，始终隐藏，避免黑色控件覆盖 Unity 发送图标。
        HideNativeSendButton();
    }

    /// <summary>设置原生输入框位置（Unity 主线程调用，坐标为面板逻辑像素）
    /// ★ 2026-08-17 统一 DPI：逻辑 → 物理客户区转换后才 SetWindowPos（144 DPI 下 Edit 位置正确）</summary>
    public static void SetInputRect(int x, int y, int w, int h)
    {
        if (!IsCreated) return;
        // IMGUI 每帧都会进入这里。相同矩形直接返回，避免每帧跨线程调用
        // GetClientRect/SetWindowPos 相关的窗口 API；点击输入框后尤其不能持续阻塞主线程。
        if (_logicalInputRectSet
            && _logicalInputX == x && _logicalInputY == y
            && _logicalInputW == w && _logicalInputH == h)
            return;

        int px, py, pw, ph;
        LogicalToClient(x, y, out px, out py);
        LogicalToClientSize(w, h, out pw, out ph);

        _logicalInputRectSet = true;
        _logicalInputX = x; _logicalInputY = y; _logicalInputW = w; _logicalInputH = h;

        bool changed = !_inputRectSet
            || _inputX != px || _inputY != py || _inputW != pw || _inputH != ph;
        _inputRectSet = true;
        _inputX = px; _inputY = py; _inputW = pw; _inputH = ph;

        // SetInputRect 由 Unity 的 IMGUI 每帧调用。原先这里每帧跨线程
        // SetWindowPos + Debug.Log，会让窗口线程和日志镜像持续争用，点击后逐渐卡死。
        // EDIT 作为持久 IME 宿主，只在输入栏矩形变化时由 WM_SIZE/聚焦阶段重新布局；
        // 这里仅缓存坐标，避免每个字符都触发原生窗口布局。
        if (changed)
            Debug.Log($"[ExternalChat] input rect changed ({px},{py},{pw},{ph})");
    }

    /// <summary>读取隐形原生输入通道的当前文字，由 Unity 主线程同步到 IMGUI。</summary>
    /// <summary>
    /// 同步 Unity 实际渲染光标矩形。原生候选栏只使用这个小矩形定位，避免跟随整个输入框左上角。
    /// 坐标与 SetInputRect 相同，均为外置窗口客户区逻辑像素。
    /// </summary>
    public static void SetInputCaretRect(int x, int y, int w, int h)
    {
        if (!IsCreated) return;
        int px, py, pw, ph;
        LogicalToClient(x, y, out px, out py);
        LogicalToClientSize(w, h, out pw, out ph);

        bool changed = !_caretRectSet
            || _caretX != px || _caretY != py || _caretW != pw || _caretH != ph;
        _caretRectSet = true;
        _caretX = px; _caretY = py; _caretW = pw; _caretH = ph;
        if (changed && _inputFocusActive)
            RequestImePosition();
    }

    public static string GetInputText()
    {
        return _inputTextCache ?? string.Empty;
    }

    /// <summary>输入文本快照版本。用于替代 Unity 每帧字符串轮询。</summary>
    public static int GetInputTextVersion()
    {
        return Volatile.Read(ref _inputTextVersion);
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
        // 可见输入框、组合文字和光标完全由 Unity 绘制；EDIT 始终是透明 IME 宿主。
        PositionEditImeHost();
        SetWindowPos_Button(rc.Right - 76, rc.Bottom - barH + 6, 68, 30);
    }

    private static bool _inputRectSet;
    private static int _inputX, _inputY, _inputW, _inputH;
    private static bool _caretRectSet;
    private static int _caretX, _caretY, _caretW, _caretH;
    private static bool _logicalInputRectSet;
    private static int _logicalInputX, _logicalInputY, _logicalInputW, _logicalInputH;
    private static void SetWindowPos_Edit(int x, int y, int w, int h)
    {
        _inputRectSet = true; _inputX = x; _inputY = y; _inputW = w; _inputH = h;
        SetWindowPos(_edit, IntPtr.Zero, x, y, w, h, 0x0004 /*SWP_NOZORDER*/);
    }

    private static void EnsureEditHostShown()
    {
        if (_edit == IntPtr.Zero) return;
        if (!_editHostShown)
        {
            // 只在输入宿主从隐藏状态进入输入生命周期时显示一次；字符输入期间不再切换
            // 原生窗口可见性，避免 IME/DWM 在每个组合事件上产生闪动。
            ShowWindow(_edit, 5 /* SW_SHOW */);
            _editHostShown = true;
        }

        // WM_SETREDRAW(FALSE) 在创建阶段关闭过，这里只重复声明状态，不触发重绘。
        SendMessageW(_edit, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
    }

    private static void PositionEditImeHost()
    {
        if (_edit == IntPtr.Zero) return;
        // EDIT 保留在 Unity 输入栏真实矩形内作为 IME 宿主；原生绘制已由 EditProc
        // 拦截并设为透明，用户看到的文字/光标全部来自 Unity。保留真实尺寸可让
        // Windows 中文输入法正确计算候选栏坐标。
        int hostX = _inputRectSet ? _inputX : 0;
        int hostY = _inputRectSet ? _inputY : 0;
        int hostW = _inputRectSet ? Math.Max(1, _inputW) : 1;
        int hostH = _inputRectSet ? Math.Max(1, _inputH) : 1;
        SetWindowPos(_edit, IntPtr.Zero, hostX, hostY, hostW, hostH,
            0x0004 /*SWP_NOZORDER*/ | 0x0010 /*SWP_NOACTIVATE*/);
    }

    /// <summary>
    /// 微软中文输入法会额外创建 CiceroUIWndFrame 组合窗口，即使应用清除了
    /// ISC_SHOWUICOMPOSITIONWINDOW，部分版本仍会显示它。该窗口只承载原生组词预览，
    /// 候选窗口是另一套 UI，不能一起隐藏；Unity 已经绘制了组词文本，因此这里只隐藏前者。
    /// </summary>
    /// <summary>
    /// 将 Win32 EDIT 设为完全透明，但不隐藏窗口本身。
    /// 它必须继续存在并保持焦点，中文输入法才能正常附着；可见文字、组合下划线和光标统一由 Unity 绘制。
    /// </summary>
    private static void MakeEditVisuallyHidden()
    {
        if (_edit == IntPtr.Zero) return;
        int exStyle = GetWindowLong(_edit, GWL_EXSTYLE);
        if ((exStyle & (int)WS_EX_LAYERED) == 0)
            SetWindowLong(_edit, GWL_EXSTYLE, new IntPtr(exStyle | (int)WS_EX_LAYERED));
        SetLayeredWindowAttributes(_edit, 0, 0, LWA_ALPHA);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);
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
