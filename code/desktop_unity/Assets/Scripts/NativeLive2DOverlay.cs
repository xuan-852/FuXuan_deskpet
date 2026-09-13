using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Win32 layered, click-through Live2D presentation using the verified synchronous 45 FPS path.</summary>
public sealed class NativeLive2DOverlay : MonoBehaviour
{
#if !UNITY_EDITOR
    private const string ClassName = "FuXuanNativeLive2DOverlay";
    private const uint WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000;
    private const uint WS_EX_LAYERED = 0x00080000, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000, ULW_ALPHA = 0x00000002;
    private const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
    private const int WM_NCHITTEST = 0x0084, WM_DESTROY = 0x0002, HTTRANSPARENT = -1;
    private const float TargetFrameRate = 45f;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WNDCLASS { public uint style; public WndProc proc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName; [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }
    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassW(ref WNDCLASS wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT dst, ref SIZE size, IntPtr hdcSrc, ref POINT src, uint key, ref BLENDFUNCTION blend, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool DeleteObject(IntPtr obj);
    private static bool s_registered; private static WndProc s_proc;
    private IntPtr _hwnd, _dc, _bitmap, _oldBitmap, _bits;
    private int _width, _height;
    private RenderTexture _source; private Rect _screenRect; private Texture2D _readback; private byte[] _bgraBuffer;
    private float _nextFrame, _lastSuccessfulPresentAt = -1f; private bool _visibleDiagnosticLogged, _failed; private string _lastFailure = "";

    public void SetSource(RenderTexture source, Rect screenRect) { _source = source; _screenRect = screenRect; }
    private void LateUpdate()
    {
        if (_failed || _source == null || !_source.IsCreated() || Time.unscaledTime < _nextFrame) return;
        _nextFrame = Time.unscaledTime + 1f / TargetFrameRate;
        try { PresentSync(); } catch (Exception exception) { FailOverlay(exception); }
    }
    private void PresentSync()
    {
        int width = _source.width, height = _source.height;
        if (width < 1 || height < 1) return;
        EnsureReadback(width, height);
        RenderTexture previous = RenderTexture.active;
        try { RenderTexture.active = _source; _readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false); _readback.Apply(false, false); }
        finally { RenderTexture.active = previous; }
        var pixels = _readback.GetRawTextureData<Color32>();
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            Color32 color = pixels[y * width + x]; int destination = ((height - 1 - y) * width + x) * 4; byte alpha = color.a;
            _bgraBuffer[destination] = (byte)(color.b * alpha / 255); _bgraBuffer[destination + 1] = (byte)(color.g * alpha / 255);
            _bgraBuffer[destination + 2] = (byte)(color.r * alpha / 255); _bgraBuffer[destination + 3] = alpha;
        }
        PresentBgra(width, height);
    }
    private void EnsureReadback(int width, int height)
    {
        if (_readback == null || _readback.width != width || _readback.height != height) { if (_readback != null) Destroy(_readback); _readback = new Texture2D(width, height, TextureFormat.RGBA32, false, false); }
        int byteCount = width * height * 4; if (_bgraBuffer == null || _bgraBuffer.Length != byteCount) _bgraBuffer = new byte[byteCount];
    }
    private void PresentBgra(int width, int height)
    {
        EnsureWindow(width, height); if (_hwnd == IntPtr.Zero) return;
        Marshal.Copy(_bgraBuffer, 0, _bits, _bgraBuffer.Length);
        POINT destination = new POINT { X = Mathf.RoundToInt(_screenRect.x), Y = Mathf.RoundToInt(_screenRect.y) }; SIZE size = new SIZE { CX = width, CY = height }; POINT source = new POINT();
        BLENDFUNCTION blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        if (!UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref destination, ref size, _dc, ref source, 0, ref blend, ULW_ALPHA)) throw new InvalidOperationException("UpdateLayeredWindow error=" + Marshal.GetLastWin32Error());
        _lastSuccessfulPresentAt = Time.unscaledTime; _lastFailure = "";
        if (!_visibleDiagnosticLogged) { _visibleDiagnosticLogged = true; Debug.Log("[NativeLive2DOverlay] sync visible: rect=(" + destination.X + "," + destination.Y + "," + width + "," + height + "), targetFps=" + TargetFrameRate + ", lastSuccess=" + _lastSuccessfulPresentAt.ToString("F2")); }
        SetWindowPos(_hwnd, HWND_TOPMOST, destination.X, destination.Y, width, height, 0x0010 | 0x0001 | 0x0002);
    }
    private void EnsureWindow(int width, int height)
    {
        if (_hwnd != IntPtr.Zero && _width == width && _height == height) return;
        _visibleDiagnosticLogged = false; DisposeWindow(); _width = width; _height = height;
        if (!s_registered) { s_proc = WindowProc; WNDCLASS wc = new WNDCLASS { proc = s_proc, hInstance = Marshal.GetHINSTANCE(typeof(NativeLive2DOverlay).Module), lpszClassName = ClassName }; ushort atom = RegisterClassW(ref wc); if (atom == 0 && Marshal.GetLastWin32Error() != 1410) throw new InvalidOperationException("RegisterClass error=" + Marshal.GetLastWin32Error()); s_registered = true; }
        _hwnd = CreateWindowExW(WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, ClassName, "", WS_POPUP | WS_VISIBLE, 0, 0, width, height, IntPtr.Zero, IntPtr.Zero, Marshal.GetHINSTANCE(typeof(NativeLive2DOverlay).Module), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx error=" + Marshal.GetLastWin32Error());
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, width, height, 0x0010 | 0x0040); IntPtr screen = GetDC(IntPtr.Zero); _dc = CreateCompatibleDC(screen); ReleaseDC(IntPtr.Zero, screen);
        BITMAPINFO info = new BITMAPINFO { bmiHeader = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER)), biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32, biCompression = 0 } };
        _bitmap = CreateDIBSection(_dc, ref info, 0, out _bits, IntPtr.Zero, 0); if (_bitmap == IntPtr.Zero || _bits == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection error=" + Marshal.GetLastWin32Error()); _oldBitmap = SelectObject(_dc, _bitmap);
        Debug.Log("[NativeLive2DOverlay] Win32 layered model window created.");
    }
    public string GetDiagnostics() { return "mode=sync-45fps, rect=" + _screenRect + ", size=" + _width + "x" + _height + ", lastSuccess=" + _lastSuccessfulPresentAt.ToString("F2") + ", failed=" + _failed + ", reason=" + _lastFailure; }
    private void FailOverlay(Exception exception) { _failed = true; _lastFailure = exception.Message; Debug.LogError("[NativeLive2DOverlay] sync present failed: rect=" + _screenRect + ", targetFps=" + TargetFrameRate + ", lastSuccess=" + _lastSuccessfulPresentAt.ToString("F2") + ", reason=" + _lastFailure); DisposeOverlay(); }
    public void DisposeOverlay() { DisposeWindow(); if (_readback != null) { Destroy(_readback); _readback = null; } _bgraBuffer = null; }
    private void OnDestroy() { DisposeOverlay(); }
    private void DisposeWindow() { if (_dc != IntPtr.Zero && _oldBitmap != IntPtr.Zero) SelectObject(_dc, _oldBitmap); if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap); if (_dc != IntPtr.Zero) DeleteDC(_dc); if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd); _hwnd = _dc = _bitmap = _oldBitmap = _bits = IntPtr.Zero; _width = _height = 0; }
    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam) { if (message == WM_NCHITTEST) return new IntPtr(HTTRANSPARENT); if (message == WM_DESTROY) return IntPtr.Zero; return DefWindowProcW(hwnd, message, wParam, lParam); }
#else
    public void SetSource(RenderTexture source, Rect screenRect) { }
    public void DisposeOverlay() { }
    public string GetDiagnostics() { return "editor-no-native-overlay"; }
#endif
}
