using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

// ================================================================
//  办公文档生成术式 — PPT / Word / Excel 三件套
//  架构：桥接服务器让 AI 组织内容 → 本地 Python (python-pptx/docx/openpyxl) 渲染
//  输出目录统一在 D:\DesktopPetData\Documents\
// ================================================================

/// <summary>
/// PPT 生成工具。接收需求描述，生成 .pptx 演示文稿（封面 + 章节页 + 结束页，16:9）。
/// </summary>
public class GeneratePptTool : AsyncToolBase
{
    public override string ToolName => "generate_ppt";
    public override string ToolDescription =>
        "【办公】生成 PowerPoint 演示文稿（.pptx）。用户说「帮我做个 PPT / 做个汇报 / 演示文稿」时调用。 " +
        "本座只需需求描述（主题、章节、要点），AI 会自动组织内容并渲染成专业排版的 PPT。生成在 D:\\DesktopPetData\\Documents\\。 " +
        "可指定主题色（blue 蓝/green 绿/purple 紫/dark 深色/orange 橙）。";
    public override string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("description", "string", "PPT 需求描述，如「做一个关于人工智能发展历程的汇报PPT，含起源、关键突破、当前应用、未来展望四个章节」"),
        ToolSchema.Opt("title", "string", "PPT 标题，用于命名文件（如「AI 发展历程」）"),
        ToolSchema.Opt("theme", "string", "主题色：blue / green / purple / dark / orange（默认 blue）")
    );

    protected override async Task<string> ExecuteAsyncTask(string argsJson)
    {
        string description = ToolHelpers.JsonRead(argsJson, "description");
        if (string.IsNullOrWhiteSpace(description))
            return "❌ 未提供 PPT 需求描述，请告诉本座想做什么主题的 PPT";

        string title = ToolHelpers.JsonRead(argsJson, "title");
        if (string.IsNullOrWhiteSpace(title)) title = null;

        string theme = ToolHelpers.JsonRead(argsJson, "theme");
        if (string.IsNullOrWhiteSpace(theme)) theme = null;

        return await OfficeTools.RunOfficeGeneration("ppt", description, title, theme);
    }
}

/// <summary>
/// Word 文档生成工具。接收需求描述，生成 .docx 文档（标题 + 正文 + 列表，中文首行缩进排版）。
/// </summary>
public class GenerateDocxTool : AsyncToolBase
{
    public override string ToolName => "generate_docx";
    public override string ToolDescription =>
        "【办公】生成 Word 文档（.docx）。用户说「帮我写一份 Word 文档 / 文案 / 报告 / 通知 / 会议纪要」且需要可编辑文档时调用。 " +
        "本座只需需求描述，AI 会自动组织内容并渲染成排版规范的 Word 文档。生成在 D:\\DesktopPetData\\Documents\\。";
    public override string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("description", "string", "文档需求描述，如「写一份关于 2026 年工作计划的 Word 文档，含目标、措施、时间表」"),
        ToolSchema.Opt("title", "string", "文档标题，用于命名文件（如「2026 工作计划」）")
    );

    protected override async Task<string> ExecuteAsyncTask(string argsJson)
    {
        string description = ToolHelpers.JsonRead(argsJson, "description");
        if (string.IsNullOrWhiteSpace(description))
            return "❌ 未提供文档需求描述，请告诉本座想生成什么样的文档";

        string title = ToolHelpers.JsonRead(argsJson, "title");
        if (string.IsNullOrWhiteSpace(title)) title = null;

        return await OfficeTools.RunOfficeGeneration("docx", description, title, null);
    }
}

/// <summary>
/// Excel 表格生成工具。接收需求描述，生成 .xlsx 表格（多 Sheet、表头样式、自动筛选、冻结首行）。
/// </summary>
public class GenerateXlsxTool : AsyncToolBase
{
    public override string ToolName => "generate_xlsx";
    public override string ToolDescription =>
        "【办公】生成 Excel 表格（.xlsx）。用户说「帮我做个表格 / 数据表 / 清单 / 统计表」时调用。 " +
        "本座只需需求描述，AI 会自动组织数据并渲染成带样式（表头高亮、边框、筛选、冻结首行）的 Excel。生成在 D:\\DesktopPetData\\Documents\\。";
    public override string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("description", "string", "表格需求描述，如「做一个项目进度表，含任务、负责人、进度百分比、状态四列，至少5行数据」"),
        ToolSchema.Opt("title", "string", "工作簿标题，用于命名文件（如「项目进度表」）")
    );

    protected override async Task<string> ExecuteAsyncTask(string argsJson)
    {
        string description = ToolHelpers.JsonRead(argsJson, "description");
        if (string.IsNullOrWhiteSpace(description))
            return "❌ 未提供表格需求描述，请告诉本座想生成什么样的表格";

        string title = ToolHelpers.JsonRead(argsJson, "title");
        if (string.IsNullOrWhiteSpace(title)) title = null;

        return await OfficeTools.RunOfficeGeneration("xlsx", description, title, null);
    }
}

/// <summary>
/// 办公工具共享逻辑：调用桥接生成 → 解析 JSON → 打开文件。
/// </summary>
public static class OfficeTools
{
    /// <summary>办公文档输出目录（与桥接层约定一致，统一走 DataPathConfig）</summary>
    public static readonly string OutputBaseDir = DataPathConfig.DocumentsDir;

    public static async Task<string> RunOfficeGeneration(string type, string description, string title, string theme)
    {
        string result = await OpenClawBridge.GenerateOfficeAsync(type, description, title, theme);

        try
        {
            var obj = JObject.Parse(result);
            bool success = obj["success"]?.Value<bool>() ?? false;
            if (success)
            {
                string path = obj["path"]?.ToString() ?? "";
                string docTitle = obj["title"]?.ToString() ?? "";
                string folderPath = obj["folder_path"]?.ToString() ?? "";
                string typeName = type == "ppt" ? "PPT" : type == "docx" ? "Word 文档" : "Excel 表格";
                Debug.Log($"[OfficeTools] ✅ {typeName} 已生成: {path}");

                // 自动打开文件
                if (!string.IsNullOrEmpty(path))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                            { UseShellExecute = true });
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[OfficeTools] 自动打开文件失败（无害）: {ex.Message}");
                    }
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"✅ {typeName} 生成成功！");
                sb.AppendLine($"📄 文件：{path}");
                if (!string.IsNullOrEmpty(folderPath))
                    sb.AppendLine($"📁 目录：{folderPath}");
                sb.Append($"💡 已自动打开，可随时对我说「修改{docTitle}」或让我重新生成。");
                return sb.ToString();
            }
            else
            {
                string err = obj["error"]?.ToString() ?? "未知错误";
                Debug.LogWarning($"[OfficeTools] ❌ 生成失败: {err}");
                return $"❌ 生成失败：{err}。可换个更明确的描述重试（如明确章节数量、内容要点等）。";
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[OfficeTools] 解析返回结果失败: {ex.Message}");
            return $"❌ 生成失败：{result}";
        }
    }
}
