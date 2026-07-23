using System;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 收纳盘 — 单个文件图标
///
/// 交互：
/// - 双击 → 用系统默认程序打开文件
/// - 右键 → 弹出菜单（打开位置 / 移出）
/// </summary>
public class DockItem : MonoBehaviour, IPointerClickHandler
{
    [Header("UI 引用")]
    public Image iconImage;
    public TMP_Text fileNameLabel;

    [Header("行为")]
    [SerializeField] private int maxNameLength = 12;

    /// <summary>关联的文件数据</summary>
    public DockItemData Data { get; private set; }

    /// <summary>移出此文件时触发</summary>
    public event Action<DockItem> OnRemoveRequest;

    /// <summary>初始化图标</summary>
    public void Init(DockItemData itemData)
    {
        Data = itemData;

        // 设置显示名（截断过长文件名）
        string displayName = Path.GetFileNameWithoutExtension(itemData.fileName);
        fileNameLabel.text = TruncateName(displayName, maxNameLength);

        // 异步加载图标（颜色块或系统图标）
        DockIconProvider.GetIcon(itemData.path, 64, sprite =>
        {
            if (iconImage != null && sprite != null)
                iconImage.sprite = sprite;
        });
    }

    #region 交互

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.clickCount == 2)
        {
            // 双击打开文件
            OpenFile();
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            // 右键菜单
            ShowContextMenu();
        }
    }

    /// <summary>用系统默认程序打开文件（支持任意类型，含 .lnk 快捷方式）</summary>
    private void OpenFile()
    {
        if (Data == null || string.IsNullOrEmpty(Data.path))
            return;

        if (!File.Exists(Data.path))
        {
            Debug.LogWarning($"[DockItem] 文件不存在: {Data.path}");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Data.path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DockItem] 打开文件失败: {Data.path}, {ex.Message}");
        }
    }

    /// <summary>右键上下文菜单</summary>
    private void ShowContextMenu()
    {
        // V1 简单实现：只有移出选项
        // 将来可以扩展为 Unity UI 弹出菜单
        OnRemoveRequest?.Invoke(this);
    }

    #endregion

    #region 工具

    private string TruncateName(string name, int maxLen)
    {
        if (string.IsNullOrEmpty(name))
            return "?";

        if (name.Length <= maxLen)
            return name;

        return name[..(maxLen - 2)] + "..";
    }

    #endregion
}
