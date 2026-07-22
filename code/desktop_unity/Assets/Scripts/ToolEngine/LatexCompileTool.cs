using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

// ================================================================
//  LaTeX 编译术式 — 将 LaTeX 源码编译为 PDF
// ================================================================

/// <summary>
/// LaTeX 编译工具。接收 LaTeX 源码，通过后台桥接服务器调 pdflatex 编译为 PDF。
/// 源码保留为 .tex 文件，便于后续修改；清理 .aux .log .out 中间产物。
/// 输出目录统一在 D:\DesktopPetData\Documents\。
/// </summary>
public class LatexCompileTool : AsyncToolBase
{
    public override string ToolName => "compile_latex";
    public override string ToolDescription =>
        "【专业排版】将 LaTeX 源码编译为 PDF 文档。用户说「帮我写一份报告/论文/简历/文档」 " +
        "且需要 PDF 输出时调用此术式。生成位置在 D:\\DesktopPetData\\Documents\\。";
    public override string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("source", "string", "完整的 LaTeX 文档源码，含 \\documentclass 和 \\begin{document}…\\end{document}"),
        ToolSchema.Opt("title", "string", "文档标题，用于命名文件夹和文件（如「学术报告」「个人简历」）"),
        ToolSchema.Opt("output", "string", "输出的 .tex 文件路径（可选，默认自动生成）"),
        ToolSchema.Opt("compiler", "string", "编译器：xelatex（默认，中文友好）/ pdflatex / lualatex"),
        ToolSchema.Opt("pin_to_desktop", "boolean", "是否在桌面创建快捷方式，默认 false")
    );

    protected override async Task<string> ExecuteAsyncTask(string argsJson)
    {
        string source = ToolHelpers.JsonRead(argsJson, "source");
        if (string.IsNullOrWhiteSpace(source))
            return "❌ 未提供 LaTeX 源码";

        // 检查源码完整性
        if (!source.Contains("\\documentclass"))
            return "⚠️ 源码缺少 \\documentclass，请补全后重试";
        if (!source.Contains("\\begin{document}") || !source.Contains("\\end{document}"))
            return "⚠️ 源码缺少 \\begin{document} / \\end{document}，请补全后重试";

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

        string result = await OpenClawBridge.CompileLatexAsync(source, output, compiler, title, pinToDesktop);

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
                Debug.LogWarning($"[LatexCompileTool] ❌ 编译失败: {err}");
                string detail = !string.IsNullOrEmpty(logTail) ? $"\n```\n{logTail}\n```" : "";
                return $"❌ 编译失败：{err}{detail}\n" +
                       $"💡 你可以把 LaTeX 源码发给我，我帮你检查语法错误。";
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LatexCompileTool] 解析返回结果失败: {ex.Message}");
            return $"❌ 编译失败：{result}";
        }
    }
}
