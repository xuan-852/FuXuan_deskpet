using System;
using System.Linq;
using UnityEngine;

/// <summary>
/// ChatManager 回复展示收尾。
/// 只集中最终文本发布与逐句队列触发，记忆和质量遥测仍由各自流程负责。
/// </summary>
public partial class ChatManager
{
    /// <summary>
    /// 发布最终回复。保留现有顺序：先通知新回复，再按需启动逐句显示队列。
    /// </summary>
    private void PublishFinalReply(string displayReply, string sentenceSource = null)
    {
        _lastReply = displayReply ?? "";
        OnNewReply?.Invoke(_lastReply);

        if (sentenceSource != null)
            StartSentenceQueue(sentenceSource);
    }

    /// <summary>记录纯文字回复到长期记忆（按重要性过滤）</summary>
    private void RecordConversationMemory(string reply)
    {
        if (IsTestMode) return; // 测试模式：不写任何对话记忆/摘要/提取
        if (PetMemory.Instance == null || string.IsNullOrEmpty(reply)) return;

        _conversationSinceSummary++;

        // ——— 只把“明确、稳定、可复用”的用户表达交给提取器 ———
        // 闲聊不再随机落盘；宽泛关键词也不再直接等同于长期记忆。
        var lastUserMsg = GetLastUserMessage();
        if (!string.IsNullOrEmpty(lastUserMsg)
            && MemoryGovernance.HasExplicitMemorySignal(lastUserMsg))
        {
            if (LocalLLMAgentService.Instance != null && LocalLLMAgentService.Instance.CanProcess)
            {
                LocalLLMAgentService.Instance.ExtractMemory(lastUserMsg, extract =>
                {
                    if (extract.shouldRemember && MemoryGovernance.AcceptExtractedMemory(
                        lastUserMsg, extract.summary, extract.importance,
                        extract.confidence, extract.memoryType))
                    {
                        PetMemory.Instance.AddMemoryWithMetadata(
                            $"主人明确告知：{extract.summary}",
                            extract.topic,
                            "conversation",
                            extract.importance,
                            "local_model",
                            extract.confidence,
                            extract.expiresAfterDays);
                        Debug.Log($"[ChatManager] 💾 已确认长期记忆: [{extract.topic}] {extract.summary}");
                    }
                    else if (string.IsNullOrWhiteSpace(extract.summary))
                    {
                        // 只有本地提取失败/无可解析结果才用原话兜底；模型明确判定“不值得记”时不写入。
                        PersistExplicitMemoryFallback(lastUserMsg);
                    }
                });
            }
            else
            {
                PersistExplicitMemoryFallback(lastUserMsg);
            }
        }

        // ——— 到达摘要间隔时，自动更新近日印象 ———
        if (_conversationSinceSummary >= SUMMARY_INTERVAL)
        {
            _conversationSinceSummary = 0;

            var summaryMessages = _history
                .Where(e => e.role == "user")
                .Select(e => e.content ?? "")
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();
            int summarySkip = Math.Max(0, summaryMessages.Count - 6);
            string summaryInput = string.Join("\n", summaryMessages.Skip(summarySkip));

            // 📝 功能3：对话压缩 — 用本地模型智能摘要（如可用）
            // ★ C2 修复：回退逻辑移入回调内部 —— 原实现里同步回退总会抢先覆盖
            //   异步智能摘要（bool 在回调返回前就读），导致智能摘要永远不生效
            Action fallbackSummary = () =>
            {
                if (!MemoryGovernance.HasExplicitMemorySignal(summaryInput)) return;
                var recentTopics = summaryMessages.Skip(summarySkip).ToList();

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

            if (LocalLLMAgentService.Instance != null && LocalLLMAgentService.Instance.CanProcess
                && !string.IsNullOrEmpty(summaryInput)
                && MemoryGovernance.HasExplicitMemorySignal(summaryInput))
            {
                LocalLLMAgentService.Instance.SummarizeConversation(summaryInput, (ok, summary) =>
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
    }

    /// <summary>本地提取失败时的确定性兜底，只保存带明确记忆信号的用户原话。</summary>
    private void PersistExplicitMemoryFallback(string userMessage)
    {
        if (!MemoryGovernance.HasStrongMemorySignal(userMessage)) return;
        string brief = userMessage.Trim();
        if (brief.Length > 120) brief = brief.Substring(0, 120) + "…";
        PetMemory.Instance.AddMemoryWithMetadata(
            $"主人明确告知：{brief}",
            "主人信息",
            "conversation",
            7,
            "user",
            0.90f,
            0);
    }
}
