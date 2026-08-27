using System;

/// <summary>
/// ChatManager 的请求生命周期入口。
/// 请求循环和上下文构建保留在主文件，发送、取消、状态通知与排队入口独立维护。
/// </summary>
public partial class ChatManager
{
    // ==================================================================
    //  主动发送 / 触发 AI 对话（不含用户输入框）
    // ==================================================================

    /// <summary>直接发送一条消息（外部调用，如 AutoChat）</summary>
    public void SendMessage(string text, Action onUpdate)
    {
        SendMessageInternal(text, onUpdate, QualityTelemetry.CurrentCaseId);
    }

    public bool CancelCurrentRequest()
    {
        if (!_isWaiting) return false;
        _abortRequested = true;
        _requestGeneration++;
        _isWaiting = false;
        _requestStartTime = 0f;
        _messageQueue.Clear();
        if (_activeRequestCoroutine != null)
        {
            StopCoroutine(_activeRequestCoroutine);
            _activeRequestCoroutine = null;
        }
        _lastError = "本次回复已停止";
        SetRequestStatus("已停止", RequestStage.Cancelled);
        OnRequestError?.Invoke("⏹ 已停止本次回复");
        _onUpdate?.Invoke();
        return true;
    }

    private void SetRequestStatus(string text, RequestStage stage)
    {
        if (string.IsNullOrEmpty(text)) text = "就绪";
        bool changed = _requestStage != stage || !string.Equals(_requestStatusText, text, StringComparison.Ordinal);
        _requestStage = stage;
        _requestStatusText = text;
        if (changed) OnRequestStatusChanged?.Invoke(text);
    }

    private void SendMessageInternal(string text, Action onUpdate, string caseId)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // 开发者指令必须在排队、写历史和启动 LLM 之前处理。
        // 这样在模型忙碌时切换模式也不会被排到普通对话队列，更不会消耗 token。
        string developerReply;
        if (DeveloperCommandSet.TryHandle(text, out developerReply))
        {
            OnDeveloperCommandReply?.Invoke(developerReply);
            onUpdate?.Invoke();
            UnityEngine.Debug.Log("[DeveloperCommand] 本地指令已处理: " + developerReply);
            return;
        }

        if (_isWaiting)
        {
            // 排队，等当前回复完自动发（上限 MAX_QUEUED_MESSAGES，超出丢弃最旧）
            if (_messageQueue.Count >= MAX_QUEUED_MESSAGES)
            {
                _messageQueue.Dequeue();
                UnityEngine.Debug.LogWarning($"[ChatManager] 消息队列已满（>{MAX_QUEUED_MESSAGES}），丢弃最旧消息");
            }
            _messageQueue.Enqueue((text.Trim(), onUpdate, caseId ?? ""));
            return;
        }

        _activeRequestCaseId = caseId ?? "";

        _history.Add(new Entry { role = "user", content = text.Trim() });
        TrimHistory(); // 裁剪旧历史，防止 token 无限增长
        _isWaiting = true;
        _lastReply = "";
        _lastError = "";
        _replyPublished = false;
        _abortRequested = false; // 重置中止标志，允许新的请求
        _apiRetryCount = 0; // 重置自动重试计数
        _toolRound = 0; // 重置工具轮次
        _requestStartTime = UnityEngine.Time.time; // 启动看门狗计时
        _onUpdate = onUpdate;
        SetRequestStatus("思考中…", RequestStage.Thinking);

        // ★ T4 修复：重置意图状态，首轮请求必须等待本次分类结果（杜绝残留）
        _lastIntent = "";
        _intentReady = false;

        // 触发"AI 开始处理"事件（悬浮球显示"思考中…"）
        OnRequestStarted?.Invoke();

        // ★ 代际递增：新请求接管；若旧协程因看门狗中止仍残留，恢复时会检测代际不符自动退场
        _requestGeneration++;
        _activeRequestCoroutine = StartCoroutine(SendRequestCoroutine(_requestGeneration, _activeRequestCaseId));

        // 🧠 功能1：意图/情绪分类（异步，SendRequestCoroutine 首轮会等待其结果）
        if (!ChatConfig.UseOllamaMode && !ChatConfig.UseCloudBaseline
            && LocalLLMAgentService.Instance != null && LocalLLMAgentService.Instance.CanProcess)
        {
            LocalLLMAgentService.Instance.ClassifyIntent(text.Trim(), intent =>
            {
                if (intent.success)
                {
                    _lastIntent = intent.intent;  // ★ 存下来供 BuildRequestBody 过滤 tools
                    UnityEngine.Debug.Log($"[ChatManager] 🏷️ 本地灵识判断: intent={intent.intent}, emotion={intent.emotion}");
                }
                _intentReady = true; // ★ 无论成败都标记就绪（失败 → 首轮走全量探测）
            });
        }
        else
        {
            // 本地模型不可用 → 立即就绪，首轮走全量探测
            _intentReady = true;
        }
    }
}
