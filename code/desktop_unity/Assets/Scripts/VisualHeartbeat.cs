using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 视觉心跳 — 桌面宠物的「法眼」
///
/// 零 LLM 成本的后台屏幕感知：
/// 1. 每 N 分钟用 Win32 GDI StretchBlt 截取 32×24 缩略图（纯内存，无磁盘 IO）
/// 2. 8×6 网格块亮度比较 → 检测画面变化幅度
/// 3. 大幅变化 → 惊讶表情（宠物看到你切屏）
/// 4. 长期静止 → 好奇/困惑表情（宠物觉得你在发呆）
///
/// 不调用任何视觉模型，仅像素比较。
/// </summary>
public class VisualHeartbeat : MonoBehaviour
{
    [Header("📷 采集")]
    [Tooltip("缩略图宽度（越小越快，默认 32）")]
    public int thumbWidth = 32;

    [Tooltip("缩略图高度（默认 24）")]
    public int thumbHeight = 24;

    [Tooltip("采集间隔（秒），默认 5 分钟")]
    public float captureInterval = 300f;

    [Header("🎯 检测")]
    [Tooltip("变化阈值（0~1），越大表示画面变化越剧烈才触发")]
    public float changeThreshold = 0.15f;

    [Tooltip("连续几次无变化视为静止画面（默认 3 次 = 15min）")]
    public int staticCountThreshold = 3;

    [Tooltip("事件冷却（秒），避免频繁触发")]
    public float eventCooldown = 60f;

    [Header("🎭 反应")]
    [Tooltip("画面突变时播放的表情")]
    public string changeExpression = "surprise";

    [Tooltip("长期静止时播放的表情")]
    public string staticExpression = "surprise";

    // ===== 内部状态 =====
    private Live2DRenderer _renderer;
    private float _captureTimer = 0f;
    private float _lastEventTime = -999f;
    private byte[] _prevThumb;
    private int _staticCount;
    private bool _skipFirst = true; // 跳过首次（Hot-plug 防误触）

    // GDI 句柄（复用避免反复创建/销毁）
    private IntPtr _screenDc = IntPtr.Zero;
    private IntPtr _memDc = IntPtr.Zero;
    private IntPtr _bitmap = IntPtr.Zero;
    private IntPtr _oldBitmap = IntPtr.Zero;
    private bool _gdiReady;

    void Start()
    {
        _renderer = GetComponent<Live2DRenderer>();
        if (_renderer == null)
            Debug.LogWarning("[VisualHeartbeat] 找不到 Live2DRenderer，视觉反应将无效");

        Debug.Log($"[VisualHeartbeat] 👁️ 法眼启动，采集间隔={captureInterval}s，变化阈值={changeThreshold}");
    }

    void OnDestroy()
    {
        ReleaseGdi();
    }

    void Update()
    {
        _captureTimer += Time.deltaTime;
        if (_captureTimer < captureInterval) return;
        _captureTimer = 0f;

        CaptureAndCompare();
    }

    // =================================================================
    //  核心逻辑
    // =================================================================

    private void CaptureAndCompare()
    {
        if (_skipFirst)
        {
            // 首次采集只做缓存，不触发任何反应（给 GDI 暖身）
            var first = CaptureThumbnail();
            if (first != null)
            {
                _prevThumb = first;
                _skipFirst = false;
            }
            return;
        }

        byte[] thumb = CaptureThumbnail();
        if (thumb == null) return;

        // ——— 变化检测 ———
        float diff = _prevThumb != null ? ComputeBlockDifference(_prevThumb, thumb) : 0f;
        _prevThumb = thumb;

        if (diff > changeThreshold)
        {
            // 画面突变 → 惊讶
            _staticCount = 0;
            TriggerReaction(changeExpression, diff);
        }
        else
        {
            _staticCount++;
            if (_staticCount >= staticCountThreshold && !IsUserIdle())
            {
                // 长期静止（用户没离开但画面没变）→ 好奇
                TriggerReaction(staticExpression, 0f);
                _staticCount = 0; // 重置避免反复触
            }
        }
    }

    /// <summary>8×6 网格块亮度比较，返回 0~1 差异度</summary>
    private float ComputeBlockDifference(byte[] a, byte[] b)
    {
        const int cols = 8, rows = 6;
        int bw = thumbWidth / cols;
        int bh = thumbHeight / rows;
        float totalDiff = 0f;
        int blockCount = 0;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                float lumA = 0f, lumB = 0f;
                int count = 0;

                for (int y = r * bh; y < (r + 1) * bh && y < thumbHeight; y++)
                {
                    int rowOff = y * thumbWidth * 4;
                    for (int x = c * bw; x < (c + 1) * bw && x < thumbWidth; x++)
                    {
                        int idx = rowOff + x * 4;
                        // DDB 在 32bpp 下通常为 BGRA
                        lumA += 0.299f * a[idx + 2] + 0.587f * a[idx + 1] + 0.114f * a[idx];
                        lumB += 0.299f * b[idx + 2] + 0.587f * b[idx + 1] + 0.114f * b[idx];
                        count++;
                    }
                }

                if (count > 0)
                {
                    totalDiff += Mathf.Abs(lumA - lumB) / (count * 255f);
                    blockCount++;
                }
            }
        }

        return blockCount > 0 ? totalDiff / blockCount : 0f;
    }

    private bool IsUserIdle()
    {
        return ActivityTracker.Instance != null &&
               ActivityTracker.Instance.CurrentCategory == "idle";
    }

    private void TriggerReaction(string expression, float magnitude)
    {
        float now = Time.time;
        if (now - _lastEventTime < eventCooldown) return;
        _lastEventTime = now;

        if (_renderer != null)
        {
            _renderer.ForceAction($"exp:{expression}");
            Debug.Log($"[VisualHeartbeat] ⚡ 触发表情: {expression} (变化幅度={magnitude:F3})");
        }
    }

    // =================================================================
    //  Win32 GDI 缩略图采集
    // =================================================================

    private byte[] CaptureThumbnail()
    {
        if (!_gdiReady && !InitGdi())
            return null;

        try
        {
            int screenW = Screen.width;
            int screenH = Screen.height;

            bool ok = StretchBlt(_memDc, 0, 0, thumbWidth, thumbHeight,
                                 _screenDc, 0, 0, screenW, screenH, SRCCOPY);
            if (!ok) return null;

            int byteCount = thumbWidth * thumbHeight * 4;
            byte[] pixels = new byte[byteCount];
            if (GetBitmapBits(_bitmap, byteCount, pixels) == 0)
                return null;

            return pixels;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[VisualHeartbeat] 采集失败: {e.Message}");
            return null;
        }
    }

    private bool InitGdi()
    {
        ReleaseGdi();

        _screenDc = CreateDC("DISPLAY", null, IntPtr.Zero, IntPtr.Zero);
        if (_screenDc == IntPtr.Zero) return false;

        _memDc = CreateCompatibleDC(_screenDc);
        if (_memDc == IntPtr.Zero) { ReleaseGdi(); return false; }

        _bitmap = CreateCompatibleBitmap(_screenDc, thumbWidth, thumbHeight);
        if (_bitmap == IntPtr.Zero) { ReleaseGdi(); return false; }

        _oldBitmap = SelectObject(_memDc, _bitmap);
        _gdiReady = true;
        return true;
    }

    private void ReleaseGdi()
    {
        if (_memDc != IntPtr.Zero && _oldBitmap != IntPtr.Zero)
            SelectObject(_memDc, _oldBitmap);
        if (_bitmap != IntPtr.Zero) { DeleteObject(_bitmap); _bitmap = IntPtr.Zero; }
        if (_memDc != IntPtr.Zero) { DeleteDC(_memDc); _memDc = IntPtr.Zero; }
        if (_screenDc != IntPtr.Zero) { DeleteDC(_screenDc); _screenDc = IntPtr.Zero; }
        _oldBitmap = IntPtr.Zero;
        _gdiReady = false;
    }

    // =================================================================
    //  Win32 P/Invoke
    // =================================================================

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDC(string lpszDriver, string lpszDevice,
        IntPtr lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest,
        int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern int GetBitmapBits(IntPtr hbmp, int cbBuffer, byte[] lpvBits);

    private const uint SRCCOPY = 0x00CC0020;
}
