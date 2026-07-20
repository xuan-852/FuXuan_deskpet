using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

// ================================================================
//  剪贴板工具
// ================================================================

public class GetClipboardTool : IPetTool
{
    public string ToolName => "get_clipboard";
    public string ToolDescription => "读取剪贴板文本内容。用户说「看剪贴板」「复制了什么」时调用。";
    public string ToolParametersJson => ToolSchema.Empty;
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string text = ToolHelpers.GetClipboardText();
        return string.IsNullOrEmpty(text)
            ? "📋 剪贴板空空如也"
            : $"📋 剪贴板内容：\n{text}";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class SetClipboardTool : IPetTool
{
    public string ToolName => "set_clipboard";
    public string ToolDescription => "将文本写入剪贴板。用户说「帮我复制这段」「记下来」时调用。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("text", "string", "要写入的文本")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string text = ToolHelpers.JsonRead(argsJson, "text");
        if (string.IsNullOrEmpty(text)) return "❌ 未说要传什么";
        ToolHelpers.SetClipboardText(text);
        return $"✅ 已写入剪贴板：{text}";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

// ================================================================
//  天气
// ================================================================

public class GetWeatherTool : IPetTool
{
    public string ToolName => "get_weather";
    public string ToolDescription => "【天气专用】直接读取桌面本地已经获取到的天气数据（使用和风天气或 wttr.in），无需打开浏览器或搜索。用户问任何关于天气/温度/冷热的问题时，必须优先使用此术式，绝对不能用 search 去搜索天气。";
    public string ToolParametersJson => ToolSchema.Empty;
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        var weatherCtrl = GameObject.FindObjectOfType<TimeWeatherController>();
        if (weatherCtrl == null) return "❌ 观云望气阵法未载入";
        var sb = new System.Text.StringBuilder();
        sb.Append(weatherCtrl.GetWeatherLabel());
        sb.Append($" {weatherCtrl.temperatureC:F0}°C");
        if (weatherCtrl.humidityPercent > 0)
            sb.Append($" 💧{weatherCtrl.humidityPercent}%");
        if (weatherCtrl.windSpeedKmh > 0)
            sb.Append($" 🌬{weatherCtrl.windSpeedKmh}km/h{weatherCtrl.windDirection}");
        sb.Append($" | 数据源: {weatherCtrl.weatherSourceLabel}");
        return sb.ToString();
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

// ================================================================
//  文件管理
// ================================================================

public class FileOpenTool : IPetTool
{
    public string ToolName => "file_open";
    public string ToolDescription => "【文件管理】打开任意文件、文件夹或应用程序（自动识别路径）。用户说「打开这个」「打开文件」「帮我打开xxx」时调用。支持 file:// URI 格式。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("path", "string", "文件/文件夹/应用路径，或 file:// URI")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string path = ToolHelpers.JsonRead(argsJson, "path");
        if (string.IsNullOrEmpty(path)) return "❌ 未指定路径";
        path = ToolHelpers.DecodeFileUri(path);
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return $"✅ 已打开「{Path.GetFileName(path)}」";
        }
        catch { }
        // 试试不转义
        try
        {
            path = ToolHelpers.JsonRead(argsJson, "path");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return $"✅ 已打开「{Path.GetFileName(path)}」";
        }
        catch (Exception e) { return $"❌ 打不开：{e.Message}"; }
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class FileMoveTool : IPetTool
{
    public string ToolName => "file_move";
    public string ToolDescription => "【文件管理】移动文件/文件夹到新位置，或重命名。用户说「把这个文件移到」「移动到」「挪到」时调用。支持中文名称。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("source", "string", "源文件/文件夹路径或 file:// URI"),
        ToolSchema.Req("destination", "string", "目标路径")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string src = ToolHelpers.DecodeFileUri(ToolHelpers.JsonRead(argsJson, "source"));
        string dest = ToolHelpers.DecodeFileUri(ToolHelpers.JsonRead(argsJson, "destination"));
        if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dest)) return "❌ 需指定源路径和目标路径";
        try
        {
            if (Directory.Exists(src)) Directory.Move(src, dest);
            else if (File.Exists(src))
            {
                string destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                File.Move(src, dest);
            }
            else return $"❌ 找不到「{Path.GetFileName(src)}」";
            return $"✅ 已将「{Path.GetFileName(src)}」移至新处";
        }
        catch (Exception e) { return $"❌ 移动失败：{e.Message}"; }
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class FileCopyTool : IPetTool
{
    public string ToolName => "file_copy";
    public string ToolDescription => "【文件管理】复制文件或文件夹到新位置。用户说「复制这个到」「拷贝到」「备份一下」时调用。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("source", "string", "源文件/文件夹路径或 file:// URI"),
        ToolSchema.Req("destination", "string", "目标路径")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string src = ToolHelpers.DecodeFileUri(ToolHelpers.JsonRead(argsJson, "source"));
        string dest = ToolHelpers.DecodeFileUri(ToolHelpers.JsonRead(argsJson, "destination"));
        if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dest)) return "❌ 需指定源和目标";
        try
        {
            if (Directory.Exists(src))
            {
                Directory.CreateDirectory(dest);
                ToolHelpers.CopyDirectoryRecursive(src, dest);
            }
            else if (File.Exists(src))
            {
                string destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                File.Copy(src, dest, true);
            }
            else return $"❌ 找不到「{Path.GetFileName(src)}」";
            return $"✅ 已复制「{Path.GetFileName(src)}」";
        }
        catch (Exception e) { return $"❌ 复制失败：{e.Message}"; }
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class FileDeleteTool : IPetTool
{
    public string ToolName => "file_delete";
    public string ToolDescription => "【文件管理】删除文件或文件夹（默认移到回收站，加 permanent=true 则永久删除）。用户说「删掉这个」「删除文件」「把这个扔掉」时调用。操作前会自动向用户确认。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("path", "string", "要删除的文件/文件夹路径或 file:// URI"),
        ToolSchema.Opt("permanent", "boolean", "是否永久删除（不经过回收站），默认 false")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string path = ToolHelpers.DecodeFileUri(ToolHelpers.JsonRead(argsJson, "path"));
        string permStr = ToolHelpers.JsonRead(argsJson, "permanent");
        bool permanent = permStr == "true";
        if (string.IsNullOrEmpty(path)) return "❌ 未指定路径";
        if (!ToolHelpers.IsPathAllowed(path)) return "❌ 系统要地，不可轻动";

        try
        {
            if (permanent)
            {
                if (Directory.Exists(path)) { Directory.Delete(path, true); return $"🗑️ 已永久抹除「{Path.GetFileName(path)}」"; }
                if (File.Exists(path)) { File.Delete(path); return $"🗑️ 已永久抹除「{Path.GetFileName(path)}」"; }
                return $"❌ 找不到「{Path.GetFileName(path)}」";
            }
            else
            {
                if (ToolHelpers.MoveToRecycleBin(path))
                    return $"🗑️ 已将「{Path.GetFileName(path)}」送入废纸篓（可还原）";
                // 回收站失败则物理删除
                if (Directory.Exists(path)) { Directory.Delete(path, true); return $"🗑️ 已删除「{Path.GetFileName(path)}」"; }
                if (File.Exists(path)) { File.Delete(path); return $"🗑️ 已删除「{Path.GetFileName(path)}」"; }
                return $"❌ 找不到「{Path.GetFileName(path)}」";
            }
        }
        catch (Exception e) { return $"❌ 删除失败：{e.Message}"; }
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class FileRenameTool : IPetTool
{
    public string ToolName => "file_rename";
    public string ToolDescription => "【文件管理】重命名文件或文件夹。用户说「重命名」「改名为」时调用。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("path", "string", "文件/文件夹路径或 file:// URI"),
        ToolSchema.Req("new_name", "string", "新文件名（含扩展名）")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string path = ToolHelpers.DecodeFileUri(ToolHelpers.JsonRead(argsJson, "path"));
        string newName = ToolHelpers.JsonRead(argsJson, "new_name");
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(newName)) return "❌ 需指定文件和目标名";
        try
        {
            string dir = Path.GetDirectoryName(path);
            string newPath = Path.Combine(dir ?? "", newName);
            if (Directory.Exists(path)) Directory.Move(path, newPath);
            else if (File.Exists(path)) File.Move(path, newPath);
            else return $"❌ 找不到「{Path.GetFileName(path)}」";
            return $"✏️ 已更名为「{newName}」";
        }
        catch (Exception e) { return $"❌ 改名失败：{e.Message}"; }
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class FileInfoTool : IPetTool
{
    public string ToolName => "file_info";
    public string ToolDescription => "【文件管理】查看文件或文件夹的详细信息（大小、创建时间、修改时间、属性等）。用户说「看看这个文件」「文件详情」「属性」「多大」时调用。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("path", "string", "文件/文件夹路径或 file:// URI")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string path = ToolHelpers.DecodeFileUri(ToolHelpers.JsonRead(argsJson, "path"));
        if (string.IsNullOrEmpty(path)) return "❌ 未指定路径";
        try
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                return $"📄 {fi.Name}\n📏 大小：{ToolHelpers.FormatFileSize(fi.Length)}\n📅 创建：{fi.CreationTime:yyyy-MM-dd HH:mm}\n📅 修改：{fi.LastWriteTime:yyyy-MM-dd HH:mm}\n📅 访问：{fi.LastAccessTime:yyyy-MM-dd HH:mm}\n🔖 属性：{fi.Attributes}";
            }
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                int fileCount = 0, dirCount = 0;
                try { fileCount = di.GetFiles().Length; dirCount = di.GetDirectories().Length; } catch { }
                return $"📁 {di.Name}\n📅 创建：{di.CreationTime:yyyy-MM-dd HH:mm}\n📅 修改：{di.LastWriteTime:yyyy-MM-dd HH:mm}\n📂 含 {dirCount} 文件夹，{fileCount} 文件";
            }
            return $"❌ 找不到「{Path.GetFileName(path)}」";
        }
        catch (Exception e) { return $"❌ 查看失败：{e.Message}"; }
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class FileCreateTool : IPetTool
{
    public string ToolName => "file_create";
    public string ToolDescription => "【文件管理】创建一个新的文本文件，可指定内容。用户说「新建文件」「创建文件」「写个文件」时调用。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""path"":{""type"":""string"",""description"":""文件完整路径""},""content"":{""type"":""string"",""description"":""文件内容（可选，不提供则创建空文件）""}},""required"":[""path""]}";
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string path = ToolHelpers.DecodeFileUri(ToolHelpers.JsonRead(argsJson, "path"));
        string content = ToolHelpers.JsonRead(argsJson, "content");
        if (string.IsNullOrEmpty(path)) return "❌ 未指定路径";
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, content ?? "", Encoding.UTF8);
            return $"📝 已写好「{Path.GetFileName(path)}」";
        }
        catch (Exception e) { return $"❌ 写文件失败：{e.Message}"; }
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class DirCreateTool : IPetTool
{
    public string ToolName => "dir_create";
    public string ToolDescription => "【文件管理】创建新文件夹。用户说「新建文件夹」「创建目录」时调用。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""path"":{""type"":""string"",""description"":""要创建的文件夹路径""}},""required"":[""path""]}";
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string path = ToolHelpers.DecodeFileUri(ToolHelpers.JsonRead(argsJson, "path"));
        if (string.IsNullOrEmpty(path)) return "❌ 未指定路径";
        try { Directory.CreateDirectory(path); return $"📁 已新建「{path}」"; }
        catch (Exception e) { return $"❌ 创建失败：{e.Message}"; }
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class FileReadTool : IPetTool
{
    public string ToolName => "file_read";
    public string ToolDescription => "【文件管理】读取文本文件的内容（自动检测是否为二进制文件）。用户说「看看这个文件里写了什么」「打开记事本看看」「读一下这个文件」时调用。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""path"":{""type"":""string"",""description"":""文件路径或 file:// URI""},""max_length"":{""type"":""integer"",""description"":""最大返回字符数，默认2000""}},""required"":[""path""]}";
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string path = ToolHelpers.DecodeFileUri(ToolHelpers.JsonRead(argsJson, "path"));
        string maxLenStr = ToolHelpers.JsonRead(argsJson, "max_length");
        int maxLen = 2000;
        if (!string.IsNullOrEmpty(maxLenStr)) int.TryParse(maxLenStr, out maxLen);
        if (string.IsNullOrEmpty(path)) return "❌ 未指定路径";
        try
        {
            if (!File.Exists(path)) return $"❌ 不见「{Path.GetFileName(path)}」踪迹";

            if (Path.GetExtension(path).ToLower() == ".pdf")
                return ToolHelpers.ReadPdfViaPython(path, maxLen);

            byte[] bytes;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                bytes = new byte[Math.Min(fs.Length, 4096)];
                fs.Read(bytes, 0, bytes.Length);
            }

            // 检测二进制
            bool isBinary = false;
            foreach (var b in bytes)
            {
                if (b == 0) { isBinary = true; break; }
            }
            if (isBinary) return $"📄 {Path.GetFileName(path)} 采二进制之形，非本座可读";

            string content = File.ReadAllText(path, Encoding.UTF8);
            if (content.Length > maxLen) content = content[..maxLen] + $"\n\n...（余 {content.Length - maxLen} 字符未展示）";
            return $"📄 {Path.GetFileName(path)}：\n{content}";
        }
        catch (Exception e) { return $"❌ 读取失败：{e.Message}"; }
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

// ================================================================
//  Search 文件搜索
// ================================================================

public class SearchFilesTool : IPetTool
{
    public string ToolName => "search_files";
    public string ToolDescription => "【搜索文件】按关键词搜索文件，若电脑有 Everything 则毫秒级搜索全盘，否则递归搜索指定目录。用户说「帮我找找xxx」「搜一下电脑里的xxx」「找文件」时调用。未指定 root 时默认搜索全盘（Everything模式）或桌面（递归回退模式）。最多返回200个结果。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""query"":{""type"":""string"",""description"":""要搜索的文件名关键词（不区分大小写）""},""root"":{""type"":""string"",""description"":""搜索根目录（有 Everything 时自动限定在此目录下搜索），为空则全盘搜索""}},""required"":[""query""]}";
    public bool IsAsync => true;

    public string Execute(string argsJson) => "⏳ 天眼通搜索中……";

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        string query = ToolHelpers.JsonRead(argsJson, "query");
        string rootDir = ToolHelpers.JsonRead(argsJson, "root");
        if (string.IsNullOrEmpty(query)) { onResult?.Invoke("❌ 未说要搜什么"); yield break; }

        string esExe = ToolHelpers.FindEverythingCli();
        bool useEverything = esExe != null;
        var results = new List<string>();

        try
        {
            if (useEverything)
            {
                try
                {
                    var psi = new ProcessStartInfo(esExe, $"-n 200 \"{query.Replace("\"", "\\\"")}\"")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.GetEncoding(936),
                        StandardErrorEncoding = Encoding.GetEncoding(936)
                    };
                    if (!string.IsNullOrEmpty(rootDir))
                        psi.Arguments = $"-n 200 -path \"{rootDir.Replace("\"", "\\\"")}\" \"{query.Replace("\"", "\\\"")}\"";
                    var p = Process.Start(psi);
                    if (p != null)
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(3000);
                        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                            if (!string.IsNullOrWhiteSpace(line)) results.Add(line);
                    }
                }
                catch { useEverything = false; }
            }

            if (!useEverything)
            {
                try
                {
                    if (string.IsNullOrEmpty(rootDir))
                        rootDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    if (Directory.Exists(rootDir))
                        ToolHelpers.SearchRecursive(rootDir, query, results, 200);
                }
                catch (Exception ex)
                {
                    results.Add($"⚠️ 搜索中断：{ex.Message}");
                }
            }

            if (results.Count == 0)
            {
                string scope = useEverything
                    ? (string.IsNullOrEmpty(rootDir) ? "本座的天眼所及之处" : $"「{rootDir}」")
                    : (string.IsNullOrEmpty(rootDir) ? "桌面" : $"「{rootDir}」");
                onResult?.Invoke($"🔍 在{scope}中未找到与「{query}」匹配的文件");
                yield break;
            }

            string method = useEverything ? "⚡本座以 Everything 天眼通搜" : "🔍本座以递归之法搜";
            string scope2 = string.IsNullOrEmpty(rootDir) ? "全境" : $"「{rootDir}」";
            var sb = new StringBuilder();
            sb.AppendLine($"{method}{scope2}，得 {results.Count} 件与「{query}」相关之物：");
            foreach (var f in results)
                sb.AppendLine($"  📄 {f}");
            onResult?.Invoke(Truncate(sb.ToString(), 2000));
        }
        catch (Exception e)
        {
            onResult?.Invoke($"❌ 搜索时出了岔子：{e.Message}");
        }
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + $"\n...（已截断）";
}

public class SearchFileTool : IPetTool
{
    public string ToolName => "search_file";
    public string ToolDescription => "【文件管理】在全盘或指定目录中搜索文件/文件夹，支持任意中文/英文/特殊字符文件名，返回匹配的文件列表（含完整路径）。用户说「帮我找一下xxx」「搜索文件」「找图片」「电脑里有xxx吗」时调用。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""query"":{""type"":""string"",""description"":""要搜索的文件名关键词（支持中英文和特殊字符，不区分大小写）""},""root"":{""type"":""string"",""description"":""搜索根目录，为空则全盘搜索""}},""required"":[""query""]}";
    public bool IsAsync => true;

    public string Execute(string argsJson) => "⏳ 天眼通搜索中……";

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        string query = ToolHelpers.JsonRead(argsJson, "query");
        string rootDir = ToolHelpers.JsonRead(argsJson, "root");
        if (string.IsNullOrEmpty(query)) { onResult?.Invoke("❌ 未说要搜什么"); yield break; }

        var task = System.Threading.Tasks.Task.Run(() => ToolHelpers.SearchFileByPython(query, rootDir));
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted)
            onResult?.Invoke($"❌ 搜索失败：{task.Exception?.InnerException?.Message}");
        else
            onResult?.Invoke(task.Result);
    }
}
