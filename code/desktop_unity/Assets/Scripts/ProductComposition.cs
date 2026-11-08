using UnityEngine;

/// <summary>
/// Product feature assembly. The explicit order preserves the existing Awake side effects.
/// Window/process lifetime and position initialization remain in DesktopPet.
/// This is not a service locator: consumers still receive normal component references.
/// </summary>
public static class ProductComposition
{
    /// <summary>Attach the product features after the window/tray foundation exists.</summary>
    public static void AttachFeatures(GameObject host)
    {
        Ensure<DragHandler>(host);
        Ensure<AttentionReactionAdapter>(host);
        Ensure<HybridRenderer>(host);
        Ensure<RightPanel>(host);
        Ensure<TimeWeatherController>(host);
        Ensure<ReminderManager>(host);
        if (PreferencesManager.Instance == null) Ensure<PreferencesManager>(host);
        if (TaskTemplateManager.Instance == null) Ensure<TaskTemplateManager>(host);
        Ensure<ServerPollService>(host);
        // Shared presentation dependencies exist before any consumer Start runs.
        Ensure<ChatBubble>(host);
        Ensure<IdleChatGenerator>(host);

        // Keep legacy panel compatibility at its old position in the startup sequence.
        // Removing it requires a separate scene/prefab migration, not a file deletion.
        var legacyPanels = Object.FindObjectsOfType<BallPanel>();
        foreach (var panel in legacyPanels)
        {
            panel.Close();
            panel.enabled = false;
        }
        if (legacyPanels.Length > 0)
            Debug.Log($"[DesktopPet] 已禁用遗留 BallPanel 实例: {legacyPanels.Length}");

        AttachChatServices(host);
        var chat = Ensure<ChatManager>(host);
        var tools = Ensure<ToolCallInvoker>(host);
        if (chat.toolInvoker == null) chat.toolInvoker = tools;

        Ensure<MotionAgent>(host);
        Ensure<GpuLoadMonitor>(host);
        Ensure<DualModelValidator>(host);
        Ensure<AutoChat>(host);
        Ensure<ProactiveMessageScheduler>(host);
        Ensure<VisualHeartbeat>(host);
    }

    /// <summary>
    /// Create shared services before ChatManager.Awake binds its references.
    /// Standalone chat hosts must call this explicitly before activating ChatManager.
    /// </summary>
    public static void AttachChatServices(GameObject host)
    {
        if (ActivityTracker.Instance == null) EnsureChild<ActivityTracker>(host);
        if (PetConfig.Instance == null) EnsureChild<PetConfig>(host);
        if (PetMemory.Instance == null) EnsureChild<PetMemory>(host);
        if (PersonalityManager.Instance == null) EnsureChild<PersonalityManager>(host);
        if (KnowledgeBaseManager.Instance == null) EnsureChild<KnowledgeBaseManager>(host);
        if (LocalLLMAgentService.Instance == null) EnsureChild<LocalLLMAgentService>(host);
    }

    private static T Ensure<T>(GameObject host) where T : Component
    {
        var component = host.GetComponent<T>();
        if (component != null) return component;
        component = host.AddComponent<T>();
        Debug.Log($"[DesktopPet] 自动添加了 {typeof(T).Name} 组件");
        return component;
    }

    private static void EnsureChild<T>(GameObject host) where T : Component
    {
        if (host.GetComponentInChildren<T>(true) != null) return;
        var child = new GameObject(typeof(T).Name);
        // Preserve the old Awake-before-parenting behavior for persistent services.
        // An inactive test host must also keep its children inactive before AddComponent.
        if (!host.activeInHierarchy) child.SetActive(false);
        child.AddComponent<T>();
        child.transform.SetParent(host.transform);
        // Parent is still inactive, so Awake remains deferred, but the service
        // must become active when a caller later enables the host.
        child.SetActive(true);
    }
}
