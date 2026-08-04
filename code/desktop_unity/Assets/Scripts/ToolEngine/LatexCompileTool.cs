using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

// ================================================================
//  LaTeX 编译术式 — 将 LaTeX 源码编译为 PDF
// ================================================================

/// <summary>
/// LaTeX 编译工具。接收用户需求描述，通过桥接服务器让 AI 生成源码并编译为 PDF。
/// 源码保留为 .tex 文件，便于后续修改；清理 .aux .log .out 中间产物。
/// 输出目录统一在 D:\DesktopPetData\Documents\。
/// </summary>
public class LatexCompileTool : AsyncToolBase
{
    public override string ToolName => "compile_latex";
    public override string ToolDescription =>
        "【专业排版】生成并编译 LaTeX 文档为 PDF。用户说「帮我写一份报告/论文/简历/文档」 " +
        "且需要 PDF 输出时调用此术式。本座只需需求描述，AI 会自动生成源码并编译。生成位置在 D:\\DesktopPetData\\Documents\\。";
    public override string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("description", "string", "文档需求描述，如「帮我写一份关于人工智能发展史的学术报告，约2000字，含摘要和参考文献」"),
        ToolSchema.Opt("title", "string", "文档标题，用于命名文件夹和文件（如「学术报告」「个人简历」）"),
        ToolSchema.Opt("output", "string", "输出的 .tex 文件路径（可选，默认自动生成）"),
        ToolSchema.Opt("compiler", "string", "编译器：xelatex（默认，中文友好）/ pdflatex / lualatex"),
        ToolSchema.Opt("pin_to_desktop", "boolean", "是否在桌面创建快捷方式，默认 false")
    );

    protected override async Task<string> ExecuteAsyncTask(string argsJson)
    {
        string description = ToolHelpers.JsonRead(argsJson, "description");
        if (string.IsNullOrWhiteSpace(description))
            return "❌ 未提供文档需求描述，请告诉本座想生成什么样的文档";

        string title = ToolHelpers.JsonRead(argsJson, "title");
        if (string.IsNullOrWhiteSpace(title)) title = null;

        string output = ToolHelpers.JsonRead(argsJson, "output");
        if (string.IsNullOrWhiteSpace(output))
            output = null;

        string compiler = ToolHelpers.JsonRead(argsJson, "compiler");
        if (string.IsNullOrWhiteSpace(compiler))
            compiler = "xelatex";

        string pinStr = ToolHelpers.JsonRead(argsJson, "pin_to_desktop");
        bool pinToDesktop = pinStr == "true";

        string result = await OpenClawBridge.CompileLatexAsync(null, output, compiler, title, pinToDesktop, description);

        try
        {
            var obj = JObject.Parse(result);
            bool success = obj["success"]?.Value<bool>() ?? false;
            if (success)
            {
                string pdfPath = obj["pdf_path"]?.ToString() ?? "未知";
                string texPath = obj["tex_path"]?.ToString() ?? "未知";
                string folderPath = obj["folder_path"]?.ToString() ?? "";
                string shortcutPath = obj["shortcut_path"]?.ToString() ?? "";
                string docTitle = obj["title"]?.ToString() ?? "文档";
                Debug.Log($"[LatexCompileTool] ✅ PDF 已生成: {pdfPath}");

                // 自动打开 PDF
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pdfPath)
                        { UseShellExecute = true });
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[LatexCompileTool] 自动打开 PDF 失败（无害）: {ex.Message}");
                }

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("✅ 文档编译成功！");
                sb.AppendLine($"📄 PDF：{pdfPath}");
                sb.AppendLine($"📝 源码：{texPath}");
                if (!string.IsNullOrEmpty(folderPath))
                    sb.AppendLine($"📁 目录：{folderPath}");
                if (!string.IsNullOrEmpty(shortcutPath))
                    sb.AppendLine($"🔗 桌面快捷方式：{shortcutPath}");
                sb.Append($"💡 可随时对我说「修改{docTitle}」，我会读取 .tex 文件帮你修改。");
                return sb.ToString();
            }
            else
            {
                string err = obj["error"]?.ToString() ?? "未知编译错误";
                string logTail = obj["log_tail"]?.ToString() ?? "";
                string logPath = obj["log_path"]?.ToString() ?? "";
                Debug.LogWarning($"[LatexCompileTool] ❌ 编译失败: {err}");
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"❌ 编译失败：{err}");
                if (!string.IsNullOrEmpty(logTail))
                {
                    string head = logTail.Length > 800 ? logTail.Substring(0, 800) + "…" : logTail;
                    sb.AppendLine("```");
                    sb.AppendLine(head);
                    sb.AppendLine("```");
                }
                if (!string.IsNullOrEmpty(logPath))
                    sb.AppendLine($"📄 编译日志：{logPath}");
                sb.AppendLine("💡 若因文档过长失败，可分段生成（如「先写第 1-4 模块，再写第 5-8 模块」）；若因内存不足，请先关闭部分程序后重试。");
                return sb.ToString();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LatexCompileTool] 解析返回结果失败: {ex.Message}");
            return $"❌ 编译失败：{result}";
        }
    }
}
