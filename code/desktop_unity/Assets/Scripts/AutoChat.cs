using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 自动聊天 — 管理定时问候和互动事件
///
/// 职责：
/// 1. 定时场景问候（根据时间/星期自动显示气泡）
/// 2. 监听点击/拖拽事件 → 注入 AI 对话
/// 3. 监听 ChatManager 新回复 → 自动显示到气泡
/// </summary>
public class AutoChat : MonoBehaviour
{
    [Header("定时问候")]
    [Tooltip("首次问候延迟（秒）")]
    public float firstGreetingDelay = 3f;

    [Tooltip("问候冷却（秒）")]
    public float greetingCooldown = 180f; // 3 分钟

    [Tooltip("问候检查间隔（秒）")]
    public float greetingCheckInterval = 60f;

    [Header("互动事件")]
    [Tooltip("AI 互动事件冷却（秒）")]
    public float interactionCooldown = 20f; // 避免频繁触发

    [Header("气泡")]
    [Tooltip("AI 回复显示时长（秒）")]
    public float aiReplyDuration = 8f;

    [Tooltip("问候显示时长（秒）")]
    public float greetingDuration = 6f;

    private ChatManager _chat;
    private ChatBubble _bubble;
    private DragHandler _drag;
    private Live2DRenderer _renderer;
    private IdleChatGenerator _idleGen;
    private float _lastGreetingTime = -999f;
    private float _lastInteractionTime = -999f;

    void Start()
    {
        _chat = GetComponent<ChatManager>();
        _bubble = GetComponent<ChatBubble>();
        if (_bubble == null) _bubble = gameObject.AddComponent<ChatBubble>();
        _renderer = GetComponent<Live2DRenderer>();

        // 监听拖拽事件
        _drag = GetComponent<DragHandler>();
        if (_drag != null)
        {
            _drag.OnPetClicked += HandleClick;
            _drag.OnDragEnded += HandleDrag;
        }

        // 获取/添加 IdleChatGenerator
        _idleGen = GetComponent<IdleChatGenerator>();
        if (_idleGen == null)
            _idleGen = gameObject.AddComponent<IdleChatGenerator>();

        // 监听 AI 新回复
        if (_chat != null)
        {
            _chat.OnNewReply += HandleNewReply;
            _chat.OnSentenceChanged += HandleSentenceChanged;
            _chat.OnRequestError += HandleRequestError;
        }

        // 首次问候
        Invoke("DoTimeGreeting", firstGreetingDelay);
        // 定时检查
        InvokeRepeating("CheckTimeGreeting", greetingCheckInterval, greetingCheckInterval);

        Debug.Log("[AutoChat] 已启动，首次问候延迟 " + firstGreetingDelay + "s");
    }

    void OnDestroy()
    {
        if (_drag != null)
        {
            _drag.OnPetClicked -= HandleClick;
            _drag.OnDragEnded -= HandleDrag;
        }
        if (_chat != null)
        {
            _chat.OnNewReply -= HandleNewReply;
            _chat.OnSentenceChanged -= HandleSentenceChanged;
            _chat.OnRequestError -= HandleRequestError;
        }
    }

    // ==================== 互动事件 ====================

    private void HandleClick()
    {
        if (_chat == null || _chat.IsWaiting) return;
        if (Time.time - _lastInteractionTime < interactionCooldown) return;
        // 高优消息显示时不打扰
        if (_bubble != null && _bubble.IsShowingHighPriority) return;

        _lastInteractionTime = Time.time;
        _bubble.ShowMessage("🌸 嗯？找本座何事呀~", 4f, ChatBubble.MsgPriority.Low);
        _chat.SendMessage("*你伸出手指，轻轻戳了戳符玄的额头*", null);
    }

    private void HandleDrag()
    {
        if (_chat == null || _chat.IsWaiting) return;
        if (Time.time - _lastInteractionTime < interactionCooldown) return;
        // 高优消息显示时不打扰
        if (_bubble != null && _bubble.IsShowingHighPriority) return;

        _lastInteractionTime = Time.time;
        _bubble.ShowMessage("🌸 哎呀，别摸头啦……", 4f, ChatBubble.MsgPriority.Low);
        _chat.SendMessage("*你温柔地抚摸了符玄的头发*", null);
    }

    // ==================== AI 回复监听 ====================

    private void HandleNewReply(string reply)
    {
        // 检测困惑 → 触发困惑动作
        if (_renderer != null && IsConfusedReply(reply))
        {
            _renderer.ForceAction("confuse");
        }

        // ⚠️ 不在这里显示气泡——有逐句切换时 OnSentenceChanged 会立刻接手
        // 如果 OnSentenceChanged 没有被触发（单句），由它自己处理
    }

    /// <summary>逐句切换时更新气泡内容</summary>
    private void HandleSentenceChanged(string sentence, int idx, int total)
    {
        if (_bubble == null) return;
        if (string.IsNullOrEmpty(sentence)) return;

        // 🛡 SkipSentenceAnimation 触发的全文(idx >= total)跳过
        if (idx >= total) return;

        if (idx == 0)
        {
            // 第一句：计算总时长 = (total-1)句间隔 + 最终阅读时间
            // 这样计时器从第一句就开始走，不会被后续句子刷新
            float totalDuration = (total - 1) * _chat.sentenceInterval + aiReplyDuration;
            _bubble.ShowMessage("🌸 " + sentence, totalDuration, ChatBubble.MsgPriority.High);
        }
        else
        {
            // 后续句子：只更新文字，不碰计时器
            _bubble.UpdateText("🌸 " + sentence);
        }
    }

    /// <summary>API 请求出错时显示错误信息到气泡</summary>
    private void HandleRequestError(string error)
    {
        if (_bubble == null) return;
        _bubble.ShowMessage("⚠️ " + error, 8f, ChatBubble.MsgPriority.High);
    }

    // ===== 困惑检测 =====

    /// <summary>AI 回复中出现这些词时，说明它没听懂，触发困惑动画</summary>
    private static readonly string[] ConfusionKeywords = new string[]
    {
        "不懂", "不明白", "没听懂", "没明白", "不理解",
        "听不懂", "搞不懂", "一头雾水", "摸不着头脑",
        "不知所云", "莫名其妙", "什么意思", "困惑",
        "没头没脑", "搞不清楚", "听不明白", "不知所谓"
    };

    private bool IsConfusedReply(string reply)
    {
        foreach (var kw in ConfusionKeywords)
        {
            if (reply.Contains(kw)) return true;
        }
        return false;
    }

    // ==================== 定时问候 ====================

    private void DoTimeGreeting()
    {
        if (_bubble == null) return;
        // 高优消息显示时不打扰
        if (_bubble.IsShowingHighPriority) return;
        string greeting = PickGreeting();
        _bubble.ShowMessage("🌸 " + greeting, greetingDuration, ChatBubble.MsgPriority.Low);
        _lastGreetingTime = Time.time;
    }

    private void CheckTimeGreeting()
    {
        // 冷却中或 AI 正在回复时不打扰
        if (_bubble == null) return;
        if (_chat != null && _chat.IsWaiting) return;
        if (_bubble.IsShowingHighPriority) return;
        if (Time.time - _lastGreetingTime < greetingCooldown) return;

        string greeting = PickGreeting();
        _bubble.ShowMessage("🌸 " + greeting, greetingDuration, ChatBubble.MsgPriority.Low);
        _lastGreetingTime = Time.time;
    }

    private string PickGreeting()
    {
        // 构建场景上下文
        int hour = System.DateTime.Now.Hour;
        bool isWeekend = System.DateTime.Now.DayOfWeek == System.DayOfWeek.Saturday
                      || System.DateTime.Now.DayOfWeek == System.DayOfWeek.Sunday;

        string timeKey;
        if (isWeekend && hour >= 8 && hour <= 22)
            timeKey = "周末";
        else if (hour >= 5 && hour < 8)
            timeKey = "清晨";
        else if (hour >= 8 && hour < 12)
            timeKey = "上午";
        else if (hour >= 12 && hour < 14)
            timeKey = "中午";
        else if (hour >= 14 && hour < 17)
            timeKey = "下午";
        else if (hour >= 17 && hour < 19)
            timeKey = "傍晚";
        else if (hour >= 19 && hour < 23)
            timeKey = "夜晚";
        else
            timeKey = "深夜";

        string context = isWeekend && hour >= 8 && hour <= 22
            ? "周末，休息日"
            : $"{timeKey}（{hour}点钟）";

        // 用 IdleChatGenerator 动态生成
        return _idleGen.GetGreeting(context);
    }
}
