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
    private const int WM_DESTROY = 0x0002;
    private const int WM_PAINT = 0x000F;
    private const int WM_SIZE = 0x0005;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_COMMAND = 0x0111;
    private const int WM_CLOSE = 0x0010;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCAPTION = 2;
    private const int HTCLIENT = 1;
    private const int HTBOTTOMRIGHT = 17;
    private const int HTNOWHERE = 0;
    private const int VK_RETURN = 0x0D;
    private const int BN_CLICKED = 0;
    private const int IDC_EDIT = 101;
    private const int IDC_SEND = 102;

    // ★ 无边框窗口：自绘星空标题栏高度（逻辑像素，与 RightPanel.EXT_TITLE_BAR_H 一致）
    public const int TITLE_BAR_H = 44;
    // ★ 右下角缩放手柄尺寸（逻辑像素）
    private const int RESIZE_GRIP = 20;

    private static IntPtr _hwnd, _edit, _sendBtn, _hInst;
    private static WndProcDelegate _wndProcDelegate; // 防止被 GC
    private static Thread _windowThread;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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
    private static extern int GetMessageW(ref MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string name);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
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
    private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    private static extern bool AdjustWindowRectEx(ref RECT rect, uint style, bool menu, uint exStyle);
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
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

    /// <summary>隐藏窗口</summary>
    public static void Hide()
    {
        if (IsCreated && IsVisible)
        {
            ShowWindow(_hwnd, 0 /*SW_HIDE*/);
            IsVisible = false;
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
        MainThreadDispatcher.Run(() => OnClosed?.Invoke());
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
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate = WndProc),
                hInstance = _hInst,
                lpszClassName = "FuXuanChatWindowClass"
            };
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

            // 原生输入控件（底部：输入框 + 发送按钮）— 整面板模式下仍创建，由 ShowInputBar 控制显隐
            RECT rc; GetClientRect(_hwnd, out rc);
            int barH = 44;
            _edit = CreateWindowExW(WS_EX_CLIENTEDGE, "EDIT", "", WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL | WS_BORDER,
                8, rc.Bottom - barH + 6, rc.Right - 90, 30, _hwnd, (IntPtr)IDC_EDIT, _hInst, IntPtr.Zero);
            _sendBtn = CreateWindowExW(0, "BUTTON", "发送", WS_CHILD | WS_VISIBLE | WS_TABSTOP,
                rc.Right - 76, rc.Bottom - barH + 6, 68, 30, _hwnd, (IntPtr)IDC_SEND, _hInst, IntPtr.Zero);
            ShowWindow(_edit, 0); // 整面板模式默认隐藏原生输入栏（面板自带 IMGUI 输入栏视觉）
            ShowWindow(_sendBtn, 0);

            IsCreated = true;
            Debug.Log("[ExternalChat] 独立窗口已创建");

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
                int sx = lParam.ToInt32() & 0xFFFF;
                int sy = (lParam.ToInt32() >> 16) & 0xFFFF;
                RECT wr;
                if (GetWindowRect(hWnd, out wr))
                {
                    int cx = sx - wr.Left;
                    int cy = sy - wr.Top;
                    int cw = Math.Max(1, wr.Right - wr.Left);
                    int ch = Math.Max(1, wr.Bottom - wr.Top);
                    // 物理→逻辑缩放
                    float fx = (float)_width / cw;
                    float fy = (float)_height / ch;
                    int lx = (int)(cx * fx);
                    int ly = (int)(cy * fy);
                    // 右下角缩放手柄（逻辑 20px 区）
                    if (lx >= _width - RESIZE_GRIP && ly >= _height - RESIZE_GRIP)
                        return new IntPtr(HTBOTTOMRIGHT);
                    // 顶部标题栏（逻辑 44px 区，全宽）
                    if (ly <= TITLE_BAR_H)
                        return new IntPtr(HTCAPTION);
                }
                return new IntPtr(HTCLIENT);
            }
            case WM_CLOSE:
                // ✕ = 隐藏（窗口生命周期归 Unity 管），先记忆位置
                SavePos();
                ShowWindow(hWnd, 0);
                IsVisible = false;
                MainThreadDispatcher.Run(() => OnClosed?.Invoke());
                return IntPtr.Zero;
            case WM_COMMAND:
                if (wParam.ToInt32() == IDC_SEND) { DoSend(); return IntPtr.Zero; }
                break;
            case WM_KEYDOWN:
                if (wParam.ToInt32() == VK_RETURN && GetFocus() == _edit) { DoSend(); return IntPtr.Zero; }
                break;
            case WM_LBUTTONDOWN:
            case WM_LBUTTONDBLCLK:
                // 面板区点击 → 物理客户区坐标 → 逻辑面板坐标（×DPI 比例，客户区与 RT 逻辑 1:1）→ 主线程命中表
            {
                int x = lParam.ToInt32() & 0xFFFF;
                int y = (lParam.ToInt32() >> 16) & 0xFFFF;
                // 物理→逻辑换算：比例 = 逻辑尺寸 / 物理客户区
                RECT cr;
                GetClientRect(hWnd, out cr);
                int physW = Math.Max(1, cr.Right - cr.Left);
                int physH = Math.Max(1, cr.Bottom - cr.Top);
                float fx = (float)_width / physW;
                float fy = (float)_height / physH;
                float lx = x * fx;
                float ly = y * fy;
                bool dbl = msg == WM_LBUTTONDBLCLK;
                MainThreadDispatcher.Run(() => OnPanelClick?.Invoke(lx, ly, dbl));
                return IntPtr.Zero;
            }
            case WM_SIZE:
                LayoutChildren();
                return IntPtr.Zero;
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
                        bmi.bmiHeader.biHeight = -_bufH; // top-down
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
                PostMessageW(_hwnd, 0x0012 /*WM_QUIT*/, IntPtr.Zero, IntPtr.Zero);
                return IntPtr.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void DoSend()
    {
        var sb = new System.Text.StringBuilder(1024);
        GetWindowTextW(_edit, sb, 1024);
        string text = sb.ToString().Trim();
        if (text.Length == 0) return;
        SetWindowTextW(_edit, "");
        MainThreadDispatcher.Run(() => OnSendText?.Invoke(text));
    }

    /// <summary>控制原生输入栏显隐（整面板模式默认隐藏，聊天聚焦时可唤起；供后续 Phase 交互层使用）</summary>
    public static void ShowInputBar(bool show)
    {
        if (!IsCreated) return;
        ShowWindow(_edit, show ? 5 : 0);
        ShowWindow(_sendBtn, show ? 5 : 0);
        if (show) LayoutChildren();
    }

    /// <summary>聚焦原生输入框（外部模式点击输入区时唤起）</summary>
    public static void FocusInput()
    {
        if (!IsCreated) return;
        ShowInputBar(true);
        SetFocus(_edit);
    }

    private static void LayoutChildren()
    {
        if (_edit == IntPtr.Zero || _sendBtn == IntPtr.Zero) return;
        RECT rc; GetClientRect(_hwnd, out rc);
        int barH = 44;
        SetWindowPos_Edit(8, rc.Bottom - barH + 6, rc.Right - 90, 30);
        SetWindowPos_Button(rc.Right - 76, rc.Bottom - barH + 6, 68, 30);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    private static void SetWindowPos_Edit(int x, int y, int w, int h) => SetWindowPos(_edit, IntPtr.Zero, x, y, w, h, 0x0004 /*SWP_NOZORDER*/);
    private static void SetWindowPos_Button(int x, int y, int w, int h) => SetWindowPos(_sendBtn, IntPtr.Zero, x, y, w, h, 0x0004);
}
