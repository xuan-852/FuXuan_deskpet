using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

// ================================================================
//  1. 观星术 — 打开网页 / 搜索信息
// ================================================================

/// <summary>
/// 后台静默搜索工具。借助本地桥接服务器获取搜索结果并返回摘要文本。
/// 不打开浏览器，适合AI不知答案时自行查阅。
/// </summary>
public class SearchWebTool : AsyncToolBase
{
    public override string ToolName => "search_web";
    public override string ToolDescription => "后台搜索互联网并返回结果摘要（不打开浏览器）。当不知道某首歌、某个人、某个知识点时用此术查探，获取信息后可直接回答用户。不可用于天气、打开网址。";
    public override string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("query", "string", "搜索关键词")
    );

    protected override async Task<string> ExecuteAsyncTask(string argsJson)
    {
        string query = ToolHelpers.JsonRead(argsJson, "query");
        if (string.IsNullOrEmpty(query))
            return "❌ 未说要搜什么";

        // 先发一条中间状态，由 AsyncToolBase 的 ExecuteAsync 处理首次返回
        string result = await OpenClawBridge.SearchWebAsync(query, 30);
        if (string.IsNullOrEmpty(result) || result.StartsWith("❌"))
            return $"❌ 天机难测，未能搜到「{query}」：{result}";

        // 截取前 2000 字以免 token 溢出
        if (result.Length > 2000)
            result = result.Substring(0, 2000) + "\n…（以下省略 " + (result.Length - 2000) + " 字）";

        return $"🔍 搜索结果——\n{result}";
    }
}

public class OpenUrlTool : IPetTool
{
    public string ToolName => "open_url";
    public string ToolDescription => "在默认浏览器中打开指定网址。比如用户说「打开B站」就调用此术。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("url", "string", "要打开的完整网址")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string url = ToolHelpers.JsonRead(argsJson, "url");
        if (string.IsNullOrEmpty(url)) return "❌ 未提供网址，本座如何观星？";
        if (!url.StartsWith("http://") && !url.StartsWith("https://")) url = "https://" + url;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return $"✅ 已开天目，窥视「{url}」";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class SearchTool : IPetTool
{
    public string ToolName => "search";
    public string ToolDescription => "在 Bing 搜索引擎上查询信息并通过默认浏览器展示网页结果（会打开浏览器窗口）。仅当用户明确要求「打开浏览器搜索」或「帮我搜一下XXX到浏览器里」时使用。如需自己查看搜索结果并直接回答用户，请用 search_web。禁止用于查询天气（天气请用 get_weather）";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("query", "string", "搜索关键词")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string query = ToolHelpers.JsonRead(argsJson, "query");
        if (string.IsNullOrEmpty(query)) return "❌ 未说要搜什么";
        string url = "https://www.bing.com/search?q=" + Uri.EscapeDataString(query);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return $"🔍 已为卜主搜索「{query}」";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

// ================================================================
//  2. 开阵术 — 启动应用 / 打开文件 / 文件夹
// ================================================================

public class OpenAppTool : IPetTool
{
    public string ToolName => "open_app";
    public string ToolDescription => "启动一个应用程序或打开文件。比如「打开计算器」「打开记事本」「帮我打开D盘下的某某文件」";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("name", "string", "应用名称或文件路径，如 calc、notepad、D:/path/file.txt")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string name = ToolHelpers.JsonRead(argsJson, "name");
        if (string.IsNullOrEmpty(name)) return "❌ 未指明要启动什么";
        return TryLaunch(name);
    }

    private string TryLaunch(string name)
    {
        try
        {
            Process.Start(new ProcessStartInfo(name) { UseShellExecute = true });
            return $"✅ 已遵法旨，召来「{name}」";
        }
        catch { }

        try
        {
            string found = ToolHelpers.FastWhich(name);
            if (found != null)
            {
                Process.Start(new ProcessStartInfo(found) { UseShellExecute = true });
                return $"✅ 在 PATH 中寻得「{Path.GetFileName(found)}」，已召来";
            }

            string startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs");
            string lnk = ToolHelpers.FastFindLink(startMenu, name);
            if (lnk != null)
            {
                Process.Start(new ProcessStartInfo(lnk) { UseShellExecute = true });
                return $"✅ 在开始菜单寻得「{Path.GetFileNameWithoutExtension(lnk)}」，已召来";
            }
        }
        catch { }

        return $"❌ 寻遍诸天也未找到「{name}」";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class OpenFolderTool : IPetTool
{
    public string ToolName => "open_folder";
    public string ToolDescription => "在文件资源管理器中打开指定文件夹。用户说「打开D盘」「打开桌面」「打开下载文件夹」时调用。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Opt("path", "string", "文件夹路径，如果为空则打开桌面")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string path = ToolHelpers.JsonRead(argsJson, "path");
        if (string.IsNullOrEmpty(path)) path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        path = Environment.ExpandEnvironmentVariables(path);
        if (!Directory.Exists(path))
        {
            // 试试常见简写
            string lower = path.ToLower();
            if (lower.Contains("桌面") || lower.Contains("desktop"))
                path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            else if (lower.Contains("下载") || lower.Contains("download"))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            else if (lower.Contains("文档") || lower.Contains("documents") || lower.Contains("document"))
                path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            else if (lower.Contains("图片") || lower.Contains("pictures") || lower.Contains("picture"))
                path = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            else if (lower.Contains("音乐") || lower.Contains("music"))
                path = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            else if (lower.Contains("视频") || lower.Contains("videos") || lower.Contains("video"))
                path = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            else if (lower.Length <= 3 && lower.EndsWith(":")) path = lower + "\\";
            else return $"❌ 找不到「{path}」这个去处";
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return $"📂 已打开「{path}」";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

// ================================================================
//  3. 系统信息 / 操作
// ================================================================

public class GetSystemInfoTool : IPetTool
{
    public string ToolName => "get_system_info";
    public string ToolDescription => "获取电脑的系统信息，包括操作系统、CPU、内存、磁盘、运行时间等。";
    public string ToolParametersJson => ToolSchema.Empty;
    public bool IsAsync => false;

    public string Execute(string argsJson) => ToolHelpers.GetSystemInfo();
    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class LockScreenTool : IPetTool
{
    public string ToolName => "lock_screen";
    public string ToolDescription => "锁定电脑屏幕。用户说「锁屏」「离开一下」时调用。";
    public string ToolParametersJson => ToolSchema.Empty;
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        try { LockWorkStation(); return "🔒 已封印桌案"; }
        catch { return "❌ 封印失败"; }
    }
    private static void LockWorkStation()
    {
        try { Process.Start(@"C:\Windows\System32\rundll32.exe", "user32.dll,LockWorkStation"); }
        catch { }
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class SetVolumeTool : IPetTool
{
    public string ToolName => "set_volume";
    public string ToolDescription => "调节系统音量。用户说「声音大一点」「音量调到50」「静音」时调用。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("level", "integer", "音量值 0～100")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string lvlStr = ToolHelpers.JsonRead(argsJson, "level");
        if (string.IsNullOrEmpty(lvlStr) || !int.TryParse(lvlStr, out int level)) return "❌ 未指定音量大小";
        ToolHelpers.SetSystemVolume(level);
        return $"🔊 已调音至 {level}";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class MuteTool : IPetTool
{
    public string ToolName => "mute";
    public string ToolDescription => "切换静音状态。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("muted", "boolean", "true=静音 false=取消静音")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string mutedStr = ToolHelpers.JsonRead(argsJson, "muted");
        bool muted = mutedStr == "true";
        ToolHelpers.SetSystemVolume(muted ? 0 : 50);
        return muted ? "🔇 已禁声" : "🔊 已解禁";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
internal struct POINT { public int X; public int Y; }

public class GetMousePosTool : IPetTool
{
    public string ToolName => "get_mouse_pos";
    public string ToolDescription => "获取鼠标当前在屏幕上的位置坐标。用户问「鼠标在哪」「光标位置」时调用。";
    public string ToolParametersJson => ToolSchema.Empty;
    public bool IsAsync => false;

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);

    public string Execute(string argsJson)
    {
        try
        {
            GetCursorPos(out var p);
            return $"🖱️ 光标在 ({p.X}, {p.Y}) 处";
        }
        catch { return "❌ 无法观测光标"; }
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class ListFilesTool : IPetTool
{
    public string ToolName => "list_files";
    public string ToolDescription => "列出指定目录下的文件和子目录。用户说「看看桌面上有什么」「D盘有什么」时调用。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Opt("path", "string", "目录路径，为空则桌面")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string path = ToolHelpers.JsonRead(argsJson, "path");
        if (string.IsNullOrEmpty(path)) path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        path = Environment.ExpandEnvironmentVariables(path);

        if (path.Length <= 3 && path.EndsWith(":")) path += "\\";
        if (!Directory.Exists(path)) return $"❌「{path}」此路不通";

        try
        {
            var files = Directory.GetFiles(path);
            var dirs = Directory.GetDirectories(path);
            var sb = new StringBuilder();
            sb.AppendLine($"📁 {path} 内有 {dirs.Length} 个文件夹，{files.Length} 个文件：");
            foreach (var d in dirs) sb.AppendLine($"  📂 {Path.GetFileName(d)}");
            foreach (var f in files)
            {
                var fi = new FileInfo(f);
                sb.AppendLine($"  📄 {fi.Name} ({ToolHelpers.FormatFileSize(fi.Length)})");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception e) { return $"❌ 打不开：{e.Message}"; }
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class NotifyTool : IPetTool
{
    public string ToolName => "notify";
    public string ToolDescription => "发送 Windows 桌面通知。当你有什么事情想主动告诉用户但用户可能在忙时，用此术弹窗。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Opt("title", "string", "通知标题"),
        ToolSchema.Req("message", "string", "通知正文")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string title = ToolHelpers.JsonRead(argsJson, "title");
        string message = ToolHelpers.JsonRead(argsJson, "message");
        if (string.IsNullOrEmpty(title)) title = "太卜司传音";
        if (string.IsNullOrEmpty(message)) return "❌ 未说传什么音";
        ToolHelpers.ShowNotification(title, message);
        return $"📨 传音已送达：「{message}」";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class RunCommandTool : IPetTool
{
    public string ToolName => "run_command";
    public string ToolDescription => "执行一条 CMD 命令。高级用法，只有用户明确要求执行特定命令时才使用。需要用户确认。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("command", "string", "要执行的命令")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string cmd = ToolHelpers.JsonRead(argsJson, "command");
        if (string.IsNullOrEmpty(cmd)) return "❌ 未说要执行什么";
        if (!ToolHelpers.IsCommandAllowed(cmd)) return "❌ 此术涉及高危操作，需主人亲口确认";
        try
        {
            var psi = new ProcessStartInfo("cmd", "/c " + cmd)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.GetEncoding(936),
                StandardErrorEncoding = Encoding.GetEncoding(936)
            };
            var p = Process.Start(psi);
            if (p == null) return "❌ 无法执行";
            string output = p.StandardOutput.ReadToEnd();
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            var sb = new StringBuilder();
            sb.AppendLine($"⚡ 执行「{cmd}」");
            if (!string.IsNullOrEmpty(output)) sb.AppendLine(output.TrimEnd());
            if (!string.IsNullOrEmpty(err)) sb.AppendLine($"⚠️ {err.TrimEnd()}");
            return sb.ToString().TrimEnd();
        }
        catch (Exception e) { return $"❌ 执行失败：{e.Message}"; }
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class PowerTool : IPetTool
{
    public string ToolName => "power";
    public string ToolDescription => "关机 / 重启 / 睡眠。用户明确说「关机」「重启电脑」时调用。需要用户确认后才能执行。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("action", "string", "shutdown=关机 restart=重启 sleep=睡眠")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string action = ToolHelpers.JsonRead(argsJson, "action");
        return action switch
        {
            "shutdown" => ExecutePower("shutdown /s /t 5", "🔌 正在关机……"),
            "restart" => ExecutePower("shutdown /r /t 5", "🔄 正在重启……"),
            "sleep" => ExecutePower("rundll32.exe powrprof.dll,SetSuspendState 0,1,0", "💤 正在入眠……"),
            _ => "❌ 不识此令"
        };
    }

    private string ExecutePower(string args, string successMsg)
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd", $"/c {args}") { CreateNoWindow = true, UseShellExecute = true });
            return successMsg;
        }
        catch (Exception e) { return $"❌ 失败：{e.Message}"; }
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}
