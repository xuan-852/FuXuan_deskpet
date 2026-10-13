using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

/// <summary>
/// 云端回环工具准入门禁：DoToolLoop 只能执行「本轮请求体实际提供过」的工具。
///
/// 覆盖边界：
/// - tools 缺失 / 为空 → 任何 tool_call 都被拒绝；
/// - 只提供 get_system_info 却调用 request_body_skill → 拒绝；
/// - 提供 get_system_info 并调用它 → 照常准入执行；
/// - 提供危险工具并调用它 → 照常准入，随后仍走既有用户审批；
/// - 未列入本轮 tools 的名称 → 一律拒绝，包括本轮未提供的未知名称。
///
/// 并验证拒绝发生在「状态 / OnToolCalled / 审批 / 执行」之前，且拒绝结果按原
/// tool_call_id 回写历史、不含参数原文。
/// </summary>
public class CloudToolCallAdmissionTests
{
    /// <summary>按 ToolRegistry.BuildToolsJson 的真实形状构造请求体 tools 字段。</summary>
    private static JArray ToolsField(params string[] toolNames)
    {
        var arr = new JArray();
        foreach (string name in toolNames)
        {
            arr.Add(JObject.Parse(
                "{\"type\":\"function\",\"function\":{\"name\":\"" + name +
                "\",\"description\":\"d\",\"parameters\":{\"type\":\"object\",\"properties\":{}}}}"));
        }
        return arr;
    }

    [TearDown]
    public void TearDown()
    {
        // 门禁不得留下任何待审批状态
        ToolConfirmManager.Resolve(false);
    }

    // ——— tools 缺失 / 为空 ———

    [Test]
    public void Tools字段缺失时不允许任何工具调用()
    {
        HashSet<string> admitted = ChatManager.ExtractAdmittedToolNames(null);

        Assert.AreEqual(0, admitted.Count);
        Assert.IsFalse(ChatManager.IsToolAdmittedThisRound(admitted, "request_body_skill"));
        Assert.IsFalse(ChatManager.IsToolAdmittedThisRound(admitted, "get_system_info"));
    }

    [Test]
    public void Tools为空数组时拒绝身体技能调用()
    {
        HashSet<string> admitted = ChatManager.ExtractAdmittedToolNames(ToolsField());
        var history = new List<ChatManager.Entry>();

        bool ok = ChatManager.TryAdmitToolCallForRound(
            admitted, "request_body_skill", "call_body_1", history, out string rejection);

        Assert.IsFalse(ok, "tools=[] 时不得准入任何工具");
        Assert.AreEqual(ChatManager.ToolNotAdmittedResult, rejection);
        Assert.AreEqual(1, history.Count);
        Assert.AreEqual("tool", history[0].role);
        Assert.AreEqual("call_body_1", history[0].tool_call_id);
        Assert.AreEqual("request_body_skill", history[0].name);
    }

    // ——— 只提供只读工具，却调用未提供的工具 ———

    [Test]
    public void 仅提供只读工具时拒绝未提供的身体技能调用()
    {
        HashSet<string> admitted = ChatManager.ExtractAdmittedToolNames(ToolsField("get_system_info"));
        var history = new List<ChatManager.Entry>();

        Assert.IsTrue(ChatManager.IsToolAdmittedThisRound(admitted, "get_system_info"));
        Assert.IsFalse(ChatManager.IsToolAdmittedThisRound(admitted, "request_body_skill"));

        bool ok = ChatManager.TryAdmitToolCallForRound(
            admitted, "request_body_skill", "call_body_2", history, out string rejection);

        Assert.IsFalse(ok);
        Assert.AreEqual(ChatManager.ToolNotAdmittedResult, rejection);
        Assert.AreEqual(1, history.Count);
        Assert.AreEqual("call_body_2", history[0].tool_call_id);
    }

    [Test]
    public void 未提供的危险工具也被拒绝且不进入审批()
    {
        HashSet<string> admitted = ChatManager.ExtractAdmittedToolNames(ToolsField("get_system_info"));
        var history = new List<ChatManager.Entry>();

        bool ok = ChatManager.TryAdmitToolCallForRound(
            admitted, "file_delete", "call_del_1", history, out string rejection);

        Assert.IsFalse(ok, "未提供的危险工具必须先被准入门禁拒绝");
        Assert.IsFalse(ToolConfirmManager.HasPending, "被拒绝的调用不得发起危险工具审批");
        Assert.AreEqual("call_del_1", history[0].tool_call_id);
    }

    // ——— 提供并调用 ———

    [Test]
    public void 本轮提供的只读工具照常准入且不写拒绝结果()
    {
        HashSet<string> admitted = ChatManager.ExtractAdmittedToolNames(ToolsField("get_system_info"));
        var history = new List<ChatManager.Entry>();

        bool ok = ChatManager.TryAdmitToolCallForRound(
            admitted, "get_system_info", "call_sys_1", history, out string rejection);

        Assert.IsTrue(ok, "本轮提供的只读工具应照常执行");
        Assert.IsNull(rejection);
        Assert.AreEqual(0, history.Count, "准入通过时门禁不写任何历史");
    }

    [Test]
    public void 本轮提供的危险工具照常准入并保留既有审批路径()
    {
        HashSet<string> admitted = ChatManager.ExtractAdmittedToolNames(ToolsField("file_delete"));
        var history = new List<ChatManager.Entry>();

        bool ok = ChatManager.TryAdmitToolCallForRound(
            admitted, "file_delete", "call_del_2", history, out string rejection);

        Assert.IsTrue(ok, "本轮提供的危险工具应准入，交由既有 IsDangerous 审批分支处理");
        Assert.IsNull(rejection);
        Assert.AreEqual(0, history.Count);
        // 准入之后仍由 ToolRegistry.IsDangerous 判定为需要用户审批
        Assert.IsTrue(ToolRegistry.IsDangerous("file_delete"));
        Assert.IsFalse(ToolConfirmManager.HasPending, "门禁本身不发起审批");
    }

    // ——— 未列入本轮清单的未知名称 ———

    [Test]
    public void 未列入本轮tools的未知名称一律被拒绝()
    {
        var history = new List<ChatManager.Entry>();

        // 清单里只有 get_system_info → 未知名称被拒绝
        HashSet<string> admitted = ChatManager.ExtractAdmittedToolNames(ToolsField("get_system_info"));
        Assert.IsFalse(ChatManager.TryAdmitToolCallForRound(
            admitted, "definitely_not_a_tool", "call_x_1", history, out _));

        // 清单为空 → 未知名称同样被拒绝
        HashSet<string> empty = ChatManager.ExtractAdmittedToolNames(ToolsField());
        Assert.IsFalse(ChatManager.TryAdmitToolCallForRound(
            empty, "definitely_not_a_tool", "call_x_2", history, out _));

        Assert.AreEqual(2, history.Count);
        Assert.AreEqual("call_x_1", history[0].tool_call_id);
        Assert.AreEqual("call_x_2", history[1].tool_call_id);
    }

    [Test]
    public void 空工具名一律拒绝()
    {
        HashSet<string> admitted = ChatManager.ExtractAdmittedToolNames(ToolsField("get_system_info"));

        Assert.IsFalse(ChatManager.IsToolAdmittedThisRound(admitted, ""));
        Assert.IsFalse(ChatManager.IsToolAdmittedThisRound(admitted, null));
    }

    [Test]
    public void 名称必须精确匹配()
    {
        HashSet<string> admitted = ChatManager.ExtractAdmittedToolNames(ToolsField("get_system_info"));

        Assert.IsTrue(ChatManager.IsToolAdmittedThisRound(admitted, "get_system_info"));
        Assert.IsFalse(ChatManager.IsToolAdmittedThisRound(admitted, "Get_System_Info"));
        Assert.IsFalse(ChatManager.IsToolAdmittedThisRound(admitted, " get_system_info"));
        Assert.IsFalse(ChatManager.IsToolAdmittedThisRound(admitted, "get_system_info "));
        Assert.IsFalse(ChatManager.IsToolAdmittedThisRound(admitted, "get_system_inf"));
    }

    // ——— 拒绝结果形状 ———

    [Test]
    public void 拒绝结果按原tool_call_id回写且不含参数原文()
    {
        HashSet<string> admitted = ChatManager.ExtractAdmittedToolNames(ToolsField("get_system_info"));
        var history = new List<ChatManager.Entry>();
        const string secretArgs = "{\"skill_id\":\"nod_secret_argument\"}";

        bool ok = ChatManager.TryAdmitToolCallForRound(
            admitted, "request_body_skill", "call_abc", history, out string rejection);

        Assert.IsFalse(ok);
        Assert.AreEqual(1, history.Count);
        ChatManager.Entry entry = history[0];
        Assert.AreEqual("tool", entry.role);
        Assert.AreEqual("call_abc", entry.tool_call_id);
        Assert.AreEqual("request_body_skill", entry.name);
        Assert.AreEqual(ChatManager.ToolNotAdmittedResult, entry.content);
        StringAssert.DoesNotContain("nod_secret_argument", entry.content, "拒绝结果不得包含参数原文");
        StringAssert.DoesNotContain(secretArgs, entry.content);
        Assert.IsTrue(entry.content.StartsWith("❌"), "应为工具错误结果，供模型退化为文字");
    }

    // ——— 拒绝先于状态 / 事件 / 审批 / 执行 ———

    [Test]
    public void 拒绝先于执行与审批被拒绝的调用不进入执行分支()
    {
        HashSet<string> admitted = ChatManager.ExtractAdmittedToolNames(ToolsField("get_system_info"));
        var history = new List<ChatManager.Entry>();

        // 复刻 DoToolLoop 的单轮消费顺序：门禁是第一道且是执行分支的唯一入口。
        var calls = new[]
        {
            ("call_ok", "get_system_info"),
            ("call_bad", "request_body_skill")
        };

        int executed = 0;
        var approvalRequests = new List<string>();

        foreach (var call in calls)
        {
            if (!ChatManager.TryAdmitToolCallForRound(admitted, call.Item2, call.Item1, history, out _))
                continue; // 被拒绝：不置 RunningTool 状态、不发 OnToolCalled、不审批、不执行

            executed++;
            if (ToolRegistry.IsDangerous(call.Item2)) approvalRequests.Add(call.Item2);
        }

        Assert.AreEqual(1, executed, "只有本轮提供的工具进入执行分支");
        Assert.IsEmpty(approvalRequests, "被拒绝的调用不得产生审批请求");
        Assert.IsFalse(ToolConfirmManager.HasPending, "被拒绝的调用不得留下待审批状态");
        Assert.AreEqual(1, history.Count, "只有被拒绝的调用写入门禁错误结果");
        Assert.AreEqual("call_bad", history[0].tool_call_id);
    }

    // ——— 与真实 schema 形状一致 ———

    [Test]
    public void 从真实工具schema提取的清单可精确匹配()
    {
        ToolRegistry.Initialize();
        JArray tools = JArray.Parse(ToolRegistry.GetToolsJson(new[] { "get_system_info" }));
        HashSet<string> admitted = ChatManager.ExtractAdmittedToolNames(tools);

        Assert.AreEqual(1, admitted.Count);
        Assert.IsTrue(ChatManager.IsToolAdmittedThisRound(admitted, "get_system_info"));
        Assert.IsFalse(ChatManager.IsToolAdmittedThisRound(admitted, "request_body_skill"));
    }
}
