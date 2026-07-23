using UnityEngine;

/// <summary>
/// 收纳盘开关 — 挂在桌宠身上，点击背包图标切换面板可见性
/// </summary>
public class DockToggle : MonoBehaviour
{
    [Header("引用")]
    [SerializeField] public DockPanel dockPanel;
    [SerializeField] public GameObject bagIcon;      // 桌宠身上的背包/收纳图标

    private bool _isVisible = true;

    private void Start()
    {
        if (bagIcon != null)
            bagIcon.SetActive(true);
    }

    private void OnMouseDown()
    {
        if (dockPanel == null) return;

        _isVisible = !_isVisible;

        // 切换整个 DockPanel 所在 GameObject
        dockPanel.gameObject.SetActive(_isVisible);

        if (bagIcon != null)
            bagIcon.SetActive(_isVisible);
    }

    /// <summary>外部调用：打开收纳盘</summary>
    public void Show()
    {
        if (dockPanel == null) return;
        _isVisible = true;
        dockPanel.gameObject.SetActive(true);
        if (bagIcon != null) bagIcon.SetActive(true);
    }

    /// <summary>外部调用：隐藏收纳盘</summary>
    public void Hide()
    {
        if (dockPanel == null) return;
        _isVisible = false;
        dockPanel.gameObject.SetActive(false);
        if (bagIcon != null) bagIcon.SetActive(false);
    }

    /// <summary>同步可见性状态（供外部唤醒后恢复）</summary>
    public void SyncVisibility(bool visible)
    {
        _isVisible = visible;
        if (dockPanel != null) dockPanel.gameObject.SetActive(visible);
        if (bagIcon != null) bagIcon.SetActive(visible);
    }
}
