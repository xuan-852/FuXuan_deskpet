using System;

/// <summary>
/// ChatManager 的上下文构建层。
/// 组装记忆、人格、知识库及工具能力说明；不注入被动桌面内容。
/// </summary>
public partial class ChatManager
{
    /// <summary>构建最终 SystemPrompt（相关记忆 + 能力说明）</summary>
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

        // 被动桌面内容不进入对话。活动类别仅供用户明确开启后的本地模式判断。

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
