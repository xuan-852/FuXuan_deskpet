using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ToolAcademicQueryTests
{
    private static IEnumerator RunUnavailable(string toolName, System.Action<string> assert)
    {
        if (Object.FindObjectOfType<ServerPollService>() != null)
        {
            Assert.Ignore("A ServerPollService exists here; refusing to hit the local timetable endpoint.");
            yield break;
        }
        string result = null;
        ToolRegistry.Initialize();
        yield return ToolRegistry.ExecuteAsync(toolName, "{}", r => result = r);
        assert(result);
    }

    [UnityTest]
    public IEnumerator ExamQueryReportsUnavailableServiceWithoutThrowing()
    {
        yield return RunUnavailable("query_exams", result =>
            Assert.AreEqual("❌ 课表传讯服务未就绪", result, "考试查询应沿用未就绪提示"));
    }

    [UnityTest]
    public IEnumerator ScoreQueryReportsUnavailableServiceWithoutThrowing()
    {
        yield return RunUnavailable("query_scores", result =>
            Assert.AreEqual("❌ 课表传讯服务未就绪", result, "成绩查询应沿用未就绪提示"));
    }

    [UnityTest]
    public IEnumerator ScheduleQueryReportsUnavailableServiceWithoutThrowing()
    {
        yield return RunUnavailable("query_schedule", result =>
            Assert.AreEqual("❌ 课表传讯服务未就绪", result, "课表查询应沿用未就绪提示"));
    }

    [UnityTest]
    public IEnumerator UserStatusQueryReportsUnavailableServiceWithoutThrowing()
    {
        yield return RunUnavailable("query_user_status", result =>
            Assert.AreEqual("❌ 课表传讯服务未就绪", result, "学业信息查询应沿用未就绪提示"));
    }
}
