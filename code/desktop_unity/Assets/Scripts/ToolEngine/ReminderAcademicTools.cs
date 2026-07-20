using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ================================================================
//  卜算记事 — 提醒管理
// ================================================================

public class SetReminderTool : IPetTool
{
    public string ToolName => "set_reminder";
    public string ToolDescription => "【卜算记事】设置提醒/待办事项。用户说「提醒我xxx」「记一下xxx」「设个提醒」时调用。支持时间（yyyy-MM-dd HH:mm）、重复周期（daily/weekly/monthly）和优先级（high/normal/low）。默认1小时后。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""text"":{""type"":""string"",""description"":""提醒内容""},""remind_at"":{""type"":""string"",""description"":""提醒时间，格式 yyyy-MM-dd HH:mm，为空则自动设为1小时后""},""recurring"":{""type"":""string"",""description"":""重复周期：daily/weekly/monthly，不重复则留空""},""priority"":{""type"":""string"",""description"":""优先级：high/normal/low，默认 normal""}},""required"":[""text""]}";
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string text = ToolHelpers.JsonRead(argsJson, "text");
        string timeStr = ToolHelpers.JsonRead(argsJson, "remind_at");
        if (string.IsNullOrEmpty(text)) return "❌ 未说要提醒何事";

        DateTime remindAt;
        if (!string.IsNullOrEmpty(timeStr))
        {
            if (!DateTime.TryParse(timeStr, out remindAt))
                return "❌ 时辰格式不对，本座只识 yyyy-MM-dd HH:mm 之流";
        }
        else
        {
            remindAt = DateTime.Now.AddHours(1);
        }

        if (remindAt <= DateTime.Now)
        {
            DateTime fixedTime = remindAt;
            for (int i = 0; i < 5 && fixedTime <= DateTime.Now; i++)
                fixedTime = fixedTime.AddYears(1);
            if (fixedTime > DateTime.Now)
                remindAt = fixedTime;
            else
                return "❌ 定在过去可不行，本座不会时光倒流之术";
        }

        string recurring = ToolHelpers.JsonRead(argsJson, "recurring");
        string priority = ToolHelpers.JsonRead(argsJson, "priority");
        if (string.IsNullOrEmpty(priority)) priority = "normal";

        var mgr = ReminderManager.Instance;
        if (mgr == null) return "❌ 卜算记事簿未就绪";

        var r = mgr.AddReminder(text, remindAt,
            string.IsNullOrEmpty(recurring) ? null : recurring,
            priority, "ai");
        return $"✅ 已记入卜算记事簿！提醒「{text}」定于 {remindAt:yyyy-MM-dd HH:mm}" +
               $"\n📌 ID: {r.id.Substring(0, 8)}… 可对我说「查提醒」查阅";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class QueryRemindersTool : IPetTool
{
    public string ToolName => "query_reminders";
    public string ToolDescription => "【卜算记事】查询所有待办提醒。用户问「有什么提醒」「查提醒」「未办事项」时调用。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{}}";
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        var mgr = ReminderManager.Instance;
        if (mgr == null) return "❌ 卜算记事簿未就绪";
        return mgr.GetPendingText();
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class MarkReminderDoneTool : IPetTool
{
    public string ToolName => "mark_reminder_done";
    public string ToolDescription => "【卜算记事】将提醒标记为已完成。用户说「完成了」「勾掉」「搞定」时调用。支持用 ID 前几位匹配。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""id"":{""type"":""string"",""description"":""提醒 ID（支持前几位模糊匹配）""}},""required"":[""id""]}";
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string id = ToolHelpers.JsonRead(argsJson, "id");
        if (string.IsNullOrEmpty(id)) return "❌ 未指定要勾销哪条提醒的 ID";

        var mgr = ReminderManager.Instance;
        if (mgr == null) return "❌ 卜算记事簿未就绪";

        var all = mgr.GetAllReminders();
        var match = all.Find(r => r.id.StartsWith(id) && !r.done);
        if (match == null) return $"❌ 未找到 ID 以「{id}」开头的待办事项";

        mgr.MarkDone(match.id);
        return $"✅ 已勾销「{match.text}」";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class DeleteReminderTool : IPetTool
{
    public string ToolName => "delete_reminder";
    public string ToolDescription => "【卜算记事】删除提醒。用户说「删掉这个提醒」「移除提醒」时调用。支持用 ID 前几位匹配。";
    public string ToolParametersJson => @"{""type"":""object"",""properties"":{""id"":{""type"":""string"",""description"":""提醒 ID（支持前几位模糊匹配）""}},""required"":[""id""]}";
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string id = ToolHelpers.JsonRead(argsJson, "id");
        if (string.IsNullOrEmpty(id)) return "❌ 未指定要删除哪条提醒的 ID";

        var mgr = ReminderManager.Instance;
        if (mgr == null) return "❌ 卜算记事簿未就绪";

        var all = mgr.GetAllReminders();
        var match = all.Find(r => r.id.StartsWith(id));
        if (match == null) return $"❌ 未找到 ID 以「{id}」开头的事项";

        mgr.DeleteReminder(match.id);
        return $"✅ 已销毁记事「{match.text}」";
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

// ================================================================
//  学业占卜 — 课表服务
// ================================================================

/// <summary>
/// 基类：在同步执行中通过 Task.Wait 等待 ServerPollService 的结果
/// </summary>
public abstract class BaseAcademicTool : IPetTool
{
    public abstract string ToolName { get; }
    public abstract string ToolDescription { get; }
    public abstract string ToolParametersJson { get; }
    public bool IsAsync => true; // 统一为异步，因为涉及网络

    public string Execute(string argsJson) => "⏳ 卜算学业中……";

    public abstract IEnumerator ExecuteAsync(string argsJson, Action<string> onResult);

    protected ServerPollService FindPoll()
    {
        var poll = GameObject.FindObjectOfType<ServerPollService>();
        return poll;
    }
}

public class QueryExamsTool : BaseAcademicTool
{
    public override string ToolName => "query_exams";
    public override string ToolDescription => "【学业占卜】查询最近的考试安排。用户问「最近有什么考试」「考试安排」时调用。数据来自课表传讯服务（ServerPollService），自动绑定学校教务系统。";
    public override string ToolParametersJson => @"{""type"":""object"",""properties"":{}}";

    public override IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        var poll = FindPoll();
        if (poll == null) { onResult?.Invoke("❌ 课表传讯服务未就绪"); yield break; }

        var task = System.Threading.Tasks.Task.Run(() => poll.QueryUpcomingExamsAsync());
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted)
            onResult?.Invoke($"❌ 查询考试时出错: {task.Exception?.InnerException?.Message}");
        else if (task.IsCanceled)
            onResult?.Invoke("⏱️ 查询考试超时");
        else
            onResult?.Invoke(task.Result);
    }
}

public class QueryScoresTool : BaseAcademicTool
{
    public override string ToolName => "query_scores";
    public override string ToolDescription => "【学业占卜】查询已出成绩。用户问「成绩出来了吗」「考了多少分」「查成绩」时调用。";
    public override string ToolParametersJson => @"{""type"":""object"",""properties"":{}}";

    public override IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        var poll = FindPoll();
        if (poll == null) { onResult?.Invoke("❌ 课表传讯服务未就绪"); yield break; }

        var task = System.Threading.Tasks.Task.Run(() => poll.QueryScoresAsync());
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted)
            onResult?.Invoke($"❌ 查询成绩时出错: {task.Exception?.InnerException?.Message}");
        else if (task.IsCanceled)
            onResult?.Invoke("⏱️ 查询成绩超时");
        else
            onResult?.Invoke(task.Result);
    }
}

public class QueryScheduleTool : BaseAcademicTool
{
    public override string ToolName => "query_schedule";
    public override string ToolDescription => "【学业占卜】查询课程表。用户问「今天有什么课」「下周课表」「什么时候上课」时调用。";
    public override string ToolParametersJson => @"{""type"":""object"",""properties"":{""week"":{""type"":""integer"",""description"":""第几周（0=本周，1=下周...），默认0""}},""required"":[]}";

    public override IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        var poll = FindPoll();
        if (poll == null) { onResult?.Invoke("❌ 课表传讯服务未就绪"); yield break; }

        int week = 0;
        int.TryParse(ToolHelpers.JsonRead(argsJson, "week"), out week);

        var task = System.Threading.Tasks.Task.Run(() => poll.QueryScheduleAsync(week));
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted)
            onResult?.Invoke($"❌ 查询课表时出错: {task.Exception?.InnerException?.Message}");
        else if (task.IsCanceled)
            onResult?.Invoke("⏱️ 查询课表超时");
        else
            onResult?.Invoke(task.Result);
    }
}

public class QueryUserStatusTool : BaseAcademicTool
{
    public override string ToolName => "query_user_status";
    public override string ToolDescription => "【学业占卜】查看用户绑定状态和学业概览。用户问「我绑定了什么」「学业信息」时调用。";
    public override string ToolParametersJson => @"{""type"":""object"",""properties"":{}}";

    public override IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        var poll = FindPoll();
        if (poll == null) { onResult?.Invoke("❌ 课表传讯服务未就绪"); yield break; }

        var task = System.Threading.Tasks.Task.Run(() => poll.QueryUserStatusAsync());
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted)
            onResult?.Invoke($"❌ 查询学业信息时出错: {task.Exception?.InnerException?.Message}");
        else if (task.IsCanceled)
            onResult?.Invoke("⏱️ 查询学业信息超时");
        else
            onResult?.Invoke(task.Result);
    }
}
