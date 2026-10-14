using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 自动聊天 — 管理定时问候和互动事件
///
/// 职责：
/// 1. 定时场景问候（根据时间/星期自动显示气泡）
/// 2. 监听点击事件 → 本地轻回应（零 AI 请求、零会话记录）
/// 3. 监听拖拽事件 → 注入 AI 对话（拖拽独立策略，未随点击一同改造）
/// 4. 监听 ChatManager 新回复 → 自动显示到气泡
///
/// 点击与拖拽的分工（2026-10-12 产品迭代）：普通点击只做即时、本地、低打扰的
/// 轻回应，不再自动发起 AI 对话；用户主动输入仍走原有聊天入口。
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
    [Tooltip("AI 互动事件冷却（秒）；点击轻回应沿用同一冷却与交互计时")]
    public float interactionCooldown = 20f; // 避免频繁触发

    [Header("点击轻回应（本地）")]
    [Tooltip("普通点击是否播放本地短回应。关闭后点击只保留既有姿势与注意力反馈。")]
    public bool clickLightResponseEnabled = true;

    [Tooltip("点击轻回应显示时长（秒）。短暂显示，不因连点延长。")]
    public float clickLightResponseDuration = 3f;

    [Tooltip("点击轻回应候选短句（纯本地文案，不请求任何本地/云端 LLM）")]
    public string[] clickLightResponses = new string[]
    {
        "嗯，我在。",
        "嗯？",
        "本座在此。",
        "怎么啦？"
    };

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
    private TimeWeatherController _timeController;
    private float _lastGreetingTime = -999f;
    private float _lastInteractionTime = -999f;
    private int _clickResponseCursor = -1;

    /// <summary>本次运行已播出的本地点击轻回应次数（只读，供测试与终端核验）。</summary>
    public int LocalClickResponseCount { get; private set; }

    void Start()
    {
        ResolveReferences();
        if (_bubble == null || _idleGen == null)
        {
            Debug.LogWarning("[AutoChat] 缺少 ChatBubble/IdleChatGenerator，请由装配入口提供，已停用");
            enabled = false;
            return;
        }

        // 监听拖拽事件
        if (_drag != null)
        {
            _drag.OnPetClicked += HandleClick;
            _drag.OnDragEnded += HandleDrag;
        }

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

    /// <summary>
    /// 解析直接依赖。除 Start 外也可安全调用（EditMode 测试、迟挂组件场景）。
    /// </summary>
    private void ResolveReferences()
    {
        if (_chat == null) _chat = GetComponent<ChatManager>();
        if (_bubble == null) _bubble = GetComponent<ChatBubble>();
        if (_idleGen == null) _idleGen = GetComponent<IdleChatGenerator>();
        if (_renderer == null) _renderer = GetComponent<Live2DRenderer>();
        if (_timeController == null) _timeController = GetComponent<TimeWeatherController>();
        if (_drag == null) _drag = GetComponent<DragHandler>();
    }

    /// <summary>
    /// 桌宠被点击时的入口（由 <see cref="DragHandler.OnPetClicked"/> 触发）。
    /// 普通点击只做本地轻回应：姿势、注意力事件与交互计时均复用既有链路，
    /// 不调用 ChatManager.SendMessage，不新增会话记录，不请求本地或云端 LLM。
    /// </summary>
    public void HandleClick()
    {
        // 审批分支与轻回应分支都要用到气泡，先在入口解析一次直接依赖。
        ResolveReferences();

        // ⚠️ 危险工具确认属于既有特殊分支，优先于普通轻回应：
        // 有待确认操作时，点击桌宠 = 允许执行（拒绝由 ESC、超时由既有看门狗处理）。
        // 该分支不得被本地短回应吞掉，也不自动确认。
        if (ToolConfirmManager.HasPending)
        {
            ToolConfirmManager.Resolve(true);
            if (_bubble != null)
                _bubble.ShowMessage("✅ 已获准许，施法！", 3f, ChatBubble.MsgPriority.High);
            return;
        }

        ShowLocalClickResponse();
    }

    /// <summary>
    /// 普通点击的本地轻回应：低优先级、短暂显示，沿用既有可配置冷却。
    /// 高优消息、正在回复、正在执行的动作与冷却中一律让位——不覆盖、不排队、
    /// 不延长、也不在忙碌结束后补播过期回应。
    /// </summary>
    private void ShowLocalClickResponse()
    {
        if (!clickLightResponseEnabled) return;
        if (_bubble == null) return;

        // 任何在播气泡（高优 AI 回复、审批提示、提醒、问候…）优先：
        // 轻回应不覆盖也不排队等待。
        if (_bubble.IsShowing) return;
        // AI 正在回复时不插话（沿用既有门控）。
        if (_chat != null && _chat.IsWaiting) return;
        // 正在执行的动作优先：既有动作锁与 AI 参数锁均视为占用。
        if (_renderer != null && (_renderer.IsActionLocked || _renderer.IsAiControlLocked)) return;
        // 沿用既有可配置交互冷却与交互计时：连点被抑制，不刷屏、不积压。
        if (Time.time - _lastInteractionTime < interactionCooldown) return;

        string line = NextClickResponseLine();
        if (string.IsNullOrEmpty(line)) return;

        _lastInteractionTime = Time.time;
        LocalClickResponseCount++;
        _bubble.ShowMessage(line, clickLightResponseDuration, ChatBubble.MsgPriority.Low);
        Debug.Log("[AutoChat] 本地点击轻回应: " + line);
    }

    /// <summary>按顺序轮换本地短句，避免连续两次同一句；不用随机数以便复现。</summary>
    private string NextClickResponseLine()
    {
        if (clickLightResponses == null || clickLightResponses.Length == 0) return null;
        _clickResponseCursor = (_clickResponseCursor + 1) % clickLightResponses.Length;
        return clickLightResponses[_clickResponseCursor];
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
            float totalDuration;
            if (total == int.MaxValue || total <= 1)
            {
                // 流式模式 total 未知，或只有一句：先用 aiReplyDuration 保底
                // 后续句子到达时会通过 ExtendDuration 修正
                totalDuration = aiReplyDuration;
            }
            else
            {
                totalDuration = (total - 1) * _chat.sentenceInterval + aiReplyDuration;
            }
            _bubble.ShowMessage("🌸 " + sentence, totalDuration, ChatBubble.MsgPriority.High);
        }
        else
        {
            // 后续句子：只更新文字，不碰计时器
            _bubble.UpdateText("🌸 " + sentence);
            // 流式模式下 total 此时已经是真实值了，修正气泡计时
            float remaining = (total - 1 - idx) * _chat.sentenceInterval + aiReplyDuration;
            _bubble.ExtendDuration(remaining);
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
        System.DateTime now = System.DateTime.Now;
        // 与昼夜/天气控制器共用调试小时，避免测试或运行时出现两套时间。
        int hour = (_timeController != null && _timeController.debugHourOverride >= 0
            && _timeController.debugHourOverride <= 23)
            ? _timeController.debugHourOverride : now.Hour;
        bool isWeekend = now.DayOfWeek == System.DayOfWeek.Saturday
                      || now.DayOfWeek == System.DayOfWeek.Sunday;

        string timeKey;
        if (isWeekend && hour >= 8 && hour < 12)
            timeKey = "周末上午";
        else if (isWeekend && hour >= 12 && hour < 14)
            timeKey = "周末中午";
        else if (isWeekend && hour >= 14 && hour < 17)
            timeKey = "周末下午";
        else if (isWeekend && hour >= 17 && hour < 19)
            timeKey = "周末傍晚";
        else if (isWeekend && hour >= 19 && hour < 23)
            timeKey = "周末夜晚";
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

        string context = $"{timeKey}（{hour}点钟）";

        // 用 IdleChatGenerator 动态生成
        return _idleGen.GetGreeting(context);
    }
}
