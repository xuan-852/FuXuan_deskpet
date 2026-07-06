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
/// Phase 8: 自律训练管理器 — "你只需提供动作，AI 自动训练到标准"
///
/// 自动完成"生成动作 → 截图 → 保存参考图 → self-review 对比"的全流程。
/// 每个动作只需首次"教学"保存参考图，后续自动评估修正。
///
/// 使用方法：
///   1. 进入 Play Mode，等待模型加载完成
///   2. Tools → Live2D → 自律训练 (self-review)
///   3. 在动作列表中勾选要训练的动作
///   4. 点击「开始训练」
///   5. 系统自动逐个训练：生成 → 截图 → 保存参考 → 对比验证
/// </summary>
public class SelfTrainingManager : EditorWindow
{
    // ================================================================
    //  Menu entry
    // ================================================================

    [MenuItem("Tools/Live2D/自律训练 (self-review)")]
    private static void OpenWindow()
    {
        var window = GetWindow<SelfTrainingManager>("自律训练");
        window.minSize = new Vector2(680, 500);
        window.Show();
    }

    // ================================================================
    //  Config
    // ================================================================

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

    private double _apiTimeout = 90.0;

    /// <summary>最大自律循环迭代次数（AI 微调 + 自省对比）</summary>
    private int _maxIterations = 3;

    // ================================================================
    //  Training action info
    // ================================================================

    [Serializable]
    private class TrainActionInfo
    {
        public int id;
        public string name;         // 英文名，用于 self_review("name")
        public string displayName;
        public string description;
        public bool enabled = true; // 是否勾选训练
    }

    private List<TrainActionInfo> _actionInfos = new List<TrainActionInfo>();

    // ================================================================
    //  State machine
    // ================================================================

    private enum TrainState
    {
        Idle,
        FindingRenderer,        // 查找 Live2DRenderer
        LoadActionConfig,       // 读取动作配置
        TriggerAction,          // 触发动作
        WaitingReset,           // 等待参数复位
        WaitingPeak,            // 等待动作到达峰值
        Capturing,              // 截图
        SavingReference,        // 保存参考图到 ActionReferenceManager
        CallingApi,             // 调用 GLM-4V（对比分析）
        WaitThenNext,           // 短暂停顿后进入下一个
        Finished                // 全部完成
    }

    private TrainState _state = TrainState.Idle;
    private double _stateEnterTime;

    // ================================================================
    //  Fields
    // ================================================================

    private Live2DRenderer _renderer;
    private CubismModel _model;

    // 当前训练动作索引
    private int _currentActionIndex = -1;
    private TrainActionInfo _currentAction;

    // 截图数据
    private byte[] _screenshotBytes;

    // API 调用（GLM-4V 对比分析）
    private UnityWebRequest _activeRequest;
    private UnityWebRequestAsyncOperation _activeRequestOp;
    private double _apiCallStartTime;

    // 当前迭代次数
    private int _currentIteration = 0;

    // 训练结果
    [Serializable]
    private class TrainResult
    {
        public int actionId;
        public string actionName;
        public string displayName;
        public string description;
        public int iterationsUsed;
        public bool referenceSaved;     // 是否保存了参考图
        public bool finalPass;          // 最终是否达标
        public string lastReviewReport; // 最后一次 self-review 的报告
        public List<string> iterationLogs = new List<string>(); // 每次迭代日志
    }

    private List<TrainResult> _results = new List<TrainResult>();
    private TrainResult _currentResult;

    // UI
    private string _statusText = "";
    private string _lastLoggedStatus = "";
    private Vector2 _scrollPos;
    private bool _updateAttached = false;

    // 配置选项
    private bool _includeSpecialActions = false;
    private bool _runSelfReview = true;    // 训练后自动跑 self-review 验证

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
        if (_state == TrainState.Idle || _state == TrainState.Finished)
            return;

        try
        {
            switch (_state)
            {
                case TrainState.FindingRenderer:
                    FindRenderer();
                    break;
                case TrainState.LoadActionConfig:
                    LoadActionConfigs();
                    break;
                case TrainState.TriggerAction:
                    TriggerCurrentAction();
                    break;
                case TrainState.WaitingReset:
                    WaitForReset();
                    break;
                case TrainState.WaitingPeak:
                    WaitForPeak();
                    break;
                case TrainState.Capturing:
                    CaptureScreenshot();
                    break;
                case TrainState.SavingReference:
                    SaveReference();
                    break;
                case TrainState.CallingApi:
                    PollApiCall();
                    break;
                case TrainState.WaitThenNext:
                    WaitThenAdvance();
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SelfTraining] 状态机异常: {ex.Message}");
            SetStatus($"❌ 错误: {ex.Message}");
            _state = TrainState.Idle;
            Repaint();
        }
    }

    // ================================================================
    //  State handlers
    // ================================================================

    private const double FIND_RENDERER_TIMEOUT = 8.0;

    private void FindRenderer()
    {
        _renderer = FindObjectOfType<Live2DRenderer>();

        if (_renderer == null)
        {
            var hybrid = FindObjectOfType<HybridRenderer>();
            if (hybrid != null)
                _renderer = hybrid.live2DRenderer;
            else
            {
                var pet = FindObjectOfType<DesktopPet>();
                if (pet == null)
                {
                    double elapsed = EditorApplication.timeSinceStartup - _stateEnterTime;
                    SetStatus($"⏳ 等待 pet 创建... ({elapsed:F0}s)");
                    if (elapsed > FIND_RENDERER_TIMEOUT)
                    {
                        SetStatus("❌ 未找到 DesktopPet/Live2DRenderer，请确保处于 Play Mode");
                        _state = TrainState.Idle;
                    }
                    Repaint();
                    return;
                }
                double petElapsed = EditorApplication.timeSinceStartup - _stateEnterTime;
                SetStatus($"⏳ 等待 HybridRenderer 创建... ({petElapsed:F0}s)");
                if (petElapsed > FIND_RENDERER_TIMEOUT)
                {
                    SetStatus("❌ DesktopPet.Start() 超时");
                    _state = TrainState.Idle;
                }
                Repaint();
                return;
            }
        }

        if (_renderer != null)
        {
            _model = _renderer.CubismModel;
            if (_model == null)
            {
                double elapsed = EditorApplication.timeSinceStartup - _stateEnterTime;
                SetStatus($"⏳ Live2D 模型加载中... ({elapsed:F0}s)");
                if (elapsed > FIND_RENDERER_TIMEOUT)
                {
                    SetStatus("❌ 模型加载超时");
                    _state = TrainState.Idle;
                }
                Repaint();
                return;
            }

            SetStatus($"✅ 已就绪: {_model.name}，加载动作配置...");
            _state = TrainState.LoadActionConfig;
            _stateEnterTime = EditorApplication.timeSinceStartup;
            Repaint();
        }
        else
        {
            double elapsed = EditorApplication.timeSinceStartup - _stateEnterTime;
            SetStatus($"⏳ 等待场景就绪... ({elapsed:F0}s)");
            if (elapsed > FIND_RENDERER_TIMEOUT)
            {
                SetStatus("❌ 未找到 Live2DRenderer，请确保处于 Play Mode");
                _state = TrainState.Idle;
            }
            Repaint();
        }
    }

    private void LoadActionConfigs()
    {
        _actionInfos.Clear();
        bool loadedFromJson = false;

        // 尝试从 IdleAction JSON 加载
        var idleAsset = Resources.Load<TextAsset>("Live2D/IdleActions/idle_actions");
        if (idleAsset != null)
        {
            try
            {
                var root = JsonUtility.FromJson<IdleActionRootConfig>(idleAsset.text);
                if (root?.actions != null)
                {
                    foreach (var a in root.actions)
                    {
                        _actionInfos.Add(new TrainActionInfo
                        {
                            id = a.id,
                            name = a.name,
                            displayName = a.displayName,
                            description = a.description,
                            enabled = (a.id != 4 && a.id != 7) // 默认不勾选特殊动作
                        });
                    }
                    loadedFromJson = true;
                    SetStatus($"✅ 已加载 {_actionInfos.Count} 个动作配置（JSON）");
                }
            }
            catch { }
        }

        if (!loadedFromJson)
        {
            // 默认动作列表
            _actionInfos = new List<TrainActionInfo>
            {
                new TrainActionInfo { id = 1, name = "tilt",      displayName = "歪头",   description = "往一侧歪头卖萌" },
                new TrainActionInfo { id = 2, name = "smile",     displayName = "微笑",   description = "眯眼微笑" },
                new TrainActionInfo { id = 3, name = "brow",      displayName = "挑眉",   description = "眉毛微动" },
                new TrainActionInfo { id = 5, name = "stretch",   displayName = "伸懒腰", description = "右手举高+身体后仰+眯眼张嘴" },
                new TrainActionInfo { id = 6, name = "cry",       displayName = "委屈",   description = "泪眼汪汪+低头+八字眉" },
                new TrainActionInfo { id = 8, name = "blush",     displayName = "害羞",   description = "脸黑+眼神躲闪+低头" },
                new TrainActionInfo { id = 9, name = "confuse",   displayName = "困惑",   description = "歪头+皱眉+眯眼" },
            };
            SetStatus($"✅ 已加载默认动作列表（{_actionInfos.Count} 个）");
        }

        _currentActionIndex = -1;
        _results.Clear();
        _state = TrainState.TriggerAction;
        _stateEnterTime = EditorApplication.timeSinceStartup;
        AdvanceToNextAction();
    }

    private void TriggerCurrentAction()
    {
        if (_renderer == null)
        {
            SetStatus("❌ Live2DRenderer 丢失");
            _state = TrainState.Idle;
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

        _state = TrainState.WaitingReset;
        _stateEnterTime = EditorApplication.timeSinceStartup;
        Repaint();
    }

    private void WaitForReset()
    {
        if (EditorApplication.timeSinceStartup - _stateEnterTime < 0.3f)
        {
            SetStatus("🔄 复位参数中...");
            Repaint();
            return;
        }

        // ★ 触发动作 — 硬编码动作(4/7)用 ForceIdleAction，其他用 ForceAction(idle:)
        bool isSpecial = (_currentAction.id == 4 || _currentAction.id == 7);
        if (isSpecial)
        {
            _renderer.ForceIdleAction(_currentAction.id);
        }
        else
        {
            _renderer.ForceAction($"idle:{_currentAction.id}");
        }

        SetStatus($"▶ [{_currentActionIndex + 1}/{GetEnabledActionCount()}] 触发 {_currentAction.displayName}...");
        _state = TrainState.WaitingPeak;
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
            _state = TrainState.Capturing;
            _stateEnterTime = EditorApplication.timeSinceStartup;
            Repaint();
        }
    }

    private void CaptureScreenshot()
    {
        if (_renderer == null)
        {
            SetStatus("❌ Live2DRenderer 丢失");
            _state = TrainState.Idle;
            Repaint();
            return;
        }

        _screenshotBytes = _renderer.CaptureModelSnapshot();

        if (_screenshotBytes == null || _screenshotBytes.Length < 50)
        {
            SetStatus($"⚠️ {_currentAction.displayName} 截图失败");
            _currentResult.lastReviewReport = "❌ 截图失败";
            _state = TrainState.WaitThenNext;
            Repaint();
            return;
        }

        int imgSizeKB = _screenshotBytes.Length / 1024;
        SetStatus($"📸 截图完成 ({imgSizeKB}KB)");

        // → 直接保存参考图
        _state = TrainState.SavingReference;
        _stateEnterTime = EditorApplication.timeSinceStartup;
        Repaint();
    }

    // ================================================================
    //  ★ 核心：保存参考图 + 自律循环（self-review 对比）
    // ================================================================

    private void SaveReference()
    {
        // ★ 保存当前截图为此动作的标准参考图
        ActionReferenceManager.SaveReference(_currentAction.name, _screenshotBytes);
        _currentResult.referenceSaved = true;
        _currentResult.iterationLogs.Add($"📸 参考图已保存: {_currentAction.name}.png");

        SetStatus($"💾 参考图已保存: {_currentAction.name}");

        // ★ 如果启用了 self-review，进入对比验证阶段
        if (_runSelfReview)
        {
            _currentIteration = 0;
            _state = TrainState.CallingApi;
            _stateEnterTime = EditorApplication.timeSinceStartup;
            StartSelfReviewCall();
            Repaint();
        }
        else
        {
            _currentResult.finalPass = true;
            _currentResult.iterationsUsed = 0;
            _state = TrainState.WaitThenNext;
            _stateEnterTime = EditorApplication.timeSinceStartup;
            Repaint();
        }
    }

    /// <summary>发起 GLM-4V 双图对比调用（self-review 核心）</summary>
    private void StartSelfReviewCall()
    {
        CleanupRequest();

        try
        {
            string apiKey = ChatConfig.GlmApiKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                SetStatus("❌ GLM_API_KEY 未设置");
                _currentResult.lastReviewReport = "❌ GLM_API_KEY 未设置";
                _currentResult.finalPass = false;
                _state = TrainState.WaitThenNext;
                Repaint();
                return;
            }

            // 加载参考图
            string refDataUrl = ActionReferenceManager.LoadReferenceAsDataUrl(_currentAction.name);
            if (refDataUrl == null)
            {
                SetStatus("❌ 参考图加载失败");
                _currentResult.lastReviewReport = "❌ 参考图加载失败";
                _state = TrainState.WaitThenNext;
                Repaint();
                return;
            }

            // 当前截图
            string currentDataUrl = "data:image/png;base64," + Convert.ToBase64String(_screenshotBytes);

            // 构建对比 Prompt
            string prompt = BuildComparisonPrompt(_currentAction.name);

            string url = ChatConfig.GlmApiBaseUrl.TrimEnd('/') + "/chat/completions";

            string jsonBody = "{";
            jsonBody += "\"model\":\"" + EscapeJson(ChatConfig.GlmVisionModel) + "\",";
            jsonBody += "\"messages\":[{";
            jsonBody += "\"role\":\"user\",";
            jsonBody += "\"content\":[";
            jsonBody += "{\"type\":\"text\",\"text\":\"" + EscapeJson(prompt) + "\"},";
            jsonBody += "{\"type\":\"image_url\",\"image_url\":{\"url\":\"" + EscapeJson(refDataUrl) + "\"}},";
            jsonBody += "{\"type\":\"image_url\",\"image_url\":{\"url\":\"" + EscapeJson(currentDataUrl) + "\"}}";
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

            SetStatus($"🔍 自律验证第 {_currentIteration + 1} 轮...");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SelfTraining] API 调用启动失败: {ex.Message}");
            _currentResult.lastReviewReport = $"❌ API 失败: {ex.Message}";
            _state = TrainState.WaitThenNext;
            Repaint();
        }
    }

    private void PollApiCall()
    {
        if (EditorApplication.timeSinceStartup - _apiCallStartTime > _apiTimeout)
        {
            _currentResult.lastReviewReport = $"⏰ API 超时（{_apiTimeout:F0}s）";
            _currentResult.iterationLogs.Add($"⏰ 第 {_currentIteration + 1} 轮超时");
            _state = TrainState.WaitThenNext;
            Repaint();
            return;
        }

        if (_activeRequestOp == null || !_activeRequestOp.isDone)
        {
            double elapsed = EditorApplication.timeSinceStartup - _apiCallStartTime;
            SetStatus($"🔍 GLM-4V 分析中 [{_currentAction.displayName}]... ({elapsed:F0}s)");
            return;
        }

        // 请求完成
        if (_activeRequest.result == UnityWebRequest.Result.Success)
        {
            string responseText = _activeRequest.downloadHandler.text;
            ParseReviewResponse(responseText);
        }
        else
        {
            string errBody = _activeRequest.downloadHandler?.text ?? "";
            string errMsg = ParseGlmError(errBody, _activeRequest.error);
            Debug.LogWarning($"[SelfTraining] API 失败: {errMsg}");
            _currentResult.lastReviewReport = $"❌ API 错误: {errMsg}";
            _currentResult.iterationLogs.Add($"❌ 第 {_currentIteration + 1} 轮 API 失败: {errMsg}");
            _state = TrainState.WaitThenNext;
            Repaint();
        }
    }

    private void ParseReviewResponse(string json)
    {
        try
        {
            var resp = JsonUtility.FromJson<GlmVisionResponse>(json);
            if (resp?.choices != null && resp.choices.Length > 0 && resp.choices[0].message != null)
            {
                string content = resp.choices[0].message.content;
                _currentResult.lastReviewReport = content;

                _currentIteration++;
                string logEntry = $"\n═══ 第 {_currentIteration} 轮自省 ═══\n{content}\n";
                _currentResult.iterationLogs.Add(logEntry);

                // ★ 判断是否达标：报告是否包含"完美达标"、"无明显差异"、"✅" 等关键词
                bool passed = content.Contains("完美达标")
                    || content.Contains("无明显差异")
                    || content.Contains("✅ 动作完美达标");

                if (passed)
                {
                    _currentResult.finalPass = true;
                    _currentResult.iterationsUsed = _currentIteration;
                    SetStatus($"✅ {_currentAction.displayName} 训练达标！({_currentIteration} 轮)");
                    _state = TrainState.WaitThenNext;
                    _stateEnterTime = EditorApplication.timeSinceStartup;
                }
                else if (_currentIteration >= _maxIterations)
                {
                    // 达到最大迭代次数，记录现状
                    _currentResult.finalPass = false;
                    _currentResult.iterationsUsed = _currentIteration;
                    SetStatus($"⏹ {_currentAction.displayName} 已达最大迭代({_maxIterations}轮)，记录中...");
                    _state = TrainState.WaitThenNext;
                    _stateEnterTime = EditorApplication.timeSinceStartup;
                }
                else
                {
                    // 未达标 + 还有迭代次数 → 暂不自动修正（由训练报告给出建议方向）
                    // 告知用户当前差异，让 AI 在后续聊天中自行调用 control_body 微调
                    SetStatus($"🔄 {_currentAction.displayName} 第 {_currentIteration} 轮未达标，差异已记录");
                    _state = TrainState.WaitThenNext;
                    _stateEnterTime = EditorApplication.timeSinceStartup;
                }

                Repaint();
            }
            else
            {
                _currentResult.lastReviewReport = "❌ API 返回空响应";
                _currentResult.iterationLogs.Add($"❌ 第 {_currentIteration + 1} 轮: API 返回空");
                _state = TrainState.WaitThenNext;
                Repaint();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SelfTraining] 解析失败: {ex.Message}");
            _currentResult.lastReviewReport = $"❌ 解析失败: {ex.Message}";
            _state = TrainState.WaitThenNext;
            Repaint();
        }
    }

    /// <summary>构建 GLM-4V 双图对比 Prompt</summary>
    private string BuildComparisonPrompt(string actionName)
    {
        // 收集当前激活的参数快照（供 GLM 参考）
        string paramSnapshot = "";
        var mapper = _renderer?.Mapper;
        if (mapper != null)
        {
            var activeLines = new List<string>();
            foreach (var kv in mapper.SemanticToId)
            {
                string semantic = kv.Key;
                if (!mapper.TryGetRange(semantic, out var range)) continue;
                float current = mapper.Get(semantic);
                float normalized = Mathf.Abs(current - range.Default) / Mathf.Max(range.Max - range.Min, 0.01f);
                if (normalized > 0.05f)
                {
                    activeLines.Add($"• {semantic} = {current:F2}  范围[{range.Min:F1}, {range.Max:F1}]");
                }
            }
            paramSnapshot = activeLines.Count > 0
                ? string.Join("\n", activeLines)
                : "（所有参数均在默认值附近）";
        }

        return
            "你是一名严格的动作质量评审员，正在评估桌面宠物（符玄）的动作执行质量。\n\n"
            + "下面给你两张图：\n"
            + "【参考图】— 此动作「" + actionName + "」的标准执行效果\n"
            + "【实际图】— 当前 AI 执行的效果\n\n"
            + "请逐项对比分析，从以下维度指出**所有可观测到的差异**：\n\n"
            + "1. **头部**：是否同角度？左右转/上下俯仰/歪头程度有无差异？\n"
            + "2. **视线与眼睛**：眼珠方向、眼睛睁开度、笑纹有无不同？\n"
            + "3. **嘴巴**：张嘴程度、嘴角形态（微笑/撇嘴/中性）是否一致？\n"
            + "4. **眉毛**：高低、角度有无差异？\n"
            + "5. **手/手臂**：位置、高度、旋转角度是否匹配？\n"
            + "6. **身体**：倾斜角度、朝向是否一致？\n"
            + "7. **整体姿态**：有没有任何姿势/氛围上的细微差异？\n\n"
            + "当前激活参数（供参考）：\n" + paramSnapshot + "\n\n"
            + "=== 回复格式要求 ===\n"
            + "先用一段话总结：动作是「基本一致」「有轻微偏差」「偏差较大」「完全不匹配」。\n"
            + "然后对每个有差异的部位，按以下格式给出修正建议：\n\n"
            + "###修正建议###\n"
            + "• [部位名]：当前估计值 → 建议值（如「右臂高度：偏低 → 抬升 10°」）\n"
            + "• [参数名]：调整方向（如「arm_right_upper：当前 0.3 → 建议 0.6」）\n\n"
            + "如果无明显差异，只需输出「✅ 动作完美达标，无需修正」。\n"
            + "如果偏差很大，先指出最明显的 3 个差异点，再列出全部修正建议。";
    }

    // ================================================================
    //  Navigation
    // ================================================================

    private int GetEnabledActionCount()
    {
        int count = 0;
        foreach (var info in _actionInfos)
        {
            if (!info.enabled) continue;
            if ((info.id == 4 || info.id == 7) && !_includeSpecialActions) continue;
            count++;
        }
        return count;
    }

    private void AdvanceToNextAction()
    {
        CleanupRequest();
        _screenshotBytes = null;
        _currentResult = null;

        while (true)
        {
            _currentActionIndex++;
            if (_currentActionIndex >= _actionInfos.Count)
            {
                _state = TrainState.Finished;
                GenerateTrainingReport();
                Repaint();
                return;
            }

            _currentAction = _actionInfos[_currentActionIndex];

            // 跳过未勾选 / 特殊动作
            if (!_currentAction.enabled) continue;
            if ((_currentAction.id == 4 || _currentAction.id == 7) && !_includeSpecialActions) continue;

            break;
        }

        _currentResult = new TrainResult
        {
            actionId = _currentAction.id,
            actionName = _currentAction.name,
            displayName = _currentAction.displayName,
            description = _currentAction.description,
            referenceSaved = false,
            finalPass = false,
            iterationsUsed = 0
        };

        SetStatus($"🔄 [{GetEnabledActionCount() - _results.Count}/{GetEnabledActionCount()}] 训练: {_currentAction.displayName}");
        _state = TrainState.TriggerAction;
        _stateEnterTime = EditorApplication.timeSinceStartup;
        Repaint();
    }

    private void WaitThenAdvance()
    {
        // 短暂停顿让模型稳定
        if (EditorApplication.timeSinceStartup - _stateEnterTime < 0.5f)
        {
            Repaint();
            return;
        }

        if (_currentResult != null)
            _results.Add(_currentResult);

        AdvanceToNextAction();
    }

    // ================================================================
    //  Report
    // ================================================================

    private void GenerateTrainingReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════");
        sb.AppendLine("  自律训练报告 (Self-Training Report)");
        sb.AppendLine($"  生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("═══════════════════════════════════════════");
        sb.AppendLine();

        int passCount = 0, partialCount = 0, failCount = 0;
        foreach (var r in _results)
        {
            string icon;
            if (r.finalPass) { icon = "✅"; passCount++; }
            else if (r.referenceSaved) { icon = "⚠️"; partialCount++; }
            else { icon = "❌"; failCount++; }

            sb.AppendLine($"{icon} {r.displayName} ({r.actionName})");
            sb.AppendLine($"   参考图: {(r.referenceSaved ? "✅ 已保存" : "❌ 未保存")}");
            sb.AppendLine($"   自省: {(r.finalPass ? $"✅ 达标 ({r.iterationsUsed}轮)" : r.referenceSaved ? $"⚠️ 已记录差异，需手动微调" : "❌ 失败")}");

            if (r.iterationLogs.Count > 0)
            {
                sb.AppendLine($"   迭代记录:");
                foreach (var log in r.iterationLogs)
                {
                    // 只取前 200 字
                    string trimmed = log.Length > 200 ? log.Substring(0, 200) + "..." : log;
                    // 替换换行为缩进
                    trimmed = trimmed.Replace("\n", "\n     ");
                    sb.AppendLine($"     {trimmed}");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("───────────────────────────────────────────");
        sb.AppendLine($"  总计: {_results.Count}  |  ✅ 达标: {passCount}  |  ⚠️ 需微调: {partialCount}  |  ❌ 失败: {failCount}");
        sb.AppendLine("───────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine("AI 驱动微调提示：");
        sb.AppendLine("  在聊天中输入以下指令让 AI 继续完善：");
        foreach (var r in _results)
        {
            if (!r.finalPass && r.referenceSaved)
            {
                sb.AppendLine($"  - 「练习{r.displayName}，直到做标准」");
            }
        }

        string report = sb.ToString();
        Debug.Log($"[SelfTraining]\n{report}");

        // 保存到文件
        try
        {
            string dir = Application.dataPath + "/../training_reports";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string path = dir + $"/training_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            File.WriteAllText(path, report, Encoding.UTF8);
            Debug.Log($"[SelfTraining] 报告已保存: {path}");
        }
        catch { }

        // 复制到剪贴板
        GUIUtility.systemCopyBuffer = report;
        SetStatus($"📋 训练完成！报告已生成（通过 {passCount}/{_results.Count}）");
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private float GetWaitTime(int actionId)
    {
        if (ACTION_WAIT_TIMES.TryGetValue(actionId, out float t))
            return t;
        return 1.2f;
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
        if (text != _lastLoggedStatus)
        {
            _lastLoggedStatus = text;
            Debug.Log($"[SelfTraining] {text}");
        }
        Repaint();
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

    // ================================================================
    //  UI
    // ================================================================

    private void OnGUI()
    {
        EditorGUILayout.Space(10);

        // ─── 标题 ────────────────────────────────────────────────
        var titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
        GUILayout.Label("🏋️ 自律训练 (self-review)", titleStyle);
        EditorGUILayout.LabelField(
            "选择动作 → 系统自动：生成 → 截图 → 保存参考 → GLM-4V 对比 → 报告差异",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(5);

        // ─── 状态栏 ──────────────────────────────────────────────
        if (!string.IsNullOrEmpty(_statusText))
        {
            var msgType = GetMessageType(_statusText);
            EditorGUILayout.HelpBox(_statusText, msgType);
        }

        // ─── 控制区 ──────────────────────────────────────────────
        EditorGUILayout.BeginHorizontal();

        bool isRunning = (_state != TrainState.Idle && _state != TrainState.Finished);

        GUI.enabled = !isRunning;
        if (GUILayout.Button("▶ 开始训练", GUILayout.Height(30)))
        {
            StartTraining();
        }
        GUI.enabled = isRunning;
        if (GUILayout.Button("⏹ 停止", GUILayout.Height(30)))
        {
            StopTraining();
        }
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();

        // ─── 配置选项 ──────────────────────────────────────────
        EditorGUILayout.Space(5);
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField("训练选项", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _runSelfReview = EditorGUILayout.ToggleLeft("🔍 训练后自动 self-review 对比验证（需 GLM-4V）", _runSelfReview);
        _maxIterations = EditorGUILayout.IntSlider("最大自律迭代次数", _maxIterations, 1, 5);
        _includeSpecialActions = EditorGUILayout.ToggleLeft("包含硬编码特殊动作（星辉 #4, 法阵 #7）", _includeSpecialActions);
        if (EditorGUI.EndChangeCheck())
        {
            EditorUtility.SetDirty(this);
        }
        EditorGUILayout.EndVertical();

        // ─── 动作列表（可勾选） ────────────────────────────────
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("训练动作列表", EditorStyles.boldLabel);

        if (_actionInfos.Count == 0 && !isRunning)
        {
            // 还没加载，显示占位
            EditorGUILayout.HelpBox("进入 Play Mode 后点击「开始训练」加载动作列表", MessageType.Info);
        }
        else
        {
            EditorGUILayout.BeginVertical("box");
            for (int i = 0; i < _actionInfos.Count; i++)
            {
                var a = _actionInfos[i];
                bool isSpecial = (a.id == 4 || a.id == 7);
                if (isSpecial && !_includeSpecialActions)
                {
                    GUI.enabled = false;
                    a.enabled = false;
                }
                else
                {
                    GUI.enabled = !isRunning;
                }

                bool wasEnabled = a.enabled;
                a.enabled = EditorGUILayout.ToggleLeft(
                    $"  {a.displayName} ({a.name}) — {a.description}",
                    a.enabled);
                if (isSpecial)
                    EditorGUILayout.LabelField("    ⭐ 硬编码特殊动作", EditorStyles.miniLabel);

                GUI.enabled = true;
            }
            EditorGUILayout.EndVertical();
        }

        // ─── 进度 ──────────────────────────────────────────────
        if (isRunning)
        {
            int total = GetEnabledActionCount();
            float progress = total > 0 ? (float)_results.Count / total : 0f;
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), progress,
                $"{_results.Count} / {total} 完成");
        }

        // ─── 结果列表 ──────────────────────────────────────────
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("训练结果", EditorStyles.boldLabel);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        if (_results.Count == 0 && !isRunning)
        {
            EditorGUILayout.HelpBox("尚未训练。勾选动作后点击「开始训练」（需处于 Play Mode）", MessageType.Info);
        }
        else
        {
            foreach (var r in _results)
            {
                DrawTrainResult(r);
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
                string icon = r.finalPass ? "✅" : r.referenceSaved ? "⚠️" : "❌";
                string detail = r.referenceSaved
                    ? (r.finalPass ? $"达标 {r.iterationsUsed}轮" : "需微调")
                    : "失败";
                sb.AppendLine($"{icon} {r.displayName}: {detail}");
            }
            GUIUtility.systemCopyBuffer = sb.ToString();
            SetStatus("📋 已复制简略报告到剪贴板");
        }
        if (GUILayout.Button("🗑 清空"))
        {
            _results.Clear();
            Repaint();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(5);
    }

    private void DrawTrainResult(TrainResult r)
    {
        string icon;
        Color bgColor;

        if (r.finalPass)
        {
            icon = "✅";
            bgColor = new Color(0.8f, 1f, 0.8f, 0.3f);
        }
        else if (r.referenceSaved)
        {
            icon = "⚠️";
            bgColor = new Color(1f, 1f, 0.8f, 0.3f);
        }
        else
        {
            icon = "❌";
            bgColor = new Color(1f, 0.8f, 0.8f, 0.3f);
        }

        var bg = new GUIStyle(EditorStyles.helpBox);
        EditorGUILayout.BeginVertical(bg);
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.LabelField($"{icon} {r.displayName} ({r.actionName})",
            EditorStyles.boldLabel, GUILayout.Width(250));

        if (r.referenceSaved)
            EditorGUILayout.LabelField($"参考图: ✅  |  迭代: {r.iterationsUsed}轮");
        else
            EditorGUILayout.LabelField("❌ 未保存参考图");

        EditorGUILayout.EndHorizontal();

        if (r.iterationLogs.Count > 0)
        {
            string lastLog = r.iterationLogs[r.iterationLogs.Count - 1];
            // 只显示最后一行总结
            if (lastLog.Length > 120)
                lastLog = lastLog.Substring(0, 120) + "...";
            lastLog = lastLog.Replace("\n", " | ");
            EditorGUILayout.LabelField($"📋 {lastLog}", EditorStyles.wordWrappedMiniLabel);
        }

        if (!r.finalPass && r.referenceSaved)
        {
            EditorGUILayout.LabelField("💡 提示：在聊天中说「练习" + r.displayName + "，直到做标准」",
                EditorStyles.wordWrappedMiniLabel);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
    }

    // ================================================================
    //  Actions
    // ================================================================

    private void StartTraining()
    {
        if (!EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("提示", "请先进入 Play Mode，然后点击「开始训练」。", "确定");
            return;
        }

        Debug.Log("[SelfTraining] ===== 开始自律训练 =====");
        _results.Clear();
        _currentActionIndex = -1;
        _state = TrainState.FindingRenderer;
        _stateEnterTime = EditorApplication.timeSinceStartup;
        SetStatus("🔍 查找 Live2DRenderer...");
    }

    private void StopTraining()
    {
        Debug.Log("[SelfTraining] 训练被用户停止");
        CleanupRequest();
        _state = TrainState.Idle;
        SetStatus("⏹ 训练已停止");
    }

    private static MessageType GetMessageType(string text)
    {
        if (text.Contains("❌")) return MessageType.Error;
        if (text.Contains("⚠") || text.Contains("⏳") || text.Contains("🔄")) return MessageType.Warning;
        if (text.Contains("✅")) return MessageType.Info;
        return MessageType.None;
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
}
