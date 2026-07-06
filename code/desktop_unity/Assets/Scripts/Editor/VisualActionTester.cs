using Live2D.Cubism.Core;
using Live2DFramework.ActionAgent;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;

/// <summary>
/// Phase 6: 空闲动作视觉自动测试器
///
/// 自动遍历所有 JSON 驱动的空闲动作（id 1~9），通过 GLM-4V 视觉模型分析
/// 每步动作的截图，验证动作是否正确播放，输出可视化的测试报告。
///
/// 使用方法：
///   1. 进入 Play Mode
///   2. Tools → Live2D → 动作视觉测试 (GLM-4V)
///   3. 点击"开始测试"
///   4. 等待测试完成，查看报告
/// </summary>
public class VisualActionTester : EditorWindow
{
    // ================================================================
    //  Menu entry
    // ================================================================

    [MenuItem("Tools/Live2D/动作视觉测试 (GLM-4V)")]
    private static void OpenWindow()
    {
        var window = GetWindow<VisualActionTester>("动作视觉测试");
        window.minSize = new Vector2(680, 500);
        window.Show();
    }

    // ================================================================
    //  Config
    // ================================================================

    /// <summary>测试的动作 ID 列表（排除硬编码的特殊动作 4,7）</summary>
    private static readonly int[] ALL_TEST_ACTION_IDS = { 1, 2, 3, 5, 6, 8, 9 };

    /// <summary>每个动作的等待时间（秒），从触发到截图的峰值位置</summary>
    private static readonly Dictionary<int, float> ACTION_WAIT_TIMES = new Dictionary<int, float>
    {
        { 1, 1.0f },   // tilt:    1.0s easeOut peak
        { 2, 1.0f },   // smile:   1.0s easeOut peak
        { 3, 1.0f },   // brow:    1.0s easeOut peak
        { 5, 1.5f },   // stretch: 0.8s easeOut + buffer → hold phase
        { 6, 1.5f },   // cry:     1.0s easeOut + buffer → smooth phase
        { 8, 1.2f },   // blush:   0.8s easeOut + buffer → smooth phase
        { 9, 1.2f },   // confuse: 0.8s easeOut + buffer → hold phase
    };

    /// <summary>动作显示名映射</summary>
    private static readonly Dictionary<int, string> ACTION_DISPLAY_NAMES = new Dictionary<int, string>
    {
        { 1, "歪头 (tilt)" },
        { 2, "微笑 (smile)" },
        { 3, "挑眉 (brow)" },
        { 4, "⭐星辉 (hardcoded)" },
        { 5, "伸懒腰 (stretch)" },
        { 6, "委屈 (cry)" },
        { 7, "✨法阵 (hardcoded)" },
        { 8, "害羞 (blush)" },
        { 9, "困惑 (confuse)" },
    };

    /// <summary>API 超时（秒）</summary>
    private double _apiTimeout = 90.0;

    // ================================================================
    //  Test action info from JSON
    // ================================================================

    private struct ActionInfo
    {
        public int id;
        public string name;
        public string displayName;
        public string description;
    }

    private List<ActionInfo> _actionInfos = new List<ActionInfo>();

    // ================================================================
    //  State machine
    // ================================================================

    private enum TestState
    {
        Idle,
        FindingRenderer,    // 查找 Live2DRenderer
        LoadActionConfig,   // 读取动作配置
        TriggerAction,      // 触发动作
        WaitingReset,       // 等待参数复位
        WaitingPeak,        // 等待动作到达峰值
        Capturing,          // 截图
        CallingApi,         // 调用 GLM-4V
        FinishAction,       // 记录结果
        Finished            // 全部完成
    }

    private TestState _state = TestState.Idle;
    private double _stateEnterTime;

    // ================================================================
    //  Fields
    // ================================================================

    private Live2DRenderer _renderer;
    private CubismModel _model;

    // 当前测试动作索引
    private int _currentActionIndex = -1;
    private ActionInfo _currentAction;

    // 截图数据
    private byte[] _screenshotBytes;

    // API 调用
    private UnityWebRequest _activeRequest;
    private UnityWebRequestAsyncOperation _activeRequestOp;
    private double _apiCallStartTime;

    // 测试结果
    [Serializable]
    private class ActionResult
    {
        public int actionId;
        public string actionName;
        public string displayName;
        public string description;
        public bool apiSuccess;
        public string glmAnalysis;      // 完整回复
        public string visibleParts;     // 可见部位
        public string issues;           // 问题
        public string confidence;       // 置信度
        public string suggestion;       // 建议
    }

    private List<ActionResult> _results = new List<ActionResult>();
    private ActionResult _currentResult;

    // UI
    private string _statusText = "";
    private string _lastLoggedStatus = "";   // 防刷屏
    private Vector2 _scrollPos;
    private bool _updateAttached = false;

    // 配置选项
    private bool _includeSpecialActions = false;   // 是否测试硬编码动作 4,7
    private bool _saveScreenshots = true;          // 是否保存截图到磁盘

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
        CleanupRequest();
    }

    // ================================================================
    //  Editor Update — 状态机驱动
    // ================================================================

    private void OnEditorUpdate()
    {
        if (_state == TestState.Idle || _state == TestState.Finished)
            return;

        try
        {
            switch (_state)
            {
                case TestState.FindingRenderer:
                    FindRenderer();
                    break;
                case TestState.LoadActionConfig:
                    LoadActionConfigs();
                    break;
                case TestState.TriggerAction:
                    TriggerCurrentAction();
                    break;
                case TestState.WaitingReset:
                    WaitForReset();
                    break;
                case TestState.WaitingPeak:
                    WaitForPeak();
                    break;
                case TestState.Capturing:
                    CaptureScreenshot();
                    break;
                case TestState.CallingApi:
                    PollApiCall();
                    break;
                case TestState.FinishAction:
                    FinishCurrentAction();
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[VisualActionTester] 状态机异常: {ex.Message}");
            SetStatus($"❌ 错误: {ex.Message}");
            _state = TestState.Idle;
            Repaint();
        }
    }

    // ================================================================
    //  State handlers
    // ================================================================

    /// <summary>查找渲染器等待超时（秒）</summary>
    private const double FIND_RENDERER_TIMEOUT = 8.0;

    private void FindRenderer()
    {
        Debug.Log("[VisualActionTester] FindRenderer: 开始查找 Live2DRenderer...");
        _renderer = FindObjectOfType<Live2DRenderer>();
        Debug.Log($"[VisualActionTester] FindRenderer: FindObjectOfType<Live2DRenderer>() = {(_renderer != null)}");

        if (_renderer == null)
        {
            var hybrid = FindObjectOfType<HybridRenderer>();
            Debug.Log($"[VisualActionTester] FindRenderer: HybridRenderer = {hybrid != null}");
            if (hybrid != null)
            {
                _renderer = hybrid.live2DRenderer;
                if (_renderer == null)
                {
                    // HybridRenderer 存在但 live2DRenderer 还没加载
                }
            }
            else
            {
                var pet = FindObjectOfType<DesktopPet>();
                Debug.Log($"[VisualActionTester] FindRenderer: DesktopPet = {pet != null}");
                if (pet == null)
                {
                    double elapsed = EditorApplication.timeSinceStartup - _stateEnterTime;
                    SetStatus($"⏳ 等待 pet (DesktopPet) 创建... ({elapsed:F0}s)");

                    if (elapsed > FIND_RENDERER_TIMEOUT)
                    {
                        string sceneHint = "请确保当前场景是 Assets/Scenes/SampleScene.scene" +
                            "（该场景含有 pet GameObject + Live2DRenderer 组件）" +
                            "\n① 双击 Assets/Scenes/SampleScene.scene 打开" +
                            "\n② 点击 Unity 上方的 Play 按钮进入 Play Mode" +
                            "\n③ 再点击此窗口的「开始测试」";
                        SetStatus("❌ 场景中未找到 DesktopPet/Live2DRenderer\n" + sceneHint);
                        Debug.LogError("[VisualActionTester] FindRenderer 超时: 未找到 DesktopPet");
                        _state = TestState.Idle;
                    }
                    Repaint();
                    return;
                }

                // 有 DesktopPet，等它的 Start() 添加 HybridRenderer
                double petElapsed = EditorApplication.timeSinceStartup - _stateEnterTime;
                SetStatus($"⏳ 等待 DesktopPet.Start() 创建 HybridRenderer... ({petElapsed:F0}s)");

                if (petElapsed > FIND_RENDERER_TIMEOUT)
                {
                    SetStatus("❌ DesktopPet.Start() 超时 — 请检查是否处于 Play Mode？");
                    Debug.LogError("[VisualActionTester] DesktopPet.Start() 超时");
                    _state = TestState.Idle;
                }
                Repaint();
                return;
            }
        }

        // 有 Live2DRenderer 了，等 CubismModel 加载
        if (_renderer != null)
        {
            _model = _renderer.CubismModel;
            Debug.Log($"[VisualActionTester] CubismModel = {_model != null}");

            if (_model == null)
            {
                double elapsed = EditorApplication.timeSinceStartup - _stateEnterTime;
                SetStatus($"⏳ Live2D 模型加载中... ({elapsed:F0}s)");

                if (elapsed > FIND_RENDERER_TIMEOUT)
                {
                    SetStatus("❌ Live2D 模型加载超时 — 检查 Console 日志看 Live2DRenderer 报错");
                    Debug.LogError("[VisualActionTester] CubismModel 加载超时");
                    _state = TestState.Idle;
                }
                Repaint();
                return;
            }

            Debug.Log($"[VisualActionTester] ✅ 模型已就绪: {_model.name}, 参数数={_model.Parameters.Length}");
            SetStatus($"✅ 已找到 Live2DRenderer (模型: {_model.name})，加载动作配置...");
            _state = TestState.LoadActionConfig;
            _stateEnterTime = EditorApplication.timeSinceStartup;
            Repaint();
        }
        else
        {
            double elapsed = EditorApplication.timeSinceStartup - _stateEnterTime;
            SetStatus($"⏳ 等待场景组件就绪... ({elapsed:F0}s)");

            if (elapsed > FIND_RENDERER_TIMEOUT)
            {
                string sceneHint = "请确保当前场景是 Assets/Scenes/SampleScene.scene" +
                    "（该场景含有 pet GameObject + Live2DRenderer 组件）" +
                    "\n① 双击 Assets/Scenes/SampleScene.scene 打开" +
                    "\n② 点击 Unity 上方的 Play 按钮进入 Play Mode" +
                    "\n③ 再点击此窗口的「开始测试」";
                SetStatus("❌ 场景中未找到 Live2DRenderer\n" + sceneHint);
                Debug.LogError("[VisualActionTester] 超时: 场景中无 Live2DRenderer/DesktopPet");
                _state = TestState.Idle;
            }
            Repaint();
        }
    }

    private void LoadActionConfigs()
    {
        // 从 JSON 资源加载动作配置
        var idleAsset = Resources.Load<TextAsset>("Live2D/IdleActions/idle_actions");
        if (idleAsset != null)
        {
            try
            {
                var root = JsonUtility.FromJson<IdleActionRootConfig>(idleAsset.text);
                if (root?.actions != null)
                {
                    _actionInfos.Clear();
                    foreach (var a in root.actions)
                    {
                        _actionInfos.Add(new ActionInfo
                        {
                            id = a.id,
                            name = a.name,
                            displayName = a.displayName,
                            description = a.description
                        });
                    }
                    SetStatus($"✅ 已加载 {_actionInfos.Count} 个动作配置");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VisualActionTester] JSON 解析失败，使用默认配置: {ex.Message}");
                UseDefaultActionInfos();
            }
        }
        else
        {
            UseDefaultActionInfos();
        }

        // 构建测试列表
        _currentActionIndex = -1;
        _results.Clear();
        _state = TestState.TriggerAction;
        _stateEnterTime = EditorApplication.timeSinceStartup;
        AdvanceToNextAction();
    }

    private void UseDefaultActionInfos()
    {
        _actionInfos = new List<ActionInfo>
        {
            new ActionInfo { id = 1, name = "tilt",      displayName = "歪头",   description = "往一侧歪头卖萌" },
            new ActionInfo { id = 2, name = "smile",     displayName = "微笑",   description = "眯眼微笑" },
            new ActionInfo { id = 3, name = "brow",      displayName = "挑眉",   description = "眉毛微动" },
            new ActionInfo { id = 4, name = "star_spin", displayName = "星辉",   description = "星辉环绕（硬编码）" },
            new ActionInfo { id = 5, name = "stretch",   displayName = "伸懒腰", description = "右手举高+身体后仰+眯眼张嘴" },
            new ActionInfo { id = 6, name = "cry",       displayName = "委屈",   description = "泪眼汪汪+低头+八字眉" },
            new ActionInfo { id = 7, name = "magic_circle", displayName = "法阵", description = "法阵显现（硬编码）" },
            new ActionInfo { id = 8, name = "blush",     displayName = "害羞",   description = "脸黑+眼神躲闪+低头" },
            new ActionInfo { id = 9, name = "confuse",   displayName = "困惑",   description = "歪头+皱眉+眯眼" },
        };
    }

    private void TriggerCurrentAction()
    {
        if (_renderer == null)
        {
            SetStatus("❌ Live2DRenderer 丢失");
            _state = TestState.Idle;
            Repaint();
            return;
        }

        // 复位所有参数到默认
        if (_model != null && _model.Parameters != null)
        {
            foreach (var p in _model.Parameters)
            {
                p.Value = p.DefaultValue;
            }
            _model.ForceUpdateNow();
        }

        // 等待极短时间让参数复位生效
        _state = TestState.WaitingReset;
        _stateEnterTime = EditorApplication.timeSinceStartup;
        Repaint();
    }

    private void WaitForReset()
    {
        // 短暂等待让参数复位生效，然后触发动作
        if (EditorApplication.timeSinceStartup - _stateEnterTime < 0.3f)
        {
            SetStatus("🔄 复位参数中...");
            Repaint();
            return;
        }

        // 触发动作
        bool isSpecial = (_currentAction.id == 4 || _currentAction.id == 7);
        if (isSpecial)
        {
            _renderer.ForceIdleAction(_currentAction.id);
        }
        else
        {
            _renderer.ForceAction($"idle:{_currentAction.id}");
        }

        SetStatus($"▶ [{_currentActionIndex + 1}/{GetTestActionCount()}] 触发动作 #{_currentAction.id} {_currentAction.displayName}...");
        _state = TestState.WaitingPeak;
        _stateEnterTime = EditorApplication.timeSinceStartup;
        Repaint();
    }

    private void WaitForPeak()
    {
        float waitTime = GetWaitTime(_currentAction.id);
        double elapsed = EditorApplication.timeSinceStartup - _stateEnterTime;

        SetStatus($"⏳ 等待动作峰值 ({elapsed:F1}s / {waitTime:F1}s)");

        if (elapsed >= waitTime)
        {
            _state = TestState.Capturing;
            _stateEnterTime = EditorApplication.timeSinceStartup;
            Repaint();
        }
    }

    private void CaptureScreenshot()
    {
        if (_renderer == null)
        {
            SetStatus("❌ Live2DRenderer 丢失");
            _state = TestState.Idle;
            Repaint();
            return;
        }

        // 使用运行时 overlay 截图
        _screenshotBytes = _renderer.CaptureModelSnapshot();

        if (_screenshotBytes == null)
        {
            SetStatus($"⚠️ 动作 #{_currentAction.id} 截图失败（overlayRT 无效）");
            // 标记失败但仍继续
            _currentResult.apiSuccess = false;
            _currentResult.glmAnalysis = "截图失败";
            _state = TestState.FinishAction;
            Repaint();
            return;
        }

        // 可选保存截图
        if (_saveScreenshots)
        {
            SaveScreenshotToDisk();
        }

        // 调用 API
        int imgSizeKB = _screenshotBytes.Length / 1024;
        SetStatus($"📸 截图完成 ({imgSizeKB}KB)，正在调用 GLM-4V 分析...");
        _state = TestState.CallingApi;
        _stateEnterTime = EditorApplication.timeSinceStartup;
        StartApiCall();
        Repaint();
    }

    private void FinishCurrentAction()
    {
        if (_currentResult != null)
        {
            _results.Add(_currentResult);
        }

        AdvanceToNextAction();
    }

    // ================================================================
    //  Navigation
    // ================================================================

    private int GetTestActionCount()
    {
        int count = 0;
        foreach (var info in _actionInfos)
        {
            if (info.id == 4 || info.id == 7)
            {
                if (_includeSpecialActions) count++;
            }
            else
            {
                count++;
            }
        }
        return count;
    }

    private void AdvanceToNextAction()
    {
        // 清理
        CleanupRequest();
        _screenshotBytes = null;
        _currentResult = null;

        // 找下一个要测试的动作
        while (true)
        {
            _currentActionIndex++;
            if (_currentActionIndex >= _actionInfos.Count)
            {
                // 全部完成
                _state = TestState.Finished;
                SetStatus($"✅ 测试完成！共测试 {_results.Count} 个动作");
                // 生成报告
                GenerateReport();
                Repaint();
                return;
            }

            _currentAction = _actionInfos[_currentActionIndex];

            // 跳过不需要测试的
            if ((_currentAction.id == 4 || _currentAction.id == 7) && !_includeSpecialActions)
                continue;

            break;
        }

        // 初始化当前结果
        _currentResult = new ActionResult
        {
            actionId = _currentAction.id,
            actionName = _currentAction.name,
            displayName = _currentAction.displayName,
            description = _currentAction.description,
            apiSuccess = false
        };

        SetStatus($"🔄 [{_currentActionIndex + 1}/{GetTestActionCount()}] 准备测试: {_currentAction.displayName}");
        _state = TestState.TriggerAction;
        _stateEnterTime = EditorApplication.timeSinceStartup;
        Repaint();
    }

    // ================================================================
    //  GLM-4V API call
    // ================================================================

    private void StartApiCall()
    {
        try
        {
            string apiKey = ChatConfig.GlmApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                SetStatus("❌ GLM_API_KEY 未设置（环境变量）");
                _currentResult.apiSuccess = false;
                _currentResult.glmAnalysis = "GLM_API_KEY 未设置";
                _state = TestState.FinishAction;
                Repaint();
                return;
            }

            string base64 = Convert.ToBase64String(_screenshotBytes);
            string dataUrl = "data:image/png;base64," + base64;

            // ─── 构建 prompt ───────────────────────────────────────
            string prompt = "你是一个 Live2D 虚拟角色动作质量分析专家。";
            prompt += "你将看到一张 Live2D 角色\"符玄\"(Fu Xuan)的动作截图。\n\n";
            prompt += "【当前动作】\n";
            prompt += "- 动作 ID: " + _currentAction.id + "\n";
            prompt += "- 动作名称: " + _currentAction.displayName + "(" + _currentAction.name + ")\n";
            prompt += "- 动作描述: " + _currentAction.description + "\n\n";
            prompt += "请从以下几个方面分析这张截图：\n\n";
            prompt += "1.【动作可见性】这个动作的典型视觉效果是否在截图中体现？哪些身体部位有明显变化或位移？\n";
            prompt += "2.【自然度】角色的姿势、表情是否看起来自然流畅？\n";
            prompt += "3.【异常检测】是否存在以下问题：\n";
            prompt += "   - 穿模(身体部位交叉、穿透)\n";
            prompt += "   - 变形(拉伸、扭曲、比例失调)\n";
            prompt += "   - 参数异常(眼睛、嘴巴等部位超出正常范围)\n";
            prompt += "   - 位置偏移(模型整体或部分不在正常位置)\n";
            prompt += "4.【整体评价】这个动作是否成功播放？\n\n";
            prompt += "【身体部位清单 - 供参考】\n";
            prompt += "- 头部&面部：左/右眼、左/右眉、嘴巴、下颌\n";
            prompt += "- 头发：刘海、鬓发、后发、发饰\n";
            prompt += "- 身体：颈部、躯干、肩\n";
            prompt += "- 右臂：上臂、前臂、手腕、手掌、手指\n";
            prompt += "- 左臂：上臂、前臂\n";
            prompt += "- 腿/裙摆：左/右腿、抬腿\n";
            prompt += "- 裙子、饰品、特效\n";
            prompt += "请用以下 JSON 格式回复(不要其他内容)：\n";
            prompt += "{\"visibleParts\":\"描述哪些部位有明显变化\",";
            prompt += "\"looksNatural\":true/false,";
            prompt += "\"issues\":\"如果有异常请描述，没有就写'无'\",";
            prompt += "\"confidence\":\"高/中/低\",";
            prompt += "\"suggestion\":\"改进建议(如有)\",";
            prompt += "\"description\":\"中文详细分析\"}\n";
            prompt += "注意：confidence 字段只能用 高/中/低。";

            string url = ChatConfig.GlmApiBaseUrl.TrimEnd('/') + "/chat/completions";

            string jsonBody = "{";
            jsonBody += "\"model\":\"" + EscapeJson(ChatConfig.GlmVisionModel) + "\",";
            jsonBody += "\"messages\":[{";
            jsonBody += "\"role\":\"user\",";
            jsonBody += "\"content\":[";
            jsonBody += "{\"type\":\"text\",\"text\":\"" + EscapeJson(prompt) + "\"},";
            jsonBody += "{\"type\":\"image_url\",\"image_url\":{\"url\":\"" + EscapeJson(dataUrl) + "\"}}";
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
            Debug.LogError($"[VisualActionTester] 启动 API 调用失败: {ex.Message}");
            _currentResult.apiSuccess = false;
            _currentResult.glmAnalysis = $"启动失败: {ex.Message}";
            _state = TestState.FinishAction;
            Repaint();
        }
    }

    private void PollApiCall()
    {
        // 超时保护
        if (EditorApplication.timeSinceStartup - _apiCallStartTime > _apiTimeout)
        {
            Debug.LogWarning($"[VisualActionTester] API 调用超时（{_apiTimeout:F0}s）");
            _currentResult.apiSuccess = false;
            _currentResult.glmAnalysis = $"API 调用超时（{_apiTimeout:F0}s）";
            _state = TestState.FinishAction;
            Repaint();
            return;
        }

        if (_activeRequestOp == null)
        {
            _currentResult.apiSuccess = false;
            _currentResult.glmAnalysis = "请求未正确初始化";
            _state = TestState.FinishAction;
            Repaint();
            return;
        }

        if (!_activeRequestOp.isDone)
        {
            double elapsed = EditorApplication.timeSinceStartup - _apiCallStartTime;
            SetStatus($"🔍 GLM-4V 分析中 [{_currentAction.displayName}]... ({elapsed:F0}s)");
            return;
        }

        // 请求完成
        if (_activeRequest.result == UnityWebRequest.Result.Success)
        {
            string responseText = _activeRequest.downloadHandler.text;
            ParseGlmResponse(responseText);
        }
        else
        {
            string errBody = _activeRequest.downloadHandler?.text ?? "";
            string errMsg = ParseGlmError(errBody, _activeRequest.error);
            Debug.LogWarning($"[VisualActionTester] API 调用失败: {errMsg}");
            _currentResult.apiSuccess = false;
            _currentResult.glmAnalysis = $"API 错误: {errMsg}";
            _state = TestState.FinishAction;
            Repaint();
        }
    }

    private void ParseGlmResponse(string json)
    {
        try
        {
            var resp = JsonUtility.FromJson<GlmVisionResponse>(json);
            if (resp?.choices != null && resp.choices.Length > 0 && resp.choices[0].message != null)
            {
                string content = resp.choices[0].message.content;
                _currentResult.apiSuccess = true;
                _currentResult.glmAnalysis = content;

                // 尝试提取 JSON
                string jsonPart = ExtractJsonFromText(content);
                if (!string.IsNullOrEmpty(jsonPart))
                {
                    var parsed = JsonUtility.FromJson<VisionAnalysisResult>(jsonPart);
                    if (parsed != null)
                    {
                        _currentResult.visibleParts = parsed.visibleParts;
                        _currentResult.issues = parsed.issues;
                        _currentResult.confidence = parsed.confidence;
                        _currentResult.suggestion = parsed.suggestion;

                        string naturalStr = parsed.looksNatural ? "自然 ✅" : "异常 ⚠️";
                        SetStatus($"✅ [{_currentAction.displayName}] 部位: {TruncateText(parsed.visibleParts, 40)} | {naturalStr} | 置信度: {parsed.confidence}");
                        _state = TestState.FinishAction;
                        Repaint();
                        return;
                    }
                }

                // JSON 提取失败，存原文前 200 字
                _currentResult.visibleParts = "（非结构化回复）";
                _currentResult.confidence = "中";
                SetStatus($"⚠️ [{_currentAction.displayName}] 回复非结构化，已保存原文");
                _state = TestState.FinishAction;
                Repaint();
            }
            else
            {
                _currentResult.apiSuccess = false;
                _currentResult.glmAnalysis = "API 返回空响应";
                SetStatus($"❌ [{_currentAction.displayName}] API 返回空响应");
                _state = TestState.FinishAction;
                Repaint();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisualActionTester] 解析响应失败: {ex.Message}");
            _currentResult.apiSuccess = false;
            _currentResult.glmAnalysis = $"解析失败: {ex.Message}";
            _state = TestState.FinishAction;
            Repaint();
        }
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private float GetWaitTime(int actionId)
    {
        if (ACTION_WAIT_TIMES.TryGetValue(actionId, out float t))
            return t;
        return 1.2f; // 默认等待
    }

    private void SaveScreenshotToDisk()
    {
        if (_screenshotBytes == null) return;
        try
        {
            string dir = Application.dataPath + "/../screenshots/action_test";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string filename = $"action{_currentAction.id}_{_currentAction.name}_{DateTime.Now:HHmmssfff}.png";
            string path = dir + "/" + filename;
            File.WriteAllBytes(path, _screenshotBytes);
            Debug.Log($"[VisualActionTester] 截图已保存: {path}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VisualActionTester] 保存截图失败: {ex.Message}");
        }
    }

    private void CleanupRequest()
    {
        if (_activeRequest != null)
        {
            _activeRequest.Dispose();
            _activeRequest = null;
        }
        _activeRequestOp = null;
    }

    private void SetStatus(string text)
    {
        _statusText = text;
        // 文本变化时才打印到 Console（防每帧刷屏）
        if (text != _lastLoggedStatus)
        {
            _lastLoggedStatus = text;
            Debug.Log($"[VisualActionTester] {text}");
        }
        Repaint();
    }

    private static string TruncateText(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen) return text ?? "";
        return text.Substring(0, maxLen) + "...";
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }

    private static string ExtractJsonFromText(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        // 尝试 ```json ... ``` 块
        int start = text.IndexOf("```json");
        if (start >= 0)
        {
            start += 7;
            int end = text.IndexOf("```", start);
            if (end > start) return text.Substring(start, end - start).Trim();
        }

        // 尝试 ``` ... ``` 块
        start = text.IndexOf("```");
        if (start >= 0)
        {
            start += 3;
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
                else if (text[i] == '}')
                {
                    braceDepth--;
                    if (braceDepth == 0)
                        return text.Substring(start, i - start + 1);
                }
            }
        }

        return null;
    }

    private static string ParseGlmError(string responseBody, string networkError)
    {
        if (!string.IsNullOrEmpty(responseBody))
        {
            try
            {
                var errResp = JsonUtility.FromJson<GlmErrorResponse>(responseBody);
                if (errResp?.error != null && !string.IsNullOrEmpty(errResp.error.message))
                    return errResp.error.message;
            }
            catch { }
        }
        return networkError ?? "未知错误";
    }

    /// <summary>生成可视化测试报告</summary>
    private void GenerateReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════");
        sb.AppendLine("  空闲动作 GLM-4V 视觉测试报告");
        sb.AppendLine($"  生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("═══════════════════════════════════════════");
        sb.AppendLine();

        int passCount = 0, failCount = 0, warnCount = 0;
        foreach (var r in _results)
        {
            string icon = r.apiSuccess ? "✅" : "❌";
            string status = r.apiSuccess ? "通过" : "失败";

            if (r.apiSuccess)
            {
                bool hasIssues = !string.IsNullOrEmpty(r.issues) && r.issues != "无" && r.issues != "无异常";
                if (hasIssues)
                {
                    icon = "⚠️";
                    status = "有异常";
                    warnCount++;
                }
                else
                {
                    passCount++;
                }
            }
            else
            {
                failCount++;
            }

            sb.AppendLine($"{icon} 动作 #{r.actionId} {r.displayName} ({r.actionName}) — {status}");
            if (r.apiSuccess)
            {
                sb.AppendLine($"   描述: {r.description}");
                sb.AppendLine($"   可见部位: {r.visibleParts ?? "N/A"}");
                sb.AppendLine($"   异常: {r.issues ?? "N/A"}");
                sb.AppendLine($"   置信度: {r.confidence ?? "-"}  建议: {r.suggestion ?? "-"}");
            }
            else
            {
                sb.AppendLine($"   失败原因: {r.glmAnalysis ?? "未知错误"}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("───────────────────────────────────────────");
        sb.AppendLine($"  总计: {_results.Count}  |  通过: {passCount}  |  有异常: {warnCount}  |  失败: {failCount}");
        sb.AppendLine("───────────────────────────────────────────");

        string report = sb.ToString();
        Debug.Log($"[VisualActionTester]\n{report}");

        // 保存到文件
        try
        {
            string dir = Application.dataPath + "/../screenshots/action_test";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = dir + $"/test_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            File.WriteAllText(path, report, Encoding.UTF8);
            Debug.Log($"[VisualActionTester] 报告已保存: {path}");
        }
        catch { }

        // 复制到剪贴板
        GUIUtility.systemCopyBuffer = report;
        SetStatus($"📋 报告已生成并复制到剪贴板（通过 {passCount}/{_results.Count}）");
    }

    // ================================================================
    //  UI
    // ================================================================

    private void OnGUI()
    {
        EditorGUILayout.Space(10);

        // ─── 标题 ────────────────────────────────────────────────
        var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
        GUILayout.Label("🧪 空闲动作视觉测试", titleStyle);
        EditorGUILayout.LabelField("自动测试所有空闲动作，通过 GLM-4V 验证视觉效果", EditorStyles.miniLabel);
        EditorGUILayout.Space(5);

        // ─── 状态栏 ──────────────────────────────────────────────
        if (!string.IsNullOrEmpty(_statusText))
        {
            var msgType = GetMessageType(_statusText);
            EditorGUILayout.HelpBox(_statusText, msgType);
        }

        // ─── 控制区 ──────────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();

        bool isRunning = (_state != TestState.Idle && _state != TestState.Finished);

        GUI.enabled = !isRunning;
        if (GUILayout.Button("▶ 开始测试", GUILayout.Height(30)))
        {
            StartTest();
        }
        GUI.enabled = isRunning;
        if (GUILayout.Button("⏹ 停止", GUILayout.Height(30)))
        {
            StopTest();
        }
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();

        // ─── 配置选项 ──────────────────────────────────────────
        EditorGUILayout.Space(5);
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("测试选项", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _includeSpecialActions = EditorGUILayout.ToggleLeft("包含硬编码特殊动作（星辉 #4, 法阵 #7）", _includeSpecialActions);
        _saveScreenshots = EditorGUILayout.ToggleLeft("保存截图到磁盘 (screenshots/action_test/)", _saveScreenshots);
        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(this);
        }

        EditorGUILayout.EndVertical();

        // ─── 进度 ──────────────────────────────────────────────
        if (isRunning && _actionInfos.Count > 0)
        {
            int total = GetTestActionCount();
            float progress = total > 0 ? (float)_results.Count / total : 0f;
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), progress,
                $"{_results.Count} / {total} 完成");
        }

        // ─── 结果列表 ──────────────────────────────────────────
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("测试结果", EditorStyles.boldLabel);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        if (_results.Count == 0)
        {
            EditorGUILayout.HelpBox("尚未测试。点击「开始测试」启动自动测试（需处于 Play Mode）", MessageType.Info);
        }
        else
        {
            foreach (var r in _results)
            {
                DrawActionResult(r);
            }
        }

        EditorGUILayout.EndScrollView();

        // ─── 底部操作 ──────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();

        GUI.enabled = (_results.Count > 0);
        if (GUILayout.Button("📋 复制报告"))
        {
            var sb = new StringBuilder();
            foreach (var r in _results)
            {
                string icon = r.apiSuccess ? "✅" : "❌";
                string detail = r.apiSuccess
                    ? $"{r.visibleParts ?? "-"} | {r.issues ?? "-"} | {r.confidence ?? "-"}"
                    : $"失败原因: {r.glmAnalysis ?? "未知"}";
                sb.AppendLine($"{icon} #{r.actionId} {r.displayName}: {detail}");
            }
            GUIUtility.systemCopyBuffer = sb.ToString();
            SetStatus("📋 已复制简略报告到剪贴板");
        }
        if (GUILayout.Button("🗑 清空结果"))
        {
            _results.Clear();
            Repaint();
        }
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5);
    }

    private void DrawActionResult(ActionResult r)
    {
        string icon = "✅";
        Color bgColor = new Color(0.8f, 1f, 0.8f, 0.3f);

        if (!r.apiSuccess)
        {
            icon = "❌";
            bgColor = new Color(1f, 0.8f, 0.8f, 0.3f);
        }
        else if (!string.IsNullOrEmpty(r.issues) && r.issues != "无" && r.issues != "无异常")
        {
            icon = "⚠️";
            bgColor = new Color(1f, 1f, 0.8f, 0.3f);
        }

        var bg = new GUIStyle(EditorStyles.helpBox);
        EditorGUILayout.BeginVertical(bg);
        EditorGUILayout.BeginHorizontal();

        // 左侧：图标 + 标题
        EditorGUILayout.LabelField($"{icon} 动作 #{r.actionId} {r.displayName}",
            EditorStyles.boldLabel, GUILayout.Width(250));

        // 右侧：置信度
        if (!string.IsNullOrEmpty(r.confidence))
        {
            Color confColor;
            switch (r.confidence)
            {
                case "高": confColor = Color.green; break;
                case "中": confColor = Color.yellow; break;
                default: confColor = Color.gray; break;
            }
            var style = new GUIStyle(EditorStyles.label) { normal = { textColor = confColor } };
            EditorGUILayout.LabelField($"置信度: {r.confidence}", style);
        }

        EditorGUILayout.EndHorizontal();

        // 部位 + 异常
        if (!string.IsNullOrEmpty(r.visibleParts))
            EditorGUILayout.LabelField($"部位: {r.visibleParts}", EditorStyles.wordWrappedMiniLabel);

        if (!string.IsNullOrEmpty(r.issues) && r.issues != "无" && r.issues != "无异常")
        {
            var issueStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = Color.red } };
            EditorGUILayout.LabelField($"⚠ 异常: {r.issues}", issueStyle);
        }

        if (!string.IsNullOrEmpty(r.suggestion))
        {
            EditorGUILayout.LabelField($"建议: {r.suggestion}", EditorStyles.wordWrappedMiniLabel);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
    }

    // ================================================================
    //  Actions
    // ================================================================

    private void StartTest()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("提示", "请先进入 Play Mode，然后点击「开始测试」。", "确定");
            return;
        }

        Debug.Log("[VisualActionTester] ===== 开始测试 =====");
        Debug.Log($"[VisualActionTester] EditorApplication.isPlaying={EditorApplication.isPlaying}");

        // 重置状态
        _results.Clear();
        _currentActionIndex = -1;
        _currentResult = null;
        _screenshotBytes = null;
        _lastLoggedStatus = "";
        CleanupRequest();

        SetStatus("🔍 正在查找 Live2DRenderer...");
        _state = TestState.FindingRenderer;
        _stateEnterTime = EditorApplication.timeSinceStartup;
        Repaint();
    }

    private void StopTest()
    {
        CleanupRequest();
        _state = TestState.Idle;
        SetStatus("⏹ 已手动停止");
        Repaint();
    }

    // ================================================================
    //  Message type helper
    // ================================================================

    private MessageType GetMessageType(string status)
    {
        if (string.IsNullOrEmpty(status)) return MessageType.None;
        if (status.StartsWith("✅") || status.StartsWith("📋") || status.StartsWith("📸"))
            return MessageType.Info;
        if (status.StartsWith("❌"))
            return MessageType.Error;
        if (status.StartsWith("⚠️"))
            return MessageType.Warning;
        return MessageType.Info;
    }

    // ================================================================
    //  Response models
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
        public string visibleParts;
        public bool looksNatural;
        public string issues;
        public string confidence;
        public string suggestion;
        public string description;
    }
}
