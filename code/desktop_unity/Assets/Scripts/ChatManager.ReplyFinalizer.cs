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
}
