using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 本地 LLM 智能服务 — 为 ChatManager 提供本地能力：
///
/// 1. 🧠 意图/情绪分类 — 用户输入实时分类（闲聊/指令/知识/情感/操作）
/// 2. 🧭 本地工具规划 — 输出受白名单约束的工具 JSON 计划
/// 3. 🔄 本地角色回复 — 结合忆境和工具结果生成最终回复
/// 4. 📝 对话压缩摘要 — 历史过长时智能压缩，替代简单截断
/// 5. 💾 记忆提取 — 从对话中提取重要信息存入忆境
///
/// 使用协程队列串行处理任务，避免并发冲突。
/// 依赖 LocalLLMClient 连接 Ollama；动作/摘要使用轻量模型，聊天使用独立的质量模型。
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

        // 设置动作/摘要模型；聊天模型由 LocalLLMClient.ChatModelName 独立管理。
        LocalLLMClient.SetModel(LocalLLMClient.ResolveConfiguredModel("qwen2.5:3b"));
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

    /// <summary>聊天模型是否可用；与动作/摘要模型的就绪状态分离。</summary>
    public bool CanProcessChat => LocalLLMClient.IsModelReady(LocalLLMClient.ChatModelName)
        && !LocalLLMClient.Paused;

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
        if (!LocalLLMClient.IsModelReady(LocalLLMClient.ChatModelName))
        {
            yield return LocalLLMClient.CheckHealthAsync((ok, msg) => {
                Debug.Log($"[LocalLLMAgent] chat {msg}");
            }, LocalLLMClient.ChatModelName);
        }
    }

    // ==================================================================
    //  协程任务队列 — 串行处理，避免并发
    // ==================================================================

    private readonly Queue<Func<IEnumerator>> _taskQueue = new Queue<Func<IEnumerator>>();
    private bool _isProcessing = false;

    /// <summary>将一个任务加入队列</summary>
    private void EnqueueTask(Func<IEnumerator> task, string requiredModel = null)
    {
        bool modelReady = string.IsNullOrWhiteSpace(requiredModel)
            ? LocalLLMClient.IsReady
            : LocalLLMClient.IsModelReady(requiredModel);
        if (!modelReady || LocalLLMClient.Paused)
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
    //  功能 2：本地工具规划
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 让轻量本地模型从给定目录中选择一个工具，返回严格 JSON 计划。
    /// 该方法只负责规划，不直接执行任何工具；执行和危险确认由 ChatManager 完成。
    /// </summary>
    public void PlanLocalTool(string userMessage, string compactCatalog, Action<LocalToolPlan> onResult)
    {
        if (string.IsNullOrWhiteSpace(userMessage) || onResult == null) return;

        string systemPrompt = @"你是符玄桌宠的本地术式规划器，不是聊天助手。
请根据用户请求和可用术式目录，判断是否需要执行一个工具。
只能从目录中选择一个 tool；不需要执行时返回 action=none。
必须只返回一个 JSON 对象，不要 Markdown，不要解释，不要输出第二个 JSON：
{""action"":""call""或""none"",""tool"":""工具名或空字符串"",""arguments"":{},""reason"":""不超过20字""}

规则：
1. 用户只是闲聊、询问建议、表达情绪时，返回 action=none。
2. 用户明确要求打开、搜索、读取、生成、运行、设置、播放或查询时，才选择工具。
3. arguments 必须严格符合目录中的参数 schema；没有参数时使用 {}。
4. 不得伪造工具名、参数或执行结果；不要把最终回复写进 JSON。
5. 危险工具可以提出计划，但执行前由桌宠单独请求用户确认，不能绕过确认。

可用术式目录：
" + (compactCatalog ?? "[]");

        EnqueueTask(() => PlanLocalToolCoroutine(userMessage, systemPrompt, onResult), LocalLLMClient.ModelName);
    }

    private IEnumerator PlanLocalToolCoroutine(string userMessage, string systemPrompt, Action<LocalToolPlan> onResult)
    {
        LocalToolPlan plan = new LocalToolPlan
        {
            Success = false,
            ShouldExecute = false,
            ToolName = "",
            ArgumentsJson = "{}",
            Reason = "",
            Error = "本地术式规划未返回"
        };

        yield return LocalLLMClient.PromptAsync(systemPrompt, userMessage, (ok, content) =>
        {
            plan = ok ? LocalToolRouter.ParsePlan(content) : plan;
            if (!ok && !string.IsNullOrEmpty(content))
                plan.Error = content;
        }, temperature: 0.1f, maxTokens: 320, timeout: 30, modelOverride: LocalLLMClient.ModelName);

        // 轻量模型偶尔会在 arguments 内输出坏 JSON。先用更短、更硬的格式提示重试一次，
        // 再交给受限关键词兜底，避免普通的“查看系统/搜索文件”被误判成闲聊。
        if (!plan.Success)
        {
            string repairPrompt = "You are a local tool JSON repairer. Output exactly one valid JSON object on one line. "
                + "No Markdown, explanation, or extra text. "
                + "Use this format: {\"action\":\"call\",\"tool\":\"TOOL_NAME\",\"arguments\":{},\"reason\":\"short\"}. "
                + "If no tool is needed use {\"action\":\"none\",\"tool\":\"\",\"arguments\":{},\"reason\":\"chat\"}. "
                + "arguments must always be a valid JSON object. Available catalog:\n" + systemPrompt;

            LocalToolPlan repaired = new LocalToolPlan
            {
                Success = false,
                ShouldExecute = false,
                ToolName = "",
                ArgumentsJson = "{}",
                Reason = "",
                Error = "本地术式修复规划未返回"
            };
            yield return LocalLLMClient.PromptAsync(repairPrompt, userMessage, (ok, content) =>
            {
                if (ok) repaired = LocalToolRouter.ParsePlan(content);
                else if (!string.IsNullOrEmpty(content)) repaired.Error = content;
            }, temperature: 0f, maxTokens: 180, timeout: 25, modelOverride: LocalLLMClient.ModelName);

            if (repaired.Success)
                plan = repaired;
        }

        // 即使模型返回了合法的 action=none，也要对明确的“生成/搜索/打开”
        // 进行一次高置信度复核，避免轻量模型把明显任务误判成闲聊。
        if ((!plan.Success || !plan.ShouldExecute)
            && LocalToolRouter.TryBuildKeywordPlan("", userMessage, out LocalToolPlan keywordPlan))
        {
            plan = keywordPlan;
            Debug.LogWarning("[LocalLLMAgent] 已采用高置信度本地术式复核: " + plan.ToolName);
        }

        onResult?.Invoke(plan);
    }

    // ──────────────────────────────────────────────────────────────────
    //  功能 3：本地角色回复
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 在本地模式或 DeepSeek API 不可用时，用本地模型生成回复。
    /// </summary>
    /// <param name="characterDesc">角色描述（不含工具定义，仅性格人设）</param>
    /// <param name="recentHistory">最近几轮对话文本</param>
    /// <param name="userMessage">用户最新消息</param>
    /// <param name="onResult">回调 (success, replyText)</param>
    public void GenerateFallbackReply(
        string characterDesc,
        string recentHistory,
        string userMessage,
        Action<bool, string> onResult,
        string toolResultContext = null)
    {
        LocalChatModelProfile profile = LocalChatModelProfiles.Get(LocalLLMClient.ChatModelName);
        string memoryContext = "";
        if (PetMemory.Instance != null)
        {
            memoryContext = PromptContextBudget.TrimSection(
                PetMemory.Instance.GetFormattedMemories(userMessage),
                PromptContextBudget.LocalMemoryChars,
                "本地忆境");
        }
        string prompt = LocalRoleplayPromptBuilder.Build(
            characterDesc, recentHistory, userMessage, profile.Model, memoryContext, toolResultContext);

        // 不同聊天模型使用各自的质量预算；动作/摘要仍走轻量模型。
        float temperature = profile.Temperature;
        int maxTokens = profile.MaxTokens;
        EnqueueTask(() => LocalLLMClient.PromptAsync(
            "你是符玄。请严格按照下面的本地角色卡和输出契约作答。\n\n" + prompt,
            userMessage,
            onResult,
            temperature: temperature,
            maxTokens: maxTokens,
            timeout: profile.TimeoutSeconds,
            modelOverride: LocalLLMClient.ChatModelName), LocalLLMClient.ChatModelName);
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
        public float confidence;     // 0-1，模型对“确实来自用户”的把握
        public string topic;         // 话题分类
        public string memoryType;    // durable / episodic / preference / constraint / none
        public string summary;       // 记忆摘要（60字以内）
        public int expiresAfterDays; // 0 表示稳定信息
        public bool shouldRemember;  // 是否需要记入忆境
    }

    /// <summary>
    /// 从用户消息中提取重要信息，判断是否需要记入忆境
    /// </summary>
    public void ExtractMemory(string userMessage, Action<MemoryExtractResult> onResult)
    {
        string systemPrompt = @"你是桌宠的记忆筛选器，不是聊天助手。只从“用户原话”提取稳定、未来仍有用的信息。
必须严格返回 JSON，不要解释：
{""shouldRemember"":true/false,""importance"":0-10,""confidence"":0-1,""memoryType"":""durable/preference/constraint/episodic/none"",""topic"":""简短分类"",""summary"":""第三人称、60字以内"",""expiresAfterDays"":0-30}

写入标准（必须同时满足）：
1. 用户明确说出自己的事实、稳定偏好、称呼、禁忌、长期目标或明确要求本座记住；
2. 未来对话能复用，而非今天发生的一次性事件；
3. 摘要只能改写用户原话，不能推测、补全或把本座的话当成用户事实。

问候、感谢、确认、瞬间情绪、天气/搜索/截图/查询结果、一次性安排、泛泛的“我今天很累”、普通指令，一律 shouldRemember=false、importance=0、memoryType=none。
只有 importance>=7 且 confidence>=0.72 才允许 shouldRemember=true；稳定事实/偏好/约束 expiresAfterDays=0。";

        EnqueueTask(() => ExtractMemoryCoroutine(userMessage, systemPrompt, onResult));
    }

    private IEnumerator ExtractMemoryCoroutine(string userMsg, string systemPrompt, Action<MemoryExtractResult> onResult)
    {
        MemoryExtractResult result = new MemoryExtractResult
        {
            shouldRemember = false,
            importance = 0,
            confidence = 0f,
            memoryType = "none",
            topic = "",
            summary = "",
            expiresAfterDays = 0
        };

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
                        float confidence = 0f;
                        if (obj["confidence"] != null)
                            float.TryParse(obj["confidence"].ToString(), out confidence);
                        result.confidence = Mathf.Clamp01(confidence);
                        result.topic = obj["topic"]?.ToString() ?? "日常";
                        result.memoryType = obj["memoryType"]?.ToString() ?? "none";
                        result.summary = obj["summary"]?.ToString() ?? "";
                        int days = 0;
                        if (obj["expiresAfterDays"] != null)
                            int.TryParse(obj["expiresAfterDays"].ToString(), out days);
                        result.expiresAfterDays = Mathf.Clamp(days, 0, 30);
                        result.shouldRemember = obj["shouldRemember"] != null
                            ? obj["shouldRemember"].ToObject<bool>()
                            : (imp >= 7 && result.confidence >= MemoryGovernance.DurableConfidenceThreshold);
                    }
                }
                catch { }
            }
        }, temperature: 0.3f, maxTokens: 80);

        onResult?.Invoke(result);
    }
}
