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
        return sb.ToString().TrimEnd();
    }

    // ================================================================
    //  文件搜索
    // ================================================================

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
        try
        {
            // 1) PATH（便携版/手动添加过）
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
            foreach (var dir in paths)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                string test = Path.Combine(dir.Trim(), "es.exe");
                if (File.Exists(test)) return test;
            }

            // 2) Everything 官方安装器的默认位置（用户级 / 全局）
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
        }
        try
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
            if (string.IsNullOrEmpty(rootDir)) rootDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var results = new List<string>();
            SearchRecursive(rootDir, query, results, 100, skipSystemDirs: true);
            if (results.Count == 0) return $"🔍 在「{rootDir}」中未找到与「{query}」匹配的文件";
            var sb = new StringBuilder();
            sb.AppendLine($"🔍 本座以递归之法搜「{rootDir}」，得 {results.Count} 件与「{query}」相关之物：");
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
        if (string.IsNullOrEmpty(path)) return true;
        string full = Path.GetFullPath(path);

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
            if (!string.IsNullOrEmpty(denied) && full.StartsWith(denied, StringComparison.OrdinalIgnoreCase))
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
                if (full.StartsWith(rootPath + sub, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
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
