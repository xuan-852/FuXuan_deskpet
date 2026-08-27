using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// ChatManager 工具回环的共享协作逻辑。
/// 当前先抽取危险工具确认，统一本地规划与云端 tool_call 的审批等待行为。
/// </summary>
public partial class ChatManager
{
    private IEnumerator DoToolLoop()
    {
        // ★ 成本熔断：每次用户消息的工具循环开始时重置「openclaw_task 致命失败」标记
        _openclawTaskFatalSeen = false;

        for (int round = 0; round <= MAX_TOOL_ROUNDS; round++)
        {
            _toolRound = round; // ★ 记录轮次，第一轮按意图过滤，后续全量

            // ★ T4 修复：首轮等待本地意图分类结果（最多 INTENT_WAIT_TIMEOUT 秒）
            //   解决原实现“首帧构建请求体早于异步分类回调”的竞态，避免用残留/空意图
            if (round == 0 && !_intentReady)
            {
                float waitStart = Time.time;
                while (!_intentReady && Time.time - waitStart < INTENT_WAIT_TIMEOUT)
                    yield return null;
                if (!_intentReady)
                {
                    // 超时兜底：按无意图处理（全量探测），并标记就绪避免重复等待
                    _lastIntent = "";
                    _intentReady = true;
                    Debug.LogWarning($"[ChatManager] ⏱️ 意图分类超时({INTENT_WAIT_TIMEOUT}s)，首轮回退全量工具");
                }
            }

            string jsonBody = BuildRequestBody();
            bool hadError = false;
            string fullContent = "";
            string toolCallsJson = null;

            // ——— 重置流式积累器 ———
            _streamBuf.Clear();
            _streamLastSplit = 0;
            _streamCompleted = false;
            _sentenceList.Clear();
            _sentenceIdx = -1;
            _isSentenceAnimating = false;
            _fullReplyText = "";

            bool finished = false;
            SetRequestStatus(round == 0 ? "连接云端…" : "整理下一轮…", RequestStage.Connecting);
            float cloudStartedAt = Time.realtimeSinceStartup;

            // ——— 流式发送 ———
            yield return StartCoroutine(
                ApiClient.StreamRequest(apiUrl, apiKey, jsonBody, 90,
                    delta =>
                    {
                        SetRequestStatus("生成回复…", RequestStage.Generating);
                        ProcessStreamContent(delta);
                    },
                    (content, calls) =>
                    {
                        fullContent = content ?? "";
                        toolCallsJson = calls;
                        _streamCompleted = true;
                        finished = true;
                    },
                    err =>
                    {
                        _lastError = err;
                        hadError = true;
                        _streamCompleted = true;
                        finished = true;
                    }));

            if (!finished) _streamCompleted = true; // 超时保护

            // ——— 刷新剩余缓冲区 ———
            FlushStreamBuffer();

            if (hadError)
            {
                QualityTelemetry.RecordChat(
                    "cloud", model, false, false,
                    Mathf.RoundToInt((Time.realtimeSinceStartup - cloudStartedAt) * 1000f),
                    "request_error", 0, false, _activeRequestCaseId);
                Debug.LogError($"[ChatManager] ❌ API 请求失败 (round={round}): {_lastError}");

                // ★ 自动重试：网络/限流错误（非 4xx 业务错误）重试最多 3 次
                if (ShouldRetry(_lastError, out int attempt))
                {
                    string retryDelayStr = attempt <= 3 ? "2" : "5";
                    Debug.Log($"[ChatManager] 🔄 {attempt}/3 自动重试 ({retryDelayStr}s 后)...");
                    SetRequestStatus($"网络不稳，正在重试 {attempt}/3…", RequestStage.Retrying);
                    yield return new WaitForSeconds(attempt <= 3 ? 2f : 5f);
                    continue; // 重新执行当前 round
                }

                // 🔄 功能2：离线回退 — DeepSeek 不可用时尝试本地模型
                if (!ChatConfig.UseCloudBaseline
                    && LocalLLMAgentService.Instance != null && LocalLLMAgentService.Instance.CanProcess)
                {
                    bool fallbackHandled = false;
                    yield return StartCoroutine(OfflineFallbackCoroutine((handled) => fallbackHandled = handled, "cloud_error_fallback"));
                    if (fallbackHandled)
                    {
                        yield break; // 回退已处理完毕，退出
                    }
                }

                SetRequestStatus("请求失败", RequestStage.Error);
                OnRequestError?.Invoke($"❌ 法阵术式失败: {_lastError}");
                yield break;
            }

            // ——— 提取 tool_calls ———
            bool hasToolCalls = !string.IsNullOrEmpty(toolCallsJson) && toolCallsJson != "[]";
            QualityTelemetry.RecordChat(
                "cloud", model, true, true,
                Mathf.RoundToInt((Time.realtimeSinceStartup - cloudStartedAt) * 1000f),
                hasToolCalls ? "tool_call" : "final_reply",
                (fullContent ?? "").Length, hasToolCalls, _activeRequestCaseId);

            // ——— 如果没有 tool_call，结束 ———
            if (!hasToolCalls)
            {
                if (!string.IsNullOrEmpty(fullContent))
                {
                    _history.Add(new Entry { role = "assistant", content = fullContent });
                }
                PublishFinalReply(_fullReplyText);
                // ℹ️ 不调 StartSentenceQueue：流式路径 AddStreamSentence 已经显示了内容
                RecordConversationMemory(fullContent);
                yield break;
            }

            // ——— 有 tool_call：将 assistant 消息记入历史 ———
            _history.Add(new Entry
            {
                role = "assistant",
                content = fullContent ?? "",
                toolCallsJson = toolCallsJson
            });

            // ——— 解析并执行工具 ———
            var calls = ParseToolCalls(toolCallsJson);
            // ★ 清洗后再赋值，避免 markdown/表情标记泄漏到 UI
            _lastReply = CleanDisplayText(fullContent ?? "[施法中……]");

            foreach (var call in calls)
            {
                SetRequestStatus($"执行：{call.name}", RequestStage.RunningTool);
                OnToolCalled?.Invoke(call.name);
                Debug.Log($"[ChatManager] ⚡ 施法: {call.name}({call.arguments})");

                // ★ 成本熔断：openclaw_task 已因「不可重试错误」失败过一次，
                //   本回合不再重复调用（防 LLM 换说法反复烧 token）
                if (_openclawTaskFatalSeen && call.name == "openclaw_task")
                {
                    string blockReason = "❌ [不可重试] 太卜神行法上次已因网络/连接问题失败（重试无益）。本座不再重复施法；请先检查网络/代理/桥接服务状态，或换个思路。";
                    Debug.Log($"[ChatManager] 🚫 熔断: 跳过重复 openclaw_task（致命失败已见）");
                    OnToolResult?.Invoke(call.name, blockReason);
                    RecordMemoryForTool(call.name, call.arguments, blockReason);
                    _history.Add(new Entry
                    {
                        role = "tool",
                        content = blockReason,
                        tool_call_id = call.id,
                        name = call.name
                    });
                    continue;
                }

                string result;

                // ⚠️ 危险工具 → 必须先经用户确认（防 AI 幻觉 / prompt 注入误删文件、关机等）
                if (toolInvoker && ToolRegistry.IsDangerous(call.name))
                {
                    bool confirmed = false;
                    string desc = ToolRegistry.GetDangerDescription(call.name);
                    yield return StartCoroutine(WaitForDangerousToolConfirmation(
                        call.name, call.arguments,
                        $"⚠️ 本座欲施「{call.name}」——{desc}。\n点一下本座 = 允许，按 ESC = 拒绝。",
                        ok => confirmed = ok));

                    if (!confirmed)
                    {
                        result = "❌ 用户拒绝了此操作";
                        Debug.Log($"[ChatManager] 🚫 用户拒绝执行: {call.name}");
                        OnToolResult?.Invoke(call.name, result);
                        RecordMemoryForTool(call.name, call.arguments, result);
                        _history.Add(new Entry
                        {
                            role = "tool",
                            content = result,
                            tool_call_id = call.id,
                            name = call.name
                        });
                        continue;
                    }

                    Debug.Log($"[ChatManager] ✅ 用户已确认: {call.name}");
                }

                if (toolInvoker && toolInvoker.IsCoroutineTool(call.name))
                {
                    // ★ 看门狗全程有效（不归零 _requestStartTime），卡死时自动超时
                    yield return StartCoroutine(toolInvoker.ExecuteCoroutine(call.name, call.arguments));
                    // 如果超时标志被设置，立即终止整个循环
                    if (_abortRequested) yield break;
                    result = toolInvoker.GetCoroutineResult();
                }
                else
                {
                    result = toolInvoker
                        ? toolInvoker.Execute(call.name, call.arguments, out _)
                        : "法阵未就绪";
                }

                Debug.Log($"[ChatManager] 📜 结果: {result}");
                OnToolResult?.Invoke(call.name, result);
                RecordMemoryForTool(call.name, call.arguments, result);

                // ★ 成本熔断：openclaw_task 返回不可重试错误 → 标记，后续轮次禁止重复调用
                if (call.name == "openclaw_task" &&
                    (result != null && result.StartsWith(OpenClawBridge.FATAL_PREFIX)))
                {
                    _openclawTaskFatalSeen = true;
                    Debug.Log($"[ChatManager] 🚫 openclaw_task 致命失败已记录，后续轮次熔断重试");
                }

                _history.Add(new Entry
                {
                    role = "tool",
                    content = ToolResultBudget.Compact(call.name, result),
                    tool_call_id = call.id,
                    name = call.name
                });
            }
            // 继续下一轮
        }

        // 超过最大轮次
        _lastReply = "♾️ 术式循环过久，本座暂且收阵。";
        _history.Add(new Entry { role = "assistant", content = _lastReply });
        PublishFinalReply(_lastReply, _lastReply);
    }

    /// <summary>
    /// 显示危险操作确认并等待用户决定；超时自动拒绝，避免工具回环永久挂起。
    /// </summary>
    private IEnumerator WaitForDangerousToolConfirmation(
        string toolName, string argsJson, string prompt, Action<bool> onResolved)
    {
        bool confirmed = false;
        bool resolved = false;
        string description = ToolRegistry.GetDangerDescription(toolName);
        var confirmBubble = FindObjectOfType<ChatBubble>();

        if (confirmBubble != null)
            confirmBubble.ShowMessage(prompt, 60f, ChatBubble.MsgPriority.High);

        ToolConfirmManager.Request(toolName, argsJson, description,
            ok => { confirmed = ok; resolved = true; });

        float confirmTimeout = Time.time + 60f;
        while (!resolved)
        {
            if (Time.time > confirmTimeout)
            {
                ToolConfirmManager.Resolve(false);
                break;
            }
            yield return null;
        }

        if (confirmed && confirmBubble != null)
            confirmBubble.ShowMessage("✅ 已获准许，施法！", 2.5f, ChatBubble.MsgPriority.Normal);

        onResolved?.Invoke(confirmed);
    }
}
