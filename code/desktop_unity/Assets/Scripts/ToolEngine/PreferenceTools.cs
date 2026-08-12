using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// 偏好工具 — set_preference / query_preferences / remove_preference
/// 让符玄能记录、查询、纠正主人的结构化偏好（P4.2）
/// </summary>
public class SetPreferenceTool : IPetTool
{
    public string ToolName => "set_preference";
    public string ToolDescription => "【心之所向】记录主人明确表达或可推断的偏好（称呼、作息、口味、习惯等），供日后遵行。主人明确说「我喜欢/我习惯/我不喜欢/叫我XX」时调用；首次记录用 source=user，符玄自行观察推断的用 source=infer。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("key", "string", "偏好键，snake_case（如 call_me / sleep_time / coffee_habit）"),
        ToolSchema.Req("value", "string", "偏好值（如 阿轩 / 23点睡 / 美式无糖）"),
        ToolSchema.Opt("source", "string", "来源：user=主人明确告知（默认），infer=符玄观察推断"),
        ToolSchema.Opt("note", "string", "备注（可选）")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        var mgr = PreferencesManager.Instance;
        if (mgr == null) return "❌ 心之所向簿未就绪";

        JObject args;
        try { args = JObject.Parse(argsJson); }
        catch { return "❌ 参数解析失败，需传入 key 与 value"; }

        string key = (string)args["key"];
        string value = (string)args["value"];
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return "❌ key 与 value 均不可为空";

        string source = (string)args["source"];
        string note = (string)args["note"];
        if (string.IsNullOrEmpty(source)) source = "user";

        mgr.SetPreference(key, value, source, note);
        return $"✅ 已记下偏好：{key} = {value}（来源: {source}）";
    }

    public IEnumerator ExecuteAsync(string argsJson, System.Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class QueryPreferencesTool : IPetTool
{
    public string ToolName => "query_preferences";
    public string ToolDescription => "【心之所向】查询已记录的偏好清单。用户问「我记得什么偏好」「你了解我什么」时调用。";
    public string ToolParametersJson => ToolSchema.Empty;
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        var mgr = PreferencesManager.Instance;
        if (mgr == null) return "❌ 心之所向簿未就绪";

        var entries = mgr.GetAllPreferences();
        if (entries.Count == 0) return "（尚无已记录的偏好，可在相处中慢慢了解主人）";

        var lines = new List<string> { "【已记录的偏好】" };
        foreach (var e in entries)
        {
            string tag = e.source == "infer" ? "（观之）" : "";
            lines.Add($"  ✦ {e.key}: {e.value}{tag}（记录于 {e.updatedAt}）");
        }
        return string.Join("\n", lines);
    }

    public IEnumerator ExecuteAsync(string argsJson, System.Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}

public class RemovePreferenceTool : IPetTool
{
    public string ToolName => "remove_preference";
    public string ToolDescription => "【心之所向】删除一条已记录的偏好。用户明确表示某项偏好已不适用/记错了时调用。";
    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("key", "string", "要删除的偏好键")
    );
    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        var mgr = PreferencesManager.Instance;
        if (mgr == null) return "❌ 心之所向簿未就绪";

        JObject args;
        try { args = JObject.Parse(argsJson); }
        catch { return "❌ 参数解析失败，需传入 key"; }

        string key = (string)args["key"];
        if (string.IsNullOrWhiteSpace(key)) return "❌ key 不可为空";

        return mgr.RemovePreference(key)
            ? $"✅ 已删除偏好：{key}"
            : $"（未找到偏好键 {key}，无需删除）";
    }

    public IEnumerator ExecuteAsync(string argsJson, System.Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }
}
