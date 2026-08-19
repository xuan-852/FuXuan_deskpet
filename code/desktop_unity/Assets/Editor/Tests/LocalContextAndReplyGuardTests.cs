using NUnit.Framework;

public class LocalContextAndReplyGuardTests
{
    [Test]
    public void TimeQuestionUsesDeterministicLocalContext()
    {
        bool handled = LocalContextAnswer.TryBuild("现在几点了？", out string reply);

        Assert.That(handled, Is.True);
        StringAssert.Contains("现在是", reply);
        StringAssert.Contains("本座", reply);
    }

    [Test]
    public void WeatherQuestionNeverInventsWhenDataIsUnavailable()
    {
        bool handled = LocalContextAnswer.TryBuild("今天天气怎么样？", out string reply);

        Assert.That(handled, Is.True);
        StringAssert.Contains("暂未取得实时天气数据", reply);
    }

    [Test]
    public void FavoriteQuestionUsesStableFuXuanAnswer()
    {
        bool handled = LocalContextAnswer.TryBuild("简单说说你最喜欢做什么。", out string reply);

        Assert.That(handled, Is.True);
        StringAssert.Contains("本座最喜欢", reply);
        StringAssert.Contains("观测星象", reply);
    }

    [Test]
    public void TwoHourStudyPlanHasExactTotalDuration()
    {
        bool handled = LocalContextAnswer.TryBuild("帮我安排一个今晚两小时的学习计划。", out string reply);

        Assert.That(handled, Is.True);
        StringAssert.Contains("合计正好120分钟", reply);
    }

    [Test]
    public void ReplyGuardStabilizesSelfReferenceAndOwnerAddress()
    {
        string reply = LocalReplyPostProcessor.Process("我觉得将军应该听我的。主人，主人，主人。");

        Assert.That(reply, Is.EqualTo("本座觉得主人应该听本座的。主人，你，你。"));
        StringAssert.DoesNotContain("将军", reply);
    }

    [Test]
    public void ReplyGuardKeepsSelfReflectionWordIntact()
    {
        Assert.That(LocalReplyPostProcessor.Process("保持自我，不要怀疑我。"), Is.EqualTo("保持自我，不要怀疑本座。"));
    }

    [Test]
    public void ReplyGuardHonorsExplicitThreeSentenceRequest()
    {
        string reply = "第一句。\n第二句。\n第三句。\n多余的收尾。";
        string processed = LocalReplyPostProcessor.Process(reply, "用三句话告诉我如何开始。");
        Assert.That(processed, Is.EqualTo("第一句。\n第二句。\n第三句。"));
    }
}
