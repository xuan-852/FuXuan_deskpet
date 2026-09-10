using System;
using System.IO;
using System.Text;
using NUnit.Framework;

public class AtomicFileWriterTests
{
    private string _root;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "fuxuan_atomic_file_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test]
    public void ReplacingExistingFilePreservesLastKnownGoodBackup()
    {
        string target = Path.Combine(_root, "memory.json");
        AtomicFileWriter.WriteAllText(target, "first", new UTF8Encoding(false));
        AtomicFileWriter.WriteAllText(target, "second", new UTF8Encoding(false));

        Assert.AreEqual("second", File.ReadAllText(target));
        Assert.AreEqual("first", File.ReadAllText(target + ".bak"));
        Assert.IsEmpty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Test]
    public void FirstWriteCreatesTargetWithoutTemporaryResidue()
    {
        string target = Path.Combine(_root, "knowledge.json");
        AtomicFileWriter.WriteAllText(target, "{}", new UTF8Encoding(false));

        Assert.IsTrue(File.Exists(target));
        Assert.IsEmpty(Directory.GetFiles(_root, "*.tmp"));
    }
}
