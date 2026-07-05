using Live2D.Cubism.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;

/// <summary>
/// Phase 2: GLM-4V 参数视觉扫描器
///
/// 对每个未映射的 Live2D 参数，自动：
///   1. 置为最小值 → 截图
///   2. 置为最大值 → 截图
///   3. 发送两张对比图到 GLM-4V 视觉模型
///   4. 获取语义分析结果并生成映射建议
///
/// 完全云端执行，不占用本地 GPU。
/// </summary>
public class ParameterVisionScanner : EditorWindow
{
    // ================================================================
    //  Menu entry
    // ================================================================

    [MenuItem("Tools/Live2D/参数视觉扫描 (GLM-4V)")]
    private static void OpenWindow()
    {
        var window = GetWindow<ParameterVisionScanner>("参数视觉扫描");
        window.minSize = new Vector2(800, 600);
        window.Show();
    }

    // ================================================================
    //  Fields
    // ================================================================

    private CubismModel _model;
    private GameObject _modelObject;

    // 参数快照（扫描前保存，扫描后恢复）
    private Dictionary<string, float> _savedValues = new Dictionary<string, float>();

    // 扫描列表
    private List<ParameterDef> _unmappedParams = new List<ParameterDef>();
    private int _scanIndex = -1; // -1 = 未开始

    // 扫描状态机
    private enum ScanState
    {
        Idle,
        Preparing,      // 重置所有参数到默认值
        WaitMinRender,  // 等待设置 min 后模型更新
        CaptureMin,     // 截图(min)
        SettingMax,     // 设为目标参数 max
        WaitMaxRender,  // 等待设置 max 后模型更新
        CaptureMax,     // 截图(max)
        CallingApi,     // 调用 GLM-4V
        ShowResult,     // 展示单个结果
        Finished        // 全部完成
    }
    private ScanState _scanState = ScanState.Idle;

    // 当前扫描的参数
    private ParameterDef _currentParam;
    private string _currentParamId;

    // 截图数据
    private byte[] _minImageBytes;
    private byte[] _maxImageBytes;

    // API 结果
    private string _apiResultText;

    // UnityWebRequest 异步操作（Editor 轮询模式）
    private UnityWebRequest _activeRequest;
    private UnityWebRequestAsyncOperation _activeRequestOp;
    private double _apiCallStartTime;

    // 扫描结果汇总
    [Serializable]
    private class ScanResult
    {
        public string paramId;
        public float min;
        public float max;
        public float defaultValue;
        public string suggestedSemantic;
        public string description;
        public string confidence;
        public bool success;
    }
    private List<ScanResult> _scanResults = new List<ScanResult>();
    private ScanResult _currentResult;

    // 渲染控制
    private Camera _captureCamera;
    private RenderTexture _captureRT;
    private const int CAPTURE_SIZE = 1024;

    // CDI 上下文（参数中文名 + 分组，用于优化 prompt）
    private Dictionary<string, string> _cdiNames = new Dictionary<string, string>();
    private Dictionary<string, string> _cdiGroupIds = new Dictionary<string, string>();
    private Dictionary<string, string> _cdiGroupNameLookup = new Dictionary<string, string>();

    // API 超时（秒），扫描时可调整
    private double _apiTimeout = 150.0;

    // 最后写入 fuxuan_map.json 的条目数
    private int _lastAppliedCount = 0;

    // 冻结状态（扫描时暂停动画，防干扰截图）
    private Dictionary<Behaviour, bool> _frozenBehaviours = new Dictionary<Behaviour, bool>();

    // 时间戳
    private double _stateEnterTime;

    // 视图滚动
    private Vector2 _scrollPos;

    // 窗口是否已附加 update
    private bool _updateAttached = false;

    // 状态文本
    private string _statusText = "";

    // 自动模式
    private bool _autoMode = false;

    // 截屏缩放系数（用户可调）
    private float _captureZoom = 2.0f;
    private float _baseOrthographicSize = 5f;

    // ================================================================
    //  Lifecycle
    // ================================================================

    private void OnEnable()
    {
        if (!_updateAttached)
        {
            EditorApplication.update += OnEditorUpdate;
            _updateAttached = true;
        }
    }

    private void OnDisable()
    {
        if (_updateAttached)
        {
            EditorApplication.update -= OnEditorUpdate;
            _updateAttached = false;
        }
        UnfreezeModel();
        CleanupCaptureCamera();
        RestoreParameters();
    }

    // ================================================================
    //  Editor Update — 状态机驱动
    // ================================================================

    private void OnEditorUpdate()
    {
        if (_scanState == ScanState.Idle || _scanState == ScanState.Finished)
            return;

        if (_scanState == ScanState.CallingApi)
        {
            PollApiCall();
            return;
        }

        try
        {
            switch (_scanState)
            {
                case ScanState.Preparing:
                    PrepareCurrentParam();
                    _scanState = ScanState.WaitMinRender;
                    _stateEnterTime = EditorApplication.timeSinceStartup;
                    Repaint();
                    break;

                case ScanState.WaitMinRender:
                    if (EditorApplication.timeSinceStartup - _stateEnterTime > 0.15)
                    {
                        _scanState = ScanState.CaptureMin;
                        Repaint();
                    }
                    break;

                case ScanState.CaptureMin:
                    _minImageBytes = CaptureModelScreenshot();
                    if (_minImageBytes == null)
                    {
                        SetStatus($"❌ 参数 [{_currentParamId}] 截图失败(min)");
                        AdvanceToNext();
                        break;
                    }
                    // 立即转为 max
                    SetParameterToValue(_currentParamId, _currentParam.max);
                    _scanState = ScanState.WaitMaxRender;
                    _stateEnterTime = EditorApplication.timeSinceStartup;
                    Repaint();
                    break;

                case ScanState.WaitMaxRender:
                    if (EditorApplication.timeSinceStartup - _stateEnterTime > 0.15)
                    {
                        _scanState = ScanState.CaptureMax;
                        Repaint();
                    }
                    break;

                case ScanState.CaptureMax:
                    _maxImageBytes = CaptureModelScreenshot();
                    if (_maxImageBytes == null)
                    {
                        SetStatus($"❌ 参数 [{_currentParamId}] 截图失败(max)");
                        AdvanceToNext();
                        break;
                    }
                    // 调用 API
                    _scanState = ScanState.CallingApi;
                    SetStatus($"🔍 正在调用 GLM-4V 分析 [{_currentParamId}]...");
                    StartApiCall();
                    Repaint();
                    break;

                case ScanState.SettingMax:
                    SetParameterToValue(_currentParamId, _currentParam.max);
                    _scanState = ScanState.WaitMaxRender;
                    _stateEnterTime = EditorApplication.timeSinceStartup;
                    Repaint();
                    break;

                case ScanState.ShowResult:
                    if (_autoMode)
                    {
                        // 自动模式：等待 0.5 秒后自动推进
                        if (EditorApplication.timeSinceStartup - _stateEnterTime > 0.5)
                        {
                            AdvanceToNext();
                        }
                    }
                    // 手动模式：等待用户点击"下一个"
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ParamVisionScanner] 扫描异常: {ex.Message}");
            SetStatus($"❌ 扫描异常: {ex.Message}");
            _scanState = ScanState.Idle;
            UnfreezeModel();
            RestoreParameters();
            Repaint();
        }
    }

    // ================================================================
    //  Core scanning logic
    // ================================================================

    private void PrepareCurrentParam()
    {
        // 将所有参数重置为默认值
        if (_model == null || _model.Parameters == null)
            return;

        foreach (var p in _model.Parameters)
        {
            p.Value = p.DefaultValue;
        }
        _model.ForceUpdateNow();

        // 将目标参数设为 min
        SetParameterToValue(_currentParamId, _currentParam.min);
    }

    private void SetParameterToValue(string paramId, float value)
    {
        if (_model == null) return;
        var param = _model.Parameters.FindById(paramId);
        if (param != null)
        {
            param.Value = value;
            _model.ForceUpdateNow();
        }
    }

    private float GetParamDefault(string paramId)
    {
        if (_model == null) return 0;
        var param = _model.Parameters.FindById(paramId);
        return param != null ? param.DefaultValue : 0;
    }

    private void AdvanceToNext()
    {
        // 清理之前的 API 请求
        if (_activeRequest != null)
        {
            _activeRequest.Dispose();
            _activeRequest = null;
        }
        _activeRequestOp = null;

        _scanIndex++;
        if (_scanIndex >= _unmappedParams.Count)
        {
            // 全部完成
            _scanState = ScanState.Finished;
            UnfreezeModel();
            RestoreParameters();
            CleanupCaptureCamera();
            SetStatus($"✅ 扫描完成！共分析 {_scanResults.Count} 个参数");
            return;
        }

        _currentParam = _unmappedParams[_scanIndex];
        _currentParamId = _currentParam.paramId;
        _currentResult = null;
        _apiResultText = null;
        _minImageBytes = null;
        _maxImageBytes = null;

        SetStatus($"🔄 [{_scanIndex + 1}/{_unmappedParams.Count}] 扫描参数: {_currentParamId}");
        _scanState = ScanState.Preparing;
    }

    private void FreezeModel()
    {
        if (_modelObject == null) return;
        _frozenBehaviours.Clear();

        // 禁用所有可能干扰参数值的 MonoBehaviour
        // 保留 Renderer、Transform、CubismModel、CubismDrawVertices 等渲染必要组件
        var allBehaviours = _modelObject.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var mb in allBehaviours)
        {
            if (mb == null) continue;
            var t = mb.GetType();
            // 保留渲染相关的 Cubism 核心组件，禁掉动画/物理/参数覆盖类
            string name = t.Name;
            if (name == "CubismRenderController" ||
                name == "CubismDrawVertices" ||
                name == "CubismRenderer" ||
                name == "CubismRendererArray" ||
                name == "CubismParameters" ||
                name.StartsWith("CubismRender")) // 渲染器系列
                continue;

            if (mb is Animator) { _frozenBehaviours[mb] = mb.enabled; mb.enabled = false; }
            else if (mb is FuXuanAnimatorController) { _frozenBehaviours[mb] = mb.enabled; mb.enabled = false; }
            else if (name == "Live2DRenderer") { _frozenBehaviours[mb] = mb.enabled; mb.enabled = false; }
            else if (name == "CubismUpdateController") { _frozenBehaviours[mb] = mb.enabled; mb.enabled = false; }
            else if (name == "CubismPhysics") { _frozenBehaviours[mb] = mb.enabled; mb.enabled = false; }
            else if (name == "CubismParameterStore") { _frozenBehaviours[mb] = mb.enabled; mb.enabled = false; }
            else if (name == "CubismAutoEyeBlink") { _frozenBehaviours[mb] = mb.enabled; mb.enabled = false; }
            else if (name == "CubismEyeTracking") { _frozenBehaviours[mb] = mb.enabled; mb.enabled = false; }
            else if (name == "CubismMouthController") { _frozenBehaviours[mb] = mb.enabled; mb.enabled = false; }
            else if (name == "CubismBreathController") { _frozenBehaviours[mb] = mb.enabled; mb.enabled = false; }
        }

        Debug.Log($"[ParamVisionScanner] 已冻结 {_frozenBehaviours.Count} 个动画/物理/参数组件");
    }

    private void UnfreezeModel()
    {
        if (_frozenBehaviours.Count == 0) return;
        foreach (var kv in _frozenBehaviours)
        {
            if (kv.Key != null)
                kv.Key.enabled = kv.Value;
        }
        _frozenBehaviours.Clear();
        Debug.Log("[ParamVisionScanner] 已恢复动画组件");
    }

    private void RestoreParameters()
    {
        if (_model == null || _model.Parameters == null || _savedValues.Count == 0)
            return;

        foreach (var p in _model.Parameters)
        {
            if (_savedValues.TryGetValue(p.Id, out float saved))
            {
                p.Value = saved;
            }
        }
        _savedValues.Clear();
    }

    // ================================================================
    //  Screenshot capture
    // ================================================================

    private byte[] CaptureModelScreenshot()
    {
        if (_model == null) return null;

        try
        {
            EnsureCaptureCamera();
            if (_captureCamera == null) return null;

            // 确保背景透明
            _captureCamera.backgroundColor = new Color(0, 0, 0, 0);
            _captureCamera.clearFlags = CameraClearFlags.Color;

            // culling mask 已在 EnsureCaptureCamera 中根据渲染器层计算
            // 无需在此覆写

            // 强制更新 Cubism 网格到 GPU，确保当前参数值已渲染
            if (_model != null) _model.ForceUpdateNow();

            // 强制渲染
            _captureCamera.Render();

            // 读取像素
            RenderTexture.active = _captureRT;
            var tex = new Texture2D(CAPTURE_SIZE, CAPTURE_SIZE, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, CAPTURE_SIZE, CAPTURE_SIZE), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            byte[] pngBytes = tex.EncodeToPNG();
            DestroyImmediate(tex);

            // 保存到磁盘供用户查看
            SaveScreenshotToDisk(pngBytes);

            return pngBytes;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ParamVisionScanner] 截图失败: {ex.Message}");
            return null;
        }
    }

    private void SaveScreenshotToDisk(byte[] pngBytes)
    {
        if (pngBytes == null || string.IsNullOrEmpty(_currentParamId)) return;

        try
        {
            string state = (_scanState == ScanState.CaptureMin) ? "min" : "max";
            string dir = Application.dataPath + "/../screenshots/vision_scan";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string filename = $"{_currentParamId}_{state}_{DateTime.Now:HHmmssfff}.png";
            string path = dir + "/" + filename;
            File.WriteAllBytes(path, pngBytes);
            Debug.Log($"[ParamVisionScanner] 截图已保存: {path}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ParamVisionScanner] 保存截图失败: {ex.Message}");
        }
    }

    private void EnsureCaptureCamera()
    {
        if (_captureCamera != null && _captureRT != null)
            return;

        CleanupCaptureCamera();

        if (_model == null) return;

        // 计算模型包围盒
        var renderers = _model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            // 退回到创建通用相机
            CreateGenericCaptureCamera();
            return;
        }

        Bounds bounds = renderers[0].bounds;
        // 先计算所有渲染器中心点的平均值，用于排除离群渲染器
        Vector3 centerSum = Vector3.zero;
        int validCount = 0;
        foreach (var r in renderers)
        {
            if (r != null) { centerSum += r.bounds.center; validCount++; }
        }
        Vector3 avgCenter = centerSum / validCount;

        foreach (var r in renderers)
        {
            if (r == null) continue;
            // 排除中心点偏离平均值 > 5 单位的渲染器（辉光、粒子等）
            if (Vector3.Distance(r.bounds.center, avgCenter) > 5f) continue;
            bounds.Encapsulate(r.bounds);
        }

        // 创建临时相机
        var go = new GameObject("__VisionCaptureCam__");
        go.hideFlags = HideFlags.HideAndDontSave;
        _captureCamera = go.AddComponent<Camera>();

        float size = Mathf.Max(bounds.extents.x, bounds.extents.y) * 0.5f;
        if (size < 0.01f) size = 5f;
        _baseOrthographicSize = size;
        Debug.Log($"[ParamVisionScanner] bounds center={bounds.center}, extents=({bounds.extents.x:F2},{bounds.extents.y:F2},{bounds.extents.z:F2}), size={size:F2}");

        _captureCamera.orthographic = true;
        _captureCamera.orthographicSize = _baseOrthographicSize / _captureZoom;
        _captureCamera.transform.position = bounds.center + Vector3.back * 10f;
        _captureCamera.transform.LookAt(bounds.center);
        _captureCamera.nearClipPlane = 0.01f;
        _captureCamera.farClipPlane = 30f;
        _captureCamera.depth = -100;

        _captureRT = new RenderTexture(CAPTURE_SIZE, CAPTURE_SIZE, 24, RenderTextureFormat.ARGB32);
        _captureRT.name = "VisionScannerRT";
        _captureCamera.targetTexture = _captureRT;

        // 构建 culling mask：包含所有包含模型渲染器的层
        var renderers2 = _model.GetComponentsInChildren<Renderer>(true);
        int cullingMask = 0;
        foreach (var r in renderers2)
        {
            if (r != null)
                cullingMask |= (1 << r.gameObject.layer);
        }
        if (cullingMask == 0)
            cullingMask = 1 << _model.gameObject.layer;

        _captureCamera.cullingMask = cullingMask;

        Debug.Log($"[ParamVisionScanner] 捕获相机已创建, cullingMask={cullingMask}, baseSize={_baseOrthographicSize:F2}, zoom={_captureZoom:F2}, finalSize={_captureCamera.orthographicSize:F2}");
    }

    private void CreateGenericCaptureCamera()
    {
        var go = new GameObject("__VisionCaptureCam__");
        go.hideFlags = HideFlags.HideAndDontSave;
        _captureCamera = go.AddComponent<Camera>();
        _captureCamera.orthographic = true;
        _captureCamera.orthographicSize = 5f / _captureZoom;
        _captureCamera.transform.position = Vector3.back * 10f;
        _captureCamera.transform.LookAt(Vector3.zero);
        _captureCamera.nearClipPlane = 0.01f;
        _captureCamera.farClipPlane = 30f;

        _captureRT = new RenderTexture(CAPTURE_SIZE, CAPTURE_SIZE, 24, RenderTextureFormat.ARGB32);
        _captureCamera.targetTexture = _captureRT;
        _captureCamera.cullingMask = 1 << _model.gameObject.layer;
    }

    private void CleanupCaptureCamera()
    {
        if (_captureCamera != null)
        {
            // 先脱钩 RenderTexture，再销毁相机
            if (_captureCamera.targetTexture == _captureRT)
                _captureCamera.targetTexture = null;
            DestroyImmediate(_captureCamera.gameObject);
            _captureCamera = null;
        }
        if (_captureRT != null)
        {
            _captureRT.Release();
            DestroyImmediate(_captureRT);
            _captureRT = null;
        }
    }

    // ================================================================
    //  GLM-4V API call (Editor polling mode)
    // ================================================================

    /// <summary>启动 API 调用（非阻塞，由 EditorUpdate 轮询完成）</summary>
    private void StartApiCall()
    {
        try
        {
            string apiKey = ChatConfig.GlmApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                SetStatus("❌ GLM_API_KEY 未设置（环境变量）");
                _currentResult = new ScanResult
                {
                    paramId = _currentParamId,
                    min = _currentParam.min,
                    max = _currentParam.max,
                    defaultValue = _currentParam.defaultValue,
                    success = false,
                    description = "GLM_API_KEY 未设置"
                };
                _scanResults.Add(_currentResult);
                _stateEnterTime = EditorApplication.timeSinceStartup;
                _scanState = ScanState.ShowResult;
                return;
            }

            string base64Min = Convert.ToBase64String(_minImageBytes);
            string base64Max = Convert.ToBase64String(_maxImageBytes);
            string dataUrlMin = "data:image/png;base64," + base64Min;
            string dataUrlMax = "data:image/png;base64," + base64Max;

            // ─── 构建含有 CDI 上下文的 prompt ───────────────────────
            string cdiCtx = "";
            if (_cdiNames.TryGetValue(_currentParamId, out string cdiName))
            {
                cdiCtx = $"\n【模型原始标注】此参数在模型文件中名为：""{cdiName}""";
                if (_cdiGroupIds.TryGetValue(_currentParamId, out string gid) &&
                    _cdiGroupNameLookup.TryGetValue(gid, out string gname))
                {
                    cdiCtx += $"，属于 ""{gname}"" 组（{gid}）。同组参数通常控制相关的部位或效果。";
                }
                else
                {
                    cdiCtx += "。该参数未归类到特定组。";
                }
            }

            string prompt = $@"你是一个严格的 Live2D 虚拟角色参数分析专家。你将看到同一个角色的两张截图：
【第一张】参数 {_currentParamId} 取最小值（{_currentParam.min:F2}）
【第二张】参数 {_currentParamId} 取最大值（{_currentParam.max:F2}）{cdiCtx}

角色是""符玄""（Fu Xuan），这是一个完整的 Live2D 模型，包含以下所有身体部位和特效分类：

【身体部位清单 - 必须逐项检查】
1. 头部 & 面部：左眼、右眼、左眉、右眉、嘴巴、下颌、脸颊、睫毛
2. 头发：刘海(a/b/c)、鬓发(a/b/c)、后发(a-d)、后发1(A/B)、头发物理(2a/2b/2c)、发饰(饰a/b/c)
3. 身体：颈部、躯干、腰、肩、颈饰(1x/1y/2x/2y/3x/3y)
4. 右臂（右上臂、右前臂、右手腕、右手掌、右手指）— 注意：右手有很多图层透视参数
5. 左臂（左上臂、左前臂、左袖）
6. 腿/裙摆：左腿摆动/旋转、右腿摆动/旋转、抬腿
7. 裙子：裙摆(1x/1y/2x/2y/3x/3y)、七星盘(X1-Y4)、穗子、帘子
8. 饰品：衣带(左/右)、环(左/右)、水杯、披肩
9. 特效：钱、泪眼、黑脸/脸红、生气、眼镜发光、星(显隐/大小/外围)、紫环/黄环(外/中/内各含显隐/大小/旋转)
10. 镜头：镜头X/Y、人物缩放

【命名约定 - 请严格参考以下已有映射的语义名风格】
- eye_l_open, eye_r_open, eye_ball_x, eye_ball_y（眼）
- brow_l_y, brow_r_y, brow_l_angle, brow_r_angle（眉）
- mouth_form, mouth_opn_y（嘴）
- body_angle_x/y/z, head_angle_x/y/z（身体/头角度）
- arm_right_upper, arm_right_mid, arm_right_lower, arm_right_rotation（右手臂各段）
- arm_left_upper, arm_left_mid, arm_left_lower（左手臂各段）
- hand_layer_98, hand_layer_95, hand_layer_100（右手图层透视类参数）
- leg_l_lift, leg_l_swing, leg_r_swing（腿）
- hair_bangs_1~3, hair_physics_1~3, hair_side_1~3, hair_back_1~4（头发各段）
- hair_ornament_1~3（发饰）
- skirt_drive_1~7（裙摆各段）
- special_money, special_tear, special_blush_dark, special_angry（特效开关）
- special_outer_mask（外蒙版）
- star_visibility, star_size, star_outer_scale（星相关）
- camera_x, camera_y, character_scale（镜头控制）

如果是开关型参数（0/1切换，如显隐），使用 toggle 风格命名，如 xxx_switch, xxx_visibility, xxx_toggle。

【重要规则 - 必须遵守】
- 你必须如实报告你实际看到的变化。如果两张图看起来几乎一样，就如实说变化很小，confidence 给""低""。
- CRITICAL：不要每张图都默认报""左臂""或""左肩""！你必须检查上述清单中所有10个部位区域后才做判断。
- 如果该参数的中文名（来自模型原始标注）与视觉变化一致，优先采纳中文名暗示的部位。
- 如果你发现多个部位都有变化，选择变化最明显的那一个。
- confidence 为""高""仅当变化非常清晰明确且无可疑。

请用以下 JSON 格式回复（不要其他内容）：
{{""bodyPart"":""部位名（尽量具体，如""右上臂""/""右眼下眼睑""/""后发b""）"", ""visualChange"":""具体变化描述"", ""suggestedSemantic"":""英文语义名"", ""confidence"":""高/中/低"", ""description"":""中文详细说明""}}
注意：confidence 字段只能用中文""高""、""中""、""低""，不要用英文。";

            string url = ChatConfig.GlmApiBaseUrl.TrimEnd('/') + "/chat/completions";

            string jsonBody = "{";
            jsonBody += "\"model\":\"" + EscapeJson(ChatConfig.GlmVisionModel) + "\",";
            jsonBody += "\"messages\":[{";
            jsonBody += "\"role\":\"user\",";
            jsonBody += "\"content\":[";
            jsonBody += "{\"type\":\"text\",\"text\":\"" + EscapeJson(prompt) + "\"},";
            jsonBody += "{\"type\":\"image_url\",\"image_url\":{\"url\":\"" + EscapeJson(dataUrlMin) + "\"}},";
            jsonBody += "{\"type\":\"image_url\",\"image_url\":{\"url\":\"" + EscapeJson(dataUrlMax) + "\"}}";
            jsonBody += "]}],";
            jsonBody += "\"request_id\":\"" + Guid.NewGuid().ToString("N") + "\"";
            jsonBody += "}";

            _activeRequest = new UnityWebRequest(url, "POST");
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            _activeRequest.uploadHandler = new UploadHandlerRaw(bodyBytes);
            _activeRequest.downloadHandler = new DownloadHandlerBuffer();
            _activeRequest.SetRequestHeader("Content-Type", "application/json");
            _activeRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);
            _activeRequest.timeout = 60;

            _activeRequestOp = _activeRequest.SendWebRequest();
            _apiCallStartTime = EditorApplication.timeSinceStartup;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ParamVisionScanner] 启动 API 调用失败: {ex.Message}");
            FinishApiCall(null, $"启动失败: {ex.Message}");
        }
    }

    /// <summary>轮询检查 API 调用是否完成（EditorUpdate 回调）</summary>
    private void PollApiCall()
    {
        // 超时保护（可调）
        if (EditorApplication.timeSinceStartup - _apiCallStartTime > _apiTimeout)
        {
            Debug.LogWarning($"[ParamVisionScanner] API 调用超时（{_apiTimeout:F0}s）");
            string errMsg = _activeRequest?.downloadHandler?.text ?? "";
            string fallback = $"API 调用超时（{_apiTimeout:F0}s）";
            FinishApiCall(errMsg, fallback);
            return;
        }

        if (_activeRequestOp == null)
        {
            FinishApiCall(null, "请求未正确初始化");
            return;
        }

        if (!_activeRequestOp.isDone)
        {
            // 还在等待中，更新状态文本
            double elapsed = EditorApplication.timeSinceStartup - _apiCallStartTime;
            SetStatus($"🔍 正在调用 GLM-4V 分析 [{_currentParamId}]... ({elapsed:F0}s)");
            return;
        }

        // 请求完成
        if (_activeRequest.result == UnityWebRequest.Result.Success)
        {
            string responseText = _activeRequest.downloadHandler.text;
            FinishApiCall(responseText, null);
        }
        else
        {
            string errBody = _activeRequest.downloadHandler?.text ?? "";
            string errMsg = ParseGlmError(errBody, _activeRequest.error);
            Debug.LogWarning($"[ParamVisionScanner] API 调用失败: {errMsg}");
            FinishApiCall(errBody, errMsg);
        }
    }

    /// <summary>API 调用完成（成功或失败均在此处理）</summary>
    private void FinishApiCall(string responseText, string errorMsg)
    {
        // 清理请求
        if (_activeRequest != null)
        {
            _activeRequest.Dispose();
            _activeRequest = null;
        }
        _activeRequestOp = null;

        if (errorMsg != null)
        {
            // 失败
            _currentResult = new ScanResult
            {
                paramId = _currentParamId,
                min = _currentParam.min,
                max = _currentParam.max,
                defaultValue = _currentParam.defaultValue,
                success = false,
                description = $"API 错误: {errorMsg}"
            };
            _scanResults.Add(_currentResult);
            SetStatus($"❌ [{_currentParamId}] API 调用失败: {errorMsg}");
            _stateEnterTime = EditorApplication.timeSinceStartup;
            _scanState = ScanState.ShowResult;
            Repaint();
            return;
        }

        // 成功 — 解析结果
        ParseGlmResponse(responseText);

        if (_currentResult == null)
        {
            _currentResult = new ScanResult
            {
                paramId = _currentParamId,
                min = _currentParam.min,
                max = _currentParam.max,
                defaultValue = _currentParam.defaultValue,
                success = false,
                description = "API 返回无法解析"
            };
            _scanResults.Add(_currentResult);
        }

        _stateEnterTime = EditorApplication.timeSinceStartup;
        _scanState = ScanState.ShowResult;
        Repaint();
    }

    private void ParseGlmResponse(string json)
    {
        try
        {
            var resp = JsonUtility.FromJson<GlmVisionResponse>(json);
            if (resp?.choices != null && resp.choices.Length > 0 && resp.choices[0].message != null)
            {
                string content = resp.choices[0].message.content;
                _apiResultText = content;

                // 尝试从内容里提取 JSON
                string jsonPart = ExtractJsonFromText(content);
                if (!string.IsNullOrEmpty(jsonPart))
                {
                    var parsed = JsonUtility.FromJson<VisionAnalysisResult>(jsonPart);
                    if (parsed != null)
                    {
                        _currentResult = new ScanResult
                        {
                            paramId = _currentParamId,
                            min = _currentParam.min,
                            max = _currentParam.max,
                            defaultValue = _currentParam.defaultValue,
                            suggestedSemantic = parsed.suggestedSemantic?.Trim(),
                            description = $"部位: {parsed.bodyPart}\n变化: {parsed.visualChange}\n说明: {parsed.description}",
                            confidence = parsed.confidence,
                            success = true
                        };
                        _scanResults.Add(_currentResult);
                        SetStatus($"✅ [{_currentParamId}] → {parsed.suggestedSemantic} ({parsed.confidence})");
                        return;
                    }
                }

                // JSON 提取失败，存原文
                _currentResult = new ScanResult
                {
                    paramId = _currentParamId,
                    min = _currentParam.min,
                    max = _currentParam.max,
                    defaultValue = _currentParam.defaultValue,
                    success = false,
                    description = content?.Length > 500 ? content.Substring(0, 500) + "..." : content
                };
                _scanResults.Add(_currentResult);
                SetStatus($"⚠️ [{_currentParamId}] 返回非结构化文本");
            }
            else
            {
                _currentResult = new ScanResult
                {
                    paramId = _currentParamId,
                    min = _currentParam.min,
                    max = _currentParam.max,
                    defaultValue = _currentParam.defaultValue,
                    success = false,
                    description = "API 返回空响应"
                };
                _scanResults.Add(_currentResult);
                SetStatus($"❌ [{_currentParamId}] API 返回空响应");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ParamVisionScanner] 解析响应失败: {ex.Message}");
            _currentResult = new ScanResult
            {
                paramId = _currentParamId,
                min = _currentParam.min,
                max = _currentParam.max,
                defaultValue = _currentParam.defaultValue,
                success = false,
                description = $"解析失败: {ex.Message}"
            };
            _scanResults.Add(_currentResult);
            SetStatus($"❌ [{_currentParamId}] 响应解析失败");
        }
    }

    private string ExtractJsonFromText(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        // 尝试找 ```json ... ``` 块
        int start = text.IndexOf("```json");
        if (start >= 0)
        {
            start += 7;
            int end = text.IndexOf("```", start);
            if (end > start) return text.Substring(start, end - start).Trim();
        }

        // 尝试找 { ... }
        start = text.IndexOf('{');
        if (start >= 0)
        {
            int braceDepth = 0;
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == '{') braceDepth++;
                else if (text[i] == '}') braceDepth--;
                if (braceDepth == 0 && i > start)
                {
                    return text.Substring(start, i - start + 1);
                }
            }
        }

        return null;
    }

    private string ParseGlmError(string errorBody, string fallback)
    {
        if (string.IsNullOrEmpty(errorBody)) return fallback ?? "未知错误";
        try
        {
            var errObj = JsonUtility.FromJson<GlmErrorResponse>(errorBody);
            if (errObj?.error != null && !string.IsNullOrEmpty(errObj.error.message))
                return errObj.error.message;
        }
        catch { }
        return fallback ?? "未知错误";
    }

    // ================================================================
    //  JSON helpers
    // ================================================================

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }

    // ================================================================
    //  Status
    // ================================================================

    private void SetStatus(string text)
    {
        _statusText = text;
        Debug.Log($"[ParamVisionScanner] {text}");
    }

    // ================================================================
    //  GUI
    // ================================================================

    private void OnGUI()
    {
        EditorGUILayout.Space(5);

        DrawHeader();

        if (_model == null)
        {
            DrawModelSelector();
            return;
        }

        // 模型信息
        EditorGUILayout.LabelField($"🎯 模型: {_model.name}  |  参数: {_model.Parameters?.Length ?? 0}  |  未映射: {_unmappedParams.Count}",
            EditorStyles.miniLabel);

        EditorGUILayout.Space(5);

        // 扫描控制区
        DrawScanControls();

        EditorGUILayout.Space(5);

        // 状态信息
        if (!string.IsNullOrEmpty(_statusText))
        {
            EditorGUILayout.HelpBox(_statusText, GetMessageType(_statusText));
        }

        EditorGUILayout.Space(5);

        // 扫描结果
        DrawScanResults();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("🧠 GLM-4V 参数视觉扫描", EditorStyles.boldLabel, GUILayout.Width(200));
        GUILayout.FlexibleSpace();

        if (_model != null)
        {
            if (GUILayout.Button("🔄 刷新参数列表", EditorStyles.miniButton, GUILayout.Width(100)))
            {
                RefreshUnmappedList();
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("对每个未映射参数自动截取 min/max 对比图 → 调用 GLM-4V 分析语义",
            EditorStyles.miniLabel);
    }

    private void DrawModelSelector()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox("请先在场景中选中一个 Live2D 模型", MessageType.Info);

        // 自动从 Selection 获取模型
        var selected = Selection.activeGameObject;
        if (selected != null)
        {
            var model = selected.GetComponentInChildren<CubismModel>(true);
            if (model != null)
            {
                if (GUILayout.Button($"🎯 使用选中的模型: {selected.name}", GUILayout.Height(30)))
                {
                    SetModel(selected, model);
                }
            }
        }

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("或手动拖入模型 GameObject：", EditorStyles.miniLabel);

        var newObj = (GameObject)EditorGUILayout.ObjectField("模型", _modelObject, typeof(GameObject), true);
        if (newObj != _modelObject)
        {
            if (newObj != null)
            {
                var m = newObj.GetComponentInChildren<CubismModel>(true);
                if (m != null)
                    SetModel(newObj, m);
                else
                    EditorUtility.DisplayDialog("错误", "选中物体中没有 CubismModel 组件", "确定");
            }
            else
            {
                _modelObject = null;
                _model = null;
            }
        }
    }

    private void SetModel(GameObject go, CubismModel model)
    {
        _modelObject = go;
        _model = model;
        RefreshUnmappedList();
    }

    /// <summary>加载 CDI 上下文：参数中文名、GroupId、Group 名称</summary>
    private void LoadCdiContext()
    {
        _cdiNames.Clear();
        _cdiGroupIds.Clear();
        _cdiGroupNameLookup.Clear();

        string cdiDir = "Live2D/Fuxuan";
        string cdiJson = RuntimeModelAnalyzer.LoadCdi3FromStreamingAssets(cdiDir);
        if (string.IsNullOrEmpty(cdiJson))
        {
            // 尝试其他路径
            string altDir = Path.Combine(Application.streamingAssetsPath, "Live2D", "Fuxuan");
            if (Directory.Exists(altDir))
            {
                var files = Directory.GetFiles(altDir, "*.cdi3.json", SearchOption.AllDirectories);
                if (files.Length > 0)
                    cdiJson = File.ReadAllText(files[0], Encoding.UTF8);
            }
        }
        if (string.IsNullOrEmpty(cdiJson)) return;

        try
        {
            var cdiData = JsonUtility.FromJson<Cdi3Json>(cdiJson);
            if (cdiData?.Parameters != null)
            {
                foreach (var p in cdiData.Parameters)
                {
                    if (!string.IsNullOrEmpty(p.Id))
                    {
                        if (!string.IsNullOrEmpty(p.Name)) _cdiNames[p.Id] = p.Name;
                        if (!string.IsNullOrEmpty(p.GroupId)) _cdiGroupIds[p.Id] = p.GroupId;
                    }
                }
            }
            // 构建 GroupId → 组名映射
            if (cdiData?.ParameterGroups != null)
            {
                foreach (var g in cdiData.ParameterGroups)
                {
                    if (!string.IsNullOrEmpty(g.Id))
                        _cdiGroupNameLookup[g.Id] = g.Name;
                }
            }
            Debug.Log($"[ParamVisionScanner] 已加载 CDI 上下文：{_cdiNames.Count} 个参数名, {_cdiGroupNameLookup.Count} 个组");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ParamVisionScanner] 解析 CDI 上下文失败: {ex.Message}");
        }
    }

    private void RefreshUnmappedList()
    {
        if (_model == null) return;

        // 加载 CDI 上下文（参数中文名 + GroupId）
        LoadCdiContext();

        var schema = Live2DModelAnalyzer.GenerateBodySchema(_model);
        _unmappedParams = schema.GetUnmappedParams();

        // 清空扫描状态
        _scanState = ScanState.Idle;
        _scanIndex = -1;
        _scanResults.Clear();
        _currentResult = null;
        _apiResultText = null;
        _minImageBytes = null;
        _maxImageBytes = null;
        _savedValues.Clear();
        CleanupCaptureCamera();
        SetStatus($"已加载 {_unmappedParams.Count} 个未映射参数，点击「开始扫描」启动");
        Repaint();
    }

    private void DrawScanControls()
    {
        EditorGUILayout.BeginHorizontal();

        bool isRunning = _scanState != ScanState.Idle && _scanState != ScanState.Finished;

        GUI.enabled = _unmappedParams.Count > 0 && !isRunning;
        if (GUILayout.Button("▶ 开始扫描", GUILayout.Height(30), GUILayout.Width(120)))
        {
            StartScan();
        }
        GUI.enabled = true;

        GUI.enabled = isRunning;
        if (GUILayout.Button("⏹ 停止", GUILayout.Height(30), GUILayout.Width(80)))
        {
            StopScan();
        }
        GUI.enabled = true;

        GUI.enabled = _scanResults.Count > 0 && !isRunning;
        if (GUILayout.Button("📋 复制结果 (JSON)", GUILayout.Height(30), GUILayout.Width(140)))
        {
            CopyResultsToClipboard();
        }
        GUI.enabled = true;

        GUI.enabled = _scanResults.Count > 0 && !isRunning;
        if (GUILayout.Button("💾 保存映射", GUILayout.Height(30), GUILayout.Width(100)))
        {
            SaveMappingToFile();
        }
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();

        // 进度条
        if (_unmappedParams.Count > 0)
        {
            float progress = _scanIndex < 0 ? 0 : Mathf.Clamp01((float)_scanIndex / _unmappedParams.Count);
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 18),
                progress,
                $"{Mathf.Max(0, _scanIndex)} / {_unmappedParams.Count}");
        }

        // 自动/手动切换
        if (isRunning || _scanResults.Count > 0)
        {
            _autoMode = EditorGUILayout.ToggleLeft("🤖 自动扫描（不等待，直接扫完拍）", _autoMode);
        }

        // 截屏缩放滑块（仅在非扫描时可安全调整）
        if (!isRunning)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("🔍 截屏缩放", GUILayout.Width(80));
            float newZoom = EditorGUILayout.Slider(_captureZoom, 0.3f, 5.0f);
            if (Mathf.Abs(newZoom - _captureZoom) > 0.01f)
            {
                _captureZoom = newZoom;
                _baseOrthographicSize = 0;  // 强制重建相机
                CleanupCaptureCamera();
                if (_model != null) _model.ForceUpdateNow();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            // API 超时滑块
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("⏱ API 超时（秒）", GUILayout.Width(100));
            _apiTimeout = EditorGUILayout.Slider((float)_apiTimeout, 30f, 300f);
            EditorGUILayout.EndHorizontal();
        }

        // ShowResult 状态下显示"下一个"按钮
        if (_scanState == ScanState.ShowResult)
        {
            if (!_autoMode)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("➡ 下一个参数", GUILayout.Height(25)))
                {
                    AdvanceToNext();
                }
                if (GUILayout.Button("⏭ 跳过 (无映射)", GUILayout.Height(25)))
                {
                    // 添加一个空的扫描结果
                    var skipResult = new ScanResult
                    {
                        paramId = _currentParamId,
                        min = _currentParam.min,
                        max = _currentParam.max,
                        defaultValue = _currentParam.defaultValue,
                        success = false,
                        description = "已跳过"
                    };
                    if (_currentResult == null)
                    {
                        _scanResults.Add(skipResult);
                    }
                    AdvanceToNext();
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                // 自动模式：快速跳过的按钮（可选）
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("⏭ 跳过此参数", GUILayout.Height(20)))
                {
                    var skipResult = new ScanResult
                    {
                        paramId = _currentParamId,
                        min = _currentParam.min,
                        max = _currentParam.max,
                        defaultValue = _currentParam.defaultValue,
                        success = false,
                        description = "已跳过"
                    };
                    if (_currentResult == null)
                    {
                        _scanResults.Add(skipResult);
                    }
                    AdvanceToNext();
                }
                EditorGUILayout.EndHorizontal();
            }

            // 显示当前参数的分析详情
            if (_currentResult != null)
            {
                DrawCurrentResult();
            }
        }

        // 完成时显示汇总
        if (_scanState == ScanState.Finished)
        {
            DrawResultSummary();
        }
    }

    private void StartScan()
    {
        if (_unmappedParams.Count == 0) return;

        // 保存当前参数值
        _savedValues.Clear();
        if (_model != null && _model.Parameters != null)
        {
            foreach (var p in _model.Parameters)
            {
                _savedValues[p.Id] = p.Value;
            }
        }

        // 冻结动画系统（走路/呼吸等），防干扰截图
        FreezeModel();

        _scanResults.Clear();
        _scanIndex = -1;
        _scanState = ScanState.Idle;

        // 创建捕获相机
        EnsureCaptureCamera();

        // 开始第一个
        AdvanceToNext();
    }

    private void StopScan()
    {
        // 取消活跃的 API 请求
        if (_activeRequest != null)
        {
            _activeRequest.Abort();
            _activeRequest.Dispose();
            _activeRequest = null;
        }
        _activeRequestOp = null;

        _scanState = ScanState.Idle;
        UnfreezeModel();
        RestoreParameters();
        CleanupCaptureCamera();
        SetStatus($"⏹ 已停止，已扫描 {_scanResults.Count} 个参数");
        Repaint();
    }

    // ================================================================
    //  Result display
    // ================================================================

    private void DrawCurrentResult()
    {
        EditorGUILayout.Space(5);
        var r = _currentResult;
        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField($"📊 参数: {r.paramId}", EditorStyles.boldLabel);

        if (r.success)
        {
            EditorGUILayout.LabelField($"推荐语义: {r.suggestedSemantic}");
            EditorGUILayout.LabelField($"置信度: {r.confidence}");
            if (!string.IsNullOrEmpty(r.description))
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("说明:", EditorStyles.miniLabel);
                EditorGUILayout.TextArea(r.description, GUILayout.MinHeight(50));
            }
        }
        else
        {
            EditorGUILayout.HelpBox($"分析失败: {r.description}", MessageType.Warning);
        }

        if (!string.IsNullOrEmpty(_apiResultText))
        {
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("原始响应:", EditorStyles.miniLabel);
            EditorGUILayout.TextArea(_apiResultText, GUILayout.MinHeight(60));
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawScanResults()
    {
        if (_scanResults.Count == 0) return;

        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField($"📋 扫描结果汇总 ({_scanResults.Count} 个)", EditorStyles.boldLabel);

        int successCount = _scanResults.FindAll(r => r.success).Count;
        string summary = $"成功: {successCount}  |  失败/跳过: {_scanResults.Count - successCount}";
        EditorGUILayout.LabelField(summary, EditorStyles.miniLabel);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(Mathf.Min(300, _scanResults.Count * 50 + 50)));

        foreach (var r in _scanResults)
        {
            string icon = r.success ? "✅" : "❌";
            string semantic = r.success ? r.suggestedSemantic : "(无映射)";
            string desc = r.description ?? "";
            if (desc.Length > 80) desc = desc.Substring(0, 80) + "...";

            EditorGUILayout.BeginHorizontal(GUI.skin.box);
            EditorGUILayout.LabelField($"{icon} {r.paramId}", GUILayout.Width(220));
            EditorGUILayout.LabelField($"→ {semantic}", GUILayout.Width(160));
            EditorGUILayout.LabelField($"置信度: {r.confidence ?? "-"}");
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawResultSummary()
    {
        int successCount = _scanResults.FindAll(r => r.success).Count;
        int highCount = _scanResults.FindAll(r => r.success && r.confidence == "高").Count;
        int medCount = _scanResults.FindAll(r => r.success && r.confidence == "中").Count;

        EditorGUILayout.BeginVertical(GUI.skin.box);

        if (successCount > 0)
        {
            EditorGUILayout.HelpBox(
                $"✅ 成功分析 {successCount} 个参数（高置信度 {highCount}，中置信度 {medCount}）",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("💾 保存映射（另存为新文件）", GUILayout.Height(32)))
            {
                SaveMappingToFile();
            }
            if (GUILayout.Button("📋 复制结果", GUILayout.Height(32)))
            {
                CopyResultsToClipboard();
            }
            EditorGUILayout.EndHorizontal();

            // ── 一键写入 fuxuan_map.json ──
            if (highCount + medCount > 0)
            {
                if (GUILayout.Button($"📝 写入高/中置信度（{highCount + medCount}条）到映射表", GUILayout.Height(36)))
                {
                    ApplyHighConfResults();
                }
                if (_lastAppliedCount > 0)
                {
                    EditorGUILayout.HelpBox($"✅ 已写入 {_lastAppliedCount} 条到 fuxuan_map.json", MessageType.Success);
                }
            }
        }
        else if (_scanResults.Count > 0)
        {
            EditorGUILayout.HelpBox(
                "⚠️ 所有参数分析均未成功。\n" +
                "请检查 GLM_API_KEY 环境变量是否正确设置，以及网络连接。",
                MessageType.Warning);
        }

        EditorGUILayout.EndVertical();
    }

    // ================================================================
    //  Output
    // ================================================================

    private void CopyResultsToClipboard()
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"formatVersion\": \"2.0\",");
        sb.AppendLine($"  \"modelName\": \"{_model?.name ?? "unknown"}\",");
        sb.AppendLine($"  \"generatedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
        sb.AppendLine("  \"visionScanResults\": [");

        for (int i = 0; i < _scanResults.Count; i++)
        {
            var r = _scanResults[i];
            sb.AppendLine("    {");
            sb.AppendLine($"      \"paramId\": \"{r.paramId}\",");
            sb.AppendLine($"      \"min\": {r.min:F2},");
            sb.AppendLine($"      \"max\": {r.max:F2},");
            sb.AppendLine($"      \"suggestedSemantic\": \"{r.suggestedSemantic ?? ""}\",");
            sb.AppendLine($"      \"confidence\": \"{r.confidence ?? "-"}\",");
            sb.AppendLine($"      \"description\": \"{EscapeJson(r.description ?? "")}\"");
            sb.Append("    }");
            if (i < _scanResults.Count - 1) sb.AppendLine(",");
            else sb.AppendLine();
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");

        GUIUtility.systemCopyBuffer = sb.ToString();
        SetStatus($"📋 已复制 {_scanResults.Count} 条结果到剪贴板");
    }

    private void SaveMappingToFile()
    {
        if (_model == null) return;

        string defaultPath = "Assets/Scripts/Live2DFramework/ParamMaps/fuxuan_map.json";
        string path = EditorUtility.SaveFilePanelInProject(
            "保存映射结果",
            $"vision_mapping_{_model.name}_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            "json",
            "保存视觉扫描映射结果",
            "Assets/Scripts/Live2DFramework/ParamMaps");

        if (string.IsNullOrEmpty(path)) return;

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"formatVersion\": \"2.0\",");
        sb.AppendLine($"  \"modelName\": \"{_model.name}\",");
        sb.AppendLine($"  \"generatedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
        sb.AppendLine("  \"visionScanResults\": [");

        int count = 0;
        foreach (var r in _scanResults)
        {
            if (!r.success || string.IsNullOrEmpty(r.suggestedSemantic))
                continue;

            if (count > 0) sb.AppendLine(",");
            sb.AppendLine("    {");
            sb.AppendLine($"      \"semantic\": \"{r.suggestedSemantic}\",");
            sb.AppendLine($"      \"paramId\": \"{r.paramId}\",");
            sb.AppendLine($"      \"min\": {r.min:F2},");
            sb.AppendLine($"      \"max\": {r.max:F2},");
            sb.AppendLine($"      \"defaultValue\": {r.defaultValue:F2},");
            sb.AppendLine($"      \"description\": \"{EscapeJson(r.description ?? "")}\"");
            sb.Append("    }");
            count++;
        }

        sb.AppendLine();
        sb.AppendLine("  ]");
        sb.AppendLine("}");

        try
        {
            File.WriteAllText(path, sb.ToString());
            AssetDatabase.Refresh();
            SetStatus($"💾 已保存 {count} 条映射到 {path}");
            EditorUtility.DisplayDialog("保存成功", $"已保存 {count} 条映射到:\n{path}", "确定");
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("保存失败", ex.Message, "确定");
        }
    }

    /// <summary>将高/中置信度的扫描结果写入 fuxuan_map.json（保留完整原始格式）</summary>
    private void ApplyHighConfResults()
    {
        string mapPath = Path.Combine(Application.dataPath, "Scripts/Live2DFramework/ParamMaps/fuxuan_map.json");
        if (!File.Exists(mapPath))
        {
            EditorUtility.DisplayDialog("错误", $"找不到映射文件:\n{mapPath}", "确定");
            return;
        }

        // 筛选高+中置信度的成功结果
        var toApply = _scanResults.FindAll(r => r.success && !string.IsNullOrEmpty(r.suggestedSemantic)
            && (r.confidence == "高" || r.confidence == "中"));
        if (toApply.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有高/中置信度的结果可写入", "确定");
            return;
        }

        try
        {
            string jsonText = File.ReadAllText(mapPath, Encoding.UTF8);

            int newCount = 0, updateCount = 0;
            foreach (var r in toApply)
            {
                // 按 paramId 查找已有条目：匹配 "p": "ParamXXX"
                string searchPattern = $"\"p\": \"{EscapeJson(r.paramId)}\"";
                int idx = jsonText.IndexOf(searchPattern, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    // ── 更新已有条目 ──
                    // 从 "p": 往前找当前行的 "s": 位置
                    int lineStart = jsonText.LastIndexOf('\n', idx) + 1;
                    int lineEnd = jsonText.IndexOf('\n', idx);
                    if (lineEnd < 0) lineEnd = jsonText.Length;
                    string oldLine = jsonText.Substring(lineStart, lineEnd - lineStart).Trim();

                    // 在 oldLine 里替换 "s": "..." → "s": "新语义"
                    string newLine = Regex.Replace(oldLine,
                        "\"s\"\\s*:\\s*\"[^\"]*\"",
                        $"\"s\": \"{EscapeJson(r.suggestedSemantic)}\"");
                    // 替换/添加 "d": "..."
                    string desc = (r.description ?? "").Replace("\"", "\\\"");
                    if (Regex.IsMatch(newLine, "\"d\"\\s*:"))
                        newLine = Regex.Replace(newLine, "\"d\"\\s*:\\s*\"[^\"]*\"", $"\"d\": \"{desc}\"");
                    else
                        newLine = newLine.TrimEnd(',') + $", \"d\": \"{desc}\"";

                    jsonText = jsonText.Substring(0, lineStart) + newLine + jsonText.Substring(lineEnd);
                    updateCount++;
                }
                else
                {
                    // ── 新增条目（追加到 entries 数组中） ──
                    // 找倒数第一个 "}" + 换行 + "] 作为插入点
                    int insertPos = jsonText.LastIndexOf("\n  ]");
                    if (insertPos < 0) { insertPos = jsonText.LastIndexOf("]"); }
                    if (insertPos < 0) continue;

                    string desc = (r.description ?? "").Replace("\"", "\\\"");
                    string newEntry = $",\n    {{\"s\": \"{EscapeJson(r.suggestedSemantic)}\", \"p\": \"{r.paramId}\", \"d\": \"{desc}\"}}";
                    jsonText = jsonText.Substring(0, insertPos) + newEntry + jsonText.Substring(insertPos);
                    newCount++;
                }
            }

            File.WriteAllText(mapPath, jsonText, Encoding.UTF8);
            AssetDatabase.Refresh();

            _lastAppliedCount = newCount + updateCount;
            SetStatus($"📝 已应用 {newCount} 新增 + {updateCount} 更新 到 fuxuan_map.json");
            EditorUtility.DisplayDialog("应用成功",
                $"已写入 {newCount + updateCount} 条到 fuxuan_map.json\n" +
                $"新增: {newCount}\n更新: {updateCount}\n\n" +
                "注意：新条目仅有 s/p/d 三字段，如需 part/domain/axis 请手动补充。", "确定");
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("写入失败", ex.Message, "确定");
        }
    }

    // ================================================================
    //  Message type helper
    // ================================================================

    private MessageType GetMessageType(string status)
    {
        if (string.IsNullOrEmpty(status)) return MessageType.None;
        if (status.StartsWith("✅") || status.StartsWith("📋") || status.StartsWith("💾"))
            return MessageType.Info;
        if (status.StartsWith("❌"))
            return MessageType.Error;
        if (status.StartsWith("⚠️"))
            return MessageType.Warning;
        return MessageType.Info;
    }

    // ================================================================
    //  Response models (same as ToolCallInvoker)
    // ================================================================

    [Serializable]
    private class GlmVisionResponse
    {
        public GlmChoice[] choices;
    }

    [Serializable]
    private class GlmChoice
    {
        public GlmMessage message;
    }

    [Serializable]
    private class GlmMessage
    {
        public string content;
    }

    [Serializable]
    private class GlmErrorResponse
    {
        public GlmErrorDetail error;
    }

    [Serializable]
    private class GlmErrorDetail
    {
        public string message;
    }

    [Serializable]
    private class VisionAnalysisResult
    {
        public string bodyPart;
        public string visualChange;
        public string suggestedSemantic;
        public string confidence;
        public string description;
    }
}
