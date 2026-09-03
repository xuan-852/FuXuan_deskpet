using System;
using System.IO;
using NUnit.Framework;

public class DataPathConfigTests
{
    private string _previousDataRoot;
    private string _tempRoot;

    [SetUp]
    public void SetUp()
    {
        _previousDataRoot = Environment.GetEnvironmentVariable("FU_XUAN_DATA");
        _tempRoot = Path.Combine(Path.GetTempPath(), "fuxuan_data_path_test_" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("FU_XUAN_DATA", _previousDataRoot);
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, true);
    }

    [Test]
    public void ExistingConfiguredDirectoryWins()
    {
        Directory.CreateDirectory(_tempRoot);
        Environment.SetEnvironmentVariable("FU_XUAN_DATA", _tempRoot);

        Assert.That(DataPathConfig.DataRoot, Is.EqualTo(_tempRoot));
        Assert.That(DataPathConfig.HasConfiguredDataRoot, Is.True);
    }

    [Test]
    public void MissingConfiguredDirectoryFallsBackToExistingDefault()
    {
        if (!Directory.Exists(DataPathConfig.DefaultDataRoot))
            Assert.Ignore("本机没有默认数据目录，跳过仅针对既有默认目录的回退断言");

        Environment.SetEnvironmentVariable(
            "FU_XUAN_DATA",
            Path.Combine(Path.GetTempPath(), "fuxuan_missing_" + Guid.NewGuid().ToString("N")));

        Assert.That(DataPathConfig.DataRoot,
            Is.EqualTo(DataPathConfig.DefaultDataRoot));
    }

    [Test]
    public void EnsureDataRootCreatesOnlyResolvedDirectory()
    {
        Environment.SetEnvironmentVariable("FU_XUAN_DATA", _tempRoot);

        string error;
        Assert.That(DataPathConfig.EnsureDataRoot(out error), Is.True);
        Assert.That(error, Is.Null);
        // 保证解析出的数据根目录存在（旧目录回退时解析结果可能是旧 C/D 目录），与环境无关。
        Assert.That(Directory.Exists(DataPathConfig.DataRoot), Is.True);
    }
}
