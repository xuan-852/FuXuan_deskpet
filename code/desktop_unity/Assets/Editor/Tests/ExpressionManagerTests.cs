using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Runtime.Serialization;

public class ExpressionManagerTests
{
    private static ExpressionManager CreateManager()
    {
        var mapper = (Live2DParameterMapper)FormatterServices.GetUninitializedObject(typeof(Live2DParameterMapper));
        return new ExpressionManager(mapper);
    }

    [Test]
    public void UnknownExpression_IsRejectedWithoutChangingState()
    {
        var manager = CreateManager();
        LogAssert.Expect(LogType.Warning, "[ExpressionManager] 未找到表情: missing");
        Assert.That(manager.TryPlay("missing"), Is.False);
        Assert.That(manager.CurrentExpression, Is.Null);
        Assert.That(manager.IsPlaying, Is.False);
    }

    [Test]
    public void RepeatedExpressionPlay_DoesNotStartAnotherTransition()
    {
        var manager = CreateManager();
        manager.RegisterPreset("happy", new ExpressionData { name = "happy", fadeIn = 0.4f });

        Assert.That(manager.TryPlay("happy"), Is.True);
        manager.Update(0.4f);
        Assert.That(manager.IsTransitioning, Is.False);
        Assert.That(manager.TryPlay("happy"), Is.True);
        Assert.That(manager.CurrentExpression, Is.EqualTo("happy"));
        Assert.That(manager.IsTransitioning, Is.False);
    }

    [Test]
    public void StopWithFade_ClearsCurrentNameButKeepsFadeState()
    {
        var manager = CreateManager();
        manager.RegisterPreset("happy", new ExpressionData { name = "happy", fadeOut = 0.3f });
        Assert.That(manager.TryPlay("happy", 0f), Is.True);

        manager.Stop(0.3f);

        Assert.That(manager.CurrentExpression, Is.Null);
        Assert.That(manager.IsPlaying, Is.True);
        Assert.That(manager.IsTransitioning, Is.True);
        manager.Update(0.31f);
        Assert.That(manager.IsPlaying, Is.False);
    }

    [Test]
    public void StopImmediate_ClearsCurrentAndOldExpression()
    {
        var manager = CreateManager();
        manager.RegisterPreset("happy", new ExpressionData { name = "happy" });
        Assert.That(manager.TryPlay("happy"), Is.True);

        manager.StopImmediate();

        Assert.That(manager.CurrentExpression, Is.Null);
        Assert.That(manager.IsPlaying, Is.False);
        Assert.That(manager.IsTransitioning, Is.False);
    }
}
