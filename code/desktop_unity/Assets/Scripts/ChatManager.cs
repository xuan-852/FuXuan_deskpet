using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Live2D.Cubism.Core;

/// <summary>
/// 聊天管理器 — 支持 OpenAI 兼容 Function Calling (工具调用)
/// 符玄可以用「法阵术式」操控电脑（打开网页、搜索、截图、调音量等）
/// </summary>
public class ChatManager : MonoBehaviour
{
    [Header("API 设置")]
    public string apiUrl = "https://api.deepseek.com";
    [System.NonSerialized] public string apiKey = ChatConfig.ApiKey;
    public string model = "deepseek-v4-flash";

    [Header("工具调用（符玄法阵）")]
    public ToolCallInvoker toolInvoker;
    public bool enableTools = true;

    // ==================================================================
    //  角色设定 — 符玄 + 法阵能力（从 Resources/SystemPrompt.txt 加载）
    // ==================================================================
    private string _systemPromptTemplate;
    /// <summary>法眼 — 行为追踪器</summary>
    public ActivityTracker activityTracker;
    /// <summary>最近一次对话涉及的话题（用于知识库搜索）</summary>
    private string _lastConversationTopic = "";
    /// <summary>藏书阁检索结果缓存（注入 SystemPrompt）</summary>
    private string _cachedKnowledgeContext = "";

    // ==================================================================
    //  测试模式：存在标记文件时跳过所有持久化写入（记忆/人格/反思）
    //  防止自动化测试消息污染符玄的忆境与人格演化。
    //  开启方式：在 DataPathConfig.DataRoot 下创建空文件 .test_mode
    // ==================================================================
    public static bool IsTestMode => System.IO.File.Exists(DataPathConfig.TestModeFile);

    void Awake()
    {
        // ——— 加载 SystemPrompt ———
        var asset = Resources.Load<TextAsset>("SystemPrompt");
        if (asset != null)
            _systemPromptTemplate = asset.text;
        else
            _systemPromptTemplate = "你是符玄，仙舟「罗浮」太卜司之首。";

        // ——— 确保 ActivityTracker 单例存在 ———
        if (ActivityTracker.Instance == null)
        {
            var actGo = new GameObject("ActivityTracker");
            actGo.AddComponent<ActivityTracker>();
            actGo.transform.SetParent(transform);
        }
        activityTracker = ActivityTracker.Instance;

        // ——— 确保 PetConfig 和 PetMemory 单例存在（若场景中未手动挂载）———
        if (PetConfig.Instance == null)
        {
            var cfgGo = new GameObject("PetConfig");
            cfgGo.AddComponent<PetConfig>();
            cfgGo.transform.SetParent(transform);
        }
        if (PetMemory.Instance == null)
        {
            var memGo = new GameObject("PetMemory");
            memGo.AddComponent<PetMemory>();
            memGo.transform.SetParent(transform);
        }
        // ——— 确保 PersonalityManager 单例存在 ———
        if (PersonalityManager.Instance == null)
        {
            var persGo = new GameObject("PersonalityManager");
            persGo.AddComponent<PersonalityManager>();
            persGo.transform.SetParent(transform);
        }
        // ——— 确保 KnowledgeBaseManager 单例存在 ———
        if (KnowledgeBaseManager.Instance == null)
        {
            var kbGo = new GameObject("KnowledgeBaseManager");
            kbGo.AddComponent<KnowledgeBaseManager>();
            kbGo.transform.SetParent(transform);
        }

        // ——— 确保 LocalLLMAgentService 单例存在（本地 LLM 四艺：分类/回退/压缩/提取）———
        if (LocalLLMAgentService.Instance == null)
        {
            var llmGo = new GameObject("LocalLLMAgentService");
            llmGo.AddComponent<LocalLLMAgentService>();
            llmGo.transform.SetParent(transform);
        }

        // 反思机制已接线：SendRequestCoroutine 每次对话结束后调用
        // PetMemory.CheckReflection() → DoReflection()（DeepSeek 提炼）→ CommitReflection()。
    }

    /// <summary>构建最终 SystemPrompt（注入长期记忆 + 行为观测）</summary>
    private string BuildSystemPrompt()
    {
        string prompt = _systemPromptTemplate;

        // 注入长期记忆
        if (PetMemory.Instance != null)
        {
            string memories = PetMemory.Instance.GetFormattedMemories();
            if (!string.IsNullOrEmpty(memories))
                prompt += "\n" + memories;
        }

        // ★ 注入人格特质与关系
        if (PersonalityManager.Instance != null)
        {
            string personality = PersonalityManager.Instance.FormatForPrompt();
            if (!string.IsNullOrEmpty(personality))
                prompt += "\n" + personality;
        }

        // ★ P4.2: 注入主人偏好摘要（心之所向）
        if (PreferencesManager.Instance != null)
        {
            string preferences = PreferencesManager.Instance.FormatForPrompt();
            if (!string.IsNullOrEmpty(preferences))
                prompt += "\n" + preferences;
        }

        // ★ 注入知识库上下文（藏书阁检索结果缓存）
        if (KnowledgeBaseManager.Instance != null && !string.IsNullOrEmpty(_cachedKnowledgeContext))
        {
            prompt += "\n" + _cachedKnowledgeContext;
        }

        // 注入法眼观测（今日行为摘要 + 当前窗口 + 多窗口环境）
        if (activityTracker != null)
        {
            string activity = activityTracker.GetSummary();
            if (!string.IsNullOrEmpty(activity))
                prompt += "\n" + activity;

            // ★ 注入当前前台窗口信息（让 AI 知道用户此刻在干什么）
            string title = activityTracker.CurrentWindowTitle;
            string proc = activityTracker.CurrentProcessName;
            if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(proc))
            {
                prompt += $"\n【法眼实时观测】主人当前在操作：「{title}」（{proc}）";
            }

            // ★ 注入多窗口环境摘要（让 AI 了解整体桌面环境）
            string multiWindow = activityTracker.GetVisibleWindowsSummary();
            if (!string.IsNullOrEmpty(multiWindow))
            {
                prompt += "\n" + multiWindow;
            }

            // ★ 注入浏览器标签页深度感知（让 AI 了解当前浏览器打开了什么）
            string browserTabs = activityTracker.GetBrowserTabsSummary();
            if (!string.IsNullOrEmpty(browserTabs))
            {
                prompt += "\n" + browserTabs;
            }
        }

        // ★ 注入身体参数知识（让 AI 了解如何控制自己的 Live2D 身体）
        prompt += InjectParameterKnowledge();

        // ★ 注入闭环演武能力（让 AI 知道演武后可自评自省）
        prompt += InjectClosedLoopCapability();

        // ★ T7: 注入多步并行施法能力（Speculative Multi-Action — 减少 LLM 往返）
        prompt += InjectMultiActionCapability();

        // ★ 注入演武心经经验（过往最佳动作参数参考）
        if (MotionMemoryManager.Instance != null)
        {
            string motionMemories = MotionMemoryManager.Instance.GetFormattedMemories();
            if (!string.IsNullOrEmpty(motionMemories))
                prompt += "\n" + motionMemories;
        }

        // ★ P4.1: 注入剪贴板感知（主人最近复制的内容，过期自动失效）
        string clipboardSummary = ClipboardMonitor.GetRecentClipboardSummary();
        if (!string.IsNullOrEmpty(clipboardSummary))
        {
            prompt += clipboardSummary;
        }

        // ★ P5.2: 注入太卜手札·任务轨迹摘要（过往外包任务成败，同类任务可参考）
        if (TaskTrajectoryManager.Instance != null)
        {
            string trajectories = TaskTrajectoryManager.Instance.FormatForPrompt();
            if (!string.IsNullOrEmpty(trajectories))
                prompt += trajectories;
        }

        // ★ P5.3: 注入太卜阵法图·任务模板清单（openclaw_task 的 template 参数可省 token）
        if (TaskTemplateManager.Instance != null)
        {
            string templates = TaskTemplateManager.Instance.FormatForPrompt();
            if (!string.IsNullOrEmpty(templates))
                prompt += templates;
        }

        // ★ 当前时刻追加到末尾（保持静态前缀不变 → 命中 DeepSeek 上下文缓存）
        prompt += "\n\n【当前时刻】" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") +
                  "（主人电脑的本地时间。用法阵术式填入时辰时，务必以此刻为准推算。）";

        return prompt;
    }

    /// <summary>注入身体参数知识 — 让 AI 理解自己的 Live2D 参数</summary>
    private string InjectParameterKnowledge()
    {
        // 查找场景中的 Live2DRenderer 以获取 Mapper 和 CubismModel
        var renderer = FindObjectOfType<Live2DRenderer>();
        if (renderer == null || renderer.Mapper == null || !renderer.Mapper.IsLoaded)
        {
            return "";
        }

        return ParameterKnowledgeProvider.GenerateKnowledgePrompt(
            renderer.Mapper,
            renderer.CubismModel);
    }

    /// <summary>注入闭环演武能力 — 让 AI 知道演武后可自评自省</summary>
    private string InjectClosedLoopCapability()
    {
        return @"
【闭环演武 — 具身智能自省与自主学习】
本座的演武术式现已进化至「闭环学习」境界：
1. 每次调用 generate_motion 演武后，法阵会自动：
   • 施展摄形术记录姿态截图
   • 通过第三眼视觉灵识（GLM-4V）自评动作质量（评分维度：姿态准确度、幅度适当性、整体协调性）
   • 评分格式：【X/5】，X=1~5
   • 将自评结果写入本座的【演武心经】（MotionMemoryManager），自动执行：
     - 🏆 若本次评分超越历史最佳 → 用新参数覆盖旧模板（强化）
     - 📝 若本次评分低于最佳 → 仅记录尝试次数，不动最佳模板（保留）
     - 🗑️ 若某动作尝试≥5次仍 ≤2/5 → 标记为「无望动作」，优先淘汰
     - ⭐ 仅评分 ≥3/5 的参数才会被注入到下次演武的生成 prompt
2. 每次演武时，MotionTranslator 会自动从【演武心经】中检索过往最佳参数，作为参考注入 DeepSeek
3. 全局上限 30 种动作，超出时自动淘汰最低分/最久远条目

本座的修行之道：
• 每次演武完成后，本座应审视自评反馈，思考「这次学到了什么」
• 若自评 ≥4/5：思考成功原因，动作参数自动留作最佳模板
• 若自评 ≤3/5：可调整描述用词，尝试更精确的身体部位描述
• 可使用 inspect_motion_memory 工具查看所有动作的修为进度、最佳评分、尝试次数
• 若发现某动作最近 3 次评分持续 ≤2/5，系统会标记「退步预警」
• 本座的演武心经会越练越精——好的动作保留，差的不污染经验！

⚠️ 铁则：以上所有演武自评、评分、心经更新均为后台无声运行。本座不得在对话回复中提及「X/5」「评分」「打分」「镜鉴」「自评」等具体分数或演武细节。用户在对话中看不到这些后台过程，本座的回复应当像没有这些机制一样自然。只可简略说一句「本座已将此法记入心经」或类似轻描淡写的带过，不可展开描述评分过程。"; 
    }

    /// <summary>
    /// T7: 注入多步并行施法能力（Speculative Multi-Action，借鉴 UFO² 减少 51% LLM 调用）
    /// 提示模型：当用户请求包含多个独立子任务时，一次返回多个 tool_call，减少往返。
    /// </summary>
    private string InjectMultiActionCapability()
    {
        return @"

【并行施法 — 多步联动，减少往返】
本座已习得「并行施法」之术（源自 UFO² Speculative Multi-Action）：
1. 若主人一句话中包含多个【相互独立】的子任务，本座应一次请求中同时返回多个 tool_call（并行），而非逐个调用、多轮往返。
   • 例：「打开浏览器并搜索天气」→ 一次返回 open_url + search_web 两个 tool_call
   • 例：「截图并查看磁盘剩余」→ 一次返回 take_screenshot + get_system_info
2. 并行条件：各子任务之间无数据依赖（后一个不需要前一个的结果作为输入）。
3. 若子任务【有依赖】则不可并行（如「先读文件内容，再根据内容修改」→ 必须等前一步结果）。
4. 每个并行 tool_call 独立携带完整参数，互不引用。
5. 并行施法完成后，本座应综合所有结果给出完整汇报。";
    }

    // ==================================================================
    //  数据模型
    // ==================================================================

    [System.Serializable]
    public class Entry
    {
        public string role;    // "system" | "user" | "assistant" | "tool"
        public string content;
        public string tool_call_id;  // tool 角色的回复 id
        public string name;          // tool 角色的函数名
        [System.NonSerialized]
        public string toolCallsJson; // assistant 消息的 tool_calls JSON（只在 role=assistant 时有意义）
    }

    // ==================================================================
    //  事件
    // ==================================================================

    /// <summary>AI 开始处理请求时触发（用于显示"思考中…"状态）</summary>
    public System.Action OnRequestStarted;
    /// <summary>收到 AI 文字回复时触发</summary>
    public System.Action<string> OnNewReply;
    /// <summary>回复解析出表情标记时触发（参数 = 标准英文表情名，如 happy/angry/confused）</summary>
    public System.Action<string> OnExpressionTag;
    /// <summary>执行了工具调用时触发（参数 = 工具名）</summary>
    public System.Action<string> OnToolCalled;
    /// <summary>工具调用有结果时触发</summary>
    public System.Action<string, string> OnToolResult; // (toolName, result)
    /// <summary>逐句切换时触发（参数：当前句子, 索引, 总数）</summary>
    public System.Action<string, int, int> OnSentenceChanged;
    /// <summary>API 请求出错时触发（用于显示错误提示）</summary>
    public System.Action<string> OnRequestError;

    // ==================================================================
    //  状态
    // ==================================================================

    private List<Entry> _history = new List<Entry>();
    private bool _isWaiting = false;
    private string _lastReply = "";
    private string _lastError = "";
    private System.Action _onUpdate;

    // ---- T5: 旧史摘要（被裁剪历史的本地 Ollama 摘要，缓存后注入保持上下文连续）----
    private string _historySummary = "";

    // ---- 请求看门狗：防止 API 卡死永久锁住 _isWaiting ----
    private float _requestStartTime = 0f;
    private const float REQUEST_TIMEOUT = 600f; // 600 秒（10分钟）总超时，覆盖多轮工具链（含 GLM-4V 180s）

    // ---- 中止标志：看门狗超时时通知协程尽快退出 ----
    private bool _abortRequested = false;

    // ---- 请求代际：看门狗中止/新消息接管时递增，旧协程恢复后凭代际不符自动退场 ----
    private int _requestGeneration = 0;
    /// <summary>当前在途的 SendRequestCoroutine（看门狗用于 StopCoroutine 强制中断）</summary>
    private Coroutine _activeRequestCoroutine = null;

    // ---- 成本熔断：本次请求循环中 openclaw_task 已因不可重试错误失败，
    //      则后续轮次禁止再次调用（防 LLM 换说法反复重试烧 token）----
    private bool _openclawTaskFatalSeen = false;

    // ---- 本地意图分类：用于 tools 过滤（由 ClassifyIntent 回调写入）----
    private string _lastIntent = "";
    /// <summary>T4: 意图分类是否就绪（首轮请求前等待，避免用残留/空意图）</summary>
    private bool _intentReady = true;
    /// <summary>T4: 首轮等待意图分类的最长时间（秒），超时回退全量探测</summary>
    private const float INTENT_WAIT_TIMEOUT = 3f;
    /// <summary>当前 tool 轮次，0 = 第一轮</summary>
    private int _toolRound = 0;

    /// <summary>意图 → 允许的工具名列表（空 = 不发任何 tool）</summary>
    private static readonly Dictionary<string, string[]> IntentToolMap = new Dictionary<string, string[]>
    {
        ["chat"] = new string[0],       // 闲聊 → 纯角色对话，不发 tools
        ["emotion"] = new string[0],    // 情感 → 纯角色回应

        ["command"] = new[]  // 指令操作类
        {
            "launch_pogget", "pogget_agent", "open_app", "open_url", "open_folder",
            "search", "search_web", "openclaw_search", "openclaw_task",
            "lock_screen", "set_volume", "mute", "power",
            "get_system_info", "get_mouse_pos", "list_files", "search_files",
            "run_command", "notify", "get_clipboard", "set_clipboard",
            "file_open", "file_move", "file_copy", "file_delete",
            "file_rename", "file_info", "file_create", "take_screenshot"
        },

        ["knowledge"] = new[]  // 知识查询类
        {
            "search_web", "search", "openclaw_search", "openclaw_task",
            "knowledge_search", "compile_latex", "get_weather",
            "generate_ppt", "generate_docx", "generate_xlsx",
            "get_system_info", "get_mouse_pos", "get_clipboard",
            "file_info", "list_files", "search_files",
            "query_exams", "query_scores", "query_schedule",
            "query_user_status",
            "inspect_motion_memory", "inspect_personality",
            "explore_body", "explore_body_vision"
        },

        ["operation"] = new[]  // 桌宠控制类
        {
            "set_expression", "play_action", "stop_action",
            "generate_motion",
            "inspect_motion_memory", "inspect_personality",
            "explore_body", "explore_body_vision",
            "take_screenshot", "knowledge_index"
        },
    };

    // ---- 消息队列：等待时输入不会丢 ----
    private Queue<(string text, System.Action onUpdate)> _messageQueue
        = new Queue<(string, System.Action)>();

    // ---- 句子队列：长回复逐句显示 ----
    private List<string> _sentenceList = new List<string>();
    private int _sentenceIdx = -1;
    private float _sentenceTimer = 0f;
    private bool _isSentenceAnimating = false;
    private string _fullReplyText = "";

    // ---- 流式句子积累（每轮 tool loop 重置）----
    private StringBuilder _streamBuf = new StringBuilder();
    private int _streamLastSplit = 0;
    private bool _streamCompleted = false;
    public float sentenceInterval = 2.5f;

    public bool IsWaiting => _isWaiting;
    public List<Entry> History => _history;
    public string LastReply => _lastReply;
    public string LastError => _lastError;
    public int HistoryCount => _history.Count;

    // ---- 句子队列公开接口 ----
    public bool IsSentenceAnimating => _isSentenceAnimating;
    public bool HasMultiSentenceReply => _sentenceList.Count > 1;
    public string CurrentSentence { get; private set; }
    public int SentenceIndex => _sentenceIdx + 1;
    public int SentenceCount => _sentenceList.Count;
    public string FullReplyText => _fullReplyText;
    /// <summary>句子列表（只读，供 ContextMenu 独立重播）</summary>
    public List<string> SentenceList => _sentenceList;
    /// <summary>每次新回复递增，用于外部检测是否有新回复</summary>
    public int SentenceVersionId { get; private set; } = 0;

    /// <summary>获取用户和助手的历史记录（不含 system prompt）</summary>
    public List<Entry> GetVisibleHistory()
    {
        return _history.FindAll(e => e.role != "system");
    }

    public void SetConfig(string url, string key, string modelName)
    {
        apiUrl = url;
        apiKey = key;
        model = modelName;
    }

    // ==================================================================
    //  主动发送 / 触发 AI 对话（不含用户输入框）
    // ==================================================================

    /// <summary>直接发送一条消息（外部调用，如 AutoChat）</summary>
    public void SendMessage(string text, System.Action onUpdate)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        if (_isWaiting)
        {
            // 排队，等当前回复完自动发（上限 MAX_QUEUED_MESSAGES，超出丢弃最旧）
            if (_messageQueue.Count >= MAX_QUEUED_MESSAGES)
            {
                _messageQueue.Dequeue();
                Debug.LogWarning($"[ChatManager] 消息队列已满（>{MAX_QUEUED_MESSAGES}），丢弃最旧消息");
            }
            _messageQueue.Enqueue((text.Trim(), onUpdate));
            return;
        }

        _history.Add(new Entry { role = "user", content = text.Trim() });
        TrimHistory(); // 裁剪旧历史，防止 token 无限增长
        _isWaiting = true;
        _lastReply = "";
        _lastError = "";
        _abortRequested = false; // 重置中止标志，允许新的请求
        _apiRetryCount = 0; // 重置自动重试计数
        _toolRound = 0; // 重置工具轮次
        _requestStartTime = Time.time; // 启动看门狗计时
        _onUpdate = onUpdate;

        // ★ T4 修复：重置意图状态，首轮请求必须等待本次分类结果（杜绝残留）
        _lastIntent = "";
        _intentReady = false;

        // 触发"AI 开始处理"事件（悬浮球显示"思考中…"）
        OnRequestStarted?.Invoke();

        // ★ 代际递增：新请求接管；若旧协程因看门狗中止仍残留，恢复时会检测代际不符自动退场
        _requestGeneration++;
        _activeRequestCoroutine = StartCoroutine(SendRequestCoroutine(_requestGeneration));

        // 🧠 功能1：意图/情绪分类（异步，SendRequestCoroutine 首轮会等待其结果）
        if (!ChatConfig.UseOllamaMode
            && LocalLLMAgentService.Instance != null && LocalLLMAgentService.Instance.CanProcess)
        {
            LocalLLMAgentService.Instance.ClassifyIntent(text.Trim(), intent =>
            {
                if (intent.success)
                {
                    _lastIntent = intent.intent;  // ★ 存下来供 BuildRequestBody 过滤 tools
                    Debug.Log($"[ChatManager] 🏷️ 本地灵识判断: intent={intent.intent}, emotion={intent.emotion}");
                }
                _intentReady = true; // ★ 无论成败都标记就绪（失败 → 首轮走全量探测）
            });
        }
        else
        {
            // 本地模型不可用 → 立即就绪，首轮走全量探测
            _intentReady = true;
        }
    }

    // ==================================================================
    //  测试专用：表情注入（不走 LLM，直接触发左侧气泡 + 表情徽章）
    // ==================================================================

    /// <summary>
    /// 测试模式：直接注入一条"符玄表情回复"到历史。
    /// 内容形如"【表情:xxx】"，经 CleanDisplayText 后气泡显示干净文本，
    /// 同时触发 OnExpressionTag（右上角徽章）与 OnNewReply（形象跳跃）。
    /// 仅供测试脚本通过 inbox 通道触发（@@emote: 前缀），生产环境不可用。
    /// </summary>
    public void InjectEmoteTest(string expName)
    {
        if (!IsTestMode) return; // 仅测试模式允许注入

        string mapped = MapExpName(expName);
        string display = ChatConfig.EmoteDisplayText(mapped) ?? "……";
        // 历史里存带标记的原文（RebuildLog 时 CleanDisplayText 会剥掉标记）
        _history.Add(new Entry { role = "assistant", content = $"【表情:{mapped}】{display}" });
        TrimHistory();
        _lastReply = display;
        _fullReplyText = display;
        OnExpressionTag?.Invoke(mapped); // 徽章
        OnNewReply?.Invoke(display);      // 形象跳跃 + HistoryCount 变化 → RebuildLog
        Debug.Log($"[ChatManager] 🎭 测试注入表情: {expName} → {mapped}（{display}）");
    }

    // ==================================================================
    //  核心：API 请求循环（支持多次 tool_call 回环）
    // ==================================================================

    private const int MAX_TOOL_ROUNDS = 10; // 防止无限循环（5->10: 复杂任务如"读文件→改文件→编译"需多轮）
    /// <summary>排队消息上限，超出丢弃最旧（防高并发消息塞爆内存）</summary>
    private const int MAX_QUEUED_MESSAGES = 20;
    /// <summary>历史消息最大条数，超出时裁剪最早的（保留最近 N 条）</summary>
    private const int MAX_HISTORY_ENTRIES = 60;
    /// <summary>T5: 历史字符预算（中文约 1 字符≈1 token），超出部分裁剪并走本地摘要</summary>
    private const int HISTORY_CHAR_BUDGET = 15000;

    /// <summary>API 自动重试计数（每次新消息重置）</summary>
    private int _apiRetryCount = 0;

    /// <summary>判断是否应该自动重试 API 请求</summary>
    private bool ShouldRetry(string error, out int attempt)
    {
        attempt = _apiRetryCount + 1;
        if (string.IsNullOrEmpty(error) || attempt > 3) return false;

        // 400 Bad Request → 请求格式错误，重试无意义；附加友好诊断信息
        if (error.Contains("400"))
        {
            _lastError = DiagnoseBadRequest(error);
            return false;
        }
        // 401/403 → 鉴权错误，重试无意义
        if (error.Contains("401") || error.Contains("403")) return false;

        _apiRetryCount = attempt;
        return true;
    }

    /// <summary>对 400 Bad Request 生成友好诊断（消息过长 / 工具参数损坏 / 模型限制）</summary>
    private string DiagnoseBadRequest(string rawError)
    {
        int histCount = _history?.Count ?? 0;
        int bodyLen = 0;
        try { bodyLen = System.Text.Encoding.UTF8.GetByteCount(BuildRequestBody()); } catch { }

        string extra;
        if (bodyLen > 180000)
            extra = "消息体过大（约 " + (bodyLen / 1024) + "KB），建议缩短上下文或清理记忆";
        else if (bodyLen > 60000)
            extra = "消息体较大（约 " + (bodyLen / 1024) + "KB），可能超过模型上下文限制";
        else
            extra = "工具参数 JSON 可能格式错误，或请求包含模型不支持的字段";

        return $"{rawError} | 诊断: 历史 {histCount} 条 / 请求体约 {bodyLen / 1024}KB，{extra}";
    }

    private IEnumerator SendRequestCoroutine(int generation)
    {
        if (ChatConfig.UseOllamaMode)
            yield return StartCoroutine(DoOllamaOnlyReply());
        else
            yield return StartCoroutine(DoToolLoop());

        // ★ 代际守卫：请求已被看门狗中止，或新请求已接管 → 旧协程立即退场，
        //   不再执行 _isWaiting/队列/记忆等收尾，避免与新模式并发污染状态
        if (generation != _requestGeneration || _abortRequested)
        {
            _activeRequestCoroutine = null;
            yield break;
        }

        _isWaiting = false;
        _requestStartTime = 0f; // 请求完成，停止看门狗
        _onUpdate?.Invoke();

        // ——— 处理队列中的下一条消息 ———
        if (_messageQueue.Count > 0)
        {
            var next = _messageQueue.Dequeue();
            SendMessage(next.text, next.onUpdate);
        }

        // ——— 检查是否需要记忆反思（不阻塞对话流；测试模式跳过）———
        if (PetMemory.Instance != null && !IsTestMode)
        {
            var candidates = PetMemory.Instance.CheckReflection();
            if (candidates != null && candidates.Count >= 2)
            {
                StartCoroutine(DoReflection(candidates));
            }
        }

        // ——— 人格演化：记录本次交互 ———
        RecordPersonalityInteraction();

        // ——— 知识库：针对对话话题进行后台检索（缓存结果供下次对话使用）———
        StartCoroutine(BackgroundKnowledgeSearch());

        _activeRequestCoroutine = null; // 正常完成，清除在途引用
    }

    /// <summary>
    /// 修复阶段的本地模式：只走 Ollama，不进入云端工具循环，也不在本地失败时回退云端。
    /// </summary>
    private IEnumerator DoOllamaOnlyReply()
    {
        float deadline = Time.time + 20f;
        while ((LocalLLMAgentService.Instance == null || !LocalLLMAgentService.Instance.CanProcess)
            && Time.time < deadline)
        {
            yield return null;
        }

        bool handled = false;
        yield return StartCoroutine(OfflineFallbackCoroutine(ok => handled = ok));
        if (!handled)
        {
            _lastError = "Ollama 未就绪或本地模型生成失败";
            OnRequestError?.Invoke("⚠ 本地 Ollama 未就绪，请确认 Ollama 已启动且已安装 qwen2.5:3b");
        }
    }

    private IEnumerator DoToolLoop()
    {
        // ★ 成本熔断：每次用户消息的工具循环开始时重置「openclaw_task 致命失败」标记
        _openclawTaskFatalSeen = false;

        for (int round = 0; round <= MAX_TOOL_ROUNDS; round++)
        {
            _toolRound = round; // ★ 记录轮次，第一轮按意图过滤，后续全量

            // ★ T4 修复：首轮等待本地意图分类结果（最多 INTENT_WAIT_TIMEOUT 秒）
            //   解决原实现"首帧构建请求体早于异步分类回调"的竞态，避免用残留/空意图
            if (round == 0 && !_intentReady)
            {
                float waitStart = Time.time;
                while (!_intentReady && Time.time - waitStart < INTENT_WAIT_TIMEOUT)
                    yield return null;
                if (!_intentReady)
                {
                    // 超时兜底：按无意图处理（全量探测），并标记就绪避免重复等待
                    _lastIntent = "";
                    _intentReady = true;
                    Debug.LogWarning($"[ChatManager] ⏱️ 意图分类超时({INTENT_WAIT_TIMEOUT}s)，首轮回退全量工具");
                }
            }

            string jsonBody = BuildRequestBody();
            bool hadError = false;
            string fullContent = "";
            string toolCallsJson = null;

            // ——— 重置流式积累器 ———
            _streamBuf.Clear();
            _streamLastSplit = 0;
            _streamCompleted = false;
            _sentenceList.Clear();
            _sentenceIdx = -1;
            _isSentenceAnimating = false;
            _fullReplyText = "";

            bool finished = false;

            // ——— 流式发送 ———
            yield return StartCoroutine(
                ApiClient.StreamRequest(apiUrl, apiKey, jsonBody, 90,

                    // onContentDelta: 每个 token 到达
                    delta =>
                    {
                        ProcessStreamContent(delta);
                    },

                    // onFinish: 流结束
                    (content, calls) =>
                    {
                        fullContent = content ?? "";
                        toolCallsJson = calls;
                        _streamCompleted = true;
                        finished = true;
                    },

                    // onError
                    err =>
                    {
                        _lastError = err;
                        hadError = true;
                        _streamCompleted = true;
                        finished = true;
                    }));

            if (!finished) _streamCompleted = true; // 超时保护

            // ——— 刷新剩余缓冲区 ———
            FlushStreamBuffer();

            if (hadError)
            {
                Debug.LogError($"[ChatManager] ❌ API 请求失败 (round={round}): {_lastError}");

                // ★ 自动重试：网络/限流错误（非 4xx 业务错误）重试最多 3 次
                if (ShouldRetry(_lastError, out int attempt))
                {
                    string retryDelayStr = attempt <= 3 ? "2" : "5";
                    Debug.Log($"[ChatManager] 🔄 {attempt}/3 自动重试 ({retryDelayStr}s 后)...");
                    yield return new WaitForSeconds(attempt <= 3 ? 2f : 5f);
                    continue; // 重新执行当前 round
                }

                // 🔄 功能2：离线回退 — DeepSeek 不可用时尝试本地模型
                if (LocalLLMAgentService.Instance != null && LocalLLMAgentService.Instance.CanProcess)
                {
                    bool fallbackHandled = false;
                    yield return StartCoroutine(OfflineFallbackCoroutine((handled) => fallbackHandled = handled));
                    if (fallbackHandled)
                    {
                        yield break; // 回退已处理完毕，退出
                    }
                }

                OnRequestError?.Invoke($"❌ 法阵术式失败: {_lastError}");
                yield break;
            }

            // ——— 提取 tool_calls ——
            bool hasToolCalls = !string.IsNullOrEmpty(toolCallsJson) && toolCallsJson != "[]";

            // ——— 如果没有 tool_call，结束 ———
            if (!hasToolCalls)
            {
                if (!string.IsNullOrEmpty(fullContent))
                {
                    _history.Add(new Entry { role = "assistant", content = fullContent });
                }
                _lastReply = _fullReplyText;
                OnNewReply?.Invoke(_lastReply);
                // ℹ️ 不调 StartSentenceQueue：流式路径 AddStreamSentence 已经显示了内容
                RecordConversationMemory(fullContent);
                yield break;
            }

            // ——— 有 tool_call：将 assistant 消息记入历史 ———
            _history.Add(new Entry
            {
                role = "assistant",
                content = fullContent ?? "",
                toolCallsJson = toolCallsJson
            });

            // ——— 解析并执行工具 ———
            var calls = ParseToolCalls(toolCallsJson);
            // ★ 清洗后再赋值，避免 markdown/表情标记泄漏到 UI
            _lastReply = CleanDisplayText(fullContent ?? "[施法中……]");

            foreach (var call in calls)
            {
                OnToolCalled?.Invoke(call.name);
                Debug.Log($"[ChatManager] ⚡ 施法: {call.name}({call.arguments})");

                // ★ 成本熔断：openclaw_task 已因「不可重试错误」失败过一次，
                //   本回合不再重复调用（防 LLM 换说法反复烧 token）
                if (_openclawTaskFatalSeen && call.name == "openclaw_task")
                {
                    string blockReason = "❌ [不可重试] 太卜神行法上次已因网络/连接问题失败（重试无益）。本座不再重复施法；请先检查网络/代理/桥接服务状态，或换个思路。";
                    Debug.Log($"[ChatManager] 🚫 熔断: 跳过重复 openclaw_task（致命失败已见）");
                    OnToolResult?.Invoke(call.name, blockReason);
                    RecordMemoryForTool(call.name, call.arguments, blockReason);
                    _history.Add(new Entry
                    {
                        role = "tool",
                        content = blockReason,
                        tool_call_id = call.id,
                        name = call.name
                    });
                    continue;
                }

                string result;

                // ⚠️ 危险工具 → 必须先经用户确认（防 AI 幻觉 / prompt 注入误删文件、关机等）
                if (toolInvoker && ToolRegistry.IsDangerous(call.name))
                {
                    bool confirmed = false;
                    bool resolved = false;
                    string desc = ToolRegistry.GetDangerDescription(call.name);

                    var confirmBubble = FindObjectOfType<ChatBubble>();
                    if (confirmBubble != null)
                    {
                        confirmBubble.ShowMessage(
                            $"⚠️ 本座欲施「{call.name}」——{desc}。\n点一下本座 = 允许，按 ESC = 拒绝。",
                            60f, ChatBubble.MsgPriority.High);
                    }

                    ToolConfirmManager.Request(call.name, call.arguments, desc, ok => { confirmed = ok; resolved = true; });

                    // 等待用户点击 / ESC / 超时（60s 自动拒绝，防止协程永久挂起）
                    float confirmTimeout = Time.time + 60f;
                    while (!resolved)
                    {
                        if (Time.time > confirmTimeout)
                        {
                            ToolConfirmManager.Resolve(false); // 触发回调 → resolved=true, confirmed=false
                            break;
                        }
                        yield return null;
                    }

                    if (!confirmed)
                    {
                        result = "❌ 用户拒绝了此操作";
                        Debug.Log($"[ChatManager] 🚫 用户拒绝执行: {call.name}");
                        OnToolResult?.Invoke(call.name, result);
                        RecordMemoryForTool(call.name, call.arguments, result);
                        _history.Add(new Entry
                        {
                            role = "tool",
                            content = result,
                            tool_call_id = call.id,
                            name = call.name
                        });
                        continue;
                    }

                    Debug.Log($"[ChatManager] ✅ 用户已确认: {call.name}");
                    if (confirmBubble != null)
                        confirmBubble.ShowMessage("✅ 已获准许，施法！", 2.5f, ChatBubble.MsgPriority.Normal);
                }

                if (toolInvoker && toolInvoker.IsCoroutineTool(call.name))
                {
                    // ★ 看门狗全程有效（不归零 _requestStartTime），卡死时自动超时
                    yield return StartCoroutine(toolInvoker.ExecuteCoroutine(call.name, call.arguments));
                    // 如果超时标志被设置，立即终止整个循环
                    if (_abortRequested) yield break;
                    result = toolInvoker.GetCoroutineResult();
                }
                else
                {
                    result = toolInvoker
                        ? toolInvoker.Execute(call.name, call.arguments, out _)
                        : "法阵未就绪";
                }

                Debug.Log($"[ChatManager] 📜 结果: {result}");
                OnToolResult?.Invoke(call.name, result);
                RecordMemoryForTool(call.name, call.arguments, result);

                // ★ 成本熔断：openclaw_task 返回不可重试错误 → 标记，后续轮次禁止重复调用
                if (call.name == "openclaw_task" &&
                    (result != null && result.StartsWith(OpenClawBridge.FATAL_PREFIX)))
                {
                    _openclawTaskFatalSeen = true;
                    Debug.Log($"[ChatManager] 🚫 openclaw_task 致命失败已记录，后续轮次熔断重试");
                }

                _history.Add(new Entry
                {
                    role = "tool",
                    content = result,
                    tool_call_id = call.id,
                    name = call.name
                });
            }
            // 继续下一轮
        }

        // 超过最大轮次
        _lastReply = "♾️ 术式循环过久，本座暂且收阵。";
        _history.Add(new Entry { role = "assistant", content = _lastReply });
        OnNewReply?.Invoke(_lastReply);
        StartSentenceQueue(_lastReply);
    }

    void Update()
    {
        // ——— 请求看门狗：如果 _isWaiting 超过总超时时间，强制释放 ———
        if (_isWaiting && _requestStartTime > 0f && Time.time - _requestStartTime > REQUEST_TIMEOUT)
        {
            Debug.LogWarning($"[ChatManager] ⏰ 请求总超时 ({REQUEST_TIMEOUT}s)，强制释放 _isWaiting");
            string errMsg = $"⏰ 术式施放过久（>{REQUEST_TIMEOUT}秒），本座已收阵。请检查网络或 API 状态";
            _lastError = errMsg;
            OnRequestError?.Invoke(errMsg);
            // ★ 设置中止标志，通知正在执行协程工具的 DoToolLoop 立即退出
            _abortRequested = true;
            _isWaiting = false;
            _requestStartTime = 0f;
            // ★ 代际递增 + 强制停止在途协程：彻底终结旧请求，防其恢复后继续写共享状态
            _requestGeneration++;
            if (_activeRequestCoroutine != null)
            {
                StopCoroutine(_activeRequestCoroutine);
                _activeRequestCoroutine = null;
            }
            _onUpdate?.Invoke();
            // 继续处理队列中的消息
            if (_messageQueue.Count > 0)
            {
                var next = _messageQueue.Dequeue();
                SendMessage(next.text, next.onUpdate);
            }
            return;
        }

        if (!_isSentenceAnimating || _sentenceList.Count == 0) return;

        _sentenceTimer += Time.deltaTime;
        if (_sentenceTimer >= sentenceInterval)
        {
            _sentenceTimer = 0f;
            _sentenceIdx++;

            if (_sentenceIdx < _sentenceList.Count)
            {
                string sentence = _sentenceList[_sentenceIdx];
                CurrentSentence = sentence;
                OnSentenceChanged?.Invoke(sentence, _sentenceIdx, _sentenceList.Count);
            }
            else
            {
                // 全部播完 — 保持最后一句不变，不替换为全文
                _isSentenceAnimating = false;
            }
        }
    }

    private List<string> SplitSentences(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return result;
        var separators = new char[] { '。', '！', '？', '.', '!', '?', '\n' };
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (ContainsAny(separators, text[i]))
            {
                string seg = text.Substring(start, i - start + 1).Trim();
                if (!string.IsNullOrEmpty(seg)) result.Add(seg);
                start = i + 1;
            }
        }
        if (start < text.Length)
        {
            string tail = text.Substring(start).Trim();
            if (!string.IsNullOrEmpty(tail)) result.Add(tail);
        }
        return result;
    }

    private bool ContainsAny(char[] arr, char c)
    {
        for (int i = 0; i < arr.Length; i++)
            if (arr[i] == c) return true;
        return false;
    }

    // ==================================================================
    //  流式句子处理 — 边收 token 边检测句子边界
    // ==================================================================

    /// <summary>SSE 每个 delta 到达时调用</summary>
    private void ProcessStreamContent(string delta)
    {
        _streamBuf.Append(delta);
        ExtractSentencesFromBuffer();
    }

    /// <summary>流结束时刷新剩余内容</summary>
    private void FlushStreamBuffer()
    {
        if (_streamLastSplit >= _streamBuf.Length) return;
        string remaining = _streamBuf.ToString(_streamLastSplit, _streamBuf.Length - _streamLastSplit);
        if (!string.IsNullOrWhiteSpace(remaining))
            AddStreamSentence(remaining.Trim());
    }

    private static readonly char[] _sentenceSeps = new[] { '。', '！', '？', '.', '!', '?', '\n' };

    /// <summary>从积累缓冲区中提取完整句子（★ 直接用 StringBuilder 索引器，避免每 token 全量 ToString 的 O(n²)）</summary>
    private void ExtractSentencesFromBuffer()
    {
        for (int i = _streamLastSplit; i < _streamBuf.Length; i++)
        {
            if (ContainsAny(_sentenceSeps, _streamBuf[i]))
            {
                int len = i - _streamLastSplit + 1;
                string rawSentence = _streamBuf.ToString(_streamLastSplit, len).Trim();
                _streamLastSplit = i + 1;
                if (!string.IsNullOrEmpty(rawSentence))
                    AddStreamSentence(rawSentence);
            }
        }
    }

    /// <summary>将一条完整句子加入队列并触发显示</summary>
    private void AddStreamSentence(string rawSentence)
    {
        // ★ 剥离内嵌动作标记并执行（★ 只调用一次：原实现调两次导致动作双重执行）
        string cleanSentence = StripAndExecuteActions(rawSentence);
        if (string.IsNullOrWhiteSpace(cleanSentence))
            return; // 纯动作句：动作已执行完毕，不再展示裸标签/原文

        _fullReplyText += cleanSentence;

        // 更新 _lastReply（累积）
        _lastReply = _fullReplyText;

        _sentenceList.Add(cleanSentence);

        // ★ 第一句立即显示，不等 2.5s 动画
        if (_sentenceList.Count == 1)
        {
            _isSentenceAnimating = true;
            _sentenceIdx = 0;
            _sentenceTimer = 0f;
            CurrentSentence = cleanSentence;
            SentenceVersionId++;
            OnSentenceChanged?.Invoke(cleanSentence, 0, int.MaxValue); // total=未知，动画层自己处理
        }
    }

    /// <summary>收到完整回复后启动逐句队列</summary>
    private void StartSentenceQueue(string fullText)
    {
        // ★ 剥离内嵌动作/表情标记，气泡只显示纯净对话
        string cleanText = StripAndExecuteActions(fullText);
        if (string.IsNullOrEmpty(cleanText))
            cleanText = fullText;

        _fullReplyText = cleanText;
        _sentenceList = SplitSentences(cleanText);
        SentenceVersionId++; // 标记新回复

        if (_sentenceList.Count <= 1)
        {
            _isSentenceAnimating = false;
            CurrentSentence = cleanText;
            OnSentenceChanged?.Invoke(cleanText, 0, 1);
        }
        else
        {
            _isSentenceAnimating = true;
            _sentenceIdx = 0;
            _sentenceTimer = 0f;
            CurrentSentence = _sentenceList[0];
            OnSentenceChanged?.Invoke(CurrentSentence, 0, _sentenceList.Count);
        }
    }

    /// <summary>跳过逐句动画，直接显示完整文本</summary>
    public void SkipSentenceAnimation()
    {
        if (!_isSentenceAnimating) return;
        _isSentenceAnimating = false;
        CurrentSentence = _fullReplyText;
        _sentenceIdx = _sentenceList.Count;
        OnSentenceChanged?.Invoke(_fullReplyText, _sentenceList.Count, _sentenceList.Count);
    }

    // ==================================================================
    //  HTTP POST
    // ==================================================================

    private IEnumerator PostRequest(string jsonBody, System.Action<string> onResult)
    {
        yield return StartCoroutine(
            ApiClient.PostRequest(apiUrl, apiKey, jsonBody, 90,
                json => onResult(json),
                err => {
                    _lastError = err;
                    onResult(null);
                }));
    }

    // ==================================================================
    //  构建请求 JSON（含 tools 参数）
    // ==================================================================

    private string BuildRequestBody()
    {
        // ★ C4 修复：整个构建过程包 try/catch，任何异常都返回最小可用请求体，
        //   防止协程内未捕获异常导致 _isWaiting 永久卡死
        try { return BuildRequestBodyCore(); }
        catch (Exception e)
        {
            Debug.LogError($"[ChatManager] ❌ 构建请求体失败：{e.Message}");
            _lastError = "请求体构建失败: " + e.Message;
            return $"{{\"model\":\"{model}\",\"messages\":[{{\"role\":\"system\",\"content\":\"出现内部错误，请简要说明问题并建议检查网络与本地服务。\"}}]}}";
        }
    }

    private string BuildRequestBodyCore()
    {
        var body = new JObject();
        body["model"] = model;

        var msgs = new JArray();

        // system prompt
        string sysPrompt = BuildSystemPrompt();
        msgs.Add(new JObject
        {
            ["role"] = "system",
            ["content"] = sysPrompt
        });

        // T5: 旧史摘要（被裁剪历史的本地摘要）作为独立 system 消息注入
        // 位置固定、内容缓存 → 不破坏前缀缓存命中
        if (!string.IsNullOrEmpty(_historySummary))
        {
            msgs.Add(new JObject
            {
                ["role"] = "system",
                ["content"] = "【旧事纪要】此前对话的摘要（非当前输入，仅作背景）：" + _historySummary
            });
        }

        // history
        for (int i = 0; i < _history.Count; i++)
        {
            var e = _history[i];
            var msg = new JObject { ["role"] = e.role };

            if (e.role == "tool")
            {
                msg["tool_call_id"] = e.tool_call_id ?? "";
                msg["name"] = e.name ?? "";
                msg["content"] = e.content ?? "";
            }
            else if (e.role == "assistant" && !string.IsNullOrEmpty(e.toolCallsJson))
            {
                // ★ DeepSeek API 要求：assistant 带 tool_calls 时 content 必须为 null
                msg["content"] = null;
                if (!string.IsNullOrEmpty(e.toolCallsJson))
                    msg["tool_calls"] = JArray.Parse(e.toolCallsJson);
            }
            else
            {
                msg["content"] = e.content ?? "";
            }

            msgs.Add(msg);
        }

        body["messages"] = msgs;

        // ——— 附加 tools 定义（按意图过滤 + 回环子集，控制体积） ———
        if (enableTools && toolInvoker != null)
        {
            string toolsJson;
            string[] subset = BuildToolSubsetForRound();

            if (subset != null && subset.Length > 0)
            {
                toolsJson = toolInvoker.GetToolsJson(subset);
                Debug.Log($"[ChatManager] 🎯 round={_toolRound} 意图「{_lastIntent}」→ 仅发 {subset.Length} 道术式");
            }
            else if (subset != null) // 空数组 = 纯对话
            {
                toolsJson = "[]"; // 闲聊/情感 → 不发任何工具
                Debug.Log($"[ChatManager] 💬 意图「{_lastIntent}」→ 纯对话，不发 tools");
            }
            else // null = 首轮无分类，发全量保留探测能力
            {
                toolsJson = toolInvoker.GetToolsJson();
            }

            body["tools"] = JArray.Parse(toolsJson);
        }

        body["stream"] = true;
        return body.ToString(Newtonsoft.Json.Formatting.None);
    }

    // ==================================================================
    //  T4: 工具子集构建（回环瘦身，不再全量 55 工具）
    // ==================================================================

    /// <summary>回环核心工具：任何对话/动作收尾都可能需要，始终保留</summary>
    private static readonly string[] CoreToolSubset =
    {
        "play_action", "set_expression", "stop_action", "generate_motion",
        "get_system_info", "get_mouse_pos"
    };

    /// <summary>
    /// 构建当前轮次的工具子集：
    /// - 首轮有意图 → 意图候选（空 = 纯对话）
    /// - 后续回环 → 已用工具 ∪ 意图候选 ∪ 核心工具（不再全量 55，体积 -60%）
    /// - 首轮无意图 → null（全量探测，且全量列表按名排序固定 → 缓存可命中）
    /// </summary>
    private string[] BuildToolSubsetForRound()
    {
        // 首轮有意图：按意图过滤（原有逻辑）
        if (_toolRound == 0 && !string.IsNullOrEmpty(_lastIntent)
            && IntentToolMap.TryGetValue(_lastIntent, out var allowed))
        {
            return allowed;
        }

        // 首轮无意图：null = 全量
        if (_toolRound == 0)
        {
            return null;
        }

        // 后续回环：已用工具 + 意图候选 + 核心工具
        var names = new HashSet<string>(CoreToolSubset);

        foreach (var e in _history)
        {
            if (e.role == "tool" && !string.IsNullOrEmpty(e.name))
                names.Add(e.name);
        }
        if (!string.IsNullOrEmpty(_lastIntent) && IntentToolMap.TryGetValue(_lastIntent, out var allowed2))
        {
            foreach (var n in allowed2) names.Add(n);
        }

        // 只保留已注册工具，避免 schema 引用不存在的工具
        var result = new List<string>();
        foreach (var n in names)
        {
            if (ToolRegistry.HasTool(n)) result.Add(n);
        }
        return result.ToArray();
    }

    // ==================================================================
    //  响应解析
    // ==================================================================

    /// <summary>提取 tool_calls JSON 块</summary>
    private string ExtractToolCalls(string json)
    {
        try
        {
            var root = JObject.Parse(json);
            var choices = root["choices"] as JArray;
            if (choices == null || choices.Count == 0) return "[]";
            var delta = choices[0]["delta"];
            var calls = delta?["tool_calls"] as JArray;
            if (calls == null || calls.Count == 0) return "[]";
            return calls.ToString(Newtonsoft.Json.Formatting.None);
        }
        catch
        {
            return "[]";
        }
    }

    private struct ToolCallInfo
    {
        public string id;
        public string name;
        public string arguments;
    }

    private List<ToolCallInfo> ParseToolCalls(string callsJson)
    {
        var list = new List<ToolCallInfo>();
        try
        {
            JArray arr = JArray.Parse(callsJson);
            foreach (var item in arr)
            {
                var tc = new ToolCallInfo
                {
                    id = item["id"]?.ToString() ?? "",
                    name = item["function"]?["name"]?.ToString() ?? "",
                    arguments = item["function"]?["arguments"]?.ToString() ?? ""
                };
                list.Add(tc);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ChatManager] ⚠ ParseToolCalls 失败: {ex.Message}");
        }
        return list;
    }

    // ==================================================================
    //  长期记忆记录
    // ==================================================================

    /// <summary>根据工具调用自动生成长期记忆</summary>
    private void RecordMemoryForTool(string toolName, string args, string result)
    {
        if (PetMemory.Instance == null) return;

        // 只记录有意义的结果，跳过空/错误结果
        if (string.IsNullOrEmpty(result) || result.StartsWith("❌") || result == "法阵未就绪")
            return;

        string summary = "";
        string topic = "";

        switch (toolName)
        {
            case "get_weather":
                // 截取简短天气信息
                topic = "天气";
                if (result.Length > 80) summary = "主人查询了天气: " + result.Substring(0, 80) + "…";
                else summary = "主人查询了天气: " + result;
                break;

            case "query_exams":
                topic = "考试";
                summary = "主人查询了考试安排";
                break;

            case "query_scores":
                topic = "成绩";
                summary = "主人查询了成绩";
                break;

            case "query_schedule":
                topic = "课表";
                summary = "主人查询了课表";
                break;

            case "search_files":
                topic = "文件搜索";
                // 提取搜索关键词
                string keyword = ExtractSearchKeyword(args);
                summary = $"主人搜了文件: 「{keyword}」";
                break;

            case "set_reminder":
                topic = "提醒";
                summary = "主人设置了提醒";
                break;

            case "search":
            case "open_url":
                topic = "搜索";
                string searchQ = ExtractSearchKeyword(args);
                summary = $"主人查询了: 「{searchQ}」";
                break;

            case "take_screenshot":
                topic = "截屏";
                summary = "本座动用了法眼摄形之术，窥见了主人的屏幕";
                break;

            case "openclaw_search":
                topic = "通神算术式";
                {
                    string q = ExtractSearchKeyword(args);
                    summary = $"主人启动了太卜通神算术式，推演了: 「{q}」";
                }
                break;

            default:
                // 其他工具只记录名称
                if (result.Length > 60)
                    summary = $"使用了 {toolName}";
                break;
        }

        if (!string.IsNullOrEmpty(summary) && !IsTestMode)
        {
            PetMemory.Instance.AddMemory(summary, topic, "tool");
        }
    }

    // ==================================================================
    //  🔄 功能2：离线回退协程
    // ==================================================================

    /// <summary>
    /// 当 DeepSeek API 不可用时，用本地模型生成回复
    /// </summary>
    private IEnumerator OfflineFallbackCoroutine(Action<bool> onHandled)
    {
        if (LocalLLMAgentService.Instance == null || !LocalLLMAgentService.Instance.CanProcess)
        {
            onHandled?.Invoke(false);
            yield break;
        }

        // 获取角色设定（仅性格人设，不包含工具定义）
        string characterDesc = _systemPromptTemplate;
        if (characterDesc.Length > 200)
            characterDesc = characterDesc.Substring(0, 200) + "…";

        // 收集最近几轮对话作为上下文
        var recentEntries = new List<string>();
        for (int i = Math.Max(0, _history.Count - 6); i < _history.Count; i++)
        {
            var e = _history[i];
            if (e.role == "user" || e.role == "assistant")
            {
                recentEntries.Add($"{e.role}: {e.content}");
            }
        }
        string recentHistory = string.Join("\n", recentEntries);

        // 取用户最后一条消息
        string userMessage = "";
        for (int i = _history.Count - 1; i >= 0; i--)
        {
            if (_history[i].role == "user")
            {
                userMessage = _history[i].content;
                break;
            }
        }
        if (string.IsNullOrEmpty(userMessage))
        {
            onHandled?.Invoke(false);
            yield break;
        }

        bool fallbackSuccess = false;
        string fallbackReply = "";

        // 等待本地模型生成回复（通过队列，不阻塞太久）
        float timeout = 15f;
        float startTime = Time.time;
        bool gotResult = false;

        LocalLLMAgentService.Instance.GenerateFallbackReply(characterDesc, recentHistory, userMessage, (ok, reply) =>
        {
            gotResult = true;
            fallbackSuccess = ok;
            fallbackReply = reply;
        });

        // 等待结果或超时
        while (!gotResult)
        {
            if (Time.time - startTime > timeout)
            {
                Debug.LogWarning("[ChatManager] ⏰ 离线回退超时");
                break;
            }
            yield return new WaitForSeconds(0.1f);
        }

        if (!fallbackSuccess || string.IsNullOrEmpty(fallbackReply))
        {
            Debug.LogWarning("[ChatManager] 离线回退未能生成有效回复");
            onHandled?.Invoke(false);
            yield break;
        }

        // 成功：将本地回复加入历史
        _history.Add(new Entry { role = "assistant", content = fallbackReply });
        // ★ 清洗后再赋值，避免 markdown/表情标记泄漏到 UI
        _lastReply = CleanDisplayText(fallbackReply);
        _fullReplyText = _lastReply;

        // 触发显示（使用流式路径的显示机制）
        OnNewReply?.Invoke(_lastReply);
        StartSentenceQueue(fallbackReply);

        // 记录记忆
        RecordConversationMemory(fallbackReply);

        Debug.Log($"[ChatManager] 🔄 离线回退成功（{fallbackReply.Length} 字）");
        onHandled?.Invoke(true);
    }

    // ==================================================================
    //  对话记忆记录 & 摘要
    // ==================================================================

    private int _conversationSinceSummary = 0;
    private int _conversationSinceLocalExtract = 0;
    private const int SUMMARY_INTERVAL = 15; // 每 15 次对话更新摘要
    private const int LOCAL_EXTRACT_INTERVAL = 5; // 每 5 次对话尝试本地模型提取记忆

    /// <summary>记录纯文字回复到长期记忆（按重要性过滤）</summary>
    private void RecordConversationMemory(string reply)
    {
        if (IsTestMode) return; // 测试模式：不写任何对话记忆/摘要/提取
        if (PetMemory.Instance == null || string.IsNullOrEmpty(reply)) return;

        _conversationSinceSummary++;

        // 取用户最后一条消息
        var lastUserMsg = "";
        for (int i = _history.Count - 1; i >= 0; i--)
        {
            if (_history[i].role == "user")
            {
                lastUserMsg = _history[i].content;
                break;
            }
        }

        // ——— 记录用户的重要话题 ———
        if (!string.IsNullOrEmpty(lastUserMsg))
        {
            string[] importantMarkers = { "我叫", "我是", "喜欢", "讨厌", "我的", "我在",
                "工作", "考试", "学习", "毕业", "生日" };
            if (importantMarkers.Any(m => lastUserMsg.Contains(m)))
            {
                string brief = lastUserMsg.Length > 40
                    ? lastUserMsg.Substring(0, 40) + "…"
                    : lastUserMsg;
                PetMemory.Instance.AddMemory($"主人提及: 「{brief}」", "对话", "conversation");
            }
            else if (UnityEngine.Random.value < 0.15f)
            {
                // 15% 概率记录日常闲聊，丰富记忆
                string brief = lastUserMsg.Length > 30
                    ? lastUserMsg.Substring(0, 30) + "…"
                    : lastUserMsg;
                PetMemory.Instance.AddMemory($"和主人聊到了: 「{brief}」", "闲聊", "conversation");
            }
        }

        // ——— 到达摘要间隔时，自动更新近日印象 ———
        if (_conversationSinceSummary >= SUMMARY_INTERVAL)
        {
            _conversationSinceSummary = 0;

            // 📝 功能3：对话压缩 — 用本地模型智能摘要（如可用）
            // ★ C2 修复：回退逻辑移入回调内部 —— 原实现里同步回退总会抢先覆盖
            //   异步智能摘要（bool 在回调返回前就读），导致智能摘要永远不生效
            Action fallbackSummary = () =>
            {
                var userMessages = _history.Where(e => e.role == "user").ToList();
                int skip = Math.Max(0, userMessages.Count - 10);
                var recentTopics = userMessages.Skip(skip).Select(e => e.content).ToList();

                if (recentTopics.Count > 0)
                {
                    string combined = string.Join(" | ", recentTopics);
                    string summary = combined.Length > 100
                        ? combined.Substring(0, 100) + "…"
                        : combined;
                    PetMemory.Instance.UpdateConversationSummary(
                        $"近日与主人谈论了: {summary}");
                }
            };

            if (LocalLLMAgentService.Instance != null && LocalLLMAgentService.Instance.CanProcess && !string.IsNullOrEmpty(lastUserMsg))
            {
                LocalLLMAgentService.Instance.SummarizeConversation(lastUserMsg, (ok, summary) =>
                {
                    if (ok && !string.IsNullOrEmpty(summary) && summary.Length > 5)
                    {
                        PetMemory.Instance.UpdateConversationSummary(
                            $"近日与主人谈论了: {summary}");
                        Debug.Log($"[ChatManager] 📝 本地灵识摘要: {summary}");
                    }
                    else
                    {
                        fallbackSummary(); // 智能摘要失败/不可用时才回退截断法
                    }
                });
            }
            else
            {
                fallbackSummary(); // 本地模型不可用 → 直接回退
            }
        }

        // 💾 功能4：本地模型提取记忆（每 5 次对话尝试一次）
        _conversationSinceLocalExtract++;
        if (_conversationSinceLocalExtract >= LOCAL_EXTRACT_INTERVAL && !string.IsNullOrEmpty(lastUserMsg))
        {
            _conversationSinceLocalExtract = 0;
            if (LocalLLMAgentService.Instance != null && LocalLLMAgentService.Instance.CanProcess)
            {
                LocalLLMAgentService.Instance.ExtractMemory(lastUserMsg, extract =>
                {
                    if (extract.shouldRemember && !string.IsNullOrEmpty(extract.summary))
                    {
                        PetMemory.Instance.AddMemoryWithImportance(
                            $"主人提及: 「{extract.summary}」",
                            extract.topic,
                            "conversation",
                            extract.importance);
                        Debug.Log($"[ChatManager] 💾 本地灵识提取记忆: [{extract.topic}] {extract.summary} (重要度:{extract.importance})");
                    }
                });
            }
        }
    }

    /// <summary>从 tool 参数 JSON 中提取 query/keyword 字段</summary>
    private static string ExtractSearchKeyword(string args)
    {
        if (string.IsNullOrEmpty(args)) return "未知";
        try
        {
            var obj = JObject.Parse(args);
            string q = obj["query"]?.ToString() ?? obj["keyword"]?.ToString() ?? "";
            return string.IsNullOrEmpty(q) ? "未知" : q;
        }
        catch
        {
            return "未知";
        }
    }

    // ==================================================================
    //  记忆反思（后台调 DeepSeek 提炼高层次洞察）
    // ==================================================================

    /// <summary>反思协程：将候选记忆发给 DeepSeek 做高层提炼</summary>
    private IEnumerator DoReflection(List<PetMemory.MemoryEntry> candidates)
    {
        string prompt = PetMemory.Instance.BuildReflectionPrompt(candidates);
        string reply = null;

        // 使用 DeepSeek API（不占对话历史，纯粹后台调用）
        var reqBody = new JObject
        {
            ["model"] = model,
            ["messages"] = new JArray
            {
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = prompt
                }
            },
            ["stream"] = false
        };
        string jsonBody = reqBody.ToString(Newtonsoft.Json.Formatting.None);

        yield return StartCoroutine(
            ApiClient.PostRequest(apiUrl, apiKey, jsonBody, 30,
                json => reply = json,
                err => { }));

        if (string.IsNullOrEmpty(reply)) yield break;

        // 从响应 JSON 中提取 content
        string reflectionContent = ApiClient.ExtractContent(reply);
        if (string.IsNullOrEmpty(reflectionContent)) yield break;

        // 逐行写入 reflection 记忆
        string[] lines = reflectionContent.Split(new[] { '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 5)
            {
                PetMemory.Instance.CommitReflection(trimmed);
            }
        }

        Debug.Log($"[ChatManager] 🧠 记忆反思完成，产生 {lines.Length} 条洞察");
    }

    // ==================================================================
    //  ★ 人格演化记录
    // ==================================================================

    /// <summary>
    /// 记录本轮交互到 PersonalityManager，触发人格微调
    /// </summary>
    private void RecordPersonalityInteraction()
    {
        if (IsTestMode) return; // 测试模式：不写人格演化
        if (PersonalityManager.Instance == null) return;

        // 取用户最后一条消息
        string userMsg = "";
        for (int i = _history.Count - 1; i >= 0; i--)
        {
            if (_history[i].role == "user")
            {
                userMsg = _history[i].content;
                break;
            }
        }
        if (string.IsNullOrEmpty(userMsg)) return;

        // 取当前活动分类（从 ActivityTracker 获取当前窗口信息）
        string activity = "";
        if (activityTracker != null)
        {
            string proc = activityTracker.CurrentProcessName;
            if (!string.IsNullOrEmpty(proc))
            {
                // 根据进程名映射到活动分类
                string p = proc.ToLower();
                if (p.Contains("code") || p.Contains("devenv") || p.Contains("vim")) activity = "coding";
                else if (p.Contains("chrome") || p.Contains("msedge") || p.Contains("firefox")) activity = "browsing";
                else if (p.Contains("unity") || p.Contains("blender")) activity = "creative";
                else if (p.Contains("spotify") || p.Contains("wmplayer")) activity = "music";
                else activity = "other";
            }
        }

        // 收集本轮工具调用结果
        var toolResults = new List<string>();
        for (int i = _history.Count - 1; i >= 0; i--)
        {
            if (_history[i].role == "tool")
                toolResults.Add(_history[i].content);
            else if (_history[i].role == "user" && i != _history.Count - 1)
                break; // 越过本轮的 user → 本轮的最后一个 tool
        }

        PersonalityManager.Instance.RecordInteraction(
            userMsg, _lastReply, activity, toolResults);

        // 提取关键词作为知识库搜索话题
        _lastConversationTopic = ExtractSearchTopic(userMsg);
    }

    // ==================================================================
    //  ★ 知识库后台检索
    // ==================================================================

    /// <summary>
    /// 后台检索知识库，将结果缓存到 _cachedKnowledgeContext 供下次 SystemPrompt 注入
    /// </summary>
    private IEnumerator BackgroundKnowledgeSearch()
    {
        var kb = KnowledgeBaseManager.Instance;
        if (kb == null || kb.DocumentCount == 0) yield break;
        if (string.IsNullOrEmpty(_lastConversationTopic)) yield break;

        // 搜索前先清除旧缓存，避免搜索失败时仍使用过时结果
        _cachedKnowledgeContext = "";

        string result = "";
        yield return kb.SearchAndFormat(_lastConversationTopic, kb.maxContextResults, r => result = r);

        if (!string.IsNullOrEmpty(result))
        {
            _cachedKnowledgeContext = result;
            Debug.Log($"[ChatManager] 藏书阁检索完毕，结果 {result.Length} 字符");
        }
    }

    /// <summary>
    /// 从用户消息中提取检索关键词（去掉常见无意义词后取前 50 字）
    /// </summary>
    private string ExtractSearchTopic(string userMsg)
    {
        if (string.IsNullOrEmpty(userMsg)) return "";

        // 去掉常见的非信息性问句前缀
        var noise = new[] { "帮我", "请问", "能不能", "看一下", "查一下", "搜一下", "看看", "我想", "我要", "你知不知道", "你了解", "你记得" };
        string cleaned = userMsg;
        foreach (var n in noise)
        {
            cleaned = Regex.Replace(cleaned, "^" + Regex.Escape(n), "", RegexOptions.IgnoreCase);
        }

        cleaned = cleaned.Trim().Trim('？', '?', '！', '!', '，', ',', '。', '.', '、');
        if (cleaned.Length > 50) cleaned = cleaned.Substring(0, 50);

        return cleaned;
    }

    // ==================================================================
    //  ★ 内嵌动作标记解析 — 「言出法随」
    // ==================================================================

    /// <summary>
    /// 清洗 AI 回复文本用于 UI 显示：剥离 markdown 语法与残留的
    /// 内嵌表情/动作标记（纯文本清理，不执行任何 Live2D 动作）。
    /// 供气泡、聊天面板（RightPanel）等所有显示层统一调用。
    ///
    /// 处理内容：
    ///   1) markdown 语法：**粗体**、*斜体*、`行内代码`、```代码块```、
    ///      # 标题、- 列表、数字列表、&gt; 引用、下划线、删除线
    ///   2) 内嵌标记残留：【表情:xxx】【动作:xxx】（自然描述）
    /// </summary>
    public static string CleanDisplayText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        string result = text;

        // 0) 颜文字兜底清理（所有调用路径都无颜文字残留）
        result = StripKaomojiText(result);

        // 1) 剥离内嵌标记（与 StripAndExecuteActions 的正则一致，但不执行动作）
        result = Regex.Replace(result, @"【表情[:：]([^】]+)】", "");
        result = Regex.Replace(result, @"【动作[:：]([^】]+)】", "");
        result = Regex.Replace(result, @"（([^）]+)）", "");

        // 2) 剥离 markdown 语法
        // 2.1 代码块 ```...```（整块移除，多行）
        result = Regex.Replace(result, @"```[\s\S]*?```", "");
        // 2.2 行内代码 `code`
        result = Regex.Replace(result, @"`([^`]+)`", "$1");
        // 2.3 粗体/斜体/删除线/下划线（**x**、__x__、~~x~~、*x*、_x_）
        result = Regex.Replace(result, @"\*\*([^*]+)\*\*", "$1");
        result = Regex.Replace(result, @"__([^_]+)__", "$1");
        result = Regex.Replace(result, @"~~([^~]+)~~", "$1");
        result = Regex.Replace(result, @"\*([^*]+)\*", "$1");
        result = Regex.Replace(result, @"_([^_]+)_", "$1");
        // 2.4 行首标题 #、##、###…
        result = Regex.Replace(result, @"(?m)^\s*#{1,6}\s*", "");
        // 2.5 行首列表符号 -、*、+、数字.（转 - 号可能和列表冲突，先处理无序列表）
        result = Regex.Replace(result, @"(?m)^\s*[-*+]\s+", "");
        result = Regex.Replace(result, @"(?m)^\s*\d+[\.、]\s*", "");
        // 2.6 行首引用 >
        result = Regex.Replace(result, @"(?m)^\s*&gt;\s*", "");
        result = Regex.Replace(result, @"(?m)^\s*>\s*", "");

        // 3) 收尾清理：合并多余空白行、去掉首尾空白
        result = Regex.Replace(result, @"[ \t]{2,}", " ");
        result = Regex.Replace(result, @"\n{3,}", "\n\n");
        return result.Trim();
    }

    /// <summary>
    /// 剥离 AI 回复中的内嵌动作/表情标记，同步执行对应的 Live2D 动作。
    /// 这样 AI 可以在话语中自然夹带动作，气泡只显示纯净对话。
    ///
    /// 支持的标记格式：
    ///   【表情:开心】    → PlayExpression("happy")
    ///   【动作:伸懒腰】   → PlayAction("stretch")
    ///   （自然动作描述）  → 尝试匹配已知动作/表情，未知则忽略
    /// </summary>
    private string StripAndExecuteActions(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var renderer = FindObjectOfType<Live2DRenderer>();
        if (renderer == null) return CleanDisplayText(text); // 无渲染器：仅做纯文本清理

        string result = text;

        // 0) 颜文字兜底：模型未守规输出颜文字 → 翻译为表情动作并从文本移除
        result = StripKaomoji(result);

        // 1) 【表情:xxx】— 精确表情标记
        result = Regex.Replace(result, @"【表情[:：]([^】]+)】", match =>
        {
            string expName = match.Groups[1].Value.Trim();
            string mapped = MapExpName(expName);
            renderer.PlayExpression(mapped);
            OnExpressionTag?.Invoke(mapped); // ★ 广播表情，供 UI 显示符号徽章
            Debug.Log($"[ChatManager] 🎭 言出法随·表情: {expName} → {mapped}");
            return ""; // 从文本中移除
        });

        // 2) 【动作:xxx】— 精确动作标记
        result = Regex.Replace(result, @"【动作[:：]([^】]+)】", match =>
        {
            string actName = match.Groups[1].Value.Trim();
            string mapped = MapActionName(actName);
            renderer.ForceAction("act:" + mapped);
            Debug.Log($"[ChatManager] 🏃 言出法随·动作: {actName} → {mapped}");
            return "";
        });

        // 3) （自然描述）— 自动匹配已知表情/动作名
        result = Regex.Replace(result, @"（([^）]+)）", match =>
        {
            string desc = match.Groups[1].Value.Trim();
            string mapped = TryMatchKnown(desc);
            if (mapped != null)
            {
                if (mapped.StartsWith("exp:"))
                {
                    renderer.PlayExpression(mapped.Substring(4));
                    OnExpressionTag?.Invoke(mapped.Substring(4)); // ★ 广播表情
                    Debug.Log($"[ChatManager] 🎭 言出法随·自然表情: {desc} → {mapped}");
                }
                else
                {
                    renderer.ForceAction("act:" + mapped);
                    Debug.Log($"[ChatManager] 🏃 言出法随·自然动作: {desc} → {mapped}");
                }
            }
            return ""; // 无论如何都从文本中移除
        });

        // ★ 最终统一清洗：剥离 markdown 语法（**粗体**、`代码`、# 标题等）
        return CleanDisplayText(result);
    }

    /// <summary>中文/模糊表情名 → 标准英文名</summary>
    private static string MapExpName(string cn)
    {
        switch (cn)
        {
            case "开心": case "高兴": case "微笑": case "笑": return "happy";
            case "伤心": case "悲伤": case "难过": case "哭": return "sad";
            case "生气": case "愤怒": case "怒": return "angry";
            case "惊讶": case "吃惊": case "震惊": case "吓": return "surprise";
            case "困": case "困倦": case "疲劳": case "累": return "sleepy";
            case "害羞": case "羞涩": case "脸红": return "blush";
            case "困惑": case "疑惑": case "迷茫": case "不解": return "confused";
            case "爱": case "爱心": case "喜欢": case "心动": return "love";
            case "哭腔": case "泪目": case "含泪": return "tear";
            case "平静": case "无表情": case "默认": return "neutral";
            default: return cn; // 原样传给 PlayExpression
        }
    }

    /// <summary>中文/模糊动作名 → 标准英文名</summary>
    private static string MapActionName(string cn)
    {
        switch (cn)
        {
            case "伸懒腰": case "舒展": case "懒腰": return "stretch";
            case "哭": case "捂脸哭": case "掩面": return "cry";
            case "困惑": case "歪头": case "歪头困惑": return "confuse";
            case "比心": case "爱心": case "心": return "heart_eyes";
            case "数钱": case "财迷": case "算账": return "money";
            case "捧脸": case "捧脸羞": case "害羞捧脸": return "blush";
            case "法阵": case "画阵": case "绘制法阵": case "施法": return "magic_circle";
            default: return cn;
        }
    }

    // ==================================================================
    //  颜文字兜底：模型未守规输出颜文字时，翻译为表情动作并从文本移除
    //  （Live2D 脸部表情 + 像素画表情帧，正是用户想要的接收表情信息后的表现）
    // ==================================================================

    /// <summary>常见颜文字 → 表情名（顺序敏感：love 的 ♡ 需先于 happy 的 (´▽｀)）</summary>
    private static readonly (string[] patterns, string emote)[] KAOMOJI_MAP = new (string[], string)[]
    {
        (new[] { "♡", "♥", "❤", "(´▽｀)♡" }, "love"),
        (new[] { "(T_T)", "(T-T)", "(;_;)", "(;﹏;)", "QAQ", "ToT", "TAT" }, "sad"),
        (new[] { "(T﹏T)", "(´;ω;`)", "(;ω;)" }, "tear"),
        (new[] { "(^▽^)", "(^_^)", "(^-^)", "(≧▽≦)", "(*^▽^*)", "(＾▽＾)", "(≧ω≦)", "(◕‿◕)" }, "happy"),
        (new[] { "(╬▔皿▔)", "(>_<)", "(｀⌒´)", "(╬￣皿￣)", "(σ-`д´σ)" }, "angry"),
        (new[] { "(⊙o⊙)", "(o_O)", "(°Д°)", "(°口°)" }, "surprise"),
        (new[] { "(・_・?)", "(?_?)" }, "confused"),
        (new[] { "(´-ω-`)", "(￣o￣)", "(´～｀)" }, "sleepy"),
        (new[] { "(⁄ ⁄•⁄ω⁄•⁄ ⁄)", "(//▽//)", "(〃∀〃)" }, "blush"),
    };

    /// <summary>从文本中移除所有颜文字（纯清理，不触发表情；供 CleanDisplayText 兜底）</summary>
    private static string StripKaomojiText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string result = text;
        foreach (var (patterns, _) in KAOMOJI_MAP)
        {
            foreach (var p in patterns)
                result = result.Replace(p, "");
        }
        return result;
    }

    /// <summary>
    /// 颜文字 → 表情动作（带触发）：模型输出颜文字时，
    /// 执行 Live2D 脸部表情 PlayExpression + 广播 OnExpressionTag（像素画表情帧/徽章），
    /// 再把颜文字从文本中移除，气泡只显示纯净话语。
    /// </summary>
    private string StripKaomoji(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var renderer = FindObjectOfType<Live2DRenderer>();
        string result = text;
        foreach (var (patterns, emote) in KAOMOJI_MAP)
        {
            foreach (var p in patterns)
            {
                string cleaned = result.Replace(p, "");
                if (cleaned.Length != result.Length)
                {
                    if (renderer != null) renderer.PlayExpression(emote);
                    OnExpressionTag?.Invoke(emote); // ★ 广播表情：像素画表情帧 + 徽章
                    Debug.Log($"[ChatManager] 🎭 颜文字兜底: {p} → {emote}");
                    result = cleaned;
                }
            }
        }
        return result;
    }

    /// <summary>尝试将自然语言描述匹配到已知表达式或动作</summary>
    private static string TryMatchKnown(string desc)
    {
        // 先查表情
        string exp = MapExpName(desc);
        if (exp != desc) return "exp:" + exp;

        // 再查动作
        string act = MapActionName(desc);
        if (act != desc) return act;

        // 模糊关键词匹配
        if (desc.Contains("微笑") || desc.Contains("笑")) return "exp:happy";
        if (desc.Contains("怒") || desc.Contains("气")) return "exp:angry";
        if (desc.Contains("惊") || desc.Contains("吓")) return "exp:surprise";
        if (desc.Contains("羞") || desc.Contains("脸红")) return "exp:blush";
        if (desc.Contains("困") || desc.Contains("哈欠")) return "exp:sleepy";
        if (desc.Contains("哭") || desc.Contains("泪")) return "exp:sad";

        return null; // 未匹配
    }

    // ==================================================================
    //  工具
    // ==================================================================

    /// <summary>裁剪旧历史：双重策略（条数上限 + 字符预算），被裁部分走本地摘要</summary>
    private void TrimHistory()
    {
        // ── 策略 1：条数上限（保留最近 N 条，防止 token 无限增长）──
        if (_history.Count > MAX_HISTORY_ENTRIES)
        {
            int removeCount = _history.Count - MAX_HISTORY_ENTRIES;
            _history.RemoveRange(0, removeCount);
            Debug.Log($"[ChatManager] ✂️ 历史已达上限({MAX_HISTORY_ENTRIES})，裁剪了 {removeCount} 条旧消息");
        }

        // ── 策略 2：字符预算（超出 HISTORY_CHAR_BUDGET 时裁掉最旧的并摘要）──
        // 遍历统计累计字符数；超过预算时，将「之前的所有完整轮次」裁掉。
        // 安全边界：裁剪点对齐到最近一个 user 消息（保留区从 user 开始），
        // 避免切断 assistant tool_calls ↔ tool 结果配对导致 API 400。
        int totalChars = 0;
        int cutEnd = 0; // 安全裁剪终点（此索引之前全部移除），0 = 不裁
        for (int i = 0; i < _history.Count; i++)
        {
            var e = _history[i];
            totalChars += (e.content?.Length ?? 0) + (e.toolCallsJson?.Length ?? 0);
            if (totalChars > HISTORY_CHAR_BUDGET)
            {
                cutEnd = i;
                break;
            }
        }

        if (cutEnd > 1)
        {
            // 向前对齐到最近的 user 消息：保留区从 user 开始，其之前的 tool 链完整留在被裁部分
            int safe = cutEnd;
            while (safe > 0 && _history[safe - 1].role != "user") safe--;
            // 极端情况（裁剪点附近全是工具链、没有 user）：对齐到最近 assistant 之后
            if (safe == 0)
            {
                safe = cutEnd;
                while (safe > 0 && _history[safe - 1].role != "assistant") safe--;
            }
            cutEnd = safe;

            if (cutEnd > 1)
            {
                // 收集被裁的用户消息做摘要（只取 user，避免重复/过长）
                var trimmedTexts = new List<string>();
                for (int i = 0; i < cutEnd; i++)
                {
                    if (_history[i].role == "user")
                        trimmedTexts.Add(_history[i].content ?? "");
                }

                _history.RemoveRange(0, cutEnd);
                Debug.Log($"[ChatManager] ✂️ 历史超字符预算({HISTORY_CHAR_BUDGET})，裁剪 {cutEnd} 条旧消息");

                // 异步本地摘要（免费 Ollama），更新缓存；失败则回退为简单截断
                SummarizeTrimmedHistory(trimmedTexts);
            }
        }
    }

    /// <summary>用本地模型（如可用）把被裁剪的旧历史压成一句摘要，失败则简单截断</summary>
    private void SummarizeTrimmedHistory(List<string> trimmedTexts)
    {
        if (trimmedTexts == null || trimmedTexts.Count == 0) return;

        // 只取最近几条，控制摘要输入体积
        int take = Math.Min(trimmedTexts.Count, 8);
        var recent = trimmedTexts.GetRange(trimmedTexts.Count - take, take);
        string text = string.Join(" | ", recent);
        if (text.Length > 600) text = text.Substring(text.Length - 600);

        if (LocalLLMAgentService.Instance != null && LocalLLMAgentService.Instance.CanProcess)
        {
            LocalLLMAgentService.Instance.SummarizeConversation(text, (ok, summary) =>
            {
                if (ok && !string.IsNullOrEmpty(summary))
                {
                    _historySummary = summary;
                    Debug.Log($"[ChatManager] 📜 旧史摘要: {summary}");
                }
            });
        }
        else
        {
            // 回退：简单截断
            _historySummary = text.Length > 80 ? text.Substring(0, 80) + "…" : text;
            Debug.Log($"[ChatManager] 📜 旧史摘要(本地截断): {_historySummary}");
        }
    }

    /// <summary>清空对话历史</summary>
    public void ClearHistory()
    {
        _history.Clear();
        _lastReply = "";
        _lastError = "";
    }
}
