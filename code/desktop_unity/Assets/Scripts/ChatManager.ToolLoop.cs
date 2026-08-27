using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// ChatManager 工具回环的共享协作逻辑。
/// 当前先抽取危险工具确认，统一本地规划与云端 tool_call 的审批等待行为。
/// </summary>
public partial class ChatManager
{
    /// <summary>
    /// 显示危险操作确认并等待用户决定；超时自动拒绝，避免工具回环永久挂起。
    /// </summary>
    private IEnumerator WaitForDangerousToolConfirmation(
        string toolName, string argsJson, string prompt, Action<bool> onResolved)
    {
        bool confirmed = false;
        bool resolved = false;
        string description = ToolRegistry.GetDangerDescription(toolName);
        var confirmBubble = FindObjectOfType<ChatBubble>();

        if (confirmBubble != null)
            confirmBubble.ShowMessage(prompt, 60f, ChatBubble.MsgPriority.High);

        ToolConfirmManager.Request(toolName, argsJson, description,
            ok => { confirmed = ok; resolved = true; });

        float confirmTimeout = Time.time + 60f;
        while (!resolved)
        {
            if (Time.time > confirmTimeout)
            {
                ToolConfirmManager.Resolve(false);
                break;
            }
            yield return null;
        }

        if (confirmed && confirmBubble != null)
            confirmBubble.ShowMessage("✅ 已获准许，施法！", 2.5f, ChatBubble.MsgPriority.Normal);

        onResolved?.Invoke(confirmed);
    }
}
