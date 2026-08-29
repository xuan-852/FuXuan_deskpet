using NUnit.Framework;

/// <summary>空闲动作配置和运行时目标缓冲测试，不启动播放器或写入模型。</summary>
public class IdleActionSchedulerTests
{
    [Test]
    public void JsonDictionaryTargetsCanBeLoadedAndInterpolated()
    {
        string json = @"{
  'formatVersion': '1.0',
  'actions': [{
    'id': 1,
    'name': 'tilt',
    'displayName': '歪头',
    'weight': 5,
    'cooldown': 8,
    'phases': [
      {'duration': 1.0, 'curve': 'easeOut', 'targets': {'head_angle_z': 8}},
      {'duration': 1.0, 'curve': 'easeIn', 'targets': {'head_angle_z': 0}}
    ]
  }]
}".Replace('\'', '"');

        var scheduler = new Live2DFramework.ActionAgent.IdleActionScheduler();
        scheduler.LoadConfig(json);
        scheduler.ForceAction(1);

        var first = scheduler.GetCurrentTargets();
        Assert.IsNotNull(first);
        Assert.IsTrue(first.ContainsKey("head_angle_z"));
        Assert.AreEqual(0f, first["head_angle_z"], 0.001f);

        scheduler.UpdatePhase(0.5f);
        var second = scheduler.GetCurrentTargets();
        Assert.AreSame(first, second, "普通动作目标应复用运行时缓冲，避免每帧分配 Dictionary");
        Assert.Greater(second["head_angle_z"], 0f);
        Assert.Less(second["head_angle_z"], 8f);
    }

    [Test]
    public void CooldownUpdateDoesNotRequireTemporaryKeyList()
    {
        string json = @"{
  'formatVersion': '1.0',
  'actions': [
    {'id': 1, 'displayName': '一', 'weight': 1, 'cooldown': 2, 'phases': []},
    {'id': 2, 'displayName': '二', 'weight': 1, 'cooldown': 2, 'phases': []}
  ]
}".Replace('\'', '"');

        var scheduler = new Live2DFramework.ActionAgent.IdleActionScheduler();
        Assert.DoesNotThrow(() =>
        {
            scheduler.LoadConfig(json);
            scheduler.UpdateCooldowns(0.1f);
        });
    }
}
