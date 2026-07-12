using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
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
    public string model = "deepseek-chat";

    [Header("工具调用（符玄法阵）")]
    public ToolCallInvoker toolInvoker;
    public bool enableTools = true;

    // ==================================================================
    //  角色设定 — 符玄 + 法阵能力（从 Resources/SystemPrompt.txt 加载）
    // ==================================================================
    private string _systemPromptTemplate;
    /// <summary>法眼 — 行为追踪器</summary>
    public ActivityTracker activityTracker;

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

        // ——— 注册反思回调：当 PetMemory 需要反思时，由我们调 LLM ———
        PetMemory.Instance.OnReflectRequest = candidates =>
        {
            // 同步方式不支持回调，改为协程触发
            return null;
        };
    }

    /// <summary>构建最终 SystemPrompt（注入时间 + 长期记忆 + 行为观测）</summary>
    private string BuildSystemPrompt()
    {
        string prompt = _systemPromptTemplate;
        prompt = prompt.Replace("{current_time}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

        // 注入长期记忆
        if (PetMemory.Instance != null)
        {
            string memories = PetMemory.Instance.GetFormattedMemories();
            if (!string.IsNullOrEmpty(memories))
                prompt += "\n" + memories;
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

        // ★ 注入演武心经经验（过往最佳动作参数参考）
        if (MotionMemoryManager.Instance != null)
        {
            string motionMemories = MotionMemoryManager.Instance.GetFormattedMemories();
            if (!string.IsNullOrEmpty(motionMemories))
                prompt += "\n" + motionMemories;
        }

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
• 本座的演武心经会越练越精——好的动作保留，差的不污染经验！"; 
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

    // ---- 请求看门狗：防止 API 卡死永久锁住 _isWaiting ----
    private float _requestStartTime = 0f;
    private const float REQUEST_TIMEOUT = 600f; // 600 秒（10分钟）总超时，覆盖多轮工具链（含 GLM-4V 180s）

    // ---- 中止标志：看门狗超时时通知协程尽快退出 ----
    private bool _abortRequested = false;

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
            // 排队，等当前回复完自动发
            _messageQueue.Enqueue((text.Trim(), onUpdate));
            return;
        }

        _history.Add(new Entry { role = "user", content = text.Trim() });
        _isWaiting = true;
        _lastReply = "";
        _lastError = "";
        _abortRequested = false; // 重置中止标志，允许新的请求
        _requestStartTime = Time.time; // 启动看门狗计时
        _onUpdate = onUpdate;

        // 触发"AI 开始处理"事件（悬浮球显示"思考中…"）
        OnRequestStarted?.Invoke();

        StartCoroutine(SendRequestCoroutine());
    }

    // ==================================================================
    //  核心：API 请求循环（支持多次 tool_call 回环）
    // ==================================================================

    private const int MAX_TOOL_ROUNDS = 5; // 防止无限循环

    private IEnumerator SendRequestCoroutine()
    {
        yield return StartCoroutine(DoToolLoop());

        _isWaiting = false;
        _requestStartTime = 0f; // 请求完成，停止看门狗
        _onUpdate?.Invoke();

        // ——— 处理队列中的下一条消息 ———
        if (_messageQueue.Count > 0)
        {
            var next = _messageQueue.Dequeue();
            SendMessage(next.text, next.onUpdate);
        }

        // ——— 检查是否需要记忆反思（不阻塞对话流）———
        if (PetMemory.Instance != null)
        {
            var candidates = PetMemory.Instance.CheckReflection();
            if (candidates != null && candidates.Count >= 2)
            {
                StartCoroutine(DoReflection(candidates));
            }
        }
    }

    private IEnumerator DoToolLoop()
    {
        for (int round = 0; round <= MAX_TOOL_ROUNDS; round++)
        {
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
            _lastReply = fullContent ?? "[施法中……]";

            foreach (var call in calls)
            {
                OnToolCalled?.Invoke(call.name);
                Debug.Log($"[ChatManager] ⚡ 施法: {call.name}({call.arguments})");

                string result;
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

    /// <summary>从积累缓冲区中提取完整句子</summary>
    private void ExtractSentencesFromBuffer()
    {
        string buf = _streamBuf.ToString();
        for (int i = _streamLastSplit; i < buf.Length; i++)
        {
            if (ContainsAny(_sentenceSeps, buf[i]))
            {
                int len = i - _streamLastSplit + 1;
                string rawSentence = buf.Substring(_streamLastSplit, len).Trim();
                _streamLastSplit = i + 1;
                if (!string.IsNullOrEmpty(rawSentence))
                    AddStreamSentence(rawSentence);
            }
        }
    }

    /// <summary>将一条完整句子加入队列并触发显示</summary>
    private void AddStreamSentence(string rawSentence)
    {
        // ★ 剥离内嵌动作标记并执行
        string cleanSentence = StripAndExecuteActions(rawSentence);
        if (string.IsNullOrEmpty(cleanSentence) && StripAndExecuteActions(rawSentence) == "")
            cleanSentence = rawSentence; // 如果纯动作标记，保留原文（可能为空时忽略）

        if (string.IsNullOrWhiteSpace(cleanSentence)) return;

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
        StringBuilder sb = new StringBuilder();
        sb.Append("{\"model\":\"");
        sb.Append(EscapeJson(model));
        sb.Append("\",\"messages\":[");

        // system prompt
        string sysPrompt = BuildSystemPrompt();
        sb.Append("{\"role\":\"system\",\"content\":\"");
        sb.Append(EscapeJson(sysPrompt));
        sb.Append("\"}");

        // history
        for (int i = 0; i < _history.Count; i++)
        {
            var e = _history[i];
            if (!string.IsNullOrEmpty(sysPrompt)) sb.Append(",");

            sb.Append("{\"role\":\"");
            sb.Append(EscapeJson(e.role));
            sb.Append("\"");

            if (e.role == "tool")
            {
                // tool 角色需要 tool_call_id 和 name
                sb.Append(",\"tool_call_id\":\"");
                sb.Append(EscapeJson(e.tool_call_id ?? ""));
                sb.Append("\",\"name\":\"");
                sb.Append(EscapeJson(e.name ?? ""));
                sb.Append("\"");
            }
            else if (e.role == "assistant" && !string.IsNullOrEmpty(e.toolCallsJson))
            {
                // assistant 消息带 tool_calls 时，原样发回
                sb.Append(",\"tool_calls\":");
                sb.Append(e.toolCallsJson);
            }

            sb.Append(",\"content\":");
            // ★ DeepSeek API 要求：assistant 带 tool_calls 时 content 必须为 null
            if (e.role == "assistant" && !string.IsNullOrEmpty(e.toolCallsJson))
            {
                sb.Append("null");
            }
            else
            {
                sb.Append("\"");
                sb.Append(EscapeJson(e.content ?? ""));
                sb.Append("\"");
            }
            sb.Append("}");
        }

        sb.Append("]");

        // ——— 附加 tools 定义 ———
        if (enableTools && toolInvoker != null)
        {
            sb.Append(",\"tools\":");
            sb.Append(toolInvoker.GetToolsJson());
        }

        sb.Append(",\"stream\":true}");
        return sb.ToString();
    }

    // ==================================================================
    //  响应解析
    // ==================================================================

    /// <summary>提取 content 字段（普通回复） — 委托给 ApiClient</summary>
    private string ExtractContent(string json) => ApiClient.ExtractContent(json);

    /// <summary>提取 tool_calls JSON 块</summary>
    private string ExtractToolCalls(string json)
    {
        // 查找 "tool_calls":[{...}]
        string key = "\"tool_calls\":";
        int idx = json.IndexOf(key);
        if (idx < 0) return "[]";

        idx += key.Length;
        // 找到匹配的 ] 结束
        int depth = 1; // 当前已经是 [
        int start = idx;
        for (int i = idx + 1; i < json.Length; i++)
        {
            if (json[i] == '[') depth++;
            else if (json[i] == ']') { depth--; if (depth == 0) return json.Substring(start, i - start + 1); }
        }
        return "[]";
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

        // 简易解析: 依次找 id, function.name, function.arguments
        int pos = 0;
        while (true)
        {
            // 找下一个 "id":"...  （在同一 tool_call 对象内）
            int idIdx = callsJson.IndexOf("\"id\":\"", pos);
            if (idIdx < 0) break;

            string id = ExtractSimpleString(callsJson, idIdx + 6);

            int nameIdx = callsJson.IndexOf("\"name\":\"", idIdx);
            string name = nameIdx >= 0 ? ExtractSimpleString(callsJson, nameIdx + 8) : "";

            int argIdx = callsJson.IndexOf("\"arguments\":", idIdx);
            string args = "";
            if (argIdx >= 0)
            {
                argIdx += 12; // skip "\"arguments\":" 
                // arguments 可能是一个 JSON 对象字符串: "\"{...}\"" 或 JSON 对象 {...}
                if (argIdx < callsJson.Length && callsJson[argIdx] == '"')
                {
                    // 字符串: 提取并转义还原
                    args = ExtractSimpleString(callsJson, argIdx + 1);
                    args = args.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\\", "\\");
                }
                else
                {
                    // JSON 对象: 提取 {...} 块
                    int objStart = argIdx;
                    if (callsJson[objStart] == '{')
                    {
                        int d = 1;
                        for (int i = objStart + 1; i < callsJson.Length; i++)
                        {
                            if (callsJson[i] == '{') d++;
                            else if (callsJson[i] == '}') { d--; if (d == 0) { args = callsJson.Substring(objStart, i - objStart + 1); break; } }
                        }
                    }
                }
            }

            list.Add(new ToolCallInfo { id = id, name = name, arguments = args });
            pos = idIdx + 1;
        }

        return list;
    }

    /// <summary>从 JSON 中提取 "key":"value" 中 value 部分的纯字符串</summary>
    private static string ExtractSimpleString(string json, int start)
        => ApiClient.ExtractSimpleString(json, start);

    /// <summary>从错误 JSON 中提取 message 字段 — 委托给 ApiClient</summary>
    private string ExtractErrorMessage(string json) => ApiClient.ExtractErrorMessage(json);

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

            default:
                // 其他工具只记录名称
                if (result.Length > 60)
                    summary = $"使用了 {toolName}";
                break;
        }

        if (!string.IsNullOrEmpty(summary))
        {
            PetMemory.Instance.AddMemory(summary, topic, "tool");
        }
    }

    // ==================================================================
    //  对话记忆记录 & 摘要
    // ==================================================================

    private int _conversationSinceSummary = 0;
    private const int SUMMARY_INTERVAL = 15; // 每 15 次对话更新摘要

    /// <summary>记录纯文字回复到长期记忆（按重要性过滤）</summary>
    private void RecordConversationMemory(string reply)
    {
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
            // 收集近期话题（兼容 .NET Standard 2.0，不用 TakeLast）
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
        }
    }

    /// <summary>从 tool 参数 JSON 中提取 query/keyword 字段</summary>
    private static string ExtractSearchKeyword(string args)
    {
        if (string.IsNullOrEmpty(args)) return "未知";
        string q = "";
        // 尝试 "query":"..."
        int idx = args.IndexOf("\"query\":\"");
        if (idx >= 0)
        {
            idx += 9;
            for (int i = idx; i < args.Length; i++)
            {
                if (args[i] == '"') break;
                if (args[i] == '\\' && i + 1 < args.Length) { q += args[i + 1]; i++; }
                else q += args[i];
            }
        }
        else
        {
            // 尝试 "keyword":"..."
            idx = args.IndexOf("\"keyword\":\"");
            if (idx >= 0)
            {
                idx += 10;
                for (int i = idx; i < args.Length; i++)
                {
                    if (args[i] == '"') break;
                    if (args[i] == '\\' && i + 1 < args.Length) { q += args[i + 1]; i++; }
                    else q += args[i];
                }
            }
        }
        return string.IsNullOrEmpty(q) ? "未知" : q;
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
        string jsonBody = "{\"model\":\"" + ApiClient.EscapeJson(model)
            + "\",\"messages\":[{\"role\":\"user\",\"content\":\""
            + ApiClient.EscapeJson(prompt) + "\"}],\"stream\":false}";

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
    //  ★ 内嵌动作标记解析 — 「言出法随」
    // ==================================================================

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
        if (renderer == null) return text;

        string result = text;

        // 1) 【表情:xxx】— 精确表情标记
        result = Regex.Replace(result, @"【表情[:：]([^】]+)】", match =>
        {
            string expName = match.Groups[1].Value.Trim();
            string mapped = MapExpName(expName);
            renderer.PlayExpression(mapped);
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

        return result.Trim();
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

    /// <summary>JSON 转义 — 委托给 ApiClient</summary>
    private string EscapeJson(string s) => ApiClient.EscapeJson(s);

    /// <summary>清空对话历史</summary>
    public void ClearHistory()
    {
        _history.Clear();
        _lastReply = "";
        _lastError = "";
    }
}
