using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 系统图标提取 — 纯 Win32 P/Invoke 方案
///
/// ⚠️ Tuanjie 2022.3 (Unity) 不含 System.Drawing，
///    不能用 Icon.FromHandle()，改用 SHGetFileInfo + 手动像素提取。
///
/// V1 先用颜色块 fallback 保底，图标提取稳定后再切真实图标。
/// </summary>
public static class DockIconProvider
{
    #region Win32 API

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    #endregion

    #region 颜色块 Fallback 表

    private static readonly Dictionary<string, (Color bg, string label)> ColorMap = new()
    {
        [".pdf"]  = (new Color(0.85f, 0.24f, 0.24f), "PDF"),
        [".zip"]  = (new Color(0.85f, 0.73f, 0.16f), "ZIP"),
        [".rar"]  = (new Color(0.85f, 0.73f, 0.16f), "RAR"),
        [".7z"]   = (new Color(0.85f, 0.73f, 0.16f), "7Z"),
        [".exe"]  = (new Color(0.24f, 0.48f, 0.85f), "EXE"),
        [".msi"]  = (new Color(0.24f, 0.48f, 0.85f), "MSI"),
        [".txt"]  = (new Color(0.75f, 0.75f, 0.75f), "TXT"),
        [".md"]   = (new Color(0.75f, 0.75f, 0.75f), "MD"),
        [".log"]  = (new Color(0.75f, 0.75f, 0.75f), "LOG"),
        [".jpg"]  = (new Color(0.24f, 0.75f, 0.45f), "JPG"),
        [".jpeg"] = (new Color(0.24f, 0.75f, 0.45f), "JPG"),
        [".png"]  = (new Color(0.24f, 0.75f, 0.45f), "PNG"),
        [".gif"]  = (new Color(0.24f, 0.75f, 0.45f), "GIF"),
        [".bmp"]  = (new Color(0.24f, 0.75f, 0.45f), "BMP"),
        [".doc"]  = (new Color(0.24f, 0.48f, 0.85f), "DOC"),
        [".docx"] = (new Color(0.24f, 0.48f, 0.85f), "DOC"),
        [".xls"]  = (new Color(0.24f, 0.75f, 0.45f), "XLS"),
        [".xlsx"] = (new Color(0.24f, 0.75f, 0.45f), "XLS"),
        [".ppt"]  = (new Color(0.85f, 0.44f, 0.16f), "PPT"),
        [".pptx"] = (new Color(0.85f, 0.44f, 0.16f), "PPT"),
        [".lnk"]  = (new Color(0.55f, 0.55f, 0.85f), "LNK"),
    };

    private static readonly (Color bg, string label) DefaultColor = (new Color(0.50f, 0.50f, 0.50f), "FIL");

    #endregion

    #region 缓存

    private static readonly Dictionary<string, Sprite> _cache = new();

    /// <summary>
    /// 获取文件类型图标（按扩展名缓存）。
    /// V1 先用颜色块，将来可切为真实系统图标。
    /// </summary>
    /// <param name="filePath">目标文件路径</param>
    /// <param name="size">图标尺寸（像素）</param>
    /// <param name="callback">回调返回 Sprite（可能为 null）</param>
    public static void GetIcon(string filePath, int size, Action<Sprite> callback)
    {
        string ext = Path.GetExtension(filePath)?.ToLowerInvariant() ?? "";

        // 缓存命中
        if (_cache.TryGetValue(ext, out Sprite cached) && cached != null)
        {
            callback(cached);
            return;
        }

        // 生成颜色块
        Sprite generated = GenerateFallbackSprite(ext, size);
        _cache[ext] = generated;
        callback(generated);
    }

    /// <summary>清空缓存（文件类型变化时调用）</summary>
    public static void ClearCache()
    {
        _cache.Clear();
    }

    #endregion

    #region 颜色块生成

    /// <summary>
    /// 按扩展名生成颜色块 Sprite
    /// </summary>
    private static Sprite GenerateFallbackSprite(string ext, int size)
    {
        if (!ColorMap.TryGetValue(ext, out var entry))
            entry = DefaultColor;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[size * size];

        // 填充背景色
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = entry.bg;

        tex.SetPixels(pixels);
        tex.Apply();

        Sprite sprite = Sprite.Create(tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect);

        return sprite;
    }

    #endregion
}
