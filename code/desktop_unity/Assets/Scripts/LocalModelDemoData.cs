using System;

/// <summary>
/// 模型设置页使用的真实本地对话样例。
///
/// 数据来自 2026-08-21 的隔离质量测试（FU_XUAN_DATA + .test_mode），
/// 只作为 UI 对比展示，不写入忆境、聊天历史或生产统计。
/// </summary>
public static class LocalModelDemoData
{
    [Serializable]
    public sealed class Sample
    {
        public readonly string CaseId;
        public readonly string Input;
        public readonly string Reply;
        public readonly int LatencyMs;
        public readonly int ReplyChars;
        public readonly int RuleScore;

        public Sample(string caseId, string input, string reply, int latencyMs, int replyChars, int ruleScore)
        {
            CaseId = caseId;
            Input = input;
            Reply = reply;
            LatencyMs = latencyMs;
            ReplyChars = replyChars;
            RuleScore = ruleScore;
        }
    }

    public const string TestDate = "2026-08-21";
    public const string TestModel = "qwen3:8b";

    public static readonly Sample[] Qwen3Samples =
    {
        new Sample(
            "chat_001",
            "你好，今天过得怎么样？",
            "本座今日事务繁杂，不过尚可。你呢？若无要紧事，不妨说说近来可有烦心之事？",
            4100, 36, 4),
        new Sample(
            "chat_002",
            "你觉得我今天看起来有点累吗？",
            "你今日神色有些倦意，眉间略带疲惫。本座虽不能细察心绪，但法眼所见，你步履不如往日轻盈，眼神也比寻常淡了些。若无要紧事，不妨先歇息片刻，养足精神再行其他。",
            1403, 76, 4),
        new Sample(
            "chat_003",
            "用简单的话解释什么是缓存。",
            "缓存就是把常用的东西暂时存起来，方便以后快速找到。比如你常点的外卖，手机会记住你喜欢的口味，下次点的时候就不用再选那么多次。\n本座虽不常接触这类现代事物，但若以太卜司的推演来看，这就像把重要的信息预先放在容易取到的地方，省去反复寻找的麻烦。\n若你常需处理大量数据或操作，不妨考虑利用缓存来提升效率。",
            2003, 153, 5)
    };

    public static int AverageLatencyMs
    {
        get
        {
            int total = 0;
            for (int i = 0; i < Qwen3Samples.Length; i++) total += Qwen3Samples[i].LatencyMs;
            return Qwen3Samples.Length == 0 ? 0 : total / Qwen3Samples.Length;
        }
    }
}
