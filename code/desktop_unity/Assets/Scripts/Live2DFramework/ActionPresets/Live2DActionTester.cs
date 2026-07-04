using UnityEngine;

/// <summary>
/// Live2D 动作/表情 测试工具
/// 挂载到 pet 对象上，在 Inspector 输入名字后，右键标题可触发
/// </summary>
[AddComponentMenu("Live2D/动作测试工具")]
public class Live2DActionTester : MonoBehaviour
{
    [Header("← 输入表情名 →")]
    public string expressionName = "happy";

    [Header("← 输入动作名 →")]
    public string actionName = "stretch";

    private Live2DRenderer _renderer;

    private void Awake()
    {
        _renderer = GetComponent<Live2DRenderer>();
    }

    // ── 以下方法通过右键菜单触发 ──
    // （在无法使用 [ContextMenu] 的引擎中，直接双击字段或通过 Console 调用）

    /// <summary>播放表情</summary>
    public void PlayExpression(string name)
    {
        if (_renderer == null) _renderer = GetComponent<Live2DRenderer>();
        if (_renderer == null) { Debug.LogError("[Tester] 未找到 Live2DRenderer"); return; }
        Debug.Log($"[Tester] ▶ 播放表情: {name}");
        _renderer.PlayExpression(name);
    }

    /// <summary>播放动作</summary>
    public void PlayAction(string name)
    {
        if (_renderer == null) _renderer = GetComponent<Live2DRenderer>();
        if (_renderer == null) { Debug.LogError("[Tester] 未找到 Live2DRenderer"); return; }
        Debug.Log($"[Tester] ▶ 播放动作: {name}");
        _renderer.PlayAction(name);
    }

    /// <summary>停止所有</summary>
    public void StopAll()
    {
        if (_renderer == null) _renderer = GetComponent<Live2DRenderer>();
        _renderer?.StopExpression(0.3f);
        Debug.Log("[Tester] ⏹ 停止所有");
    }

    /// <summary>列出可用表情</summary>
    public void ListExpressions()
    {
        if (_renderer == null) _renderer = GetComponent<Live2DRenderer>();
        Debug.Log($"[Tester] 可用表情: {_renderer?.GetAvailableExpressions()}");
    }

    /// <summary>列出可用动作</summary>
    public void ListActions()
    {
        if (_renderer == null) _renderer = GetComponent<Live2DRenderer>();
        Debug.Log($"[Tester] 可用动作: {_renderer?.GetAvailableActions()}");
    }

#if UNITY_EDITOR
    // ── 右键菜单（仅 Editor 下可用）──
    [UnityEditor.MenuItem("CONTEXT/Live2DActionTester/▶ 播放表情")]
    private static void PlayExprMenu(UnityEditor.MenuCommand cmd)
    {
        var t = (Live2DActionTester)cmd.context;
        t.PlayExpression(t.expressionName);
    }

    [UnityEditor.MenuItem("CONTEXT/Live2DActionTester/▶ 播放动作")]
    private static void PlayActMenu(UnityEditor.MenuCommand cmd)
    {
        var t = (Live2DActionTester)cmd.context;
        t.PlayAction(t.actionName);
    }

    [UnityEditor.MenuItem("CONTEXT/Live2DActionTester/⏹ 停止所有")]
    private static void StopAllMenu(UnityEditor.MenuCommand cmd)
    {
        var t = (Live2DActionTester)cmd.context;
        t.StopAll();
    }

    [UnityEditor.MenuItem("CONTEXT/Live2DActionTester/ℹ 可用表情")]
    private static void ListExprMenu(UnityEditor.MenuCommand cmd)
    {
        var t = (Live2DActionTester)cmd.context;
        t.ListExpressions();
    }

    [UnityEditor.MenuItem("CONTEXT/Live2DActionTester/ℹ 可用动作")]
    private static void ListActMenu(UnityEditor.MenuCommand cmd)
    {
        var t = (Live2DActionTester)cmd.context;
        t.ListActions();
    }
#endif
}
