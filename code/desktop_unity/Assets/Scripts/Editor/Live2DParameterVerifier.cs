using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Live2D 参数验证器 — Editor 可视化工具
///
/// 工作流：
/// 1. 从模型加载所有参数 + 从 cdi3.json 加载中文显示名
/// 2. Editor 窗口中显示列表，每个参数带滑块，可拖拽观察模型变化
/// 3. 验证后可直接将语义名 ↔ 参数 ID 写入映射 JSON
/// 4. 支持 AI 辅助分析（生成提示文本供 AI 理解参数行为）
///
/// 使用方式：
/// - 选中挂载了 CubismModel 的 GameObject
/// - Tools → Live2D → 参数验证器
/// </summary>
#if UNITY_EDITOR
public class Live2DParameterVerifier : EditorWindow
{
    [SerializeField] private CubismModel _model;
    [SerializeField] private GameObject _modelObject;
    private CubismParametersInspector _inspector;

    // 加载状态
    private enum LoadState { None, Creating, WaitingForInit, Ready }
    private LoadState _loadState = LoadState.None;
    private int _waitFrames = 0;

    // 参数列表
    private Vector2 _scrollPos;
    private List<ParamEntry> _params = new List<ParamEntry>();
    private Dictionary<string, string> _cdiNames = new Dictionary<string, string>(); // ParamId → 中文名
    private bool _paramsLoaded = false;

    // 映射管理
    private Dictionary<string, string> _semanticToId = new Dictionary<string, string>(); // 语义名 → ParamId
    private Dictionary<string, string> _idToSemantic = new Dictionary<string, string>(); // ParamId → 语义名
    private Dictionary<string, string> _idToDescription = new Dictionary<string, string>(); // ParamId → 中文描述
    private string _mapFilePath = "Assets/Scripts/Live2DFramework/ParamMaps/fuxuan_map.json";
    private Vector2 _semanticScrollPos;

    // 过滤/搜索
    private string _searchFilter = "";
    private string _groupFilter = "all"; // all, mapped, unmapped
    private string _bodyPartFilter = "all";

    // 显示模式
    private enum ViewMode { Sliders, MappingEditor, AI_Report }
    private ViewMode _viewMode = ViewMode.Sliders;
    private int _selectedParamIndex = -1;

    // AI 分析
    private string _aiPrompt = "";
    private StringBuilder _aiReport = new StringBuilder();

    // 自动验证模式
    private bool _autoAdvance = false;
    private int _autoIndex = 0;
    private float _autoTimer = 0f;

    // 预设语义名（用于映射编辑器）
    private static readonly string[] PRESET_SEMANTICS = new string[]
    {
        "body_angle_x", "body_angle_y", "body_angle_z",
        "head_angle_x", "head_angle_y", "head_angle_z",
        "breath",
        "eye_l_open", "eye_r_open", "eye_ball_x", "eye_ball_y",
        "eye_l_smile", "eye_r_smile", "eye_heart",
        "brow_r_y", "brow_l_y", "brow_l_angle", "brow_r_angle",
        "mouth_form", "mouth_open_y",
        "arm_right_upper", "arm_right_mid", "arm_right_lower",
        "arm_right_rotation", "arm_right_base_rotation",
        "arm_right_switch", "arm_right_reach", "arm_right_wrist_z",
        "arm_left_upper", "arm_left_mid", "arm_left_lower",
        "arm_left_extra", "arm_left_extra2",
        "hand_layer_95", "hand_layer_117", "hand_layer_98",
        "hand_layer_100", "hand_layer_116", "hand_layer_120",
        "hand_layer_108", "hand_layer_119",
        "leg_l_lift", "leg_r_lift", "leg_l_swing", "leg_l_bend",
        "leg_r_swing", "leg_r_bend", "shoulder",
        "hair_bangs_1", "hair_bangs_2", "hair_bangs_3",
        "hair_physics_1", "hair_physics_2", "hair_physics_3",
        "hair_back_b_1", "hair_back_b_2",
        "hair_side_1", "hair_side_2", "hair_side_3",
        "hair_back_1", "hair_back_2", "hair_back_3", "hair_back_4",
        "hair_ornament_1", "hair_ornament_2", "hair_ornament_3", "hair_head_ornament",
        "skirt_drive_1", "skirt_drive_2", "skirt_drive_3",
        "skirt_drive_4", "skirt_drive_5", "skirt_drive_6", "skirt_drive_7",
        "special_money", "special_tear", "special_blush_dark",
        "special_angry", "special_outer_mask",
        "sword_finger_switch",
        "finger_normal_1", "finger_normal_2", "finger_normal_3",
        "finger_normal_4", "finger_normal_5",
        "finger_z_rotate", "finger_thumb", "finger_index",
        "finger_middle", "finger_ring", "finger_pinky",
        "camera_x", "camera_y", "character_scale"
    };

    // 参数条目
    private class ParamEntry
    {
        public CubismParameter parameter;
        public string id;
        public string cdiName;       // 来自 cdi3.json 的中文名
        public string semantic;      // 已映射的语义名（null=未映射）
        public string description;   // 中文描述（来自 JSON 或手动填写）
        public float min;
        public float max;
        public float defaultValue;
        public float sliderValue;    // Editor 滑块当前值
        public bool verified;        // 是否已人工确认
        public string verifiedNote;  // 验证备注（"控制左手"等）
        public bool pinned;          // 固定在列表顶部
    }

    [MenuItem("Tools/Live2D/参数验证器")]
    private static void OpenWindow()
    {
        var window = GetWindow<Live2DParameterVerifier>("Live2D 参数验证器");
        window.minSize = new Vector2(500, 400);
        window.Show();
    }

    private void OnEnable()
    {
        // 尝试自动加载映射
        LoadExistingMapping();

        // 重编译后自动恢复参数列表（_paramsLoaded 非序列化会重置）
        if (_model != null)
        {
            _paramsLoaded = false;
            _loadState = LoadState.WaitingForInit;
            _waitFrames = 0;
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(5);

        // === 工具栏 ===
        DrawToolbar();

        // === 模型选择 ===
        DrawModelSelector();

        // 状态机：自动等待 SDK 初始化
        if (_loadState == LoadState.WaitingForInit)
        {
            _waitFrames++;
            if (_model != null && _model.Parameters != null && _model.Parameters.Length > 0)
            {
                _loadState = LoadState.Ready;
                LoadModelParameters();
            }
            else if (_waitFrames > 600)
            {
                // 超时（~10秒），停止等待
                _loadState = LoadState.None;
                Debug.LogWarning("[参数验证器] 等待模型初始化超时，请点击「🔄 刷新」");
            }
            else
            {
                // 显示等待动画
                EditorGUILayout.HelpBox($"⏳ 等待模型初始化... ({_waitFrames / 60:F0}s)", MessageType.Info);
                Repaint();
                return;
            }
        }

        if (_model == null)
        {
            EditorGUILayout.HelpBox("请点击上方「🎯 创建符玄模型」按钮，或手动拖入模型", MessageType.Info);
            return;
        }

        if (!_paramsLoaded)
        {
            // 没有加载中状态时显示按钮
            if (_loadState != LoadState.WaitingForInit)
            {
                if (GUILayout.Button("🔄 加载参数（模型已就绪）", GUILayout.Height(30)))
                    LoadModelParameters();
            }
            return;
        }

        EditorGUILayout.Space(5);

        switch (_viewMode)
        {
            case ViewMode.Sliders:
                DrawSliderView();
                break;
            case ViewMode.MappingEditor:
                DrawMappingEditor();
                break;
            case ViewMode.AI_Report:
                DrawAIReportView();
                break;
        }
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        string[] tabs = { "🎚 滑块验证", "📋 映射编辑", "🤖 AI 报告" };
        var modeValues = new ViewMode[] { ViewMode.Sliders, ViewMode.MappingEditor, ViewMode.AI_Report };
        for (int i = 0; i < tabs.Length; i++)
        {
            var isActive = _viewMode == modeValues[i];
            var style = isActive ? EditorStyles.toolbarButton : new GUIStyle(EditorStyles.toolbarButton);
            if (isActive)
                style.normal = EditorStyles.toolbarButton.active;

            if (GUILayout.Button(tabs[i], EditorStyles.toolbarButton, GUILayout.Width(120)))
                _viewMode = modeValues[i];
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("🔄 刷新", EditorStyles.toolbarButton, GUILayout.Width(60)))
            LoadModelParameters();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawModelSelector()
    {
        // 如果还没绑定模型，显示一键创建按钮
        if (_modelObject == null && _model == null)
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField("📌 快速开始", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("点击下方按钮自动在场景中创建符玄模型：", EditorStyles.wordWrappedLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🎯 创建符玄模型", GUILayout.Height(35)))
            {
                CreateModelInScene("Assets/Live2D/Models/Fuxuan/符玄.prefab");
            }

            // 也支持从 Prefabs 目录
            if (GUILayout.Button("创建 FuXuan (封装版)", GUILayout.Height(35)))
            {
                CreateModelInScene("Assets/Prefabs/FuXuan.prefab");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("或者手动拖入模型 GameObject 到下方：", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.BeginHorizontal();

        var newObj = (GameObject)EditorGUILayout.ObjectField("模型 GameObject", _modelObject, typeof(GameObject), true);

        // 如果拖入的不是场景对象（如 prefab 资源），自动实例化
        if (newObj != null && newObj.scene.name == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(newObj);
            if (instance != null)
            {
                instance.name = newObj.name;
                Selection.activeGameObject = instance;
                newObj = instance;
                Debug.Log($"[参数验证器] 已自动实例化模型到场景: {instance.name}");
            }
        }

        if (newObj != _modelObject)
        {
            _modelObject = newObj;
            // 尝试多种方式查找 CubismModel
            _model = TryFindCubismModel(_modelObject);
            _paramsLoaded = false;
            if (_model != null)
                LoadModelParameters();
            else
                Repaint();
        }

        EditorGUILayout.EndHorizontal();

        // 诊断信息 — 帮用户找到问题
        if (_modelObject != null && _model == null)
        {
            EditorGUILayout.HelpBox(
                "未找到 CubismModel 组件。\n" +
                "请拖入 Assets/Live2D/Models/Fuxuan/ 下的「符玄」预制体，\n" +
                "或已在场景中的符玄模型 GameObject",
                MessageType.Warning);

            if (GUILayout.Button("🔍 扫描子对象查找 CubismModel"))
            {
                _model = _modelObject.GetComponentInChildren<CubismModel>(true);
                if (_model != null)
                {
                    Debug.Log($"[参数验证器] 在子对象中找到 CubismModel: {_model.name}");
                    _paramsLoaded = false;
                    LoadModelParameters();
                }
                else
                {
                    // 列出所有组件帮用户排查
                    var components = _modelObject.GetComponentsInChildren<Component>(true);
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    sb.AppendLine("该对象上的组件列表:");
                    foreach (var c in components)
                        if (c != null) sb.AppendLine($"  - {c.GetType().Name} ({c.name})");
                    Debug.LogWarning($"[参数验证器] 未找到 CubismModel。\n{sb}");
                    EditorUtility.DisplayDialog("诊断结果",
                        $"在 {_modelObject.name} 及其子对象上未找到 CubismModel 组件。\n" +
                        $"请确保拖入正确的符玄 Live2D 模型对象。\n" +
                        $"详情请查看 Console 日志。", "确定");
                }
            }
        }

        // 显示模型信息
        if (_model != null)
        {
            EditorGUILayout.LabelField($"模型: {_model.name}  参数: {_model.Parameters?.Length ?? 0}", EditorStyles.miniLabel);
        }
    }

    /// <summary>在场景中创建模型</summary>
    private void CreateModelInScene(string prefabPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[参数验证器] 找不到预制体: {prefabPath}");
            EditorUtility.DisplayDialog("错误", $"找不到预制体:\n{prefabPath}\n\n请手动拖入模型。", "确定");
            return;
        }

        // 检查场景中是否已有
        var existing = GameObject.Find(prefab.name) ?? GameObject.Find("符玄");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("模型已存在",
                $"场景中已有「{existing.name}」，是否使用它？", "使用", "重新创建"))
            {
                DestroyImmediate(existing);
            }
            else
            {
                _modelObject = existing;
                _model = TryFindCubismModel(existing);
                _paramsLoaded = false;
                if (_model != null && _model.Parameters != null && _model.Parameters.Length > 0)
                    LoadModelParameters();
                else
                    _loadState = LoadState.WaitingForInit;
                return;
            }
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (instance == null) return;

        instance.transform.position = Vector3.zero;
        _modelObject = instance;

        // 查找 CubismModel 组件（虽然 SDK 还没初始化）
        _model = TryFindCubismModel(instance);
        if (_model == null)
        {
            EditorUtility.DisplayDialog("警告",
                $"已创建模型但未找到 CubismModel 组件。\n请点「扫描子对象」按钮排查。", "确定");
            return;
        }

        Selection.activeGameObject = instance;
        _paramsLoaded = false;

        // 自动聚焦 Scene 视角到模型
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
            SceneView.lastActiveSceneView.LookAt(instance.transform.position + Vector3.one * 3f, Quaternion.LookRotation(-Vector3.forward), 5f);
        }

        // 启动状态机等待 SDK 初始化
        _loadState = LoadState.WaitingForInit;
        _waitFrames = 0;
        Debug.Log($"[参数验证器] 🎯 已创建模型「{instance.name}」，等待 SDK 初始化...");
    }

    /// <summary>多级查找 CubismModel 组件</summary>
    private CubismModel TryFindCubismModel(GameObject go)
    {
        if (go == null) return null;

        // 1. 直接 GetComponent
        var model = go.GetComponent<CubismModel>();
        if (model != null) return model;

        // 2. GetComponentInChildren (含非激活)
        model = go.GetComponentInChildren<CubismModel>(true);
        if (model != null) return model;

        // 3. 在父级链上找（有时附加脚本在父级）
        model = go.GetComponentInParent<CubismModel>();
        if (model != null) return model;

        return null;
    }

    /// <summary>加载模型所有参数 + cdi3 中文名</summary>
    private void LoadModelParameters()
    {
        if (_model == null)
        {
            Debug.LogWarning("[参数验证器] _model 为空");
            return;
        }

        var parameters = _model.Parameters;
        if (parameters == null || parameters.Length == 0)
        {
            Debug.LogWarning($"[参数验证器] _model.Parameters 为空，CubismSDK 尚未初始化完成");
            _paramsLoaded = false;
            EditorUtility.DisplayDialog("参数未就绪",
                "模型参数尚未初始化完成，请点击「🔄 刷新」按钮重试。", "确定");
            return;
        }

        _params.Clear();
        _cdiNames.Clear();

        // 1. 从 cdi3.json 加载中文名
        LoadCdiNames();

        // 2. 枚举模型参数
        foreach (var p in _model.Parameters)
        {
            string id = p.Id;

            // 查找已有映射
            string semantic = _idToSemantic.ContainsKey(id) ? _idToSemantic[id] : null;

            _params.Add(new ParamEntry
            {
                parameter = p,
                id = id,
                cdiName = _cdiNames.ContainsKey(id) ? _cdiNames[id] : "",
                semantic = semantic,
                description = _idToDescription.ContainsKey(id) ? _idToDescription[id] : "",
                min = p.MinimumValue,
                max = p.MaximumValue,
                defaultValue = p.DefaultValue,
                sliderValue = p.Value,
                verified = semantic != null, // 已有映射视为"已验证"
                pinned = semantic != null
            });
        }

        // 已映射的排前面
        _params.Sort((a, b) =>
        {
            if (a.pinned != b.pinned) return a.pinned ? -1 : 1;
            return string.Compare(a.id, b.id, StringComparison.Ordinal);
        });

        _paramsLoaded = true;
        _loadState = LoadState.Ready;
        Debug.Log($"[参数验证器] 已加载 {_params.Count} 个参数，其中 {_cdiNames.Count} 个有中文名");
    }

    /// <summary>读取 cdi3.json 中的参数中文名</summary>
    private void LoadCdiNames()
    {
        _cdiNames.Clear();

        if (_model == null) return;

        // 搜索模型所在目录的 cdi3.json
        string modelPath = AssetDatabase.GetAssetPath(_model);
        if (string.IsNullOrEmpty(modelPath)) return;
        string modelDir = Path.GetDirectoryName(modelPath);
        string cdiPath = "";

        if (modelDir != null)
        {
            // 在模型目录及同级目录搜索
            var cdiFiles = Directory.GetFiles(modelDir, "*.cdi3.json", SearchOption.AllDirectories);
            if (cdiFiles.Length > 0)
                cdiPath = cdiFiles[0];
        }

        if (string.IsNullOrEmpty(cdiPath) || !File.Exists(cdiPath))
        {
            Debug.LogWarning("[参数验证器] 未找到 cdi3.json 文件，将仅使用参数 ID");
            return;
        }

        try
        {
            string json = File.ReadAllText(cdiPath);
            var cdiData = JsonUtility.FromJson<Cdi3Json>(json);
            if (cdiData?.Parameters != null)
            {
                foreach (var param in cdiData.Parameters)
                {
                    if (!string.IsNullOrEmpty(param.Id) && !string.IsNullOrEmpty(param.Name))
                        _cdiNames[param.Id] = param.Name;
                }
                Debug.Log($"[参数验证器] 从 {Path.GetFileName(cdiPath)} 加载了 {_cdiNames.Count} 个中文名");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[参数验证器] 读取 cdi3.json 失败: {ex.Message}");
        }
    }

    /// <summary>加载已有的映射文件</summary>
    private void LoadExistingMapping()
    {
        _semanticToId.Clear();
        _idToSemantic.Clear();
        _idToDescription.Clear();

        string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", _mapFilePath));
        // 也尝试相对于项目
        string altPath = Path.Combine(Application.dataPath, "Scripts/Live2DFramework/ParamMaps/fuxuan_map.json");

        string jsonPath = File.Exists(fullPath) ? fullPath :
                          File.Exists(altPath) ? altPath : null;

        if (jsonPath == null) return;

        try
        {
            string json = File.ReadAllText(jsonPath);

            // 解析 entries 格式
            var wrapper = JsonUtility.FromJson<ParamMapWrapper>(json);
            if (wrapper?.entries != null)
            {
                foreach (var entry in wrapper.entries)
                {
                    if (string.IsNullOrEmpty(entry.s) || string.IsNullOrEmpty(entry.p)) continue;
                    _semanticToId[entry.s] = entry.p;
                    _idToSemantic[entry.p] = entry.s;
                    if (!string.IsNullOrEmpty(entry.d))
                        _idToDescription[entry.p] = entry.d;
                }
                Debug.Log($"[参数验证器] 已加载 {_semanticToId.Count} 条映射");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[参数验证器] 加载映射文件失败（不影响功能）: {ex.Message}");
        }
    }

    // ===================================================================
    //  视图1: 滑块验证
    // ===================================================================
    private void DrawSliderView()
    {
        // 搜索/过滤栏
        EditorGUILayout.BeginHorizontal();
        _searchFilter = EditorGUILayout.TextField("🔍 搜索", _searchFilter);

        string[] groupOptions = { "全部", "已映射", "未映射" };
        string[] groupValues = { "all", "mapped", "unmapped" };
        int currentGroupIdx = Array.IndexOf(groupValues, _groupFilter);
        if (currentGroupIdx < 0) currentGroupIdx = 0;
        int newGroupIdx = EditorGUILayout.Popup(currentGroupIdx, groupOptions, GUILayout.Width(90));
        _groupFilter = groupValues[newGroupIdx];

        EditorGUILayout.EndHorizontal();

        // 计数
        int total = _params.Count;
        int mapped = 0, verified = 0;
        foreach (var p in _params) { if (p.semantic != null) mapped++; if (p.verified) verified++; }
        EditorGUILayout.LabelField($"参数: {total} | 已映射: {mapped} | 已验证: {verified}");

        EditorGUILayout.Space(3);

        // 批量操作
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("重置所有为默认值", GUILayout.Width(150)))
            ResetAllToDefault();
        if (GUILayout.Button("收起所有", GUILayout.Width(100)))
            _selectedParamIndex = -1;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // 参数列表
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        for (int i = 0; i < _params.Count; i++)
        {
            var entry = _params[i];

            // 搜索过滤
            if (!string.IsNullOrEmpty(_searchFilter))
            {
                string lower = _searchFilter.ToLower();
                bool match = entry.id.ToLower().Contains(lower) ||
                             entry.cdiName.ToLower().Contains(lower) ||
                             (entry.semantic != null && entry.semantic.ToLower().Contains(lower));
                if (!match) continue;
            }

            // 分组过滤
            if (_groupFilter == "mapped" && entry.semantic == null) continue;
            if (_groupFilter == "unmapped" && entry.semantic != null) continue;

            DrawParamSlider(entry, i);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawParamSlider(ParamEntry entry, int index)
    {
        bool isSelected = (_selectedParamIndex == index);
        float range = Mathf.Abs(entry.max - entry.min);

        // 卡片背景
        var bgStyle = new GUIStyle(GUI.skin.box);
        if (isSelected)
            bgStyle.normal.background = MakeTexture(1, 1, new Color(0.2f, 0.3f, 0.5f, 0.3f));
        else if (entry.verified)
            bgStyle.normal.background = MakeTexture(1, 1, new Color(0.1f, 0.5f, 0.1f, 0.15f));
        else if (entry.semantic != null)
            bgStyle.normal.background = MakeTexture(1, 1, new Color(0.5f, 0.5f, 0.1f, 0.15f));
        else
            bgStyle.normal.background = MakeTexture(1, 1, new Color(0.3f, 0.3f, 0.3f, 0.1f));

        EditorGUILayout.BeginVertical(bgStyle);
        EditorGUILayout.BeginHorizontal();

        // 标题行
        string label = entry.id;
        if (!string.IsNullOrEmpty(entry.cdiName))
            label += $" 「{entry.cdiName}」";
        if (entry.semantic != null)
            label += $" → {entry.semantic}";
        if (!string.IsNullOrEmpty(entry.description))
            label += $"  — {entry.description}";

        GUILayout.Label(label, GUILayout.MinWidth(200));

        GUILayout.FlexibleSpace();

        // 状态标记
        if (entry.verified)
            GUILayout.Label("✅", GUILayout.Width(20));
        else if (entry.semantic != null)
            GUILayout.Label("⏳", GUILayout.Width(20));
        else
            GUILayout.Label("⬜", GUILayout.Width(20));

        // 展开/折叠
        if (GUILayout.Button(isSelected ? "▲" : "▼", GUILayout.Width(25)))
            _selectedParamIndex = isSelected ? -1 : index;

        EditorGUILayout.EndHorizontal();

        // 范围信息行
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"范围: [{entry.min:F1}, {entry.max:F1}]  默认: {entry.defaultValue:F2}",
            EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        // 滑块
        EditorGUILayout.BeginHorizontal();
        float newVal = EditorGUILayout.Slider(entry.sliderValue, entry.min, entry.max);
        if (Math.Abs(newVal - entry.sliderValue) > 0.001f)
        {
            entry.sliderValue = newVal;
            entry.parameter.Value = newVal;
            if (_model != null) _model.ForceUpdateNow();
            SceneView.RepaintAll();
        }
        if (GUILayout.Button("复位", GUILayout.Width(40)))
        {
            entry.sliderValue = entry.defaultValue;
            entry.parameter.Value = entry.defaultValue;
            if (_model != null) _model.ForceUpdateNow();
            SceneView.RepaintAll();
        }
        EditorGUILayout.EndHorizontal();

        // 展开后的详细控制面板
        if (isSelected)
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("--- 验证面板 ---", EditorStyles.boldLabel);

            // 验证备注
            entry.verifiedNote = EditorGUILayout.TextField("观察到的作用:", entry.verifiedNote);

            // 中文描述
            EditorGUI.BeginChangeCheck();
            string newDesc = EditorGUILayout.TextField("中文描述:", entry.description);
            if (EditorGUI.EndChangeCheck())
            {
                entry.description = newDesc;
                _idToDescription[entry.id] = newDesc;
            }

            EditorGUILayout.BeginHorizontal();

            // 快捷语义名选择
            if (entry.semantic == null)
            {
                // 显示预设语义名下拉
                int selectedIdx = Array.IndexOf(PRESET_SEMANTICS, entry.semantic ?? "");
                int newIdx = EditorGUILayout.Popup("语义名", Math.Max(0, selectedIdx), PRESET_SEMANTICS);
                if (newIdx >= 0 && newIdx < PRESET_SEMANTICS.Length)
                {
                    string chosen = PRESET_SEMANTICS[newIdx];
                    if (chosen != entry.semantic)
                    {
                        entry.semantic = chosen;
                        _semanticToId[chosen] = entry.id;
                        _idToSemantic[entry.id] = chosen;
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField($"语义名: {entry.semantic}");
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("✅ 确认映射", GUILayout.Height(25)))
            {
                entry.verified = true;
                if (string.IsNullOrEmpty(entry.semantic))
                {
                    entry.semantic = entry.id.ToLower();
                    _semanticToId[entry.semantic] = entry.id;
                    _idToSemantic[entry.id] = entry.semantic;
                }
                _selectedParamIndex = -1;
            }

            GUI.color = Color.yellow;
            if (GUILayout.Button("⏭ 跳过", GUILayout.Height(25)))
            {
                _selectedParamIndex = -1;
            }
            GUI.color = Color.white;

            if (entry.verified && GUILayout.Button("❌ 取消验证", GUILayout.Height(25)))
            {
                entry.verified = false;
            }

            EditorGUILayout.EndHorizontal();

            // 极值快速测试按钮
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("测试 Min"))
            {
                entry.sliderValue = entry.min;
                entry.parameter.Value = entry.min;
            }
            if (GUILayout.Button("测试 Max"))
            {
                entry.sliderValue = entry.max;
                entry.parameter.Value = entry.max;
            }
            if (GUILayout.Button("测试 默认"))
            {
                entry.sliderValue = entry.defaultValue;
                entry.parameter.Value = entry.defaultValue;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
    }

    // ===================================================================
    //  视图2: 映射编辑器
    // ===================================================================
    private void DrawMappingEditor()
    {
        EditorGUILayout.BeginHorizontal();

        // 文件路径
        _mapFilePath = EditorGUILayout.TextField("映射文件路径", _mapFilePath);

        if (GUILayout.Button("浏览...", GUILayout.Width(80)))
        {
            string selected = EditorUtility.OpenFilePanel("选择映射 JSON", Application.dataPath, "json");
            if (!string.IsNullOrEmpty(selected))
            {
                // 转为相对于 Assets 的路径
                string dataPath = Application.dataPath.Replace("\\", "/");
                string selectedNorm = selected.Replace("\\", "/");
                if (selectedNorm.StartsWith(dataPath))
                    _mapFilePath = "Assets" + selectedNorm.Substring(dataPath.Length);
                else
                    _mapFilePath = selected;
                LoadExistingMapping();
            }
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // 统计
        EditorGUILayout.LabelField($"语义映射: {_semanticToId.Count} 条 | 模型参数: {_params.Count} 个");

        EditorGUILayout.Space(3);

        // 操作按钮
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("📥 从滑块验证同步映射"))
        {
            SyncFromSliders();
        }
        if (GUILayout.Button("📤 保存映射到文件"))
        {
            SaveMappingToFile();
        }
        if (GUILayout.Button("📋 从 JSON 加载"))
        {
            LoadExistingMapping();
            // 同步到 ParamEntry
            foreach (var entry in _params)
            {
                if (_idToSemantic.TryGetValue(entry.id, out string sem))
                {
                    entry.semantic = sem;
                    entry.verified = true;
                }
                if (_idToDescription.TryGetValue(entry.id, out string desc))
                    entry.description = desc;
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);

        // 映射表格
        _semanticScrollPos = EditorGUILayout.BeginScrollView(_semanticScrollPos);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("语义名", EditorStyles.boldLabel, GUILayout.Width(150));
        EditorGUILayout.LabelField("参数 ID", EditorStyles.boldLabel, GUILayout.Width(120));
        EditorGUILayout.LabelField("CDI 名", EditorStyles.boldLabel, GUILayout.Width(120));
        EditorGUILayout.LabelField("描述", EditorStyles.boldLabel, GUILayout.Width(150));
        EditorGUILayout.LabelField("", GUILayout.Width(30));
        EditorGUILayout.EndHorizontal();

        var keys = new List<string>(_semanticToId.Keys);
        keys.Sort();

        foreach (var sem in keys)
        {
            string paramId = _semanticToId[sem];
            string cdiName = _cdiNames.ContainsKey(paramId) ? _cdiNames[paramId] : "";
            string desc = _idToDescription.ContainsKey(paramId) ? _idToDescription[paramId] : "";

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(sem, GUILayout.Width(150));
            EditorGUILayout.LabelField(paramId, GUILayout.Width(120));

            // 允许修改 CDI 名
            GUI.color = Color.yellow;
            EditorGUILayout.LabelField(cdiName, GUILayout.Width(120));
            GUI.color = Color.white;

            // 描述字段（可编辑）
            string newDesc = EditorGUILayout.TextField(desc, GUILayout.Width(150));
            if (newDesc != desc)
            {
                _idToDescription[paramId] = newDesc;
            }

            if (GUILayout.Button("✕", GUILayout.Width(25)))
            {
                _semanticToId.Remove(sem);
                _idToSemantic.Remove(paramId);
                // 同步更新 Params
                foreach (var entry in _params)
                {
                    if (entry.semantic == sem)
                    {
                        entry.semantic = null;
                        entry.verified = false;
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    /// <summary>从滑块验证视图同步映射关系</summary>
    private void SyncFromSliders()
    {
        int added = 0;
        foreach (var entry in _params)
        {
            if (entry.verified && !string.IsNullOrEmpty(entry.semantic))
            {
                if (!_semanticToId.ContainsKey(entry.semantic))
                {
                    _semanticToId[entry.semantic] = entry.id;
                    _idToSemantic[entry.id] = entry.semantic;
                    added++;
                }
                // 同步描述
                if (!string.IsNullOrEmpty(entry.description))
                    _idToDescription[entry.id] = entry.description;
            }
        }
        Debug.Log($"[参数验证器] 从滑块同步了 {added} 条新映射");
    }

    /// <summary>保存映射到 JSON 文件</summary>
    private void SaveMappingToFile()
    {
        if (_semanticToId.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有映射数据可保存", "OK");
            return;
        }

        string fullPath = _mapFilePath;
        if (!Path.IsPathRooted(fullPath))
            fullPath = Path.Combine(Application.dataPath, "..", fullPath);

        fullPath = Path.GetFullPath(fullPath);
        string dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var wrapper = new ParamMapWrapper
        {
            formatVersion = "1.0",
            modelName = _model != null ? _model.name : "fuxuan",
            description = $"由参数验证器生成 ({DateTime.Now:yyyy-MM-dd HH:mm})",
            entries = new MapEntry[_semanticToId.Count]
        };

        int i = 0;
        foreach (var kv in _semanticToId)
        {
            wrapper.entries[i++] = new MapEntry { s = kv.Key, p = kv.Value, d = _idToDescription.ContainsKey(kv.Value) ? _idToDescription[kv.Value] : null };
        }

        string json = JsonUtility.ToJson(wrapper, true);
        File.WriteAllText(fullPath, json);

        AssetDatabase.Refresh();
        Debug.Log($"[参数验证器] 已保存 {_semanticToId.Count} 条映射到 {fullPath}");

        EditorUtility.DisplayDialog("保存成功",
            $"已保存 {_semanticToId.Count} 条映射\n{fullPath}", "OK");
    }

    // ===================================================================
    //  视图3: AI 分析报告
    // ===================================================================
    private void DrawAIReportView()
    {
        EditorGUILayout.HelpBox(
            "此模式生成一个结构化报告，包含所有参数的信息，" +
            "可复制给 AI 协助分析参数行为。\n" +
            "AI 可根据参数范围、CDI 名称、命名规律等给出映射建议。",
            MessageType.Info);

        EditorGUILayout.Space(5);

        if (GUILayout.Button("🤖 生成 AI 分析提示", GUILayout.Height(30)))
            GenerateAIReport();

        EditorGUILayout.Space(5);

        // 显示报告
        _aiReport = new StringBuilder();
        _aiReport.AppendLine("请分析以下 Live2D 参数列表，给出每个参数对应的身体部位：");
        _aiReport.AppendLine();
        _aiReport.AppendLine("## 已确认的映射（参考）");
        _aiReport.AppendLine("| 语义名 | 参数 ID | CDI 中文名 | 描述 | 范围 |");
        _aiReport.AppendLine("|--------|---------|-----------|------|------|");

        foreach (var entry in _params)
        {
            if (entry.verified && entry.semantic != null)
            {
                _aiReport.AppendLine($"| {entry.semantic} | {entry.id} | {entry.cdiName} | {entry.description} | [{entry.min:F1}, {entry.max:F1}] |");
            }
        }

        _aiReport.AppendLine();
        _aiReport.AppendLine("## 待分析的参数");
        _aiReport.AppendLine("| 参数 ID | CDI 中文名 | 范围 | 建议语义名 | 作用描述 |");
        _aiReport.AppendLine("|---------|-----------|------|-----------|--------|");

        foreach (var entry in _params)
        {
            if (!entry.verified)
            {
                // 检查有没有 y 后缀的配对参数
                string note = "";
                if (entry.id.EndsWith("y", StringComparison.OrdinalIgnoreCase) || entry.cdiName.EndsWith("y", StringComparison.OrdinalIgnoreCase))
                    note = "可能是同前缀参数的 Y 轴版本";
                _aiReport.AppendLine($"| {entry.id} | {entry.cdiName} | [{entry.min:F1}, {entry.max:F1}] | | {note} |");
            }
        }

        _aiReport.AppendLine();
        _aiReport.AppendLine("## 映射规则说明");
        _aiReport.AppendLine("- 语义名使用 snake_case 英文");
        _aiReport.AppendLine("- 部位前缀: body_/head_/arm_/leg_/hair_/skirt_/finger_");
        _aiReport.AppendLine("- 特殊效果: special_");
        _aiReport.AppendLine("- 左手参数实际可能是右手（模型师左右反向），以实际观察为准");
        _aiReport.AppendLine("- 带 'y' 后缀的参数通常是同组参数的次要轴");

        _aiPrompt = _aiReport.ToString();

        // 显示可复制的文本
        EditorGUILayout.LabelField("AI 提示（可复制）:");
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(300));
        var textStyle = new GUIStyle(EditorStyles.textArea);
        textStyle.wordWrap = true;
        string displayText = EditorGUILayout.TextArea(_aiPrompt, textStyle, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("📋 复制到剪贴板"))
        {
            EditorGUIUtility.systemCopyBuffer = _aiPrompt;
            Debug.Log("[参数验证器] AI 提示已复制到剪贴板");
        }
        EditorGUILayout.EndHorizontal();
    }

    private void GenerateAIReport()
    {
        // 在上面 DrawAIReportView 中实时生成
    }

    // ===================================================================
    //  工具方法
    // ===================================================================
    private void ResetAllToDefault()
    {
        foreach (var entry in _params)
        {
            entry.sliderValue = entry.defaultValue;
            entry.parameter.Value = entry.defaultValue;
        }
    }

    private Texture2D MakeTexture(int width, int height, Color color)
    {
        var tex = new Texture2D(width, height);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                tex.SetPixel(x, y, color);
        tex.Apply();
        return tex;
    }

    private void OnDisable()
    {
        // 清理纹理资源
    }

    // ===================================================================
    //  JSON 数据模型
    // ===================================================================
    [Serializable]
    private class Cdi3Json
    {
        public int Version;
        public Cdi3Parameter[] Parameters;
    }

    [Serializable]
    private class Cdi3Parameter
    {
        public string Id;
        public string GroupId;
        public string Name;
    }

    [Serializable]
    private class ParamMapWrapper
    {
        public string formatVersion;
        public string modelName;
        public string description;
        public MapEntry[] entries;
    }

    [Serializable]
    private class MapEntry
    {
        public string s; // semantic name
        public string p; // ParamId
        public string d; // 中文描述（可选）
    }
}
#endif
