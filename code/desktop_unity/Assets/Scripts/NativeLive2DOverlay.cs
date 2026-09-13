using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 将 Live2D 局部 RenderTexture 以 Win32 分层窗口的逐像素 Alpha 显示在桌面上。
/// Unity/DWM 主窗口只承担输入和托盘生命周期；此类避免 D3D11 Player 的色键与玻璃
/// 合成缺陷。窗口永远返回 HTTRANSPARENT，点击仍交给下方 Unity 的 DragHandler。
/// </summary>
public sealed class NativeLive2DOverlay : MonoBehaviour
{
#if !UNITY_EDITOR
    private const string ClassName = "FuXuanNativeLive2DOverlay";
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_DESTROY = 0x0002;
    private const int HTTRANSPARENT = -1;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style; public WndProc proc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER
    { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }
    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT dst, ref SIZE size, IntPtr hdcSrc, ref POINT src, uint colorKey, ref BLENDFUNCTION blend, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteObject(IntPtr obj);

    private static bool s_registered;
    private static WndProc s_proc;
    private IntPtr _hwnd, _dc, _bitmap, _oldBitmap, _bits;
    private int _width, _height;
    private RenderTexture _source;
    private Rect _screenRect;
    private Texture2D _readback;
    private float _nextFrame;
    private bool _failed;

    public void SetSource(RenderTexture source, Rect screenRect)
    {
        _source = source;
        _screenRect = screenRect;
    }

    private void LateUpdate()
    {
        if (_failed || _source == null || !_source.IsCreated() || Time.unscaledTime < _nextFrame) return;
        _nextFrame = Time.unscaledTime + 1f / 20f;
        try { Present(); }
        catch (Exception e) { _failed = true; Debug.LogError("[NativeLive2DOverlay] 逐像素透明层失败: " + e.Message); DisposeOverlay(); }
    }

    private void Present()
    {
        int w = _source.width, h = _source.height;
        if (w < 1 || h < 1) return;
        EnsureWindow(w, h);
        if (_hwnd == IntPtr.Zero) return;
        if (_readback == null || _readback.width != w || _readback.height != h)
        {
            if (_readback != null) Destroy(_readback);
            _readback = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        }
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = _source;
        _readback.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
        _readback.Apply(false, false);
        RenderTexture.active = active;

        Color32[] pixels = _readback.GetPixels32();
        byte[] bgra = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int srcRow = y * w;
            int dstRow = (h - 1 - y) * w * 4;
            for (int x = 0; x < w; x++)
            {
                Color32 c = pixels[srcRow + x];
                // UpdateLayeredWindow 要求预乘 Alpha 的 BGRA。
                int i = dstRow + x * 4;
                bgra[i] = (byte)(c.b * c.a / 255); bgra[i + 1] = (byte)(c.g * c.a / 255);
                bgra[i + 2] = (byte)(c.r * c.a / 255); bgra[i + 3] = c.a;
            }
        }
        Marshal.Copy(bgra, 0, _bits, bgra.Length);
        POINT dst = new POINT { X = Mathf.RoundToInt(_screenRect.x), Y = Mathf.RoundToInt(_screenRect.y) };
        SIZE size = new SIZE { CX = w, CY = h }; POINT src = new POINT();
        BLENDFUNCTION blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        if (!UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref dst, ref size, _dc, ref src, 0, ref blend, ULW_ALPHA))
            throw new InvalidOperationException("UpdateLayeredWindow error=" + Marshal.GetLastWin32Error());
        // Unity 主窗会在焦点/外置聊天窗口变化时重申自身 TOPMOST；每帧重申模型层
        // 的层级，确保透明输入层不会盖住实际可见的 Live2D 像素。
        SetWindowPos(_hwnd, HWND_TOPMOST, dst.X, dst.Y, w, h, 0x0010 | 0x0001 | 0x0002);
    }

    private void EnsureWindow(int w, int h)
    {
        if (_hwnd != IntPtr.Zero && _width == w && _height == h) return;
        DisposeWindow(); _width = w; _height = h;
        if (!s_registered)
        {
            s_proc = WindowProc;
            WNDCLASS wc = new WNDCLASS { proc = s_proc, hInstance = Marshal.GetHINSTANCE(typeof(NativeLive2DOverlay).Module), lpszClassName = ClassName };
            ushort atom = RegisterClassW(ref wc);
            if (atom == 0 && Marshal.GetLastWin32Error() != 1410) throw new InvalidOperationException("RegisterClass error=" + Marshal.GetLastWin32Error());
            s_registered = true;
        }
        // 不使用 WS_EX_TRANSPARENT：该标志会让 Windows 把窗口延后到 Unity 主窗之后绘制，
        // 视觉上又会被 DWM 透明层盖住。鼠标穿透统一由 WM_NCHITTEST/HTTRANSPARENT 完成。
        _hwnd = CreateWindowExW(WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, ClassName, "", WS_POPUP | WS_VISIBLE, 0, 0, w, h, IntPtr.Zero, IntPtr.Zero, Marshal.GetHINSTANCE(typeof(NativeLive2DOverlay).Module), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx error=" + Marshal.GetLastWin32Error());
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, w, h, 0x0010 | 0x0040);
        IntPtr screen = GetDC(IntPtr.Zero); _dc = CreateCompatibleDC(screen); ReleaseDC(IntPtr.Zero, screen);
        BITMAPINFO info = new BITMAPINFO { bmiHeader = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER)), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32, biCompression = 0 } };
        _bitmap = CreateDIBSection(_dc, ref info, 0, out _bits, IntPtr.Zero, 0);
        if (_bitmap == IntPtr.Zero || _bits == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection error=" + Marshal.GetLastWin32Error());
        _oldBitmap = SelectObject(_dc, _bitmap);
        Debug.Log("[NativeLive2DOverlay] 已启用 Windows 逐像素 Alpha 透明模型层");
    }

    public void DisposeOverlay() { DisposeWindow(); if (_readback != null) { Destroy(_readback); _readback = null; } }
    private void OnDestroy() { DisposeOverlay(); }
    private void DisposeWindow()
    {
        if (_dc != IntPtr.Zero && _oldBitmap != IntPtr.Zero) SelectObject(_dc, _oldBitmap);
        if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap); if (_dc != IntPtr.Zero) DeleteDC(_dc);
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        _hwnd = _dc = _bitmap = _oldBitmap = _bits = IntPtr.Zero; _width = _height = 0;
    }
    private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_NCHITTEST) return new IntPtr(HTTRANSPARENT);
        if (msg == WM_DESTROY) return IntPtr.Zero;
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }
#else
    public void SetSource(RenderTexture source, Rect screenRect) { }
    public void DisposeOverlay() { }
#endif
}
