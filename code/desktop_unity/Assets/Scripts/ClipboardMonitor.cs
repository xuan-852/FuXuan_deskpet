using System;
using UnityEngine;

/// <summary>
/// 剪贴板感知 — P4.1 监听用户复制行为，缓存最近内容供 AI 感知
///
/// 设计：
///   ▸ 由 SystemTrayManager 在隐藏窗口收到 WM_CLIPBOARDUPDATE 时于主线程调用
///     NotifyClipboardUpdated()（消息只通知"变了"，内容需主动读取）
///   ▸ 内容去重：与上次相同则忽略（同内容重复复制对感知无新信息）
///   ▸ 长度截断：超过 MaxLength 截断，防止 prompt 膨胀
///   ▸ 超时失效：超过 ExpireMinutes 的复制不再注入 prompt
/// </summary>
public static class ClipboardMonitor
{
    /// <summary>缓存文本最大长度（防止 prompt 膨胀）</summary>
    private const int MaxLength = 200;

    /// <summary>最短感知长度（复制单个字符/极短文本不算有效感知）</summary>
    private const int MinLength = 2;

    /// <summary>复制内容的感知时效（分钟），超过后不再注入 prompt</summary>
    private const double ExpireMinutes = 30;

    /// <summary>最近一次复制的内容（截断后）</summary>
    public static string LastText { get; private set; } = "";

    /// <summary>最近一次复制发生的时间</summary>
    public static DateTime LastCaptureTime { get; private set; } = DateTime.MinValue;

    /// <summary>剪贴板内容变化事件（供其他系统订阅，如主动提示）</summary>
    public static event Action<string> OnClipboardChanged;

    /// <summary>
    /// 由 SystemTrayManager 在收到 WM_CLIPBOARDUPDATE 时调用（必须主线程）。
    /// 读取剪贴板并去重缓存。
    /// </summary>
    public static void NotifyClipboardUpdated()
    {
        string text;
        try
        {
            text = ToolHelpers.GetClipboardText();
        }
        catch (Exception e)
        {
            // 剪贴板可能被其他程序占用/锁定，读取失败静默忽略
            Debug.LogWarning($"[ClipboardMonitor] ⚠️ 读取剪贴板失败: {e.Message}");
            return;
        }

        if (string.IsNullOrEmpty(text)) return;
        text = text.Trim();
        if (text.Length < MinLength) return;
        if (text.Length > MaxLength) text = text.Substring(0, MaxLength);

        // 去重：内容未变则忽略
        if (text == LastText) return;

        LastText = text;
        LastCaptureTime = DateTime.Now;
        OnClipboardChanged?.Invoke(text);
        Debug.Log($"[ClipboardMonitor] 📋 检测到复制: {text}");
    }

    /// <summary>供 ChatManager 注入 SystemPrompt 的剪贴板感知摘要（过期内容返回空串）</summary>
    public static string GetRecentClipboardSummary()
    {
        if (string.IsNullOrEmpty(LastText) || LastCaptureTime == DateTime.MinValue) return "";
        double minutes = (DateTime.Now - LastCaptureTime).TotalMinutes;
        if (minutes > ExpireMinutes) return "";
        string when = minutes < 1 ? "刚刚" : $"{(int)minutes}分钟前";
        return $"\n【剪贴板感知】主人{when}复制了：「{LastText}」（仅作背景参考，勿主动提起，除非相关内容与当前话题相关）";
    }
}
