using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
/// <summary>
/// 收纳盘主控 — 文件拖放收纳面板
///
/// 交互：
/// - 默认折叠为右上角迷你条
/// - 鼠标悬停 → 展开面板，显示已收纳文件
/// - 鼠标离开 2s → 自动折叠
/// - 从资源管理器拖放文件到面板 → 收纳
/// - 清空按钮 → 移除所有文件
/// </summary>
public class DockPanel : MonoBehaviour
{
    [Header("UI 引用")]
    [SerializeField] private RectTransform dockRoot;         // 面板根 Rect
    [SerializeField] private GameObject collapsedBar;        // 折叠态迷你条
    [SerializeField] private GameObject expandedPanel;       // 展开态面板
    [SerializeField] private Transform iconGrid;             // 图标容器
    [SerializeField] private GameObject dockItemPrefab;      // DockItem 预制体
    [SerializeField] private Button clearButton;
    [SerializeField] private Text countLabel;
    [SerializeField] private Text collapsedCountLabel;       // 迷你条上的数量文本

    [Header("行为")]
    [SerializeField] private float topMargin = 10f;
    [SerializeField] private float rightMargin = 10f;
    [SerializeField] private float collapseDelay = 2f;
    [SerializeField] private int maxColumns = 4;

    [Header("调试")]
    [SerializeField] private bool debugLog = true;

    // 状态
    private DockStateData _state;
    private bool _isExpanded = false;
    private float _mouseLeaveTimestamp = 0f;
    private bool _isMouseOver = false;
    private List<DockItem> _itemInstances = new();

    // 初始化标志
    private bool _initialized = false;

    #region 生命周期

    private void Start()
    {
        // 加载持久化状态
        _state = DockState.Load();
        ApplyPosition();
        RebuildIconGrid();

        // 清空按钮
        if (clearButton != null)
            clearButton.onClick.AddListener(OnClear);

        // 订阅拖放事件
        DockDropHandler.OnFilesDropped += OnFilesDropped;

        // 初始强制关闭拖放接收（折叠状态），文件穿透到桌面/其他窗口
        DockDropHandler.SetAcceptFiles(false);

        _initialized = true;
        Log("收纳盘初始化完成");
    }

    private void Update()
    {
        if (!_initialized) return;

        // 1. 处理挂起的拖放队列（主线程安全）
        DockDropHandler.ProcessPendingDrops();

        // 2. 悬停检测 & 展开/折叠逻辑
        UpdateHoverState();

        // 3. 保持右上角定位（分辨率变化时）
        ApplyPosition();
    }

    private void OnDestroy()
    {
        // 反注册事件
        DockDropHandler.OnFilesDropped -= OnFilesDropped;

        // 保存状态
        if (_state != null)
            DockState.Save(_state);
    }

    private void OnApplicationQuit()
    {
        if (_state != null)
            DockState.Save(_state);
    }

    #endregion

    #region 悬停检测

    /// <summary>
    /// 每帧检测鼠标悬停，控制展开/折叠
    /// 使用 AABB 检测代替 RectTransformUtility（多屏兼容）
    /// </summary>
    private void UpdateHoverState()
    {
        if (dockRoot == null) return;

        // 获取鼠标在屏幕坐标中的位置（Unity: 左下角原点,Y向上）
        Vector3 mousePos = Input.mousePosition;

        // 判断鼠标是否在 dockRoot 的屏幕范围内
        _isMouseOver = IsPointInRectTransformScreenSpace(mousePos, dockRoot);

        if (_isMouseOver)
        {
            // 鼠标在面板内 → 展开
            if (!_isExpanded)
                Expand();

            _mouseLeaveTimestamp = 0f;
        }
        else if (_isExpanded)
        {
            // 鼠标离开 → 计时折叠
            if (_mouseLeaveTimestamp == 0f)
                _mouseLeaveTimestamp = Time.time;
            else if (Time.time - _mouseLeaveTimestamp >= collapseDelay)
                Collapse();
        }
    }

    /// <summary>
    /// AABB 检测：判断屏幕点是否在 RectTransform 的屏幕空间矩形内
    /// 比 RectTransformUtility 更稳定（多屏适配）
    /// </summary>
    private bool IsPointInRectTransformScreenSpace(Vector3 screenPoint, RectTransform rectTransform)
    {
        // 获取 RectTransform 的四个角（屏幕坐标）
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        // corners 顺序: 0=左下, 1=左上, 2=右上, 3=右下
        float minX = corners[0].x;
        float maxX = corners[2].x;
        float minY = corners[0].y;
        float maxY = corners[1].y;

        return screenPoint.x >= minX && screenPoint.x <= maxX &&
               screenPoint.y >= minY && screenPoint.y <= maxY;
    }

    #endregion

    #region 展开 / 折叠

    private void Expand()
    {
        _isExpanded = true;

        if (collapsedBar != null)
            collapsedBar.SetActive(false);

        if (expandedPanel != null)
            expandedPanel.SetActive(true);

        _mouseLeaveTimestamp = 0f;

        // 展开时开启拖放接收，仅收纳盘区域可拖入文件
        DockDropHandler.SetAcceptFiles(true);

        Log("收纳盘展开");
    }

    private void Collapse()
    {
        _isExpanded = false;
        _mouseLeaveTimestamp = 0f;

        if (collapsedBar != null)
            collapsedBar.SetActive(true);

        if (expandedPanel != null)
            expandedPanel.SetActive(false);

        // 折叠时关闭拖放接收，文件穿透到桌面/其他窗口
        DockDropHandler.SetAcceptFiles(false);

        Log("收纳盘折叠");
    }

    /// <summary>外部强制切换展开/折叠（由 DockToggle 调用）</summary>
    public void Toggle()
    {
        if (_isExpanded)
            Collapse();
        else
            Expand();
    }

    #endregion

    #region 定位

    /// <summary>将 dockRoot 固定在屏幕右上角</summary>
    private void ApplyPosition()
    {
        if (dockRoot == null) return;

        // 基于归一化坐标定位
        // panelX: 0=靠左, 1=靠右（从右起算）
        // panelY: 0=靠上, 1=靠下（从上起算）
        Vector2 anchoredPos = dockRoot.anchoredPosition;

        // 在 Canvas Scaler 下用 anchoredPosition 定位
        // pivot 设为 (1, 1) 时，anchoredPosition 从右上角偏移
        // 简单做法：每帧设置 anchoredPosition 相对右上角
        float x = -rightMargin;
        float y = -topMargin;

        anchoredPos.x = x;
        anchoredPos.y = y;
        dockRoot.anchoredPosition = anchoredPos;
    }

    #endregion

    #region 图标管理

    /// <summary>根据 _state.items 重建图标网格</summary>
    private void RebuildIconGrid()
    {
        if (iconGrid == null || dockItemPrefab == null) return;

        // 清除旧图标
        ClearIconGrid();

        if (_state == null || _state.items.Count == 0)
        {
            UpdateCountLabel();
            return;
        }

        foreach (DockItemData itemData in _state.items)
        {
            // 检查文件是否还存在
            if (!File.Exists(itemData.path))
                continue;

            GameObject go = Instantiate(dockItemPrefab, iconGrid);
            DockItem dockItem = go.GetComponent<DockItem>();

            if (dockItem != null)
            {
                dockItem.Init(itemData);
                dockItem.OnRemoveRequest += OnItemRemoveRequest;
                _itemInstances.Add(dockItem);
            }
        }

        UpdateCountLabel();
    }

    private void ClearIconGrid()
    {
        foreach (DockItem item in _itemInstances)
        {
            if (item != null)
            {
                item.OnRemoveRequest -= OnItemRemoveRequest;
                Destroy(item.gameObject);
            }
        }
        _itemInstances.Clear();
    }

    private void UpdateCountLabel()
    {
        int count = _state?.items.Count ?? 0;
        if (countLabel != null)
            countLabel.text = $"收纳盘 ({count}项)";
        if (collapsedCountLabel != null)
            collapsedCountLabel.text = count.ToString();
    }

    #endregion

    #region 拖放处理

    /// <summary>
    /// DockDropHandler 回调 — 收到拖放的文件
    /// </summary>
    private void OnFilesDropped(string[] files)
    {
        if (files == null || files.Length == 0) return;

        Log($"收到 {files.Length} 个拖放文件");

        foreach (string file in files)
        {
            AddItem(file);
        }
    }

    /// <summary>添加一个文件到收纳盘（防重复）</summary>
    public void AddItem(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        if (_state == null) return;

        // 忽略目录
        if (Directory.Exists(filePath)) return;

        // 忽略已经被收纳的文件
        if (_state.items.Exists(item =>
            item.path.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
            return;

        DockItemData itemData = new DockItemData
        {
            path = filePath,
            fileName = Path.GetFileName(filePath),
            extension = Path.GetExtension(filePath)?.ToLowerInvariant() ?? "",
            addedTicks = DateTime.Now.Ticks
        };

        _state.items.Add(itemData);

        // 排序：新添加的在最前
        _state.items = _state.items
            .OrderByDescending(i => i.addedTicks)
            .ToList();

        // 限制最大数量（避免撑爆面板）
        const int maxItems = 32;
        while (_state.items.Count > maxItems)
            _state.items.RemoveAt(_state.items.Count - 1);

        // 刷新 UI
        RebuildIconGrid();

        // 自动展开
        if (!_isExpanded)
            Expand();

        // 保存
        DockState.Save(_state);
        Log($"已收纳: {filePath}");
    }

    #endregion

    #region 删除 / 清空

    /// <summary>单个文件移出（由 DockItem 右键触发）</summary>
    private void OnItemRemoveRequest(DockItem item)
    {
        if (item?.Data == null || _state == null) return;

        _state.items.RemoveAll(i => i.path == item.Data.path);
        RebuildIconGrid();
        DockState.Save(_state);
        Log($"已移出: {item.Data.fileName}");
    }

    /// <summary>清空所有文件</summary>
    private void OnClear()
    {
        if (_state == null || _state.items.Count == 0) return;

        _state.items.Clear();
        RebuildIconGrid();
        DockState.Save(_state);
        Log("收纳盘已清空");
    }

    #endregion

    #region 公共查询

    /// <summary>收纳盘是否处于展开状态</summary>
    public bool IsExpanded => _isExpanded;

    /// <summary>当前文件数量</summary>
    public int ItemCount => _state?.items.Count ?? 0;

    /// <summary>
    /// 外部检测：指定屏幕坐标是否在收纳盘区域内
    /// 供 DragHandler.UpdateClickThrough() 调用
    /// </summary>
    /// <param name="screenPoint">屏幕坐标（Unity 坐标系，左下原点 Y 向上）</param>
    public bool IsPointInDockArea(Vector3 screenPoint)
    {
        if (dockRoot == null || !_isExpanded) return false;
        return IsPointInRectTransformScreenSpace(screenPoint, dockRoot);
    }

    #endregion

    #region 运行时注入（供 DockAutoSetup 使用）

    /// <summary>
    /// 运行时自动搭建时注入所有 UI 引用。
    /// 仅在 Start() 前调用有效，否则需要手动 RebuildIconGrid。
    /// </summary>
    public void SetRefs(
        RectTransform dockRoot,
        GameObject collapsedBar,
        GameObject expandedPanel,
        Transform iconGrid,
        GameObject dockItemPrefab,
        Button clearButton,
        Text countLabel,
        Text collapsedCountLabel)
    {
        this.dockRoot = dockRoot;
        this.collapsedBar = collapsedBar;
        this.expandedPanel = expandedPanel;
        this.iconGrid = iconGrid;
        this.dockItemPrefab = dockItemPrefab;
        this.clearButton = clearButton;
        this.countLabel = countLabel;
        this.collapsedCountLabel = collapsedCountLabel;
    }

    /// <summary>
    /// 运行时替换 item prefab（编辑器下创建预制体，运行时用模板 GameObject）
    /// </summary>
    public void SetItemPrefab(GameObject prefab)
    {
        dockItemPrefab = prefab;
    }

    #endregion

    #region 日志

    private void Log(string msg)
    {
        if (debugLog)
            Debug.Log($"[DockPanel] {msg}");
    }

    #endregion
}
