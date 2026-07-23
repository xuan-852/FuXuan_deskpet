using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 收纳盘运行时自动搭建 — 场景中无 UI 时自动创建
///
/// 挂载到 DesktopPet 上，Start 时检测并搭建。
/// 搭建完成后自我销毁，不留痕迹。
/// </summary>
public class DockAutoSetup : MonoBehaviour
{
    private void Start()
    {
        // 如果已有 DockPanelRoot，跳过
        if (GameObject.Find("DockPanelRoot") != null)
        {
            Destroy(this);
            return;
        }

        Debug.Log("[DockAutoSetup] 未检测到收纳盘 UI，自动搭建中...");
        BuildDockUI();
        Destroy(this);
    }

    private void BuildDockUI()
    {
        // ── 1. Canvas ──
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGo = new GameObject("DockCanvas", typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        // ── 2. EventSystem ──
        if (FindObjectOfType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        // ── 3. DockPanelRoot ──
        GameObject dockRootObj = new GameObject("DockPanelRoot", typeof(RectTransform));
        dockRootObj.AddComponent<DockPanel>();
        dockRootObj.transform.SetParent(canvas.transform, false);

        RectTransform dockRoot = dockRootObj.GetComponent<RectTransform>();
        dockRoot.pivot = new Vector2(1f, 1f);
        dockRoot.anchorMin = new Vector2(1f, 1f);
        dockRoot.anchorMax = new Vector2(1f, 1f);
        dockRoot.anchoredPosition = new Vector2(-10f, -10f);
        dockRoot.sizeDelta = new Vector2(280f, 380f);

        // ── 3a. CollapsedBar ──
        GameObject collapsedBar = CreateChild("CollapsedBar", dockRoot);
        Image collapsedBg = collapsedBar.AddComponent<Image>();
        collapsedBg.color = new Color(0.12f, 0.12f, 0.12f, 0.85f);
        collapsedBar.AddComponent<Shadow>();

        RectTransform cr = collapsedBar.GetComponent<RectTransform>();
        cr.pivot = new Vector2(0.5f, 0.5f);
        cr.anchorMin = new Vector2(0.5f, 0.5f);
        cr.anchorMax = new Vector2(0.5f, 0.5f);
        cr.anchoredPosition = Vector2.zero;
        cr.sizeDelta = new Vector2(48f, 48f);

        GameObject collapsedIcon = CreateChild("IconText", collapsedBar.transform);
        Text iconText = collapsedIcon.AddComponent<Text>();
        iconText.text = "\U0001f4ce"; // 📎
        iconText.fontSize = 20;
        iconText.alignment = TextAnchor.MiddleCenter;
        iconText.color = Color.white;
        RectTransform iconRt = collapsedIcon.GetComponent<RectTransform>();
        iconRt.pivot = new Vector2(0.5f, 0.6f);
        iconRt.anchorMin = Vector2.zero;
        iconRt.anchorMax = Vector2.one;
        iconRt.sizeDelta = Vector2.zero;

        GameObject collapsedCountTextObj = CreateChild("CountText", collapsedBar.transform);
        Text collapsedCountText = collapsedCountTextObj.AddComponent<Text>();
        collapsedCountText.text = "0";
        collapsedCountText.fontSize = 11;
        collapsedCountText.alignment = TextAnchor.LowerCenter;
        collapsedCountText.color = Color.white;
        RectTransform cct = collapsedCountTextObj.GetComponent<RectTransform>();
        cct.anchorMin = Vector2.zero;
        cct.anchorMax = Vector2.one;
        cct.sizeDelta = Vector2.zero;
        cct.offsetMin = new Vector2(0, 2);
        cct.offsetMax = new Vector2(0, 0);

        // ── 3b. ExpandedPanel ──
        GameObject expandedPanel = CreateChild("ExpandedPanel", dockRoot);
        Image expandBg = expandedPanel.AddComponent<Image>();
        expandBg.color = new Color(0.15f, 0.15f, 0.17f, 0.92f);
        expandedPanel.AddComponent<Shadow>();

        RectTransform ep = expandedPanel.GetComponent<RectTransform>();
        ep.pivot = new Vector2(0.5f, 0.5f);
        ep.anchorMin = Vector2.zero;
        ep.anchorMax = Vector2.one;
        ep.offsetMin = Vector2.zero;
        ep.offsetMax = Vector2.zero;

        // 标题行
        GameObject header = CreateChild("Header", expandedPanel.transform);
        RectTransform hdr = header.GetComponent<RectTransform>();
        hdr.anchorMin = new Vector2(0, 1);
        hdr.anchorMax = new Vector2(1, 1);
        hdr.pivot = new Vector2(0.5f, 1);
        hdr.sizeDelta = new Vector2(0, 30);
        hdr.anchoredPosition = new Vector2(0, -4);

        Text headerLabel = header.AddComponent<Text>();
        headerLabel.text = "收纳盘 (0项)";
        headerLabel.fontSize = 13;
        headerLabel.alignment = TextAnchor.MiddleLeft;
        headerLabel.color = new Color(0.85f, 0.85f, 0.85f);
        RectTransform hlRt = headerLabel.GetComponent<RectTransform>();
        hlRt.anchorMin = new Vector2(0, 0);
        hlRt.anchorMax = new Vector2(1, 1);
        hlRt.sizeDelta = new Vector2(-50, 0);
        hlRt.offsetMin = new Vector2(8, 0);

        // 清空按钮
        GameObject clearBtnObj = CreateChild("ClearButton", header.transform);
        Button clearButton = clearBtnObj.AddComponent<Button>();
        Image btnBg = clearBtnObj.AddComponent<Image>();
        btnBg.color = new Color(0.6f, 0.1f, 0.1f, 0.6f);

        RectTransform btnRt = clearBtnObj.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(1, 0);
        btnRt.anchorMax = new Vector2(1, 1);
        btnRt.pivot = new Vector2(1, 0.5f);
        btnRt.sizeDelta = new Vector2(40, 22);
        btnRt.anchoredPosition = new Vector2(-4, 0);

        // Text 作为子对象（不能和 Image 在同一 GameObject）
        GameObject btnLabelObj = CreateChild("Label", clearBtnObj.transform);
        Text btnLabel = btnLabelObj.AddComponent<Text>();
        btnLabel.text = "\U0001f5d1"; // 🗑
        btnLabel.fontSize = 14;
        btnLabel.alignment = TextAnchor.MiddleCenter;
        btnLabel.color = Color.white;
        btnLabel.raycastTarget = true;
        RectTransform blRt = btnLabelObj.GetComponent<RectTransform>();
        blRt.anchorMin = Vector2.zero;
        blRt.anchorMax = Vector2.one;
        blRt.sizeDelta = Vector2.zero;

        // ── 3c. IconGrid ──
        GameObject iconGridObj = CreateChild("IconGrid", expandedPanel.transform);
        GridLayoutGroup grid = iconGridObj.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(56f, 68f);
        grid.spacing = new Vector2(8f, 8f);
        grid.padding = new RectOffset(8, 8, 4, 8);
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;

        ContentSizeFitter fitter = iconGridObj.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform ig = iconGridObj.GetComponent<RectTransform>();
        ig.anchorMin = new Vector2(0, 0);
        ig.anchorMax = new Vector2(1, 1);
        ig.offsetMin = new Vector2(0, 34);
        ig.offsetMax = new Vector2(0, -4);

        // ── 连线 DockPanel ──
        DockPanel panel = dockRootObj.GetComponent<DockPanel>();
        panel.SetRefs(
            dockRoot: dockRoot,
            collapsedBar: collapsedBar,
            expandedPanel: expandedPanel,
            iconGrid: iconGridObj.transform,
            dockItemPrefab: null, // 在下方创建
            clearButton: clearButton,
            countLabel: headerLabel,
            collapsedCountLabel: collapsedCountText
        );

        // ── 创建 DockItem 预制体（运行时用 GameObject 代替） ──
        // 运行时不需要真的预制体，我们创建模板对象后直接引用
        GameObject dockItemTemplate = new GameObject("DockItemTemplate", typeof(RectTransform));
        dockItemTemplate.transform.SetParent(iconGridObj.transform, false);
        dockItemTemplate.SetActive(false);

        // iconImage
        GameObject itemIcon = CreateChild("Icon", dockItemTemplate.transform);
        Image iconImg = itemIcon.AddComponent<Image>();
        iconImg.color = new Color(0.5f, 0.5f, 0.5f);
        RectTransform ii = itemIcon.GetComponent<RectTransform>();
        ii.anchorMin = new Vector2(0.5f, 0.5f);
        ii.anchorMax = new Vector2(0.5f, 0.5f);
        ii.pivot = new Vector2(0.5f, 0.7f);
        ii.sizeDelta = new Vector2(48f, 48f);
        ii.anchoredPosition = new Vector2(0, 4);

        // fileNameLabel
        GameObject itemLabel = CreateChild("FileNameLabel", dockItemTemplate.transform);
        Text fnLabel = itemLabel.AddComponent<Text>();
        fnLabel.text = "文件名";
        fnLabel.fontSize = 10;
        fnLabel.alignment = TextAnchor.UpperCenter;
        fnLabel.color = new Color(0.85f, 0.85f, 0.85f);
        fnLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        RectTransform fl = itemLabel.GetComponent<RectTransform>();
        fl.anchorMin = new Vector2(0, 0);
        fl.anchorMax = new Vector2(1, 0);
        fl.pivot = new Vector2(0.5f, 0);
        fl.sizeDelta = new Vector2(0, 16);
        fl.anchoredPosition = new Vector2(0, 2);

        // 挂 DockItem 组件
        DockItem dockItemComp = dockItemTemplate.AddComponent<DockItem>();
        dockItemComp.iconImage = iconImg;
        dockItemComp.fileNameLabel = fnLabel;

        // 更新 panel 的 itemPrefab 引用
        panel.SetItemPrefab(dockItemTemplate);

        // ── DockToggle → 挂到 DesktopPet ──
        DockToggle toggle = GetComponent<DockToggle>();
        if (toggle == null)
            toggle = gameObject.AddComponent<DockToggle>();

        toggle.dockPanel = panel;
        toggle.bagIcon = collapsedBar;

        // DragHandler 通过 FindObjectOfType<DockPanel>() 自动获取引用，无需手动注册

        Debug.Log("[DockAutoSetup] 收纳盘 UI 运行时自动搭建完成");
    }

    private static GameObject CreateChild(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }
}
