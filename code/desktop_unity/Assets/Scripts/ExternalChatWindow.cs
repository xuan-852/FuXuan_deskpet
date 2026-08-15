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

    // ─── 状态 ───
    public static bool IsCreated { get; private set; }
    public static bool IsVisible { get; private set; }
    private static int _width = 640, _height = 480;

    // ─── 像素缓冲（Unity → 窗口线程） ───
    private static readonly object _bufLock = new object();
    private static byte[] _buffer;       // BGRA32
    private static int _bufW, _bufH;

    // ─── Win32 常量 ───
    private const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CHILD = 0x40000000;
    private const int WS_TABSTOP = 0x00010000;
    private const int WS_BORDER = 0x00800000;
    private const int ES_AUTOHSCROLL = 0x0080;
    private const int WS_EX_CLIENTEDGE = 0x00000200;
    private const int WM_DESTROY = 0x0002;
    private const int WM_PAINT = 0x000F;
    private const int WM_SIZE = 0x0005;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_COMMAND = 0x0111;
    private const int WM_CLOSE = 0x0010;
    private const int VK_RETURN = 0x0D;
    private const int BN_CLICKED = 0;
    private const int IDC_EDIT = 101;
    private const int IDC_SEND = 102;

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
            PostMessageW(_hwnd, WM_SIZE, IntPtr.Zero, IntPtr.Zero); // 触发布局
            ShowWindow(_hwnd, 5 /*SW_SHOW*/);
            IsVisible = true;
        }
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

            _hwnd = CreateWindowExW(0, "FuXuanChatWindowClass", "符玄 · 对话",
                WS_OVERLAPPEDWINDOW | WS_VISIBLE, 200, 200, _width, _height, IntPtr.Zero, IntPtr.Zero, _hInst, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                Debug.LogError("[ExternalChat] CreateWindowExW 失败");
                return;
            }

            // 原生输入控件（底部：输入框 + 发送按钮）
            RECT rc; GetClientRect(_hwnd, out rc);
            int barH = 44;
            _edit = CreateWindowExW(WS_EX_CLIENTEDGE, "EDIT", "", WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL | WS_BORDER,
                8, rc.Bottom - barH + 6, rc.Right - 90, 30, _hwnd, (IntPtr)IDC_EDIT, _hInst, IntPtr.Zero);
            _sendBtn = CreateWindowExW(0, "BUTTON", "发送", WS_CHILD | WS_VISIBLE | WS_TABSTOP,
                rc.Right - 76, rc.Bottom - barH + 6, 68, 30, _hwnd, (IntPtr)IDC_SEND, _hInst, IntPtr.Zero);

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
            case WM_CLOSE:
                // ✕ = 隐藏（窗口生命周期归 Unity 管）
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
            case WM_SIZE:
                LayoutChildren();
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
