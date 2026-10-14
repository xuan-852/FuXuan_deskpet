using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 普通点击的本地轻回应（2026-10-12 产品迭代）。
///
/// 断言边界：
/// - 普通点击零 AI 请求、零会话记录，只播本地短句；
/// - 冷却与连点抑制，不刷屏、不积压、不延长；
/// - 高优消息优先，不被轻回应覆盖；
/// - 待工具审批的既有特殊分支保持确认语义，不被轻回应吞掉。
///
/// EditMode 下 AddComponent 不触发 Awake/Start/Update，因此用例直接调用
/// <see cref="AutoChat.HandleClick"/>（生产中的 OnPetClicked 委托目标），
/// 并依赖 AutoChat 内部的惰性依赖解析。真实淡出结束与实时冷却到期由运行时
/// 驱动脚本 scripts/test/click_light_response_drive.cjs 覆盖。
/// </summary>
public class ClickLightResponseTests
{
    private GameObject _go;
    private AutoChat _autoChat;
    private ChatManager _chat;
    private ChatBubble _bubble;

    [SetUp]
    public void SetUp()
    {
        // 审批状态是静态的：每个用例从"无待审批"开始。
        ToolConfirmManager.Resolve(false);

        _go = new GameObject("ClickLightResponseTest");
        _go.SetActive(false);
        _chat = _go.AddComponent<ChatManager>();
        _bubble = _go.AddComponent<ChatBubble>();
        _autoChat = _go.AddComponent<AutoChat>();
    }

    [TearDown]
    public void TearDown()
    {
        ToolConfirmManager.Resolve(false);
        if (_go != null) Object.DestroyImmediate(_go);
    }

    /// <summary>用新气泡替换已播完的旧气泡，等价于真实运行中淡出结束。</summary>
    private void ReplaceWithFreshBubble()
    {
        Object.DestroyImmediate(_bubble);
        _bubble = _go.AddComponent<ChatBubble>();
    }

    // ==================== 零 AI 请求 ====================

    [Test]
    public void 普通点击零AI请求且不新增聊天记录()
    {
        int historyBefore = _chat.HistoryCount;

        _autoChat.HandleClick();

        Assert.That(_chat.HistoryCount, Is.EqualTo(historyBefore),
            "普通点击不得写入会话记录（ChatManager.SendMessage 会同步追加 user 条目）");
        Assert.That(_chat.IsWaiting, Is.False,
            "普通点击不得发起请求（ChatManager.SendMessage 会同步置 _isWaiting）");
        Assert.That(_chat.QueuedMessageCount, Is.EqualTo(0), "普通点击不得排队 AI 消息");
        Assert.That(_autoChat.LocalClickResponseCount, Is.EqualTo(1), "普通点击应播出一次本地轻回应");
        Assert.That(_bubble.IsShowing, Is.True);
    }

    [Test]
    public void 连续多次点击仍为零AI请求()
    {
        for (int i = 0; i < 8; i++) _autoChat.HandleClick();

        Assert.That(_chat.HistoryCount, Is.EqualTo(0), "任何一次点击都不得写会话记录");
        Assert.That(_chat.IsWaiting, Is.False);
        Assert.That(_chat.QueuedMessageCount, Is.EqualTo(0));
    }

    // ==================== 本地短句 ====================

    [Test]
    public void 轻回应只使用本地短句且不含戳额头话术()
    {
        _autoChat.HandleClick();

        Assert.That(_autoChat.clickLightResponses, Is.Not.Empty);
        CollectionAssert.Contains(_autoChat.clickLightResponses, _bubble.CurrentText,
            "轻回应文案必须来自本地短句池");
        Assert.That(_bubble.CurrentPriority, Is.EqualTo(ChatBubble.MsgPriority.Low),
            "轻回应必须是低优先级");
        Assert.That(_bubble.IsShowingHighPriority, Is.False);

        foreach (string line in _autoChat.clickLightResponses)
            Assert.That(line, Does.Not.Contain("戳"),
                "普通点击不得再注入虚构的戳额头对话");
    }

    [Test]
    public void 短句池为空时点击安全且零AI请求()
    {
        _autoChat.clickLightResponses = new string[0];

        Assert.DoesNotThrow(() => _autoChat.HandleClick());

        Assert.That(_autoChat.LocalClickResponseCount, Is.EqualTo(0));
        Assert.That(_bubble.IsShowing, Is.False);
        Assert.That(_chat.HistoryCount, Is.EqualTo(0));
        Assert.That(_chat.IsWaiting, Is.False);
    }

    [Test]
    public void 关闭轻回应后点击仍零AI请求()
    {
        _autoChat.clickLightResponseEnabled = false;

        _autoChat.HandleClick();

        Assert.That(_autoChat.LocalClickResponseCount, Is.EqualTo(0));
        Assert.That(_bubble.IsShowing, Is.False);
        Assert.That(_chat.HistoryCount, Is.EqualTo(0));
        Assert.That(_chat.IsWaiting, Is.False);
    }

    // ==================== 冷却与连点 ====================

    [Test]
    public void 冷却内连点不刷屏不延长()
    {
        _autoChat.HandleClick();
        string first = _bubble.CurrentText;
        Assert.That(_autoChat.LocalClickResponseCount, Is.EqualTo(1));

        for (int i = 0; i < 6; i++) _autoChat.HandleClick();

        Assert.That(_autoChat.LocalClickResponseCount, Is.EqualTo(1),
            "沿用既有可配置冷却：冷却内的连点不得重复播出");
        Assert.That(_bubble.CurrentText, Is.EqualTo(first),
            "连点不得改写当前气泡（不刷屏、不延长回应）");
        Assert.That(_chat.HistoryCount, Is.EqualTo(0));
    }

    [Test]
    public void 冷却到期后再次点击会再次回应且不补播过期回应()
    {
        // 冷却置 0 等价于"已到期"：EditMode 无法推进 Time.time。
        _autoChat.interactionCooldown = 0f;
        _autoChat.HandleClick();
        string first = _bubble.CurrentText;
        Assert.That(_autoChat.LocalClickResponseCount, Is.EqualTo(1));

        // 上一轮气泡已淡出结束（EditMode 下 Update 不推进，手动换新气泡）。
        ReplaceWithFreshBubble();

        _autoChat.HandleClick();

        Assert.That(_autoChat.LocalClickResponseCount, Is.EqualTo(2),
            "冷却到期后应能再次回应，不得被永久闩锁");
        Assert.That(_bubble.CurrentText, Is.Not.EqualTo(first),
            "短句应按顺序轮换，而不是补播上一句过期回应");
        CollectionAssert.Contains(_autoChat.clickLightResponses, _bubble.CurrentText);
        Assert.That(_chat.HistoryCount, Is.EqualTo(0));
    }

    // ==================== 高优消息优先 ====================

    [Test]
    public void 高优消息优先且不被轻回应覆盖()
    {
        const string highPriorityText = "🌸 这是高优先级 AI 回复";
        _bubble.ShowMessage(highPriorityText, 10f, ChatBubble.MsgPriority.High);

        _autoChat.HandleClick();

        Assert.That(_autoChat.LocalClickResponseCount, Is.EqualTo(0),
            "高优消息显示期间不得播出轻回应");
        Assert.That(_bubble.CurrentText, Is.EqualTo(highPriorityText),
            "轻回应不得覆盖高优消息");
        Assert.That(_bubble.IsShowingHighPriority, Is.True);
        Assert.That(_chat.HistoryCount, Is.EqualTo(0));
    }

    [Test]
    public void 普通在播气泡优先且轻回应不排队等待()
    {
        const string normalPriorityText = "⏰ 提醒：该喝水了";
        _bubble.ShowMessage(normalPriorityText, 10f, ChatBubble.MsgPriority.Normal);

        _autoChat.HandleClick();

        Assert.That(_autoChat.LocalClickResponseCount, Is.EqualTo(0),
            "已有气泡在播时轻回应让位，且不排队等待");
        Assert.That(_bubble.CurrentText, Is.EqualTo(normalPriorityText));
        Assert.That(_chat.HistoryCount, Is.EqualTo(0));
    }

    // ==================== 待审批特殊分支 ====================

    [Test]
    public void 待审批时点击保持确认语义且不播轻回应()
    {
        bool? resolved = null;
        ToolConfirmManager.Request("file_delete", "{\"path\":\"x\"}", "删除文件",
            ok => resolved = ok);
        Assert.That(ToolConfirmManager.HasPending, Is.True);

        _autoChat.HandleClick();

        Assert.That(resolved, Is.True, "点击桌宠应确认待审批操作");
        Assert.That(ToolConfirmManager.HasPending, Is.False, "确认后应清空待审批状态");
        Assert.That(_autoChat.LocalClickResponseCount, Is.EqualTo(0),
            "审批分支不得被普通轻回应吞掉");
        Assert.That(_bubble.CurrentText, Does.Contain("已获准许"));
        Assert.That(_bubble.IsShowingHighPriority, Is.True);
        Assert.That(_chat.HistoryCount, Is.EqualTo(0),
            "确认审批不得顺带发起 AI 对话");
        Assert.That(_chat.IsWaiting, Is.False);
    }

    [Test]
    public void 待审批时连续点击只确认一次()
    {
        int resolveCount = 0;
        ToolConfirmManager.Request("file_delete", "{}", "删除文件", ok => resolveCount++);

        _autoChat.HandleClick();
        _autoChat.HandleClick();
        _autoChat.HandleClick();

        Assert.That(resolveCount, Is.EqualTo(1), "同一待审批操作只应被确认一次");
        Assert.That(ToolConfirmManager.HasPending, Is.False);
        Assert.That(_autoChat.LocalClickResponseCount, Is.EqualTo(0));
    }

    [Test]
    public void 拒绝语义不受轻回应影响()
    {
        bool? resolved = null;
        ToolConfirmManager.Request("file_delete", "{}", "删除文件", ok => resolved = ok);

        // 拒绝路径由 DesktopPet 的 ESC 处理，本轮未改动；此处只确认它仍能清空待审批。
        ToolConfirmManager.Resolve(false);

        Assert.That(resolved, Is.False);
        Assert.That(ToolConfirmManager.HasPending, Is.False);

        // 拒绝后普通点击恢复为本地轻回应，不再残留审批态。
        _autoChat.HandleClick();
        Assert.That(_autoChat.LocalClickResponseCount, Is.EqualTo(1));
        Assert.That(_chat.HistoryCount, Is.EqualTo(0));
    }
}
