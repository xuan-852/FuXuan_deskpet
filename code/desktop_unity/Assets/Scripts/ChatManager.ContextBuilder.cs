using System;

/// <summary>
/// ChatManager 的上下文构建层。
/// 保持原有注入顺序与预算配置，只隔离 SystemPrompt 组装职责。
/// </summary>
public partial class ChatManager
{
    /// <summary>构建最终 SystemPrompt（注入长期记忆 + 行为观测）</summary>
    private string BuildSystemPrompt()
    {
        string prompt = _systemPromptTemplate;

        // 记忆治理：当前用户问题已在 BuildRequestBody 前写入历史，作为检索 query。
        // 这样长期记忆按问题相关性选择，而不是每轮固定注入同一批 Top-N。
        string memoryQuery = "";
        for (int i = _history.Count - 1; i >= 0; i--)
        {
            if (_history[i].role == "user")
            {
                memoryQuery = _history[i].content ?? "";
                break;
            }
        }

        // 注入长期记忆
        if (PetMemory.Instance != null)
        {
            string memories = PromptContextBudget.TrimSection(
                PetMemory.Instance.GetFormattedMemories(memoryQuery), PromptContextBudget.MemoryChars, "长期记忆");
            if (!string.IsNullOrEmpty(memories))
                prompt += "\n" + memories;
        }

        // ★ 注入人格特质与关系
        if (PersonalityManager.Instance != null)
        {
            string personality = PromptContextBudget.TrimSection(
                PersonalityManager.Instance.FormatForPrompt(), PromptContextBudget.PersonalityChars, "人格关系");
            if (!string.IsNullOrEmpty(personality))
                prompt += "\n" + personality;
        }

        // ★ P4.2: 注入主人偏好摘要（心之所向）
        if (PreferencesManager.Instance != null)
        {
            string preferences = PromptContextBudget.TrimSection(
                PreferencesManager.Instance.FormatForPrompt(), PromptContextBudget.PreferenceChars, "主人偏好");
            if (!string.IsNullOrEmpty(preferences))
                prompt += "\n" + preferences;
        }

        // ★ 注入知识库上下文（藏书阁检索结果缓存）
        if (KnowledgeBaseManager.Instance != null && !string.IsNullOrEmpty(_cachedKnowledgeContext))
        {
            prompt += "\n" + PromptContextBudget.TrimSection(
                _cachedKnowledgeContext, PromptContextBudget.KnowledgeChars, "知识库");
        }

        // 注入法眼观测（今日行为摘要 + 当前窗口 + 多窗口环境）
        if (activityTracker != null)
        {
            string activity = PromptContextBudget.TrimSection(
                activityTracker.GetSummary(), PromptContextBudget.ActivityChars, "活动摘要");
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
            string multiWindow = PromptContextBudget.TrimSection(
                activityTracker.GetVisibleWindowsSummary(), PromptContextBudget.VisibleWindowsChars, "多窗口");
            if (!string.IsNullOrEmpty(multiWindow))
            {
                prompt += "\n" + multiWindow;
            }

            // ★ 注入浏览器标签页深度感知（让 AI 了解当前浏览器打开了什么）
            string browserTabs = PromptContextBudget.TrimSection(
                activityTracker.GetBrowserTabsSummary(), PromptContextBudget.BrowserTabsChars, "浏览器标签");
            if (!string.IsNullOrEmpty(browserTabs))
            {
                prompt += "\n" + browserTabs;
            }
        }

        // ★ 注入身体参数知识（让 AI 了解如何控制自己的 Live2D 身体）
        prompt += PromptContextBudget.TrimSection(
            InjectParameterKnowledge(), PromptContextBudget.ParameterKnowledgeChars, "身体参数知识");

        // ★ 注入闭环演武能力（让 AI 知道演武后可自评自省）
        prompt += InjectClosedLoopCapability();

        // ★ T7: 注入多步并行施法能力（Speculative Multi-Action — 减少 LLM 往返）
        prompt += InjectMultiActionCapability();

        // ★ 注入演武心经经验（过往最佳动作参数参考）
        if (MotionMemoryManager.Instance != null)
        {
            string motionMemories = PromptContextBudget.TrimSection(
                MotionMemoryManager.Instance.GetFormattedMemories(), PromptContextBudget.MotionMemoryChars, "演武心经");
            if (!string.IsNullOrEmpty(motionMemories))
                prompt += "\n" + motionMemories;
        }

        // ★ P4.1: 注入剪贴板感知（主人最近复制的内容，过期自动失效）
        string clipboardSummary = PromptContextBudget.TrimSection(
            ClipboardMonitor.GetRecentClipboardSummary(), PromptContextBudget.ClipboardChars, "剪贴板");
        if (!string.IsNullOrEmpty(clipboardSummary))
        {
            prompt += clipboardSummary;
        }

        // ★ P5.2: 注入太卜手札·任务轨迹摘要（过往外包任务成败，同类任务可参考）
        if (TaskTrajectoryManager.Instance != null)
        {
            string trajectories = PromptContextBudget.TrimSection(
                TaskTrajectoryManager.Instance.FormatForPrompt(), PromptContextBudget.TrajectoryChars, "任务轨迹");
            if (!string.IsNullOrEmpty(trajectories))
                prompt += trajectories;
        }

        // ★ P5.3: 注入太卜阵法图·任务模板清单（openclaw_task 的 template 参数可省 token）
        if (TaskTemplateManager.Instance != null)
        {
            string templates = PromptContextBudget.TrimSection(
                TaskTemplateManager.Instance.FormatForPrompt(), PromptContextBudget.TemplateChars, "任务模板");
            if (!string.IsNullOrEmpty(templates))
                prompt += templates;
        }

        // ★ 当前时刻追加到末尾（保持静态前缀不变 → 命中 DeepSeek 上下文缓存）
        prompt += "\n\n【当前时刻】" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") +
                  "（主人电脑的本地时间。用法阵术式填入时辰时，务必以此刻为准推算。）";

        return prompt;
    }
}
