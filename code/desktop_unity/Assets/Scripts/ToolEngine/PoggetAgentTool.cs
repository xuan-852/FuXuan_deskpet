using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 开阵·PoggetAgent — 通过 --exec 单次模式调用 Pogget 桌面收纳能力
///
/// 用途：让 AI 直接调用 Pogget 的整理/收纳/查询功能，无需打开 GUI。
/// 通信方式：启动 PoggetAgent-debug.exe --exec 'JSON'
///
/// 命令列表：
///   ping                    — 连通性测试
///   list_containers         — 列出所有收纳盒
///   get_container_items     — 查看指定收纳盒内文件
///   add_to_container        — 添加文件到收纳盒
///   remove_from_container   — 从收纳盒移除文件
///   create_container        — 创建新收纳盒
///   organize_desktop        — 自动整理桌面文件
/// </summary>
public class PoggetAgentTool : IPetTool
{
    public static string AgentExePath { get; set; } =
        System.Environment.GetEnvironmentVariable("POGGET_AGENT_EXE") ?? @"D:\pogget\agent\bin\PoggetAgent-debug.exe";
    public static int TimeoutMs { get; set; } = 15000;

    public string ToolName => "pogget_agent";
    public string ToolDescription => @"通过 IPC 调用桌面收纳工具 Pogget 的文件管理能力。

支持以下操作（参数必须放在 cmd 字段，不是 task）：
- ping: 连通性测试
- list_containers: 列出所有收纳盒
- get_container_items: 查看指定收纳盒内的文件（需 params.containerId）
- add_to_container: 将文件添加到收纳盒（需 params.containerId, params.paths）
- remove_from_container: 从收纳盒移除文件（需 params.containerId, params.itemId）
- create_container: 创建新收纳盒（需 params.title, 可选 params.targetFolder）
- organize_desktop: 自动扫描桌面并按类型整理文件（一次性完成，已整理过的文件不会重复移动，无需反复调用）
- quickpanel_status: 查询快速面板（侧边栏）状态

关于侧边栏/快速面板：Pogget 有快速面板（侧边栏），它是所有收纳盒内容的聚合视图，不单独存储文件。**侧边栏无法创建、不存在 create_sidebar 命令**——用户说「侧边栏收拾/收纳」时直接调用 add_to_container 或 organize_desktop 即可；用户想打开侧边栏窗口则用 launch_pogget 工具（不是 pogget_agent）。";
    public string ToolParametersJson => @"{
  ""type"": ""object"",
  ""properties"": {
    ""cmd"": { ""type"": ""string"", ""enum"": [""ping"",""list_containers"",""get_container_items"",""add_to_container"",""remove_from_container"",""create_container"",""organize_desktop"",""quickpanel_status""], ""description"": ""要执行的操作"" },
    ""params"": { ""type"": ""object"", ""description"": ""操作参数"", ""properties"": {
      ""containerId"": { ""type"": ""string"", ""description"": ""收纳盒 ID"" },
      ""paths"": { ""type"": ""array"", ""items"": { ""type"": ""string"" }, ""description"": ""要添加的文件路径列表"" },
      ""itemId"": { ""type"": ""integer"", ""description"": ""要移除的文件序号"" },
      ""title"": { ""type"": ""string"", ""description"": ""新收纳盒标题"" },
      ""targetFolder"": { ""type"": ""string"", ""description"": ""收纳盒目标文件夹路径"" }
    } }
  },
  ""required"": [""cmd""]
}";
    public bool IsAsync => true;

    public string Execute(string argsJson) => RunAgent(argsJson);

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        string result = null;
        bool done = false;
        System.Threading.ThreadPool.QueueUserWorkItem(_ => {
            try { result = RunAgent(argsJson); }
            catch (Exception ex) { result = $"\u274C PoggetAgent 异常：{ex.Message}"; }
            done = true;
        });

        float timeout = TimeoutMs / 1000f;
        float elapsed = 0;
        // 注意：不能用 WaitForSeconds —— EditMode 测试只允许 yield null，运行时也依赖场景时间；
        // 后台线程完成即置 done=true 退出，轮询间隔以固定步长累加保持超时语义一致
        while (!done && elapsed < timeout) { yield return null; elapsed += 0.1f; }
        onResult?.Invoke(result ?? "\u274C PoggetAgent 执行超时");
    }

    private string RunAgent(string argsJson)
    {
        var args = JsonUtility.FromJson<PoggetAgentArgs>(argsJson);
        if (args == null || string.IsNullOrEmpty(args.cmd)) return "\u274C 缺少必要参数 'cmd'";

        string exe = ResolveExePath();
        if (exe == null) return "\u274C 找不到 PoggetAgent.exe，请确保已编译（默认 D:\\pogget\\agent\\bin\\PoggetAgent-debug.exe）";

        var reqObj = new PoggetAgentRequest { cmd = args.cmd, @params = args.@params };
        string requestJson = JsonUtility.ToJson(reqObj);

        try
        {
            var psi = new ProcessStartInfo(exe, $"--exec \"{requestJson.Replace("\"", "\\\"")}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe)
            };

            using (var process = new Process { StartInfo = psi })
            {
                process.Start();
                bool exited = process.WaitForExit(TimeoutMs);
                string stdout = "", stderr = "";
                if (exited)
                {
                    stdout = process.StandardOutput.ReadToEnd().Trim();
                    stderr = process.StandardError.ReadToEnd().Trim();
                }
                else { process.Kill(); return "\u274C PoggetAgent 执行超时"; }

                if (process.ExitCode != 0 && string.IsNullOrEmpty(stdout))
                    return $"\u274C 执行失败：{(string.IsNullOrEmpty(stderr) ? "退出码 " + process.ExitCode : stderr)}";

                return string.IsNullOrEmpty(stdout) ? "\u274C 返回空响应" : FormatResponse(args.cmd, stdout);
            }
        }
        catch (Exception ex) { return $"\u274C 执行错误：{ex.Message}"; }
    }

    private string ResolveExePath()
    {
        if (File.Exists(AgentExePath)) return AgentExePath;
        string alt = Path.Combine(Application.dataPath, "..", "Tools", "PoggetAgent", "PoggetAgent-debug.exe");
        if (File.Exists(alt)) return alt;
        alt = AgentExePath.Replace("-debug", "");
        if (File.Exists(alt)) return alt;
        return null;
    }

    private string FormatResponse(string cmd, string rawJson)
    {
        try
        {
            var resp = JsonUtility.FromJson<PoggetAgentResponse>(rawJson);
            if (resp == null) return $"\u2705 操作完成: {Truncate(rawJson, 200)}";
            if (!resp.ok) return $"\u274C 操作失败：{resp.error ?? "未知错误"}";

            return cmd switch
            {
                "ping" => $"\u2705 PoggetAgent 已连接（版本 {resp.version ?? "unknown"}）",
                "list_containers" => FormatContainers(resp),
                "get_container_items" => FormatItems(resp),
                "add_to_container" => $"\u2705 {resp.message ?? "文件已添加到收纳盒"}",
                "remove_from_container" => $"\u2705 {resp.message ?? "文件已从收纳盒移除"}",
                "create_container" => $"\u2705 {resp.message ?? "收纳盒已创建"}",
                "quickpanel_status" => FormatQuickPanel(resp),
                "organize_desktop" => FormatOrganize(resp),
                _ => $"\u2705 {resp.message ?? "操作成功"}"
            };
        }
        catch { return $"\u2705 操作完成: {Truncate(rawJson, 500)}"; }
    }

    private string FormatContainers(PoggetAgentResponse resp)
    {
        if (resp.result?.containers == null || resp.result.containers.Length == 0) return "\U0001f4e6 暂无收纳盒";
        var sb = new StringBuilder($"\U0001f4e6 共 {resp.result.containers.Length} 个收纳盒：");
        foreach (var c in resp.result.containers) sb.Append($"\n  \u2022 {c.title ?? c.id}");
        return sb.ToString();
    }

    private string FormatQuickPanel(PoggetAgentResponse resp)
    {
        if (resp.result == null) return $"\u2705 {resp.message ?? "侧边栏查询完成"}";
        var sb = new StringBuilder($"\U0001f4e6 {resp.message ?? "侧边栏状态"}");
        if (resp.result.hasQuickPanel.HasValue)
            sb.Append($"\n  \u2022 是否有侧边栏: {(resp.result.hasQuickPanel.Value ? "有（快速面板）" : "无")}");
        if (resp.result.configExists.HasValue)
            sb.Append($"\n  \u2022 配置文件: {(resp.result.configExists.Value ? "存在" : "不存在")}");
        if (!string.IsNullOrEmpty(resp.result.description))
            sb.Append($"\n  \u2022 说明: {resp.result.description}");
        return sb.ToString();
    }

    private string FormatItems(PoggetAgentResponse resp)
    {
        if (resp.result?.items == null || resp.result.items.Length == 0) return "\U0001f4c4 收纳盒为空";
        var sb = new StringBuilder($"\U0001f4c4 收纳盒内共 {resp.result.items.Length} 个文件：");
        foreach (var item in resp.result.items) sb.Append($"\n  [{item.id}] {item.name ?? item.path}");
        return sb.ToString();
    }

    private string FormatOrganize(PoggetAgentResponse resp)
    {
        var sb = new StringBuilder($"\u2705 {resp.message ?? "桌面已整理"}");
        if (resp.result?.containers != null && resp.result.containers.Length > 0)
        {
            sb.Append($"，创建了 {resp.result.containers.Length} 个收纳盒");
            foreach (var c in resp.result.containers)
                sb.Append($"、「{c.title}」");
        }
        if (resp.result != null)
        {
            if (resp.result.totalMoved.HasValue) sb.Append($"，整理了 {resp.result.totalMoved} 个文件");
            if (resp.result.skipped.HasValue && resp.result.skipped > 0) sb.Append($"，跳过 {resp.result.skipped} 个");
        }
        return sb.ToString();
    }

    private static string Truncate(string v, int max) => string.IsNullOrEmpty(v) ? v : v.Length <= max ? v : v[..max] + "...";

    [Serializable] private class PoggetAgentArgs { public string cmd; public PoggetAgentParams @params; }
    [Serializable] private class PoggetAgentParams { public string containerId; public string[] paths; public int itemId; public string title; public string targetFolder; }
    [Serializable] private class PoggetAgentRequest { public string cmd; public PoggetAgentParams @params; }
    [Serializable] private class PoggetAgentResponse { public bool ok; public string error; public string message; public string version; public PoggetAgentResult result; }
    [Serializable] private class PoggetAgentResult { public ContainerInfo[] containers; public ItemInfo[] items; public int? totalMoved; public int? skipped; public bool? hasQuickPanel; public bool? configExists; public string description; }
    [Serializable] private class ContainerInfo { public string id; public string title; }
    [Serializable] private class ItemInfo { public int id; public string name; public string path; }
}
