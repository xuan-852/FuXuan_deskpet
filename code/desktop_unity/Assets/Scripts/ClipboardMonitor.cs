using System;

/// <summary>
/// 旧剪贴板感知兼容入口。被动读取已关闭；显式工具读取仍须审批。
/// </summary>
public static class ClipboardMonitor
{
    /// <summary>旧兼容字段；被动感知停用后始终为空。</summary>
    public static string LastText { get; private set; } = "";

    /// <summary>最近一次复制发生的时间</summary>
    public static DateTime LastCaptureTime { get; private set; } = DateTime.MinValue;

    /// <summary>旧兼容事件；被动感知停用后不会触发。</summary>
    public static event Action<string> OnClipboardChanged;

    /// <summary>
    /// 兼容旧调用者：不读取系统剪贴板，也不发出内容事件。
    /// </summary>
    public static void NotifyClipboardUpdated()
    {
        LastText = "";
        LastCaptureTime = DateTime.MinValue;
    }

    /// <summary>旧摘要入口，始终返回空串。</summary>
    public static string GetRecentClipboardSummary()
    {
        return "";
    }
}
