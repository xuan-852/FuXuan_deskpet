using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ================================================================
//  太卜阵法图 — 任务模板库查询/管理工具（P5.3）
//  配合 openclaw_task 的 template 参数使用：
//    用户说「看看 xx 官网有没有新公告」→ LLM 选中 check_website_updates 模板 →
//    以 template=模板名 + template_args={...} 调用 openclaw_task，省 token
// ================================================================

public class QueryTaskTemplatesTool : IPetTool
{
    public string ToolName => "query_task_templates";
    public string ToolDescription => "【太卜阵法图】查询本座预置/自建的任务模板清单（模板名、用途、分类）。当用户请求属于高频任务（查官网公告/看仓库更新/下载文件/查价格/总结网页）时，先查此清单确认可用模板，再通过 openclaw_task 的 template 参数调用以节省 token。";
    public string ToolParametersJson => ToolSchema.Empty;
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        if (TaskTemplateManager.Instance == null)
            return "❌ 太卜阵法图尚未展开（TaskTemplateManager 未初始化）";
        return TaskTemplateManager.Instance.ListTemplatesText();
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class SaveTaskTemplateTool : IPetTool
{
    public string ToolName => "save_task_template";
    public string ToolDescription => "【太卜阵法图】把高频任务保存为任务模板（后续 openclaw_task 可用 template=模板名 直接调用）。当用户反复请求同类任务、或用户明确要求「记住这个任务流程」时使用。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("name", "string", "模板名（snake_case，如 check_website_updates）"),
        ToolSchema.Req("template", "string", "任务描述模板，占位符用 {变量名} 包裹，如「访问 {url} 查看最新公告并汇总」"),
        ToolSchema.Opt("description", "string", "用途描述（给 AI 判断何时用此模板）"),
        ToolSchema.Opt("category", "string", "分类（信息/下载/监控/研究等）")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        if (TaskTemplateManager.Instance == null)
            return "❌ 太卜阵法图尚未展开（TaskTemplateManager 未初始化）";

        string name = ToolHelpers.JsonRead(argsJson, "name");
        string template = ToolHelpers.JsonRead(argsJson, "template");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(template))
            return "❌ 模板名（name）与任务描述（template）均为必填";

        string description = ToolHelpers.JsonRead(argsJson, "description");
        string category = ToolHelpers.JsonRead(argsJson, "category");

        if (TaskTemplateManager.Instance.SaveTemplate(name, description, template, category))
            return $"✅ 模板已保存「{name}」。之后可用 openclaw_task 的 template={name} 参数直接调用。";
        return "❌ 模板保存失败：模板名非法或已达上限（" + TaskTemplateManager.Instance.maxTemplates + " 个）";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class RemoveTaskTemplateTool : IPetTool
{
    public string ToolName => "remove_task_template";
    public string ToolDescription => "【太卜阵法图】删除一个已保存的任务模板（预置模板也可删，下次启动会补回）。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("name", "string", "要删除的模板名")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        if (TaskTemplateManager.Instance == null)
            return "❌ 太卜阵法图尚未展开（TaskTemplateManager 未初始化）";

        string name = ToolHelpers.JsonRead(argsJson, "name");
        if (string.IsNullOrWhiteSpace(name))
            return "❌ 请指定要删除的模板名（name）";

        if (TaskTemplateManager.Instance.RemoveTemplate(name))
            return $"✅ 模板「{name}」已删除。";
        return $"❌ 未找到模板「{name}」，可先调用 query_task_templates 查看现有模板。";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}
