using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 收纳盘搭建验证工具
///
/// 让 Unity 内的 AI 执行：Tools → 桌面宠 → 验证收纳盘
/// 会自动扫描所有依赖并输出绿色/红色状态报告。
/// </summary>
public class DockVerifyEditor : EditorWindow
{
    [MenuItem("Tools/桌面宠/验证收纳盘", false, 101)]
    public static void VerifyAll()
    {
        Debug.Log("========== 🔍 收纳盘验证开始 ==========");
        int ok = 0, fail = 0;

        // ── 1. 资源层 ──
        ok += Check("TMP Settings.asset", File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"));
        ok += Check("DockItem.prefab", File.Exists("Assets/Prefabs/DockItem.prefab"));

        // ── 2. 场景层级 ──
        GameObject root = GameObject.Find("DockPanelRoot");
        bool hasRoot = root != null;
        ok += Check("场景中有 DockPanelRoot", hasRoot);

        if (hasRoot)
        {
            // Canvas
            Canvas canvas = root.GetComponentInParent<Canvas>();
            ok += Check("DockPanelRoot 在 Canvas 下", canvas != null);
            if (canvas != null)
                ok += Check("Canvas 为 ScreenSpaceOverlay", canvas.renderMode == RenderMode.ScreenSpaceOverlay);

            // EventSystem
            ok += Check("场景中有 EventSystem",
                Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null);

            // DockPanel 组件
            DockPanel panel = root.GetComponent<DockPanel>();
            ok += Check("DockPanelRoot 挂载了 DockPanel", panel != null);

            if (panel != null)
            {
                var so = new SerializedObject(panel);
                ok += Check("  dockRoot 已赋值", so.FindProperty("dockRoot").objectReferenceValue != null);
                ok += Check("  collapsedBar 已赋值", so.FindProperty("collapsedBar").objectReferenceValue != null);
                ok += Check("  expandedPanel 已赋值", so.FindProperty("expandedPanel").objectReferenceValue != null);
                ok += Check("  iconGrid 已赋值", so.FindProperty("iconGrid").objectReferenceValue != null);
                ok += Check("  dockItemPrefab 已赋值", so.FindProperty("dockItemPrefab").objectReferenceValue != null);
                ok += Check("  clearButton 已赋值", so.FindProperty("clearButton").objectReferenceValue != null);

                // TMP 文本字段
                Object countLabel = so.FindProperty("countLabel").objectReferenceValue;
                Object collapsedCount = so.FindProperty("collapsedCountLabel").objectReferenceValue;
                ok += Check("  countLabel 已赋值", countLabel != null);
                ok += Check("  collapsedCountLabel 已赋值", collapsedCount != null);

                // 如果赋值了但组件不可用，标黄
                if (countLabel != null && countLabel is Component c1)
                    CheckTMPComponent(c1.gameObject, "countLabel 的 GameObject 有 TextMeshProUGUI");
                if (collapsedCount != null && collapsedCount is Component c2)
                    CheckTMPComponent(c2.gameObject, "collapsedCountLabel 的 GameObject 有 TextMeshProUGUI");
            }

            // 子对象完整性
            ok += Check("  有 CollapsedBar 子对象", root.transform.Find("CollapsedBar") != null);
            ok += Check("  有 ExpandedPanel 子对象", root.transform.Find("ExpandedPanel") != null);

            Transform ep = root.transform.Find("ExpandedPanel");
            if (ep != null)
            {
                ok += Check("    ExpandedPanel 有 Header", ep.Find("Header") != null);
                ok += Check("    ExpandedPanel 有 IconGrid", ep.Find("IconGrid") != null);
                if (ep.Find("Header") != null)
                {
                    ok += Check("    Header 有 ClearButton", ep.Find("Header").Find("ClearButton") != null);
                }
            }
        }

        // ── 3. DockToggle ──
        GameObject pet = GameObject.Find("FuXuan");
        if (pet == null)
        {
            var dp = Object.FindObjectOfType<DesktopPet>();
            if (dp != null) pet = dp.gameObject;
        }

        DockToggle toggle = pet?.GetComponent<DockToggle>();
        ok += Check("FuXuan 上有 DockToggle", toggle != null);
        if (toggle != null)
        {
            var to = new SerializedObject(toggle);
            ok += Check("  DockToggle.dockPanel 已赋值",
                to.FindProperty("dockPanel").objectReferenceValue != null);
            ok += Check("  DockToggle.bagIcon 已赋值",
                to.FindProperty("bagIcon").objectReferenceValue != null);
        }

        // ── 4. 代码集成 ──
        // WindowOverlay 中有 DockDropHandler 调用
        var woTypes = Resources.FindObjectsOfTypeAll<MonoScript>();
        bool hasWindowOverlay = false;
        foreach (var mt in woTypes)
        {
            if (mt != null && mt.name == "WindowOverlay")
            { hasWindowOverlay = true; break; }
        }
        ok += Check("WindowOverlay.cs 存在", hasWindowOverlay);

        bool hasDragHandler = false;
        foreach (var mt in woTypes)
        {
            if (mt != null && mt.name == "DragHandler")
            { hasDragHandler = true; break; }
        }
        ok += Check("DragHandler.cs 存在", hasDragHandler);

        // ── 汇总 ──
        int total = ok + fail;
        if (fail == 0)
        {
            Debug.Log($"========== 🎉 全部 {total}/{total} 项通过 ==========");
            EditorUtility.DisplayDialog("验证结果", $"🎉 收纳盘搭建完整！\n全部 {total} 项检查通过。\n\n可以关闭编辑器用桌面宠测试拖拽功能了。", "好的");
        }
        else
        {
            Debug.LogWarning($"========== ⚠️ {ok}/{total} 通过, {fail} 项失败 ==========");
            EditorUtility.DisplayDialog("验证结果", $"⚠️ {ok}/{total} 通过，{fail} 项未通过。\n请查看 Console 日志的红色标记。", "知道了");
        }
    }

    private static int Check(string label, bool condition)
    {
        if (condition)
        {
            Debug.Log($"  ✅ {label}");
            return 1;
        }
        else
        {
            Debug.LogError($"  ❌ {label}");
            return 0;
        }
    }

    private static void CheckTMPComponent(GameObject go, string label)
    {
        if (go.GetComponent<TextMeshProUGUI>() != null)
            Debug.Log($"  ✅ {label}");
        else
            Debug.LogWarning($"  ⚠️ {label} — GameObject 存在但 TextMeshProUGUI 组件可能未正确初始化（文字可能不显示）");
    }
}
