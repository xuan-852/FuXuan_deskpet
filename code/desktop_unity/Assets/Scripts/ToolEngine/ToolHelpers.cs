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

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);
    private const uint WM_APPCOMMAND = 0x0319;
    private const uint APPCOMMAND_VOLUME_UP = 0x0a0000;
    private const uint APPCOMMAND_VOLUME_DOWN = 0x090000;
    private const uint APPCOMMAND_VOLUME_MUTE = 0x080000;

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
    //  音量
    // ================================================================

    public static void SetSystemVolume(int level)
    {
        level = Mathf.Clamp(level, 0, 100);
        // 使用简化方法：先静音，再按百分比逐级调
        IntPtr hWnd = Process.GetCurrentProcess().MainWindowHandle;
        SendMessageW(hWnd, WM_APPCOMMAND, UIntPtr.Zero, (IntPtr)APPCOMMAND_VOLUME_MUTE);
        SendMessageW(hWnd, WM_APPCOMMAND, UIntPtr.Zero, (IntPtr)APPCOMMAND_VOLUME_MUTE);
        // 调到 50%，再微调
        for (int i = 0; i < 50; i++)
            SendMessageW(hWnd, WM_APPCOMMAND, UIntPtr.Zero, (IntPtr)APPCOMMAND_VOLUME_UP);
        int target = level;
        int current = 50;
        while (current < target)
        {
            SendMessageW(hWnd, WM_APPCOMMAND, UIntPtr.Zero, (IntPtr)APPCOMMAND_VOLUME_UP);
            current++;
        }
        while (current > target)
        {
            SendMessageW(hWnd, WM_APPCOMMAND, UIntPtr.Zero, (IntPtr)APPCOMMAND_VOLUME_DOWN);
            current--;
        }
    }

    // ================================================================
    //  通知
    // ================================================================

    public static void ShowNotification(string title, string message)
    {
        try { MessageBox(IntPtr.Zero, message, title, MB_OK | MB_ICONINFORMATION); }
        catch { }
    }

    // ================================================================
    //  JSON 解析
    // ================================================================

    public static string JsonRead(string json, string key)
    {
        string search = $"\"{key}\":\"";
        int idx = json.IndexOf(search);
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
            var psi = new ProcessStartInfo("where", name)
            {
                UseShellExecute = false, RedirectStandardOutput = true,
                CreateNoWindow = true, StandardOutputEncoding = Encoding.GetEncoding(936)
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

    public static string FindEverythingCli()
    {
        try
        {
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';');
            foreach (var dir in paths)
            {
                string test = Path.Combine(dir.Trim(), "es.exe");
                if (File.Exists(test)) return test;
            }
        }
        catch { }
        return null;
    }

    public static void SearchRecursive(string dir, string query, List<string> results, int maxResults)
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
                SearchRecursive(d, query, results, maxResults);
            }
        }
        catch { }
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
            SearchRecursive(rootDir, query, results, 100);
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
            path = path.Replace("file://", "");
            path = Uri.UnescapeDataString(path);
        }
        return path.TrimStart('/').Replace("/", "\\");
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
        Directory.CreateDirectory(destDir);
        foreach (var f in Directory.GetFiles(sourceDir))
        {
            string dest = Path.Combine(destDir, Path.GetFileName(f));
            File.Copy(f, dest, true);
        }
        foreach (var d in Directory.GetDirectories(sourceDir))
        {
            string dest = Path.Combine(destDir, Path.GetFileName(d));
            CopyDirectoryRecursive(d, dest);
        }
    }

    // ================================================================
    //  安全校验
    // ================================================================

    public static readonly HashSet<string> AllowedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ipconfig", "ping", "tracert", "nslookup", "netstat", "dir", "tree",
        "systeminfo", "tasklist", "whoami", "ver", "date", "time", "echo",
        "type", "findstr", "more", "sort",
        "notepad", "calc", "mspaint", "write",
        "python", "python3", "node", "npm", "npx",
        "powershell", "pwsh", "cmd", "where", "which",
    };

    public static bool IsCommandAllowed(string command)
    {
        if (string.IsNullOrEmpty(command)) return false;
        string trimmed = command.TrimStart();
        // 检查首个词
        string first = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        // 去掉路径符号
        first = first.Trim('"').Trim('\'');
        string exe = Path.GetFileNameWithoutExtension(first);
        return AllowedCommands.Contains(exe);
    }

    public static bool IsPathAllowed(string path)
    {
        if (string.IsNullOrEmpty(path)) return true;
        string full = Path.GetFullPath(path);
        // 禁止操作系统关键目录
        string[] deniedPrefixes = {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"),
        };
        foreach (var denied in deniedPrefixes)
        {
            if (!string.IsNullOrEmpty(denied) && full.StartsWith(denied, StringComparison.OrdinalIgnoreCase))
                return false;
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
