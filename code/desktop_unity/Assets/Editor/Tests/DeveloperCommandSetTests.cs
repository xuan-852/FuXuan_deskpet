using System;
using System.IO;
using NUnit.Framework;

public class DeveloperCommandSetTests
{
    [Test]
    public void ParsesTheThreeSupportedCommands()
    {
        DeveloperCommandSet.ParsedCommand parsed;

        Assert.IsTrue(DeveloperCommandSet.TryParse("/mode set test", out parsed));
        Assert.AreEqual(DeveloperCommandSet.CommandType.SetTestMode, parsed.Type);

        Assert.IsTrue(DeveloperCommandSet.TryParse(" /mode   set   normality ", out parsed));
        Assert.AreEqual(DeveloperCommandSet.CommandType.SetNormalMode, parsed.Type);

        Assert.IsTrue(DeveloperCommandSet.TryParse("/tell mode", out parsed));
        Assert.AreEqual(DeveloperCommandSet.CommandType.TellMode, parsed.Type);
    }

    [Test]
    public void OrdinaryTextIsNotADeveloperCommand()
    {
        DeveloperCommandSet.ParsedCommand parsed;

        Assert.IsFalse(DeveloperCommandSet.TryParse("请告诉我今天的安排", out parsed));
        Assert.AreEqual(DeveloperCommandSet.CommandType.None, parsed.Type);
    }

    [Test]
    public void InvalidDeveloperCommandIsHandledLocally()
    {
        string reply;

        Assert.IsTrue(DeveloperCommandSet.TryHandle("/mode set production", out reply));
        StringAssert.Contains("/mode set test", reply);
    }

    [Test]
    public void ModeSwitchUsesOnlyTheResolvedDataRoot()
    {
        string previous = Environment.GetEnvironmentVariable("FU_XUAN_DATA");
        string tempRoot = Path.Combine(Path.GetTempPath(), "fuxuan_developer_command_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            Environment.SetEnvironmentVariable("FU_XUAN_DATA", tempRoot);

            string reply;
            Assert.IsTrue(DeveloperCommandSet.TryHandle("/mode set test", out reply));
            Assert.IsTrue(File.Exists(Path.Combine(tempRoot, ".test_mode")));

            Assert.IsTrue(DeveloperCommandSet.TryHandle("/tell mode", out reply));
            StringAssert.Contains("测试模式", reply);

            Assert.IsTrue(DeveloperCommandSet.TryHandle("/mode set normality", out reply));
            Assert.IsFalse(File.Exists(Path.Combine(tempRoot, ".test_mode")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FU_XUAN_DATA", previous);
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
    }
}
