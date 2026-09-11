using System;
using System.IO;
using NUnit.Framework;

/// <summary>首启状态与本地版本文本均在隔离目录验证，不触碰正式用户数据。</summary>
public class UserExperienceStateTests
{
    private string _previousDataRoot;
    private string _tempRoot;

    [SetUp]
    public void SetUp()
    {
        _previousDataRoot = Environment.GetEnvironmentVariable("FU_XUAN_DATA");
        _tempRoot = Path.Combine(Path.GetTempPath(), "fuxuan_experience_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        Environment.SetEnvironmentVariable("FU_XUAN_DATA", _tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("FU_XUAN_DATA", _previousDataRoot);
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true);
    }

    [Test]
    public void 新数据目录首次聊天应提供引导并持久化状态()
    {
        var state = UserExperienceState.Load();
        Assert.That(state.ShouldOfferOnboarding, Is.True);
        Assert.That(File.Exists(DataPathConfig.UserExperienceStateFile), Is.True);

        state.MarkCompleted(false);
        var restored = UserExperienceState.Load();
        Assert.That(restored.ShouldOfferOnboarding, Is.False);
        Assert.That(restored.onboardingCompleted, Is.True);
    }

    [Test]
    public void 已有偏好文件的旧用户不自动触发引导()
    {
        File.WriteAllText(Path.Combine(_tempRoot, "pet_preferences.json"), "{}");
        var state = UserExperienceState.Load();
        Assert.That(state.legacyUserDetected, Is.True);
        Assert.That(state.ShouldOfferOnboarding, Is.False);
    }

    [Test]
    public void 版本文件不存在时回退到开发版本()
    {
        Assert.That(RightPanel.ReadReleaseText(_tempRoot, "version.txt", "1.0-dev"), Is.EqualTo("1.0-dev"));
        File.WriteAllText(Path.Combine(_tempRoot, "version.txt"), "v1.0.13\r\n");
        Assert.That(RightPanel.ReadReleaseText(_tempRoot, "version.txt", "1.0-dev"), Is.EqualTo("v1.0.13"));
    }
}
