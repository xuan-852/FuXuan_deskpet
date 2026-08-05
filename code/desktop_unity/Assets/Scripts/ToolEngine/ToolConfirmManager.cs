using System;

/// <summary>
/// 工具确认管理器 — 危险工具（删文件/关机/锁屏等）执行前的用户确认协调器
///
/// 流程：
/// 1. ChatManager 执行危险工具前调用 Request()，弹确认气泡
/// 2. 用户点击桌宠 → AutoChat.HandleClick 检测 HasPending → Resolve(true)
///    用户按 ESC → DesktopPet 检测 HasPending → Resolve(false)
/// 3. Resolve 触发回调，ChatManager 的等待协程恢复
/// 4. 超时（默认 60s）→ 自动拒绝，防止协程永久挂起
/// </summary>
public static class ToolConfirmManager
{
    /// <summary>待确认的工具名（null 表示无待确认）</summary>
    public static string PendingTool { get; private set; } = null;

    /// <summary>待确认工具的原始参数 JSON</summary>
    public static string PendingArgs { get; private set; } = null;

    /// <summary>待确认操作的描述（显示给用户）</summary>
    public static string PendingDescription { get; private set; } = null;

    /// <summary>是否有待确认的操作</summary>
    public static bool HasPending => PendingTool != null;

    /// <summary>解析回调（传 true=允许，false=拒绝），Resolve 后自动清空</summary>
    public static event Action<bool> OnResolved;

    /// <summary>发起一次确认请求（同一时刻只允许一个待确认）</summary>
    public static void Request(string toolName, string argsJson, string description, Action<bool> onResolved)
    {
        if (HasPending)
        {
            // 已有待确认操作 → 直接拒绝新请求（防覆盖）
            onResolved?.Invoke(false);
            return;
        }
        PendingTool = toolName;
        PendingArgs = argsJson;
        PendingDescription = description;
        if (onResolved != null)
            OnResolved += onResolved;
    }

    /// <summary>
    /// 解析当前待确认操作
    /// </summary>
    /// <param name="confirmed">true=用户允许，false=用户拒绝/超时</param>
    public static void Resolve(bool confirmed)
    {
        if (!HasPending) return;
        string tool = PendingTool;
        PendingTool = null;
        PendingArgs = null;
        PendingDescription = null;

        // 广播一次后清空，避免重复触发
        var handler = OnResolved;
        OnResolved = null;
        handler?.Invoke(confirmed);
    }
}
