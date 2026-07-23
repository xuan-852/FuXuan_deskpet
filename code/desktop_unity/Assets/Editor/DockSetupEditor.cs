using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.IO;

/// <summary>
/// 收纳盘一键搭建工具
///
/// 用法：菜单栏 Tools → 收纳盘 — 一键搭建 UI
///
/// 自动完成：
/// 1. 创建 Canvas（含 CanvasScaler）+ EventSystem
/// 2. 创建 DockPanel 完整 UI 层级
/// 3. 创建 DockItem 预制体
/// 4. 在 DesktopPet 上挂 DockToggle
/// 5. 所有引用自动连线
/// </summary>
public class DockSetupEditor : EditorWindow
{
    private const string PrefabDir = "Assets/Prefabs";

    [MenuItem("Tools/桌面宠/搭建收纳盘", false, 100)]
    public static void SetupDockPanel()
    {
        // ── 防止重复搭建：已有 DockPanelRoot 则询问删除 ──
        GameObject existingRoot = GameObject.Find("DockPanelRoot");
        if (existingRoot != null)
        {
            bool rebuild = EditorUtility.DisplayDialog(
                "收纳盘已存在",
                "场景中已有 DockPanelRoot，是否删除重建？",
                "是，删除重建", "否，取消");
            if (!rebuild) return;
            Undo.DestroyObjectImmediate(existingRoot);
        }

        // ── 0. 选中桌宠 ──
        GameObject pet = GameObject.Find("FuXuan");
        if (pet == null)
        {
            var found = GameObject.FindObjectOfType<DesktopPet>();
            if (found != null) pet = found.gameObject;
        }

        // ── 1. 确保 Canvas ──
        Canvas canvas = FindOrCreateCanvas();
        if (canvas == null)
        {
            Debug.LogError("[DockSetup] 无法创建 Canvas");
            return;
        }
        Undo.RecordObject(canvas.gameObject, "Setup DockPanel");

        // ── 2. 确保 EventSystem ──
        if (Object.FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(es, "Setup DockPanel");
        }

        // ── 3. 创建 DockPanel 根 ──
        GameObject dockRootObj = CreateUIObject("DockPanelRoot", canvas.transform);
        dockRootObj.AddComponent<DockPanel>();
        RectTransform dockRoot = dockRootObj.GetComponent<RectTransform>();
        if (dockRoot == null)
        {
            Debug.LogError("[DockSetup] dockRoot RectTransform 为空");
            return;
        }


        // pivot 右上角，锚点右上角
        dockRoot.pivot = new Vector2(1f, 1f);
        dockRoot.anchorMin = new Vector2(1f, 1f);
        dockRoot.anchorMax = new Vector2(1f, 1f);
        dockRoot.anchoredPosition = new Vector2(-10f, -10f);
        dockRoot.sizeDelta = new Vector2(280f, 380f);

        // ── 3a. collapsedBar（迷你条） ──
        GameObject collapsedBar = CreateUIObject("CollapsedBar", dockRoot);
        Image collapsedBg = collapsedBar.AddComponent<Image>();
        collapsedBg.color = new Color(0.12f, 0.12f, 0.12f, 0.85f);
        collapsedBar.AddComponent<Shadow>();

        RectTransform cr = collapsedBar.GetComponent<RectTransform>();
        cr.pivot = new Vector2(0.5f, 0.5f);
        cr.anchorMin = new Vector2(0.5f, 0.5f);
        cr.anchorMax = new Vector2(0.5f, 0.5f);
        cr.anchoredPosition = Vector2.zero;
        cr.sizeDelta = new Vector2(48f, 48f);

        // collapsedBar 子：图标文本 "📎"
        GameObject collapsedIcon = CreateUIObject("IconText", collapsedBar.transform);
        Text iconText = collapsedIcon.AddComponent<Text>();
        iconText.text = "📎";
        iconText.fontSize = 20;
        iconText.alignment = TextAnchor.MiddleCenter;
        iconText.color = Color.white;
        RectTransform iconRt = collapsedIcon.GetComponent<RectTransform>();
        iconRt.pivot = new Vector2(0.5f, 0.6f);
        iconRt.anchorMin = Vector2.zero;
        iconRt.anchorMax = Vector2.one;
        iconRt.sizeDelta = Vector2.zero;

        // collapsedBar 子：数量文本
        GameObject collapsedCountText = CreateUIObject("CountText", collapsedBar.transform);
        Text countText = collapsedCountText.AddComponent<Text>();
        countText.text = "0";
        countText.fontSize = 11;
        countText.alignment = TextAnchor.LowerCenter;
        countText.color = Color.white;
        RectTransform crt = collapsedCountText.GetComponent<RectTransform>();
        crt.anchorMin = Vector2.zero;
        crt.anchorMax = Vector2.one;
        crt.sizeDelta = Vector2.zero;
        crt.offsetMin = new Vector2(0, 2);
        crt.offsetMax = new Vector2(0, 0);

        // ── 3b. expandedPanel（展开面板） ──
        GameObject expandedPanel = CreateUIObject("ExpandedPanel", dockRoot);
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
        GameObject header = CreateUIObject("Header", expandedPanel.transform);
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
        GameObject clearBtn = CreateUIObject("ClearButton", header.transform);
        Button btn = clearBtn.AddComponent<Button>();
        Image btnBg = clearBtn.AddComponent<Image>();
        btnBg.color = new Color(0.6f, 0.1f, 0.1f, 0.6f);

        RectTransform btnRt = clearBtn.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(1, 0);
        btnRt.anchorMax = new Vector2(1, 1);
        btnRt.pivot = new Vector2(1, 0.5f);
        btnRt.sizeDelta = new Vector2(40, 22);
        btnRt.anchoredPosition = new Vector2(-4, 0);

        // Text 作为子对象（不能和 Image 在同一 GameObject）
        GameObject btnLabelObj = CreateUIObject("Label", clearBtn.transform);
        Text btnLabel = btnLabelObj.AddComponent<Text>();
        btnLabel.text = "🗑";
        btnLabel.fontSize = 14;
        btnLabel.alignment = TextAnchor.MiddleCenter;
        btnLabel.color = Color.white;
        btnLabel.raycastTarget = true;
        RectTransform blRt = btnLabelObj.GetComponent<RectTransform>();
        blRt.anchorMin = Vector2.zero;
        blRt.anchorMax = Vector2.one;
        blRt.sizeDelta = Vector2.zero;

        // ── 3c. iconGrid（图标网格） ──
        GameObject iconGrid = CreateUIObject("IconGrid", expandedPanel.transform);
        GridLayoutGroup grid = iconGrid.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(56f, 68f);
        grid.spacing = new Vector2(8f, 8f);
        grid.padding = new RectOffset(8, 8, 4, 8);
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;

        // ContentSizeFitter 让网格自适应高度
        ContentSizeFitter fitter = iconGrid.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform ig = iconGrid.GetComponent<RectTransform>();
        ig.anchorMin = new Vector2(0, 0);
        ig.anchorMax = new Vector2(1, 1);
        ig.offsetMin = new Vector2(0, 34);
        ig.offsetMax = new Vector2(0, -4);

        // ── 4. 创建 DockItem 预制体 ──
        GameObject dockItemObj = CreateUIObject("DockItem", iconGrid.transform);
        dockItemObj.SetActive(false);

        // iconImage
        GameObject itemIcon = CreateUIObject("Icon", dockItemObj.transform);
        Image iconImg = itemIcon.AddComponent<Image>();
        iconImg.color = new Color(0.5f, 0.5f, 0.5f);
        RectTransform ii = itemIcon.GetComponent<RectTransform>();
        ii.anchorMin = new Vector2(0.5f, 0.5f);
        ii.anchorMax = new Vector2(0.5f, 0.5f);
        ii.pivot = new Vector2(0.5f, 0.7f);
        ii.sizeDelta = new Vector2(48f, 48f);
        ii.anchoredPosition = new Vector2(0, 4);

        // fileNameLabel
        GameObject itemLabel = CreateUIObject("FileNameLabel", dockItemObj.transform);
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
        DockItem dockItemComp = dockItemObj.AddComponent<DockItem>();
        dockItemComp.iconImage = iconImg;
        dockItemComp.fileNameLabel = fnLabel;

        // 保存预制体
        if (!AssetDatabase.IsValidFolder(PrefabDir))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        string prefabPath = PrefabDir + "/DockItem.prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(dockItemObj, prefabPath);
        Object.DestroyImmediate(dockItemObj);

        Debug.Log($"[DockSetup] ✅ 预制体已保存: {prefabPath}");

        // ── 5. 连线 DockPanel ──
        DockPanel panel = dockRootObj.GetComponent<DockPanel>();
        var panelSerial = new SerializedObject(panel);

        SetField(panelSerial, "dockRoot", dockRoot);
        SetField(panelSerial, "collapsedBar", collapsedBar);
        SetField(panelSerial, "expandedPanel", expandedPanel);
        SetField(panelSerial, "iconGrid", iconGrid.transform);
        SetField(panelSerial, "dockItemPrefab", prefab);
        SetField(panelSerial, "clearButton", btn);
        SetField(panelSerial, "countLabel", headerLabel);
        SetField(panelSerial, "collapsedCountLabel", countText);

        panelSerial.ApplyModifiedProperties();

        // ── 6. DockToggle → 挂在桌宠身上 ──
        if (pet != null)
        {
            DockToggle toggle = pet.GetComponent<DockToggle>();
            if (toggle == null)
                toggle = Undo.AddComponent<DockToggle>(pet);

            var toggleSerial = new SerializedObject(toggle);
            SetField(toggleSerial, "dockPanel", panel);
            SetField(toggleSerial, "bagIcon", collapsedBar); // 默认用 collapsedBar 当开关图标
            toggleSerial.ApplyModifiedProperties();

            Debug.Log($"[DockSetup] ✅ DockToggle 已挂到 {pet.name}");
        }
        else
        {
            Debug.LogWarning("[DockSetup] ⚠️ 未找到 DesktopPet，DockToggle 需手动挂载");
        }

        // ── 7. 自动保存场景 ──
        // ★ 关键：不保存的话构建时拿的是旧场景，UI 就没了！
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Undo.RegisterCreatedObjectUndo(dockRootObj, "Setup DockPanel");
        Selection.activeGameObject = dockRootObj;

        Debug.Log("[DockSetup] 🎉 收纳盘 UI 搭建完成！场景已保存。");
    }

    // ── 工具方法 ──

    private static Canvas FindOrCreateCanvas()
    {
        Canvas existing = Object.FindObjectOfType<Canvas>();
        if (existing != null)
            return existing;

        // 创建 Canvas
        GameObject go = new GameObject("DockCanvas", typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // 确保在顶层

        // CanvasScaler
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        Debug.Log("[DockSetup] 已创建 Canvas: DockCanvas");
        return canvas;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void SetField(SerializedObject serial, string fieldName, Object value)
    {
        var prop = serial.FindProperty(fieldName);
        if (prop != null)
            prop.objectReferenceValue = value;
        else
            Debug.LogWarning($"[DockSetup] 找不到字段 {fieldName}");
    }
}
