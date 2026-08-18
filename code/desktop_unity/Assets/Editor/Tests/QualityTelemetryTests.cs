using System;
using NUnit.Framework;

/// <summary>质量遥测协议测试：只验证字段和脱敏，不写生产数据。</summary>
public class QualityTelemetryTests
{
    [Test]
    public void 日志包含来源任务和质量字段()
    {
        string line = QualityTelemetry.BuildJsonLine(new QualityTelemetry.QualityEvent
        {
            Task = "motion_translation",
            Source = "local",
            Model = "qwen2.5:3b",
            CaseId = "motion_001",
            Success = true,
            Accepted = true,
            ParseValid = true,
            SafetyValid = true,
            Score = -1,
            LatencyMs = 123,
            ReplyChars = -1,
            Keyframes = 6,
            Reason = "local_parse_ok"
        }, new DateTime(2026, 8, 18, 12, 0, 0));

        StringAssert.Contains("\"task\":\"motion_translation\"", line);
        StringAssert.Contains("\"src\":\"local\"", line);
        StringAssert.Contains("\"model\":\"qwen2.5:3b\"", line);
        StringAssert.Contains("\"case_id\":\"motion_001\"", line);
        StringAssert.Contains("\"parse\":true", line);
        StringAssert.Contains("\"keyframes\":6", line);
    }

    [Test]
    public void 原文不会进入遥测且原因会去换行和截断()
    {
        string reason = "local_generation_failed\n" + new string('x', 200);
        string line = QualityTelemetry.BuildJsonLine(new QualityTelemetry.QualityEvent
        {
            Task = "chat",
            Source = "local",
            Model = "qwen2.5:3b",
            Success = false,
            Reason = reason
        }, DateTime.UtcNow);

        StringAssert.DoesNotContain("\n", line.TrimEnd('\n'));
        Assert.LessOrEqual(line.Length, 600);
    }

    [Test]
    public void 案例编号只保留安全标识字符()
    {
        QualityTelemetry.SetCaseId("chat/001 中文");
        Assert.AreEqual("chat_001_中文", QualityTelemetry.CurrentCaseId);
        QualityTelemetry.SetCaseId("");
        Assert.AreEqual("", QualityTelemetry.CurrentCaseId);
    }
}
