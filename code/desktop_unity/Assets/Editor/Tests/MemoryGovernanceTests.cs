using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 核心记忆治理第一阶段测试：兼容元数据、过滤、近似去重和按问题检索。
/// 所有测试必须运行在 build.ps1 创建的 .test_mode 隔离目录中。
/// </summary>
public class MemoryGovernanceTests
{
    private GameObject _memoryGo;
    private PetMemory _memory;

    private static void ResetInstance()
    {
        var field = typeof(PetMemory)
            .GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
        field?.SetValue(null, null);
    }

    [SetUp]
    public void SetUp()
    {
        Assume.That(File.Exists(DataPathConfig.TestModeFile), Is.True,
            "记忆测试必须在 .test_mode 隔离目录中运行");
        ResetInstance();
        _memoryGo = new GameObject("TestPetMemory");
        _memory = _memoryGo.AddComponent<PetMemory>();
        var field = typeof(PetMemory)
            .GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
        field?.SetValue(null, _memory);
        _memory.ClearMemories();
    }

    [TearDown]
    public void TearDown()
    {
        if (_memoryGo != null) GameObject.DestroyImmediate(_memoryGo);
        ResetInstance();
    }

    [Test]
    public void 新记忆包含治理元数据()
    {
        bool added = _memory.AddMemoryWithMetadata(
            "主人正在准备考试", "考试", "conversation", 8, "user", 0.95f);

        Assert.IsTrue(added);
        var entry = _memory.GetAllMemories().Single();
        Assert.IsNotEmpty(entry.id);
        Assert.AreEqual("user", entry.source);
        Assert.AreEqual(0.95f, entry.confidence, 0.001f);
        Assert.IsNotEmpty(entry.timestamp);
        Assert.IsNotEmpty(entry.lastAccessAt);
    }

    [Test]
    public void 过短记忆被写入闸门拒绝()
    {
        Assert.IsFalse(_memory.AddMemoryWithMetadata("嗯", "闲聊", "conversation", 3, "system", 0.6f));
        Assert.AreEqual(0, _memory.MemoryCount);
    }

    [Test]
    public void 近似重复记忆合并而不是新增()
    {
        Assert.IsTrue(_memory.AddMemoryWithMetadata(
            "主人喜欢喝热茶", "偏好", "conversation", 5, "system", 0.6f));
        Assert.IsFalse(_memory.AddMemoryWithMetadata(
            "主人喜欢喝热茶。", "偏好", "conversation", 8, "local_model", 0.9f));

        Assert.AreEqual(1, _memory.MemoryCount);
        var entry = _memory.GetAllMemories().Single();
        Assert.AreEqual(8, entry.importance);
        Assert.AreEqual(0.9f, entry.confidence, 0.001f);
    }

    [Test]
    public void 当前问题优先检索相关记忆()
    {
        _memory.AddMemoryWithMetadata("主人正在准备考试", "考试", "conversation", 8, "user", 0.9f);
        _memory.AddMemoryWithMetadata("主人喜欢古典音乐", "音乐", "conversation", 6, "user", 0.9f);

        var result = _memory.GetRelevantMemories("考试安排", 5);

        Assert.IsTrue(result.Any(e => e.summary.Contains("考试")));
        Assert.IsFalse(result.Any(e => e.summary.Contains("古典音乐")),
            "无关记忆不应因为固定 Top-N 被带入当前问题");
    }

    [Test]
    public void 相关记忆注入保留核心事实并限制普通记忆()
    {
        _memory.AddCoreFact("主人称呼本座为符玄");
        for (int i = 0; i < 10; i++)
            _memory.AddMemoryWithMetadata($"主人正在处理考试事项{i}", "考试", "conversation", 5, "user", 0.8f);

        string formatted = _memory.GetFormattedMemories("考试");

        Assert.IsTrue(formatted.Contains("主人称呼本座为符玄"));
        Assert.LessOrEqual(formatted.Length, 3500);
    }
}
