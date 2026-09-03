using System;
using NUnit.Framework;

public class LocalRoleplayPromptBuilderTests
{
    private string _previousVariant;

    [SetUp]
    public void SetUp()
    {
        _previousVariant = Environment.GetEnvironmentVariable("FU_XUAN_LOCAL_PROMPT_VARIANT");
        Environment.SetEnvironmentVariable("FU_XUAN_LOCAL_PROMPT_VARIANT", null);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("FU_XUAN_LOCAL_PROMPT_VARIANT", _previousVariant);
    }

    [Test]
    public void DefaultPromptUsesMicroContractAndCorrectAddressing()
    {
        string prompt = LocalRoleplayPromptBuilder.Build("ignored", "user: 你好", "我有点累");

        Assert.That(LocalRoleplayPromptBuilder.Variant, Is.EqualTo(LocalRoleplayPromptBuilder.MicroVariant));
        StringAssert.Contains("自称必须优先用“本座”", prompt);
        StringAssert.Contains("可自然混用“主人”和“你”", prompt);
        StringAssert.Contains("不要称呼“将军”", prompt);
        StringAssert.Contains("用多句短句组成有内容的完整回复", prompt);
    }

    [Test]
    public void CardPromptAddsFinalSelfCheckWithoutChangingCoreContract()
    {
        Environment.SetEnvironmentVariable("FU_XUAN_LOCAL_PROMPT_VARIANT", LocalRoleplayPromptBuilder.CardVariant);
        string prompt = LocalRoleplayPromptBuilder.Build("ignored", "", "请给我建议");

        StringAssert.Contains("card_v1 额外规则", prompt);
        StringAssert.Contains("输出前默检", prompt);
        StringAssert.Contains("每句只表达一个意思", prompt);
    }

    [Test]
    public void BaselinePromptRemainsAvailableForAblation()
    {
        Environment.SetEnvironmentVariable("FU_XUAN_LOCAL_PROMPT_VARIANT", LocalRoleplayPromptBuilder.BaselineVariant);
        string prompt = LocalRoleplayPromptBuilder.Build("符玄角色描述", "user: 你好", "你好");

        Assert.That(LocalRoleplayPromptBuilder.Variant, Is.EqualTo(LocalRoleplayPromptBuilder.BaselineVariant));
        StringAssert.Contains("1-3句话", prompt);
        StringAssert.DoesNotContain("card_v1 额外规则", prompt);
    }

    [Test]
    public void MicroPromptIncludesLayeredCharacterCard()
    {
        string prompt = LocalRoleplayPromptBuilder.Build("ignored", "", "景元最近在做什么？");

        StringAssert.Contains("仙舟罗浮太卜司之首", prompt);
        StringAssert.Contains("推演是为了看清选择", prompt);
        StringAssert.Contains("【示例对话】", prompt);
        StringAssert.Contains("【当前相关设定】", prompt);
        StringAssert.Contains("景元是罗浮将军", prompt);
        StringAssert.Contains("【历史末尾护栏】", prompt);
        StringAssert.Contains("fu_card_v2", LocalRoleplayPromptBuilder.TelemetryModelName("qwen3:8b"));
    }

    [Test]
    public void UnrelatedPromptDoesNotInjectLorebook()
    {
        string prompt = LocalRoleplayPromptBuilder.Build("ignored", "", "今天适合休息吗？");

        StringAssert.DoesNotContain("【当前相关设定】", prompt);
        StringAssert.Contains("【示例对话】", prompt);
    }

    [Test]
    public void RelevantMemoryIsInjectedAsBoundedBackground()
    {
        string prompt = LocalRoleplayPromptBuilder.Build(
            "ignored", "", "我最近想听什么音乐？", "qwen3:8b",
            "【本座的忆境线索】\n- 主人喜欢古典音乐");

        StringAssert.Contains("【相关忆境】", prompt);
        StringAssert.Contains("主人喜欢古典音乐", prompt);
        StringAssert.Contains("可能过时的背景线索", prompt);
        StringAssert.Contains("【用户最新消息】", prompt);
    }

    [Test]
    public void BaselineAlsoReceivesMemoryContext()
    {
        Environment.SetEnvironmentVariable("FU_XUAN_LOCAL_PROMPT_VARIANT", LocalRoleplayPromptBuilder.BaselineVariant);
        string prompt = LocalRoleplayPromptBuilder.Build(
            "符玄角色描述", "", "你还记得我的偏好吗？", "qwen2.5:3b",
            "【本座的忆境线索】\n- 主人喜欢古典音乐");

        StringAssert.Contains("【相关忆境】", prompt);
        StringAssert.Contains("主人喜欢古典音乐", prompt);
    }
}
