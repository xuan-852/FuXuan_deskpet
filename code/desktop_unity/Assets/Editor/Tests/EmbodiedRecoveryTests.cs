using System;
using System.Collections.Generic;
using NUnit.Framework;

public class EmbodiedRecoveryTests
{
    [Test] public void 记录写入后可恢复到基线且幂等()
    {
        var state = new EmbodiedPoseState();
        state.RecordWrite("Param94", 12.5f, 0f);
        state.RecordWrite("Param94", 15f, 0f);
        Assert.IsTrue(state.HasPendingRestore);
        var applied = new List<string>();
        var restored = state.RestoreAll((id, value) => { applied.Add($"{id}={value}"); });
        Assert.AreEqual(1, restored.Count);
        Assert.AreEqual("Param94", restored[0]);
        CollectionAssert.AreEqual(new[] { "Param94=0" }, applied);
        Assert.IsFalse(state.HasPendingRestore);
        var second = state.RestoreAll((id, value) => applied.Add("again"));
        Assert.AreEqual(0, second.Count);
        Assert.AreEqual(1, applied.Count);
    }

    [Test] public void 多参数分别还原到各自基线()
    {
        var state = new EmbodiedPoseState();
        state.RecordWrite("ParamA", 3f, 1f);
        state.RecordWrite("ParamB", -2f, 0f);
        var map = new Dictionary<string, float>();
        state.RestoreAll((id, value) => map[id] = value);
        Assert.AreEqual(1f, map["ParamA"]);
        Assert.AreEqual(0f, map["ParamB"]);
    }

    [Test] public void 无记录时恢复为空且不调用回调()
    {
        var state = new EmbodiedPoseState();
        var calls = 0;
        var restored = state.RestoreAll((id, value) => calls++);
        Assert.AreEqual(0, restored.Count);
        Assert.AreEqual(0, calls);
    }

    [Test] public void 空参数名被拒绝()
    {
        var state = new EmbodiedPoseState();
        Assert.Throws<ArgumentException>(() => state.RecordWrite("", 1f, 0f));
    }
}
