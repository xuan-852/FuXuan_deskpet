using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// 法阵公用术器 — 所有工具共享的辅助方法集合
/// 包含：JSON 解析、P/Invoke（剪贴板/音量/锁屏/鼠标/通知）、文件搜索、回收站等
/// </summary>
public static class ToolHelpers
{
    public sealed class IndexedSearchResult
    {
        public bool Succeeded;
        public List<string> Results;
        public string FailureReason;
    }
    /// <summary>
    /// 仅供 EditMode 测试替换 Shell 打开行为。生产环境保持 null，仍使用系统默认关联程序。
    /// 返回非空异常表示模拟启动失败。
    /// </summary>
    public static Func<ProcessStartInfo, Exception> ShellOpenOverrideForTests;

    /// <summary>仅供 EditMode 覆盖 Everything 探测结果，null 表示按真实环境探测。</summary>
    public static Func<string> EverythingCliOverrideForTests;

    /// <summary>仅供 EditMode 替换 Windows 搜索索引，生产环境为 null。</summary>
    public static Func<string, string, int, IndexedSearchResult> WindowsSearchOverrideForTests;

    /// <summary>日志脱敏：工具参数/结果不得把凭据或隐私内容写入 Player.log。</summary>
    public static string SanitizeLogValue(string toolName, string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (string.Equals(toolName, "file_read", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "get_clipboard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "take_screenshot", StringComparison.OrdinalIgnoreCase))
            return "[sensitive content omitted]";

        string redacted = System.Text.RegularExpressions.Regex.Replace(
            value,
            @"(?i)(token|password|passwd|secret|api[_-]?key|authorization)(\s*[:=]\s*)[^,;\s}]+",
            "$1$2[REDACTED]");
        if (redacted.Length > 400) redacted = redacted.Substring(0, 400) + "…";
        return redacted;
    }

    // ================================================================
    //  P/Invoke 声明
    // ================================================================

    #region P/Invoke

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);
    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);
    [DllImport("user32.dll")]
    private static extern bool SetClipboardData(uint uFormat, IntPtr hMem);
    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVABLE = 0x0002;
    private const uint GMEM_ZEROINIT = 0x0040;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
    private const uint MB_OK = 0x000000;
    private const uint MB_ICONINFORMATION = 0x00000040;

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    // 回收站
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);
    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
    }

    private const uint FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;

    #endregion

    // ================================================================
    //  剪贴板
    // ================================================================

    public static string GetClipboardText()
    {
        if (!OpenClipboard(IntPtr.Zero)) return "";
        try
        {
            IntPtr hData = GetClipboardData(CF_UNICODETEXT);
            if (hData == IntPtr.Zero) return "";
            IntPtr ptr = GlobalLock(hData);
            if (ptr == IntPtr.Zero) return "";
            try { return Marshal.PtrToStringUni(ptr); }
            finally { GlobalUnlock(hData); }
        }
        finally { CloseClipboard(); }
    }

    public static void SetClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
            IntPtr hMem = GlobalAlloc(GMEM_MOVABLE | GMEM_ZEROINIT, (UIntPtr)bytes.Length);
            if (hMem == IntPtr.Zero) return;
            IntPtr ptr = GlobalLock(hMem);
            if (ptr != IntPtr.Zero)
            {
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                GlobalUnlock(hMem);
            }
            SetClipboardData(CF_UNICODETEXT, hMem);
        }
        finally { CloseClipboard(); }
    }

    // ================================================================
    //  音量（Core Audio 直控，精度 0-100）
    // ================================================================

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDevice ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
        [PreserveSig] int GetDevice(string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IAudioEndpointVolume ppInterface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
        [PreserveSig] int GetId(out IntPtr ppstrId);
        [PreserveSig] int GetState(out int pdwState);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute(bool bMute, ref Guid pguidEventContext);
        [PreserveSig] int GetMute(out bool pbMute);
    }

    private static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
    private const int CLSCTX_INPROC_SERVER = 0x1;
    private const int EDataFlow_Render = 0;
    private const int ERole_Multimedia = 1;

    /// <summary>设置系统主音量（0-100）。Core Audio 直控；仅当 COM 不可用时回退按键模拟。</summary>
    public static void SetSystemVolume(int level)
    {
        level = Mathf.Clamp(level, 0, 100);
        if (TrySetVolumeScalar(level / 100f)) return;
        // 回退：按键模拟。先尽力读当前值，再按差值调节（读不到则从 0 起跳，不再盲目假设 50）
        int current = GetSystemVolume();
        if (current < 0) current = 0;
        for (int i = current; i < level; i++) PressVolumeKey(true);
        for (int i = level; i < current; i++) PressVolumeKey(false);
    }

    /// <summary>读取系统主音量（0-100），失败返回 -1</summary>
    public static int GetSystemVolume()
    {
        try
        {
            Guid iid = IID_IAudioEndpointVolume; // static readonly 不能作 ref 实参，用局部变量
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice device;
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow_Render, ERole_Multimedia, out device) != 0) return -1;
            IAudioEndpointVolume volume;
            if (device.Activate(ref iid, CLSCTX_INPROC_SERVER, IntPtr.Zero, out volume) != 0) return -1;
            float scalar;
            int hr = volume.GetMasterVolumeLevelScalar(out scalar);
            Marshal.ReleaseComObject(volume);
            Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
            if (hr != 0) return -1;
            return Mathf.RoundToInt(Mathf.Clamp01(scalar) * 100f);
        }
        catch { return -1; }
    }

    private static bool TrySetVolumeScalar(float scalar)
    {
        try
        {
            Guid iid = IID_IAudioEndpointVolume; // static readonly 不能作 ref 实参，用局部变量
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice device;
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow_Render, ERole_Multimedia, out device) != 0) return false;
            IAudioEndpointVolume volume;
            if (device.Activate(ref iid, CLSCTX_INPROC_SERVER, IntPtr.Zero, out volume) != 0) return false;
            Guid ctx = Guid.Empty; // static readonly 不能作 ref 实参，用局部变量
            int hr = volume.SetMasterVolumeLevelScalar(Mathf.Clamp01(scalar), ref ctx);
            Marshal.ReleaseComObject(volume);
            Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
            return hr == 0;
        }
        catch { return false; }
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    private const byte VK_VOLUME_UP = 0xAF;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static void PressVolumeKey(bool up)
    {
        byte vk = up ? VK_VOLUME_UP : VK_VOLUME_DOWN;
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    // ================================================================
    //  通知
    // ================================================================

    public static void ShowNotification(string title, string message)
    {
        // 模态 MessageBox 会阻塞主线程 → 放到线程池，避免桌宠卡死
        try
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { MessageBox(IntPtr.Zero, message, title, MB_OK | MB_ICONINFORMATION); }
                catch { }
            });
        }
        catch { }
    }

    // ================================================================
    //  JSON 解析
    // ================================================================

    public static string JsonRead(string json, string key)
    {
        string search = $"\"{key}\":\"";
        int idx = json.IndexOf(search);
        if (idx < 0) { search = $"\"{key}\": \""; idx = json.IndexOf(search); }
        if (idx >= 0)
        {
            idx += search.Length;
            var sb = new StringBuilder();
            for (int i = idx; i < json.Length; i++)
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    char n = json[i + 1];
                    if (n == '"') { sb.Append('"'); i++; }
                    else if (n == '\\') { sb.Append('\\'); i++; }
                    else if (n == 'n') { sb.Append('\n'); i++; }
                    else if (n == 'u' && i + 5 < json.Length)
                    {
                        try { string hex = json.Substring(i + 2, 4); sb.Append((char)Convert.ToInt32(hex, 16)); i += 5; }
                        catch { sb.Append(json[i]); }
                    }
                    else sb.Append(json[i]);
                }
                else if (json[i] == '"') break;
                else sb.Append(json[i]);
            }
            return sb.ToString().Trim();
        }

        search = $"\"{key}\":";
        idx = json.IndexOf(search);
        if (idx >= 0)
        {
            idx += search.Length;
            var sb = new StringBuilder();
            for (int i = idx; i < json.Length; i++)
            {
                char c = json[i];
                if (c == ',' || c == '}' || c == ']') break;
                sb.Append(c);
            }
            return sb.ToString().Trim().Trim('"');
        }

        return "";
    }

    public static Dictionary<string, float> JsonReadDict(string json, string objKey)
    {
        var dict = new Dictionary<string, float>();
        if (string.IsNullOrEmpty(json)) return dict;

        string search = $"\"{objKey}\":";
        int start = json.IndexOf(search);
        if (start < 0) return dict;
        start += search.Length;
        while (start < json.Length && json[start] != '{') start++;
        if (start >= json.Length) return dict;
        start++;

        int braceDepth = 1;
        int end = start;
        while (end < json.Length && braceDepth > 0)
        {
            if (json[end] == '{') braceDepth++;
            else if (json[end] == '}') braceDepth--;
            end++;
        }
        if (braceDepth != 0) return dict;

        string objContent = json.Substring(start, end - start - 1);
        var parts = objContent.Split(',');
        foreach (var part in parts)
        {
            var eq = part.IndexOf(':');
            if (eq < 0) continue;
            string k = part.Substring(0, eq).Trim().Trim('"');
            float v;
            if (float.TryParse(part.Substring(eq + 1).Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v))
                dict[k] = v;
        }
        return dict;
    }

    public static string EscapeJsonStr(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    // ================================================================
    //  系统信息
    // ================================================================

    public static string GetSystemInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🖥️ {Environment.OSVersion}");
        sb.AppendLine($"💾 处理器: {Environment.ProcessorCount} 核");
        try
        {
            string sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows)[..2] + "\\";
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.IsReady && d.Name == sysRoot)
                {
                    sb.AppendLine($"💿 系统盘: {FormatFileSize(d.TotalSize)} 总 / {FormatFileSize(d.AvailableFreeSpace)} 空");
                    break;
                }
            }
        }
        catch { }
        try
        {
            // PerformanceCounter 在 Unity Mono 中不可用，跳过
            sb.AppendLine($"🧠 可用内存: 查询暂不支持 (Unity Mono)");
        }
        catch { }
        sb.AppendLine($"⏱️ 运行时间: {TimeSpan.FromMilliseconds(Environment.TickCount):dd\\.hh\\:mm\\:ss}");
        sb.AppendLine($"🔤 系统语言: {System.Globalization.CultureInfo.InstalledUICulture.DisplayName}");

        // P4.4: 多屏感知 — 虚拟桌面尺寸大于主屏即存在多显示器
        try
        {
            int w = GetSystemMetrics(SM_CXSCREEN);
            int h = GetSystemMetrics(SM_CYSCREEN);
            int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            bool multi = vw > w || vh > h;
            sb.AppendLine(multi
                ? $"🖥️ 显示器: {w}x{h} 主屏（检测到多屏，虚拟桌面 {vw}x{vh}）"
                : $"🖥️ 显示器: {w}x{h} 单屏");
        }
        catch { }
        return sb.ToString().TrimEnd();
    }

    // ================================================================
    //  文件搜索
    // ================================================================

    /// <summary>通过 Windows Shell 打开文件、目录或 URL，并把系统异常转换为可读错误。</summary>
    public static bool TryShellOpen(string target, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(target))
        {
            error = "未指定要打开的目标";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(target) { UseShellExecute = true };
            if (ShellOpenOverrideForTests != null)
            {
                Exception simulated = ShellOpenOverrideForTests(startInfo);
                if (simulated != null) throw simulated;
            }
            else
            {
                Process.Start(startInfo);
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>把桌面、下载、文档等别名转换为用户真实目录；空值默认桌面。</summary>
    public static string ResolveFolderPath(string rawPath, bool defaultToDesktop = true)
    {
        string value = DecodeFileUri(rawPath ?? "").Trim().Trim('"');
        if (string.IsNullOrEmpty(value))
            return defaultToDesktop ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) : "";

        string lower = value.ToLowerInvariant();
        if (lower == "desktop" || value.Contains("桌面"))
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (lower == "downloads" || value.Contains("下载"))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (lower == "documents" || lower == "document" || value.Contains("文档"))
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (lower == "pictures" || lower == "picture" || value.Contains("图片"))
            return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (lower == "music" || value.Contains("音乐"))
            return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (lower == "videos" || lower == "video" || value.Contains("视频"))
            return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        try { return Path.GetFullPath(value); }
        catch { return value; }
    }

    /// <summary>
    /// 从自然语言中解析文件搜索范围：显式存在路径优先，其次处理常用目录别名和开发项目根。
    /// 空字符串代表由 Everything 执行全盘搜索或由安全目录回退策略决定范围。
    /// </summary>
    public static string ResolveSearchRoot(string userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage)) return "";

        string explicitPath = ExtractExistingPathFromText(userMessage);
        if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;

        if (userMessage.Contains("项目") && TryGetDevelopmentProjectRoot(out string projectRoot))
            return projectRoot;

        if (userMessage.Contains("桌面")) return ResolveFolderPath("Desktop", false);
        if (userMessage.Contains("下载")) return ResolveFolderPath("Downloads", false);
        if (userMessage.Contains("文档")) return ResolveFolderPath("Documents", false);
        if (userMessage.Contains("图片")) return ResolveFolderPath("Pictures", false);
        return "";
    }

    /// <summary>Everything 不可用时的安全搜索根：显式路径优先，否则用户目录、数据目录和开发项目。</summary>
    public static List<string> GetSafeSearchRoots(string requestedRoot)
    {
        var roots = new List<string>();
        AddSafeSearchRoot(roots, requestedRoot);
        if (roots.Count > 0) return roots;

        AddSafeSearchRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddSafeSearchRoot(roots, DataPathConfig.DataRoot);
        if (TryGetDevelopmentProjectRoot(out string projectRoot)) AddSafeSearchRoot(roots, projectRoot);
        return roots;
    }

    /// <summary>按安全根依次递归搜索，保留一个全局结果上限。</summary>
    public static void SearchSafeRoots(IEnumerable<string> roots, string query, List<string> results, int maxResults)
    {
        if (roots == null || results == null) return;
        foreach (string root in roots)
        {
            if (results.Count >= maxResults) break;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            SearchRecursive(root, query, results, maxResults, skipSystemDirs: true);
        }
    }

    public static string FormatSearchRoots(IEnumerable<string> roots)
    {
        if (roots == null) return "安全目录";
        var names = new List<string>();
        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            names.Add(root);
            if (names.Count >= 3) break;
        }
        return names.Count == 0 ? "安全目录" : string.Join("、", names);
    }

    private static void AddSafeSearchRoot(List<string> roots, string candidate)
    {
        if (roots == null || string.IsNullOrWhiteSpace(candidate)) return;
        string full;
        try { full = Path.GetFullPath(DecodeFileUri(candidate)); }
        catch { return; }
        if (!Directory.Exists(full) || !IsPathAllowed(full)) return;
        foreach (string existing in roots)
            if (string.Equals(existing, full, StringComparison.OrdinalIgnoreCase)) return;
        roots.Add(full);
    }

    private static string ExtractExistingPathFromText(string text)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(
            text ?? "", @"(?<![A-Za-z0-9])(?:[A-Za-z]:[\\/]|\\\\)[^\""<>|?*\r\n，。；！？]*");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            string candidate = match.Value.Trim().Trim('"', '\'', '。', '，', '；', '！', '？');
            int suffix = candidate.IndexOfAny(new[] { '里', '中', '的' });
            if (suffix > 2) candidate = candidate.Substring(0, suffix).TrimEnd('\\', '/');
            try
            {
                string full = Path.GetFullPath(candidate);
                if (Directory.Exists(full) && IsPathAllowed(full)) return full;
            }
            catch { }
        }
        return "";
    }

    private static bool TryGetDevelopmentProjectRoot(out string projectRoot)
    {
        projectRoot = "";
        var starts = new[] { Application.dataPath, Directory.GetCurrentDirectory() };
        foreach (string start in starts)
        {
            if (string.IsNullOrWhiteSpace(start)) continue;
            DirectoryInfo current;
            try { current = new DirectoryInfo(start); }
            catch { continue; }
            for (int i = 0; current != null && i < 8; i++, current = current.Parent)
            {
                string candidate = current.FullName;
                if (File.Exists(Path.Combine(candidate, "README.md"))
                    && Directory.Exists(Path.Combine(candidate, "code", "desktop_unity")))
                {
                    projectRoot = candidate;
                    return true;
                }
            }
        }
        return false;
    }

    public static string FastWhich(string name)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd", $"/c chcp 65001 >nul & where {name}")
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8
            };
            var p = Process.Start(psi);
            if (p != null)
            {
                string line = p.StandardOutput.ReadLine();
                p.WaitForExit(1000);
                if (!string.IsNullOrEmpty(line) && !line.StartsWith("警告", StringComparison.OrdinalIgnoreCase)
                    && !line.StartsWith("信息", StringComparison.OrdinalIgnoreCase))
                    return line.Trim();
            }
        }
        catch { }
        return null;
    }

    /// <summary>查找 Everything 命令行客户端 es.exe</summary>
    /// <remarks>
    /// ★ 修复（2026-08-10）：旧实现只查 PATH，但 Everything 默认装到
    ///   %LOCALAPPDATA%\Everything（不进 PATH）→ es.exe 永远找不到 → 每次搜索
    ///   都退化成全目录递归遍历（用户观察到的"全文件遍历"）。现在按可能性
    ///   从高到低探测：PATH → 官方安装器常见位置。
    ///   注：Unity .NET Standard profile 无 Microsoft.Win32.Registry，故不做
    ///   注册表探测（%LOCALAPPDATA%\Everything 已覆盖官方默认安装位置）。
    /// </remarks>
    public static string FindEverythingCli()
    {
        if (EverythingCliOverrideForTests != null)
            return EverythingCliOverrideForTests();

        try
        {
            // 1) 显式配置（便携版、非标准盘符或企业软件分发目录）
            string configured = Environment.GetEnvironmentVariable("FU_XUAN_EVERYTHING_ES");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured.Trim()))
                return configured.Trim();

            // 2) PATH（便携版/手动添加过）
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
            foreach (var dir in paths)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                string test = Path.Combine(dir.Trim(), "es.exe");
                if (File.Exists(test)) return test;
            }

            // 3) Everything 官方安装器的默认位置（用户级 / 全局）
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Everything", "es.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Everything", "es.exe"),
                @"C:\Program Files\Everything\es.exe",
                @"C:\Program Files (x86)\Everything\es.exe",
                @"D:\Everything\es.exe",
                @"D:\Program Files\Everything\es.exe",
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
        }
        catch { }
        return null;
    }

    /// <summary>
    /// 用 Everything CLI (es.exe) 搜索，返回结果列表；不可用或失败返回 null（调用方回退递归）。
    ///
    /// ★ 修复（2026-08-12）：Unity Mono 运行时是 .NET Standard 2.0 profile，缺少
    ///   I18N.CJK.dll → Encoding.GetEncoding(936) 抛异常（旧实现每次都被 catch 静默吞掉，
    ///   导致搜索永远降级成递归）。改用 es.exe -export-txt 导出 UTF-8 BOM 临时文件再读回，
    ///   完全绕开 GBK 解码。
    /// </summary>
    public static List<string> SearchWithEverything(string query, string rootDir, int maxResults = 200)
    {
        return TrySearchWithEverything(query, rootDir, maxResults, out var results, out _) ? results : null;
    }

    /// <summary>
    /// 查询 Windows Search 的 SYSTEMINDEX。它只覆盖用户在 Windows 中已建立索引的位置，
    /// 因此只能作为 Everything 不可用后的快速后备，而不是伪装成全盘搜索。
    /// </summary>
    public static bool TrySearchWindowsIndex(string query, string rootDir, int maxResults,
        out List<string> results, out string failureReason)
    {
        results = null;
        failureReason = null;
        if (WindowsSearchOverrideForTests != null)
        {
            IndexedSearchResult simulated = WindowsSearchOverrideForTests(query, rootDir, maxResults);
            results = simulated?.Results;
            failureReason = simulated?.FailureReason;
            return simulated != null && simulated.Succeeded;
        }

        try
        {
            string safeQuery = (query ?? "").Replace("'", "''");
            string where = $"System.FileName LIKE '%{safeQuery}%'";
            if (!string.IsNullOrWhiteSpace(rootDir))
            {
                string safeRoot = Path.GetFullPath(rootDir).TrimEnd('\\', '/').Replace("'", "''");
                where += $" AND System.ItemPathDisplay LIKE '{safeRoot.Replace("\\", "\\\\")}\\\\%'";
            }
            string script = "$ErrorActionPreference='Stop';[Console]::OutputEncoding=[Text.Encoding]::UTF8;" +
                "$c=New-Object -ComObject ADODB.Connection;$c.Open(\"Provider=Search.CollatorDSO;Extended Properties='Application=Windows';\");" +
                $"$r=$c.Execute(\"SELECT TOP {Math.Max(1, Math.Min(maxResults, 200))} System.ItemPathDisplay FROM SYSTEMINDEX WHERE {where}\");" +
                "while(-not $r.EOF){$p=$r.Fields.Item('System.ItemPathDisplay').Value;if($p){[Console]::WriteLine($p)};$r.MoveNext()};$r.Close();$c.Close();";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) { failureReason = "无法启动 Windows Search 查询"; return false; }
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } failureReason = "Windows Search 查询超时"; return false; }
                if (p.ExitCode != 0)
                {
                    failureReason = "Windows 搜索索引不可用";
                    return false;
                }
                results = stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                return true;
            }
        }
        catch { failureReason = "Windows 搜索索引不可用"; return false; }
    }

    /// <summary>
    /// 使用 Everything 搜索，并保留不可用原因供界面作诚实的降级说明。
    /// Everything 的 CLI 是通过当前用户会话中的 IPC 窗口通信；找到 es.exe 不等于 IPC 一定可用。
    /// </summary>
    public static bool TrySearchWithEverything(string query, string rootDir, int maxResults,
        out List<string> results, out string failureReason)
    {
        results = null;
        failureReason = null;
        string esExe = FindEverythingCli();
        if (esExe == null)
        {
            failureReason = "未找到 Everything 命令行客户端 es.exe";
            return false;
        }

        // 默认 IPC 协议失败时依次兼容 Everything 1.4 的三种 IPC 协议。
        // 这也覆盖用户升级/降级 Everything 后保留旧客户端的情况。
        string[] ipcModes = { "", "-ipc1", "-ipc2", "-ipc3" };
        string lastError = null;
        foreach (var ipcMode in ipcModes)
        {
            if (TryRunEverything(esExe, ipcMode, query, rootDir, maxResults, out results, out string error))
                return true;
            lastError = error;
        }

        failureReason = string.IsNullOrWhiteSpace(lastError)
            ? "Everything 未能返回搜索结果"
            : $"Everything IPC 不可用（{lastError}）";
        return false;
    }

    private static bool TryRunEverything(string esExe, string ipcMode, string query, string rootDir,
        int maxResults, out List<string> results, out string error)
    {
        results = null;
        error = null;
        string tmpFile = Path.Combine(Path.GetTempPath(), "es_search_" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            string exportArgs = $"{ipcMode} -n {maxResults} -no-header -utf8-bom -export-txt \"{tmpFile}\"";
            if (!string.IsNullOrEmpty(rootDir))
                exportArgs += $" -path \"{rootDir.Replace("\"", "\\\"")}\"";
            exportArgs += $" \"{query.Replace("\"", "\\\"")}\"";

            var psi = new ProcessStartInfo(esExe, exportArgs)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) { error = "无法启动 es.exe"; return false; }
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(5000))
                {
                    try { p.Kill(); } catch { }
                    error = "es.exe 响应超时";
                    return false;
                }
                if (p.ExitCode != 0)
                {
                    error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    error = string.IsNullOrWhiteSpace(error) ? $"es.exe 退出码 {p.ExitCode}" : error.Trim();
                    return false;
                }
            }
            if (!File.Exists(tmpFile)) { error = "es.exe 未生成结果文件"; return false; }

            results = new List<string>();
            foreach (var line in File.ReadAllLines(tmpFile, Encoding.UTF8))
                if (!string.IsNullOrWhiteSpace(line)) results.Add(line);
            return true;
        }
        catch (Exception e) { error = e.Message; return false; }
        finally { try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { } }
    }

    public static void SearchRecursive(string dir, string query, List<string> results, int maxResults, bool skipSystemDirs = false)
    {
        try
        {
            if (results.Count >= maxResults) return;
            foreach (var f in Directory.GetFiles(dir))
            {
                if (results.Count >= maxResults) return;
                try { if (Path.GetFileName(f).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) results.Add(f); }
                catch { }
            }
            foreach (var d in Directory.GetDirectories(dir))
            {
                if (results.Count >= maxResults) return;
                try { if (Path.GetFileName(d).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) results.Add(d); }
                catch { }
                // ★ 跳过系统/缓存目录：全盘递归时避免遍历 Windows、AppData、node_modules 等
                //   巨量目录（既慢又无意义），防止搜索耗时数分钟
                if (skipSystemDirs && IsSystemDir(Path.GetFileName(d))) continue;
                SearchRecursive(d, query, results, maxResults, skipSystemDirs);
            }
        }
        catch { }
    }

    /// <summary>判断目录是否属于系统/缓存目录（全盘搜索时跳过）</summary>
    public static bool IsSystemDir(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        switch (name.ToLowerInvariant())
        {
            // 系统核心
            case "windows":
            case "program files":
            case "program files (x86)":
            case "programdata":
            case "$recycle.bin":
            case "system volume information":
            case "perflogs":
            case "recovery":
            case "msocache":
            case "intel":
            case "amd":
            case "nvidia":
            case "drivers":
            case "boot":
            case "efi":
            // 用户缓存/AppData（海量小文件，几乎不含用户要找的文件）
            case "appdata":
            case "local settings":
            case "temp":
            case "cache":
            case "caches":
            case "logs":
            case "log":
            // 开发/构建产物
            case "node_modules":
            case ".git":
            case ".svn":
            case ".hg":
            case "library":
            case "obj":
            case ".vs":
            case ".idea":
            case ".vscode":
            case "build":
            case "dist":
            case "target":
            case "bin":
                return true;
            default:
                return false;
        }
    }

    public static string FastFindLink(string rootDir, string keyword)
    {
        try
        {
            if (!Directory.Exists(rootDir)) return null;
            foreach (var f in Directory.GetFiles(rootDir, "*.lnk"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return f;
            }
        }
        catch { }
        return null;
    }

    // ================================================================
    //  Python 桥接文件搜索
    // ================================================================

    public static string FindPythonExe()
    {
        try
        {
            var psi = new ProcessStartInfo("where", "python")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            var p = Process.Start(psi);
            if (p != null)
            {
                string line = p.StandardOutput.ReadLine();
                p.WaitForExit(1000);
                if (!string.IsNullOrEmpty(line)) return line;
            }
        }
        catch { }
        try
        {
            // 尝试找 python3
            var psi = new ProcessStartInfo("where", "python3")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            var p = Process.Start(psi);
            if (p != null)
            {
                string line = p.StandardOutput.ReadLine();
                p.WaitForExit(1000);
                if (!string.IsNullOrEmpty(line)) return line;
            }
        }
        catch { }
        return "python";
    }

    public static string FindPythonScript()
    {
        string scriptPath = Path.Combine(Application.dataPath, "Scripts", "search_file.py");
        if (File.Exists(scriptPath)) return scriptPath;
        return null;
    }

    public static string SearchFileByPython(string query, string rootDir)
    {
        string pyExe = FindPythonExe();
        string script = FindPythonScript();
        if (script == null || !File.Exists(script))
        {
            return SearchFileFallback(query, rootDir);
        }        try
        {
            string pyArgs = $"\"{script}\" \"{EscapeJsonStr(query)}\"";
            if (!string.IsNullOrEmpty(rootDir)) pyArgs += $" \"{EscapeJsonStr(rootDir)}\"";
            var psi = new ProcessStartInfo(pyExe, pyArgs)
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            var p = Process.Start(psi);
            if (p == null) return SearchFileFallback(query, rootDir);
            string output = p.StandardOutput.ReadToEnd();
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            if (p.ExitCode == 0 && !string.IsNullOrEmpty(output))
                return output.Trim();
            return SearchFileFallback(query, rootDir);
        }
        catch { return SearchFileFallback(query, rootDir); }
    }

    private static string SearchFileFallback(string query, string rootDir)
    {
        try
        {
            // ★ 优先 Everything（毫秒级全盘索引），失败才递归
            bool usedEverything = TrySearchWithEverything(query, rootDir, 100, out var esResults, out string everythingFailure);
            if (usedEverything)
            {
                if (esResults.Count == 0)
                {
                    string scope = string.IsNullOrEmpty(rootDir) ? "全境" : $"「{rootDir}」";
                    return $"🔍 在{scope}中未找到与「{query}」匹配的文件";
                }
                var sb0 = new StringBuilder();
                sb0.AppendLine($"⚡本座以 Everything 天眼通搜，得 {esResults.Count} 件与「{query}」相关之物：");
                foreach (var f in esResults) sb0.AppendLine($"  📄 {f}");
                return sb0.ToString();
            }

            bool usedWindowsIndex = TrySearchWindowsIndex(query, rootDir, 100, out var indexedResults, out string windowsSearchFailure);
            if (usedWindowsIndex)
            {
                string scope = string.IsNullOrEmpty(rootDir) ? "Windows 已索引位置" : $"Windows 索引中的「{rootDir}」";
                if (indexedResults.Count == 0)
                    return $"🔍 在{scope}中未找到与「{query}」匹配的文件（Everything 不可用，已使用 Windows 搜索索引）";
                var indexed = new StringBuilder();
                indexed.AppendLine($"⚡Everything 不可用，已使用 Windows 搜索索引（范围：{scope}），找到 {indexedResults.Count} 项与「{query}」相关的文件：");
                foreach (var f in indexedResults) indexed.AppendLine($"  📄 {f}");
                return indexed.ToString();
            }

            var results = new List<string>();
            List<string> roots = GetSafeSearchRoots(rootDir);
            SearchSafeRoots(roots, query, results, 100);
            string scope2 = FormatSearchRoots(roots);
            if (results.Count == 0)
                return $"🔍 在{scope2}中未找到与「{query}」匹配的文件（{everythingFailure}；{windowsSearchFailure}，已使用安全目录递归搜索）";
            var sb = new StringBuilder();
            sb.AppendLine($"🔍{everythingFailure}；{windowsSearchFailure}，已使用安全目录递归搜索（范围：{scope2}），找到 {results.Count} 项与「{query}」相关的文件：");
            foreach (var f in results) sb.AppendLine($"  📄 {f}");
            return sb.ToString();
        }
        catch (Exception e) { return $"❌ 搜索时出了岔子：{e.Message}"; }
    }

    // ================================================================
    //  文件操作辅助
    // ================================================================

    public static string DecodeFileUri(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith("file://"))
        {
            string rest = path.Substring("file://".Length);
            // file://server/share → \\server\share（UNC，不能裁掉开头的双斜杠）
            if (rest.StartsWith("//"))
                return Uri.UnescapeDataString("\\\\" + rest.Substring(2)).Replace("/", "\\");
            // file:///C:/x → C:\x
            if (rest.StartsWith("/"))
                rest = rest.Substring(1);
            return Uri.UnescapeDataString(rest).Replace("/", "\\");
        }
        // 普通路径：统一斜杠方向（//server/share → \\server\share 恰好正确）
        return path.Replace("/", "\\");
    }

    public static string FormatFileSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unitIdx = 0;
        double size = bytes;
        while (size >= 1024 && unitIdx < units.Length - 1) { size /= 1024; unitIdx++; }
        return $"{size:F1} {units[unitIdx]}";
    }

    public static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        string srcFull = Path.GetFullPath(sourceDir);
        string destFull = Path.GetFullPath(destDir);
        // 🔒 环检测：目标位于源内部 → 拒绝，避免无限递归复制
        if (string.Equals(destFull, srcFull, StringComparison.OrdinalIgnoreCase)
            || destFull.StartsWith(srcFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("目标目录位于源目录内部，拒绝复制以免无限递归");
        CopyDirectoryRecursiveCore(srcFull, destFull);
    }

    private static void CopyDirectoryRecursiveCore(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var f in Directory.GetFiles(sourceDir))
        {
            string dest = Path.Combine(destDir, Path.GetFileName(f));
            // 🔒 不静默覆盖：目标已存在同名文件 → 拒绝
            if (File.Exists(dest))
                throw new InvalidOperationException($"目标已存在同名文件「{Path.GetFileName(dest)}」，拒绝覆盖");
            File.Copy(f, dest);
        }
        foreach (var d in Directory.GetDirectories(sourceDir))
        {
            string dest = Path.Combine(destDir, Path.GetFileName(d));
            CopyDirectoryRecursiveCore(d, dest);
        }
    }

    // ================================================================
    //  安全校验
    // ================================================================

    // 🔒 允许执行的命令白名单 — 仅保留只读/查看类 + 无害应用启动。
    // 已移除解释器（powershell/pwsh/cmd/python/node/npm/npx）：
    //   它们可执行任意代码（如 powershell -c "Remove-Item ..."），风险远大于收益，
    //   需要脚本能力时应走专门的工具（如 Bridge /compile_latex）。
    public static readonly HashSet<string> AllowedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // 网络诊断（只读；ping 默认 4 次自动结束）
        "ipconfig", "ping", "nslookup", "netstat",
        // 系统信息（只读）
        "systeminfo", "tasklist", "whoami", "ver",
        // 文件查看（只读）
        "dir", "tree", "type", "findstr", "echo",
        // 无害应用启动
        "notepad", "calc", "mspaint", "write", "where", "which",
    };

    public static bool IsCommandAllowed(string command)
    {
        if (string.IsNullOrEmpty(command)) return false;
        string trimmed = command.TrimStart();
        if (trimmed.Length > 500) return false; // 防超长命令

        // 🚫 拒绝组合命令/重定向/换行/空字符：& | > < ; \r \n \0
        //    —— 防 "dir & 删文件"、"echo x > file"、"dir\necho 危险" 多行注入绕过白名单
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, "[&|<>;\r\n\0]"))
            return false;

        // 检查首个词
        string first = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        // 去掉路径符号
        first = first.Trim('"').Trim('\'');
        string exe = Path.GetFileNameWithoutExtension(first);
        if (!AllowedCommands.Contains(exe)) return false;

        // 各命令专属限制（防挂起/交互）
        if (exe == "ping" && System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"(^|\s)-t(\s|$)"))
            return false; // ping -t 无限循环
        return true;
    }

    public static bool IsPathAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string full;
        try { full = Path.GetFullPath(path); }
        catch { return false; }
        if (full.IndexOf('\0') >= 0) return false;

        bool IsSameOrChild(string candidate, string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return false;
            string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(candidate, normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        // 禁止操作系统关键目录（含 Program Files / 用户 AppData — 装删系统组件/配置文件）
        string[] deniedPrefixes = {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),       // AppData\Roaming
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),  // AppData\Local
        };
        foreach (var denied in deniedPrefixes)
        {
            if (IsSameOrChild(full, denied))
                return false;
        }

        // 禁止驱动器根下的系统隐藏/保留目录（回收站、卷影、恢复、ProgramData 等）
        string rootPath = Path.GetPathRoot(full); // 如 C:\
        if (!string.IsNullOrEmpty(rootPath))
        {
            string[] deniedSubs = {
                "$Recycle.Bin", "System Volume Information", "Recovery",
                "ProgramData", "Boot", "PerfLogs", "Windows.old",
            };
            foreach (var sub in deniedSubs)
            {
                if (IsSameOrChild(full, Path.Combine(rootPath, sub)))
                    return false;
            }
        }

        // Junction / symbolic link may point an apparently safe path into a protected tree.
        string probe = full;
        while (!string.IsNullOrEmpty(probe))
        {
            try
            {
                if ((File.Exists(probe) || Directory.Exists(probe))
                    && (File.GetAttributes(probe) & FileAttributes.ReparsePoint) != 0)
                    return false;
            }
            catch { return false; }

            string parent;
            try { parent = Path.GetDirectoryName(probe); }
            catch { return false; }
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, probe, StringComparison.OrdinalIgnoreCase)) break;
            probe = parent;
        }
        return true;
    }

    // ================================================================
    //  PDF 读取
    // ================================================================

    public static string ReadPdfViaPython(string pdfPath, int maxLen)
    {
        string pyExe = FindPythonExe();
        string extractScript = FindPdfExtractScript();
        if (extractScript == null) return "❌ 未找到 PDF 提取脚本";
        try
        {
            var psi = new ProcessStartInfo(pyExe, $"\"{extractScript}\" \"{pdfPath}\" {maxLen}")
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            var p = Process.Start(psi);
            if (p == null) return "❌ PDF 提取进程启动失败";
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return !string.IsNullOrEmpty(output) ? output.Trim() : "❌ PDF 提取无输出";
        }
        catch (Exception e) { return $"❌ PDF 提取出错: {e.Message}"; }
    }

    public static string FindPdfExtractScript()
    {
        string[] candidates = {
            Path.Combine(Application.dataPath, "Scripts", "extract_pdf.py"),
            Path.Combine(Application.dataPath, "Scripts", "pdf_extract.py"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return null;
    }

    // ================================================================
    //  回收站
    // ================================================================

    public static bool MoveToRecycleBin(string path)
    {
        try
        {
            var shf = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO | FOF_SILENT | FOF_NOCONFIRMATION
            };
            return SHFileOperation(ref shf) == 0;
        }
        catch { return false; }
    }

    public static bool EmptyRecycleBin()
    {
        try { return SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND) == 0; }
        catch { return false; }
    }

    // ================================================================
    //  截图
    // ================================================================

    public static string SaveScreenshot()
    {
        try
        {
            string dir = Path.Combine(Application.dataPath, "..", "Screenshots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            ScreenCapture.CaptureScreenshot(path);
            return path;
        }
        catch (Exception e) { return $"❌ 截屏失败: {e.Message}"; }
    }

    public static string SaveScreenshotTemp()
    {
        try
        {
            string tempDir = Path.Combine(Application.temporaryCachePath, "screenshots");
            Directory.CreateDirectory(tempDir);
            string path = Path.Combine(tempDir, $"tmp_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            ScreenCapture.CaptureScreenshot(path);
            return path;
        }
        catch (Exception e) { return $"❌ 截屏失败: {e.Message}"; }
    }
}
