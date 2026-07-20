using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 本地 LLM 智能服务 — 为 ChatManager 提供 4 项离线能力：
///
/// 1. 🧠 意图/情绪分类 — 用户输入实时分类（闲聊/指令/知识/情感/操作）
/// 2. 🔄 离线回退回复 — DeepSeek API 不可用时本地模型替代
/// 3. 📝 对话压缩摘要 — 历史过长时智能压缩，替代简单截断
/// 4. 💾 记忆提取 — 从对话中提取重要信息存入忆境
///
/// 使用协程队列串行处理任务，避免并发冲突。
/// 依赖 LocalLLMClient 连接 Ollama（qwen2.5:3b）。
/// </summary>
public class LocalLLMAgentService : MonoBehaviour
{
    // ==================================================================
    //  单例
    // ==================================================================
    public static LocalLLMAgentService Instance { get; private set; }

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 设置模型为 3b（覆盖可能被 MotionAgent 改掉的值）
        LocalLLMClient.SetModel("qwen2.5:3b");
    }

    void Start()
    {
        StartCoroutine(LazyHealthCheck());
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>本地模型是否可用（就绪且未暂停）</summary>
    public bool CanProcess => LocalLLMClient.IsReady && !LocalLLMClient.Paused;

    /// <summary>延迟 3 秒后做健康检查，避免启动时并发</summary>
    private IEnumerator LazyHealthCheck()
    {
        yield return new WaitForSeconds(3f);
        if (!LocalLLMClient.IsReady)
        {
            yield return LocalLLMClient.CheckHealthAsync((ok, msg) => {
                Debug.Log($"[LocalLLMAgent] {msg}");
            });
        }
    }

    // ==================================================================
    //  协程任务队列 — 串行处理，避免并发
    // ==================================================================

    private readonly Queue<Func<IEnumerator>> _taskQueue = new Queue<Func<IEnumerator>>();
    private bool _isProcessing = false;

    /// <summary>将一个任务加入队列</summary>
    private void EnqueueTask(Func<IEnumerator> task)
    {
        if (!CanProcess)
        {
            Debug.LogWarning("[LocalLLMAgent] 本地模型不可用，跳过任务");
            return;
        }
        _taskQueue.Enqueue(task);
        if (!_isProcessing)
            StartCoroutine(ProcessQueue());
    }

    private IEnumerator ProcessQueue()
    {
        _isProcessing = true;
        while (_taskQueue.Count > 0)
        {
            var task = _taskQueue.Dequeue();
            yield return StartCoroutine(task());
        }
        _isProcessing = false;
    }

    // ──────────────────────────────────────────────────────────────────
    //  功能 1：意图/情绪分类
    // ──────────────────────────────────────────────────────────────────

    /// <summary>意图分类结果</summary>
    public struct IntentResult
    {
        public string intent;    // chat/command/knowledge/emotion/operation
        public string emotion;   // positive/neutral/negative/surprised/anxious
        public string brief;     // 一句话摘要
        public bool success;
    }

    /// <summary>
    /// 对用户输入进行意图和情绪分类（异步，结果通过回调返回）
    /// </summary>
    public void ClassifyIntent(string userMessage, Action<IntentResult> onResult)
    {
        if (string.IsNullOrEmpty(userMessage) || onResult == null) return;

        string systemPrompt = @"你是一个意图和情绪分类器。分析用户的输入，返回 JSON 格式结果，不要包含其他内容。

意图分类（intent）：
- chat — 闲聊、打招呼、日常对话
- command — 指令、请求执行操作（打开网页、搜索等）
- knowledge — 询问知识、信息查询
- emotion — 情感表达、倾诉、分享感受
- operation — 关于桌面宠物自身的操作（设置、控制等）

情绪标签（emotion）：positive / neutral / negative / surprised / anxious

JSON 格式：{""intent"": ""类型"", ""emotion"": ""情绪"", ""brief"": ""一句话摘要""}";

        EnqueueTask(() => ClassifyIntentCoroutine(userMessage, systemPrompt, onResult));
    }

    private IEnumerator ClassifyIntentCoroutine(string userMsg, string systemPrompt, Action<IntentResult> onResult)
    {
        IntentResult result = new IntentResult { success = false };

        yield return LocalLLMClient.PromptAsync(systemPrompt, userMsg, (ok, content) =>
        {
            if (ok && !string.IsNullOrEmpty(content))
            {
                try
                {
                    int start = content.IndexOf('{');
                    int end = content.LastIndexOf('}');
                    if (start >= 0 && end > start)
                    {
                        string json = content.Substring(start, end - start + 1);
                        var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                        result.intent = obj["intent"]?.ToString() ?? "chat";
                        result.emotion = obj["emotion"]?.ToString() ?? "neutral";
                        result.brief = obj["brief"]?.ToString() ?? "";
                        result.success = true;
                    }
                }
                catch { }
            }
        }, temperature: 0.3f, maxTokens: 80);

        if (!result.success)
        {
            result.intent = "chat";
            result.emotion = "neutral";
            result.brief = "";
        }

        onResult?.Invoke(result);
    }

    // ──────────────────────────────────────────────────────────────────
    //  功能 2：离线回退回复
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 在 DeepSeek API 不可用时，用本地模型生成回复
    /// </summary>
    /// <param name="characterDesc">角色描述（不含工具定义，仅性格人设）</param>
    /// <param name="recentHistory">最近几轮对话文本</param>
    /// <param name="userMessage">用户最新消息</param>
    /// <param name="onResult">回调 (success, replyText)</param>
    public void GenerateFallbackReply(string characterDesc, string recentHistory, string userMessage, Action<bool, string> onResult)
    {
        string prompt = $@"{characterDesc}

以下是与主人的最近对话：
{recentHistory}

请以角色身份回复主人的最新消息：「{userMessage}」
回复应当简短自然（1-3句话即可），符合角色性格。注意：你只能进行对话回复，没有工具调用能力。";

        EnqueueTask(() => LocalLLMClient.SimplePromptAsync(prompt, onResult, temperature: 0.8f, maxTokens: 256));
    }

    // ──────────────────────────────────────────────────────────────────
    //  功能 3：对话压缩
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 对历史对话进行智能摘要压缩，保留重要信息和话题。
    /// 替代原来的简单字符串截断。
    /// </summary>
    /// <param name="conversationText">需要压缩的对话文本</param>
    /// <param name="onResult">回调 (success, summaryText)</param>
    public void SummarizeConversation(string conversationText, Action<bool, string> onResult)
    {
        string prompt = $@"压缩以下对话为简洁的摘要（50字以内），保留重要信息和话题：

{conversationText}

摘要：";

        EnqueueTask(() => LocalLLMClient.SimplePromptAsync(prompt, onResult, temperature: 0.3f, maxTokens: 100));
    }

    // ──────────────────────────────────────────────────────────────────
    //  功能 4：记忆提取
    // ──────────────────────────────────────────────────────────────────

    /// <summary>记忆提取结果</summary>
    public struct MemoryExtractResult
    {
        public int importance;       // 1-10（0 表示不需要记）
        public string topic;         // 话题分类
        public string summary;       // 记忆摘要（20字以内）
        public bool shouldRemember;  // 是否需要记入忆境
    }

    /// <summary>
    /// 从用户消息中提取重要信息，判断是否需要记入忆境
    /// </summary>
    public void ExtractMemory(string userMessage, Action<MemoryExtractResult> onResult)
    {
        string systemPrompt = @"判断以下用户输入是否值得记住。如果是重要信息，返回 JSON 格式：

{""importance"": 1-10的数字, ""topic"": ""话题分类"", ""summary"": ""记忆摘要（20字以内）""}

重要性标准：
1-3：日常闲聊，不值得记住
4-6：一般信息，可记住
7-8：重要个人信息
9-10：极其重要的关键信息

如果完全不需要记住（如问候、简单指令），返回：{""importance"": 0}

话题分类：天气/学习/工作/兴趣/日常/情感/健康/日程/其他";

        EnqueueTask(() => ExtractMemoryCoroutine(userMessage, systemPrompt, onResult));
    }

    private IEnumerator ExtractMemoryCoroutine(string userMsg, string systemPrompt, Action<MemoryExtractResult> onResult)
    {
        MemoryExtractResult result = new MemoryExtractResult { shouldRemember = false };

        yield return LocalLLMClient.PromptAsync(systemPrompt, userMsg, (ok, content) =>
        {
            if (ok && !string.IsNullOrEmpty(content))
            {
                try
                {
                    int start = content.IndexOf('{');
                    int end = content.LastIndexOf('}');
                    if (start >= 0 && end > start)
                    {
                        string json = content.Substring(start, end - start + 1);
                        var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                        int imp = 0;
                        if (obj["importance"] != null)
                            int.TryParse(obj["importance"].ToString(), out imp);
                        result.importance = Mathf.Clamp(imp, 0, 10);
                        result.topic = obj["topic"]?.ToString() ?? "日常";
                        result.summary = obj["summary"]?.ToString() ?? "";
                        result.shouldRemember = imp >= 4; // 4+ 才记入忆境
                    }
                }
                catch { }
            }
        }, temperature: 0.3f, maxTokens: 80);

        onResult?.Invoke(result);
    }
}
