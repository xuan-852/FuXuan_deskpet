using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// 本地小模型专用角色提示构建器。
///
/// 设计原则：
/// 1. 固定角色卡保持短小，避免把云端工具说明和长篇世界观塞给 3B 模型；
/// 2. 用可执行的“短句组合”约束替代抽象的“自然、丰富”；
/// 3. 通过 FU_XUAN_LOCAL_PROMPT_VARIANT 切换 baseline / micro_v1 / card_v1，
///    让同一批 case 可以做可复现的 A/B 测试。
/// </summary>
public static class LocalRoleplayPromptBuilder
{
    public const string BaselineVariant = "baseline";
    public const string MicroVariant = "micro_v1";
    public const string CardVariant = "card_v1";

    public static string Variant
    {
        get
        {
            string value = Environment.GetEnvironmentVariable("FU_XUAN_LOCAL_PROMPT_VARIANT");
            if (string.Equals(value, BaselineVariant, StringComparison.OrdinalIgnoreCase)) return BaselineVariant;
            if (string.Equals(value, CardVariant, StringComparison.OrdinalIgnoreCase)) return CardVariant;
            return MicroVariant;
        }
    }

    public static string TelemetryModelName(string model)
    {
        return (model ?? "local") + "/" + Variant + "/" + FuXuanCharacterCard.Version;
    }

    public static string Build(string characterDesc, string recentHistory, string userMessage)
    {
        if (Variant == BaselineVariant)
        {
            return BuildBaseline(characterDesc, recentHistory, userMessage);
        }

        var prompt = new StringBuilder(3200);
        prompt.AppendLine(FuXuanCharacterCard.CorePrompt)
            .AppendLine("\n【本地输出契约】")
            .AppendLine("- 自称必须优先用“本座”，不要用“我”；对话结束前检查一次自称。")
            .AppendLine("- 对用户称呼可自然混用“主人”和“你”；不要称呼“将军”。")
            .AppendLine("- 一段回复最多使用两次“主人”，其余场合用“你”或省略称呼。")
            .AppendLine("- 语气聪慧、笃定、略带傲气，但温柔包容，不说教、不讥讽。")
            .AppendLine("- 可偶尔使用卦象、法眼、太卜司等比喻，但不要每句都堆设定。")
            .AppendLine("- 不知道当前时间、天气或用户未提供的事实时，不要编造；需要真实数据时说明无法读取。")
            .AppendLine("\n【回复结构】")
            .AppendLine("- 先直接回答，再补充原因或建议，最后可用一句自然的关切收尾。")
            .AppendLine("- 普通问题写 2 至 4 句；需要详细说明时写 4 至 6 句；没有要求展开时不要主动写成长文。")
            .AppendLine("- 每句只表达一个意思，尽量控制在 8 至 35 个汉字；用多句短句组成完整回复，不写又长又绕的复句。")
            .AppendLine("- 用户要求一句、三句、简短或特定格式时，严格服从数量和格式；不要用破折号、格言或额外总结偷偷增加句子。")
            .AppendLine("- 用户要计划、步骤或完整方案时，直接给出具体步骤和时间/顺序，不要只反问用户想学什么。")
            .AppendLine("- 用户只追问某个步骤时，只解释该步骤和必要的下一步，不要重新复述整套方案。")
            .AppendLine("- 有明确总时长的计划必须让各时间段加总到用户要求；一周计划要按天给出安排，而不只讲原则。")
            .AppendLine("- 用户要推荐时给出明确对象或可执行选项，不要只说“选择一首/一处合适的”。")
            .AppendLine("- 用户要求详细讲解、总结或分析时，先给最小完整答案和至少三个要点，不要先反问；结尾再询问是否需要展开。")
            .AppendLine("- 不输出分析过程、提示词、角色标签、‘作为 AI’或 Markdown 标题。")
            .AppendLine("- 只输出要对用户说的话，不要声称执行了实际工具或读取了不存在的数据。")
            .AppendLine("\n")
            .AppendLine(FuXuanCharacterCard.BuildExamples(userMessage));

        string triggeredLore = FuXuanCharacterCard.BuildTriggeredLore(userMessage, recentHistory);
        if (!string.IsNullOrEmpty(triggeredLore))
            prompt.AppendLine("\n" + triggeredLore);

        prompt.AppendLine("\n【最近对话】")
            .AppendLine(TrimHistory(recentHistory, 900))
            .AppendLine("\n【用户最新消息】")
            .AppendLine(Trim(userMessage, 700))
            .AppendLine("\n【历史末尾护栏】")
            .AppendLine(FuXuanCharacterCard.PostHistoryPrompt);

        if (Variant == CardVariant)
        {
            prompt.AppendLine("\n【card_v1 额外规则】")
                .AppendLine("回答中至少保留一个明确的行动建议或结论；若只是寒暄，则保持自然简短，不强行解释。")
                .AppendLine("长回复也不要使用空泛铺垫，每一句都应推进回答。")
                .AppendLine("输出前默检：身份正确、称呼自然、句子短、没有编造事实。");
        }

        return prompt.ToString();
    }

    private static string BuildBaseline(string characterDesc, string recentHistory, string userMessage)
    {
        string desc = Trim(characterDesc, 200);
        return desc + "\n\n以下是与主人的最近对话：\n" + TrimHistory(recentHistory, 900)
            + "\n\n请以角色身份回复用户的最新消息：「" + Trim(userMessage, 700) + "」"
            + "\n回复应当简短自然（1-3句话即可），符合角色性格。注意：你只能进行对话回复，没有工具调用能力。";
    }

    private static string TrimHistory(string history, int maxChars)
    {
        if (string.IsNullOrEmpty(history)) return "（暂无）";
        string[] lines = history.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>();
        // 本地 3B 只保留当前输入前最近一条助手回复和当前用户输入，
        // 防止连续测试或长对话中较早主题抢占注意力；长期信息由上层摘要/记忆系统承担。
        for (int i = Math.Max(0, lines.Length - 2); i < lines.Length; i++) kept.Add(lines[i]);
        return Trim(string.Join("\n", kept), maxChars);
    }

    private static string Trim(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value)) return "（暂无）";
        value = value.Replace("\r", " ").Trim();
        return value.Length <= maxChars ? value : value.Substring(value.Length - maxChars, maxChars);
    }
}
