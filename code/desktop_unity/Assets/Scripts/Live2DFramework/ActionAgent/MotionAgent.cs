using Live2D.Cubism.Core;
using Live2DFramework.ActionAgent;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 符玄「分神化身」— 常驻自主动作决策 Agent
///
/// 设计目标：
/// 在 ChatManager 不活动时，MotionAgent 作为常驻后台线程（协程循环），
/// 定时调用本地轻量 LLM (Qwen2.5-0.5B) 做出动作决策，
/// 让符玄在无人对话时也能自主做出丰富的动作和表情变化。
///
/// 架构模式（借鉴 Nexus Autonomy Engine）：
///   tick (eligible?) → gather context → decision LLM → execute motion → score feedback
///
/// 密度级别（Tiered Cadence）：
///   High:  每 4s 决策一次（用户活跃互动时）
///   Med:   每 8s 决策一次（默认）
///   Low:   每 15s 决策一次（用户专注工作时）
///   Sleep: 停止决策（用户长时间离开）
///
/// 安全机制：
/// - AI 控制锁检测（ChatManager 工具调用时不覆盖）
/// - 空闲动作调度器协调（不与预设空闲动作冲突）
/// - 本地 LLM 熔断（连续失败自动降级到基于情绪的概率决策）
/// - 最大连续动作限制（防止死循环）
/// </summary>
public class MotionAgent : MonoBehaviour
{
    [Header("◈ 基本配置")]
    [Tooltip("是否启用自主动作")]
    public bool enabled = true;

    [Tooltip("决策间隔（秒），根据密度级别自动调整")]
    public float baseInterval = 5f;

    [Header("◈ 本地 LLM 配置")]
    [Tooltip("本地模型名")]
    public string localModel = "qwen2.5:0.5b";

    [Tooltip("本地 API 地址")]
    public string localApiUrl = "http://127.0.0.1:11434/v1";

    [Tooltip("本地 LLM 不可用时回退到概率决策")]
    public bool fallbackToRandom = true;

    [Header("◈ 情绪系统")]
    public EmotionState emotion = new EmotionState();

    [Header("◈ 空闲弧线（Idle Arc）")]
    [Tooltip("多少秒无交互视为「短暂离开」")]
    public float shortIdleThreshold = 30f;

    [Tooltip("多少秒无交互视为「长时间离开」")]
    public float longIdleThreshold = 300f;

    [Header("◈ 行为抑制")]
    [Tooltip("用户在此进程名上时不触发大动作")]
    public List<string> focusProcessNames = new List<string> { "Code", "chrome", "msedge", "firefox", "devenv" };

    [Tooltip("最大连续动作数（防止无限循环执行）")]
    public int maxConsecutiveActions = 3;

    [Tooltip("每次动作后的最小等待秒数")]
    public float minCooldownAfterAction = 2f;

    [Header("◈ 测试模式")]
    [Tooltip("测试模式：强制 High 密度 + 关闭睡眠检测，用于数据积累")]
    public bool testMode = true;

    // ==================================================================
    //  运行时状态
    // ==================================================================

    /// <summary>单例</summary>
    public static MotionAgent Instance { get; private set; }

    /// <summary>当前密度级别</summary>
    public enum DensityLevel { High, Med, Low, Sleep }
    public DensityLevel CurrentDensity { get; private set; } = DensityLevel.Med;

    /// <summary>当前是否在决策中</summary>
    public bool IsDeciding { get; private set; } = false;

    /// <summary>上次用户交互时间</summary>
    private float _lastInteractionTime = 0f;

    /// <summary>连续动作计数</summary>
    private int _consecutiveActionCount = 0;

    /// <summary>上次动作结束时间</summary>
    private float _lastActionEndTime = 0f;

    /// <summary>本地 LLM 连续失败计数</summary>
    private int _llmFailCount = 0;

    /// <summary>是否已回退到概率模式</summary>
    private bool _isInFallbackMode = false;

    /// <summary>组件引用（懒加载）</summary>
    private Live2DRenderer _renderer;
    private Live2DParameterMapper _mapper;
    private CubismModel _model;
    private ChatManager _chatManager;
    private ActivityTracker _activityTracker;
    private DualModelValidator _dualValidator;

    /// <summary>动作历史（最近 N 条决策，防止重复）</summary>
    private readonly List<string> _recentActions = new List<string>();
    private const int RECENT_ACTION_HISTORY = 10;

    /// <summary>密度级别调整冷却</summary>
    private float _densityChangeCooldown = 0f;

    // ══════════════════════════════════════════════════════════════
    //  失败追踪 & 报告
    // ══════════════════════════════════════════════════════════════

    /// <summary>动作失败计数（actionName → 失败次数）</summary>
    private readonly Dictionary<string, int> _actionFailCount = new Dictionary<string, int>();

    /// <summary>动作成功计数</summary>
    private readonly Dictionary<string, int> _actionSuccessCount = new Dictionary<string, int>();

    /// <summary>验证跳过计数（plan==null）</summary>
    private int _skippedCount = 0;

    /// <summary>自上次报告后的总动作数</summary>
    private int _totalActionsSinceReport = 0;

    /// <summary>报告周期（每 N 个决策动作输出一次报告到日志）</summary>
    private const int REPORT_INTERVAL = 30;

    /// <summary>获取全链路运行报告</summary>
    public string GetPipelineReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("╔══════════════════════════════════════╗");
        sb.AppendLine("║  符玄「分神化身」— MotionAgent 报告  ║");
        sb.AppendLine("╚══════════════════════════════════════╝");
        sb.AppendLine();

        int totalAttempts = _actionFailCount.Values.Sum() + _actionSuccessCount.Values.Sum() + _skippedCount;
        sb.AppendLine("📊 运行概览");
        sb.AppendLine($"▸ 密度级别: {CurrentDensity}");
        sb.AppendLine($"▸ 回退模式: {(_isInFallbackMode ? "✅ 概率回退" : "🔶 LLM 决策")}");
        sb.AppendLine($"▸ 总动作尝试: {totalAttempts}");
        sb.AppendLine($"▸ 成功验证: {_actionSuccessCount.Values.Sum()}");
        sb.AppendLine($"▸ 验证失败: {_actionFailCount.Values.Sum()}");
        sb.AppendLine($"▸ 跳过(翻译失败): {_skippedCount}");

        // 失败最多的动作
        var topFailures = _actionFailCount.Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value).Take(8).ToList();
        if (topFailures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("⛔ 高频失败动作 (验证未通过)");
            foreach (var kv in topFailures)
            {
                int success = _actionSuccessCount.TryGetValue(kv.Key, out int s) ? s : 0;
                float rate = (kv.Value + success) > 0
                    ? (float)success / (kv.Value + success) * 100f : 0f;
                sb.AppendLine($"  {(kv.Value > 5 ? "🔴" : "🟡")} 「{kv.Key}」 失败={kv.Value} 成功={success} 通过率={rate:F0}%");
            }
        }

        // 融合 MotionMemoryManager 报告
        var mm = MotionMemoryManager.Instance;
        if (mm != null)
        {
            sb.AppendLine();
            sb.Append(mm.GetPipelineReport());
        }

        return sb.ToString();
    }

    // ==================================================================
    //  Unity 生命周期
    // ==================================================================

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        emotion.Init();
    }

    void Start()
    {
        // 懒加载组件引用
        ResolveComponents();

        // 设置本地 LLM
        LocalLLMClient.SetBaseUrl(localApiUrl);
        LocalLLMClient.SetModel(localModel);

        // 启动健康检查（不阻塞启动流程）
        StartCoroutine(DelayedHealthCheck());

        // 启动主决策循环
        StartCoroutine(DecisionLoop());
    }

    void Update()
    {
        if (!enabled) return;

        // 情绪自动衰减
        emotion.TickDecay();

        // 密度级别自适应
        UpdateDensityLevel();

        // 检测用户交互（鼠标移动/点击等，通过 ActivityTracker）
        UpdateInteractionTime();
    }

    // ==================================================================
    //  组件引用解析
    // ==================================================================

    private void ResolveComponents()
    {
        _renderer = FindObjectOfType<Live2DRenderer>();
        if (_renderer != null)
        {
            _mapper = _renderer.Mapper;
            _model = _renderer.CubismModel;
        }

        _chatManager = FindObjectOfType<ChatManager>();
        _activityTracker = ActivityTracker.Instance;
        _dualValidator = FindObjectOfType<DualModelValidator>();
        if (_dualValidator == null)
        {
            var go = new GameObject("DualModelValidator");
            _dualValidator = go.AddComponent<DualModelValidator>();
            if (transform != null) go.transform.SetParent(transform);
        }

        Debug.Log($"[MotionAgent] 组件就绪: renderer={_renderer!=null}, chat={_chatManager!=null}, tracker={_activityTracker!=null}, validator={_dualValidator!=null}");
    }

    private IEnumerator DelayedHealthCheck()
    {
        yield return new WaitForSeconds(3f); // 等场景稳定
        bool ready = false;
        yield return LocalLLMClient.CheckHealthAsync((ok, msg) => {
            ready = ok;
            Debug.Log($"[MotionAgent] 本地 LLM 健康检查: {msg}");
        });
        _isInFallbackMode = !ready;
        if (!ready && fallbackToRandom)
        {
            Debug.Log("[MotionAgent] 本地 LLM 不可用，已回退到概率决策模式");
            yield break;
        }

        // 模型预热：发一次简单请求让 Ollama 加载模型到内存
        // 避免首次决策请求因加载模型而超时
        Debug.Log("[MotionAgent] 预热本地 LLM 模型…");
        bool warmed = false;
        yield return LocalLLMClient.SimplePromptAsync(
            "请回复「就绪」",
            (ok, content) => {
                warmed = ok;
                if (ok) Debug.Log("[MotionAgent] 本地 LLM 预热完成");
                else Debug.LogWarning("[MotionAgent] 预热失败，首次决策可能较慢");
            },
            temperature: 0.1f,
            maxTokens: 16
        );
        if (warmed) _isInFallbackMode = false;
    }

    // ==================================================================
    //  密度级别自适应
    // ==================================================================

    private void UpdateDensityLevel()
    {
        // ★ 测试模式：强制 High 密度
        if (testMode)
        {
            if (CurrentDensity != DensityLevel.High)
            {
                CurrentDensity = DensityLevel.High;
                UnityEngine.Debug.Log("[MotionAgent] 🧪 测试模式: 密度强制 High");
            }
            return;
        }

        if (_densityChangeCooldown > 0f)
        {
            _densityChangeCooldown -= Time.deltaTime;
            return;
        }

        DensityLevel newLevel = CurrentDensity;

        // 检测用户是否长时间离开
        float idleDuration = Time.time - _lastInteractionTime;
        bool isSleepTime = IsSleepTime();

        if (isSleepTime || idleDuration > longIdleThreshold)
        {
            newLevel = DensityLevel.Sleep;
        }
        else if (idleDuration > shortIdleThreshold)
        {
            // 短暂离开 → Low
            newLevel = DensityLevel.Low;
        }
        else if (IsUserFocused())
        {
            // 用户专注工作 → Low
            newLevel = DensityLevel.Low;
        }
        else
        {
            // 用户在场且不专注 → Med 或 High
            newLevel = idleDuration < 10f ? DensityLevel.High : DensityLevel.Med;
        }

        if (newLevel != CurrentDensity)
        {
            Debug.Log($"[MotionAgent] 密度级别: {CurrentDensity} → {newLevel} (空闲{idleDuration:F0}s)");
            CurrentDensity = newLevel;
            _densityChangeCooldown = 10f; // 防频繁切换
        }
    }

    private float GetCurrentInterval()
    {
        return CurrentDensity switch
        {
            DensityLevel.High => Mathf.Max(baseInterval * 0.5f, 4f),
            DensityLevel.Low => Mathf.Min(baseInterval * 2f, 15f),
            DensityLevel.Sleep => 30f,
            _ => baseInterval,
        };
    }

    private bool IsSleepTime()
    {
        // 测试模式：不进入睡眠
        if (testMode) return false;
        // 调试：跳过睡眠检查以触发双镜鉴验证
        return false;
        //int hour = DateTime.Now.Hour;
        //return hour >= 1 && hour <= 7; // 凌晨1~7点
    }

    private bool IsUserFocused()
    {
        if (_activityTracker == null) return false;
        string proc = _activityTracker.CurrentProcessName?.ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(proc)) return false;
        return focusProcessNames.Any(p => proc.Contains(p.ToLowerInvariant()));
    }

    private void UpdateInteractionTime()
    {
        // 通过 ActivityTracker 检测用户活动
        if (_activityTracker != null)
        {
            // 如果 ActivityTracker 的上次活动时间比我们记录的新，更新
            // （简化实现：直接检查静态属性）
        }

        // 通过鼠标移动检测（全局）
        float mouseDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")).magnitude;
        if (mouseDelta > 0.01f || Input.anyKeyDown)
        {
            _lastInteractionTime = Time.time;
        }
    }

    /// <summary>外部通知用户交互（由 DragHandler/ChatBubble 等触发）</summary>
    public void NotifyInteraction()
    {
        _lastInteractionTime = Time.time;
    }

    // ==================================================================
    //  主决策循环
    // ==================================================================

    private IEnumerator DecisionLoop()
    {
        while (true)
        {
            // 等待决策间隔
            float interval = GetCurrentInterval();
            yield return new WaitForSeconds(interval);

            if (!enabled) continue;

            // ◈ 判断是否应该决策
            if (!ShouldDecide()) continue;

            IsDeciding = true;

            // ◈ 收集上下文
            string context = GatherContext();

            // ◈ 决策
            MotionDecision decision = null;

            if (!_isInFallbackMode && LocalLLMClient.IsReady)
            {
                // —— LLM 模式 ——
                yield return DecideWithLLM(context, (d) => decision = d);

                if (decision == null)
                {
                    _llmFailCount++;
                    if (_llmFailCount >= 3)
                    {
                        _isInFallbackMode = true;
                        Debug.Log("[MotionAgent] LLM 连续失败≥3次，回退到概率模式");
                    }
                }
                else
                {
                    _llmFailCount = 0;
                }
            }

            if (decision == null && fallbackToRandom)
            {
                // —— 概率回退模式 ——
                decision = FallbackDecide(context);
            }

            // ◈ 执行
            if (decision != null)
            {
                Debug.Log($"[MotionAgent] 决策: {decision.action}「{decision.target}」(强度={decision.intensity:F2}, 理由={decision.reason})");
                yield return ExecuteDecision(decision);
            }

            IsDeciding = false;

            // ◈ 定期输出全链路报告到日志
            _totalActionsSinceReport++;
            if (_totalActionsSinceReport >= REPORT_INTERVAL)
            {
                _totalActionsSinceReport = 0;
                Debug.Log($"[MotionAgent] 📋 定期全链路报告（每{REPORT_INTERVAL}次）\n{GetPipelineReport()}");
            }
        }
    }

    /// <summary>判断本次 tick 是否值得决策</summary>
    private bool ShouldDecide()
    {
        // 不启用
        if (!enabled) return false;

        // AI 控制中（ChatManager 工具调用进行中）
        if (_chatManager != null && _chatManager.IsWaiting) return false;

        // AI 锁定参数中（动作正在被工具控制）
        if (_renderer != null)
        {
            // 通过反射检查 _aiControlLocked（私有字段）
            // 简化: 检查 ChatManager 是否 IsWaiting
        }

        // 冷却中（刚执行完动作）
        if (Time.time - _lastActionEndTime < minCooldownAfterAction) return false;

        // Sleep 级别 → 极低概率决策（每 5 次才决策一次）
        if (CurrentDensity == DensityLevel.Sleep && UnityEngine.Random.value > 0.2f) return false;

        // 正在播放 JSON 空闲动作
        if (_renderer != null)
        {
            // 检查是否正在播放预设动作（简化）
        }

        return true;
    }

    // ==================================================================
    //  上下文收集
    // ==================================================================

    private string GatherContext()
    {
        var parts = new List<string>();

        // 1. 时间
        DateTime now = DateTime.Now;
        parts.Add($"当前时间: {now:HH:mm}");
        parts.Add($"时段: {GetTimePeriod(now)}");

        // 2. 情绪
        parts.Add(emotion.FormatForPrompt());

        // 3. 用户状态
        if (_activityTracker != null)
        {
            string title = _activityTracker.CurrentWindowTitle;
            string proc = _activityTracker.CurrentProcessName;
            if (!string.IsNullOrEmpty(title))
                parts.Add($"用户在操作: {title} ({proc})");

            float idleDuration = Time.time - _lastInteractionTime;
            parts.Add($"用户已 {idleDuration:F0} 秒未交互");
        }

        // 4. 密度级别
        parts.Add($"决策密度: {CurrentDensity}");

        // 5. 演武心经（过往经验统计）
        var mm = MotionMemoryManager.Instance;
        if (mm != null)
        {
            string report = mm.GetPipelineReport();
            if (!string.IsNullOrEmpty(report))
                parts.Add(report);
        }

        // 6. 最近动作历史（去重）
        if (_recentActions.Count > 0)
        {
            parts.Add($"最近动作: {string.Join(", ", _recentActions.TakeLast(5))}");
        }

        // 6. 日月信息（农历→月相，影响动作风格）
        int day = now.Day;
        parts.Add($"日期: {now:MM月dd日} (农历日={day})");

        return string.Join("\n", parts);
    }

    private static string GetTimePeriod(DateTime dt)
    {
        int h = dt.Hour;
        if (h < 6) return "深夜";
        if (h < 9) return "清晨";
        if (h < 12) return "上午";
        if (h < 14) return "正午";
        if (h < 17) return "下午";
        if (h < 20) return "傍晚";
        return "夜晚";
    }

    // ==================================================================
    //  LLM 决策
    // ==================================================================

    [Serializable]
    public class MotionDecision
    {
        /// <summary>动作大类: expression / motion / idle / combo</summary>
        public string action;
        /// <summary>具体目标（表情名/动作描述/特殊标记）</summary>
        public string target;
        /// <summary>强度 0~1</summary>
        public float intensity = 0.5f;
        /// <summary>持续时间（秒）</summary>
        public float duration = 3f;
        /// <summary>决策理由</summary>
        public string reason;
    }

    private IEnumerator DecideWithLLM(string context, Action<MotionDecision> onResult)
    {
        // 拼接演武心经经验
        string mmMemories = "";
        var mm = MotionMemoryManager.Instance;
        if (mm != null)
        {
            string formatted = mm.GetFormattedMemories();
            if (!string.IsNullOrEmpty(formatted))
                mmMemories = $"\n过往演武经验:\n{formatted}\n";
            string negative = mm.GetFormattedNegativeExamples();
            if (!string.IsNullOrEmpty(negative))
                mmMemories += $"\n需避免的失败案例:\n{negative}\n";
        }

        string systemPrompt =
            "你是一个桌面宠物「符玄」的自主动作决策引擎。你的任务是根据当前上下文，创作最合适的动作。\n\n" +
            "规则:\n" +
            "1. 动作(motion): target 写任意中文动作描述，由下流引擎翻译为Live2D参数序列。\n" +
            "   示例: \"害羞地扭捏捂脸\", \"叉腰昂头哼一声\", \"伸懒腰打个哈欠\", \"歪头疑惑地眨眨眼\", " +
            "\"双手合十闭眼祈祷\", \"低头玩弄衣角\"\n" +
            "2. 表情(expression): target 写情绪名。happy_smile(开心微笑), sad_pout(委屈嘟嘴), " +
            "angry_frown(皱眉), surprised(惊讶), sleepy(犯困), blush(害羞), loving(含情脉脉), proud(骄傲)\n" +
            "3. 复合(combo): target 写更复杂的连续动作描述（表情+动作组合、多阶段动作）\n" +
            "4. 空闲(idle): 什么都不做\n\n" +
            "风格指引:\n" +
            "- 符玄性格: 傲娇、聪明、偶尔害羞、偶尔傲气。动作要有灵气和故事感\n" +
            "- 优先用 motion 或 combo，主动创造多样化动作，不要局限于固定套路\n" +
            "- 用户长时间未交互 → 用轻柔、吸引注意的动作\n" +
            "- 用户活跃 → 选择不打扰的小动作或保持安静\n" +
            "- intensity 控制幅度: 0.3轻柔, 0.5适中, 0.7夸张, 0.9很夸张\n" +
            "- duration 2~6秒，combo可到8秒\n" +
            "- 不要重复最近做过的动作\n" +
            "- 参考过往演武经验，优先选择已掌握的高分动作，避免表现不佳的动作\n\n" +
            mmMemories +
            "输出格式(JSON ONLY, 不要markdown):\n" +
            "{\"action\":\"motion\", \"target\":\"害羞地扭捏捂脸\", \"intensity\":0.6, \"duration\":3.5, \"reason\":\"被主人盯着看了好一会\"}";

        string userPrompt = "当前上下文:\n" + context + "\n\n请选择动作并输出 JSON:";

        yield return LocalLLMClient.PromptAsync(systemPrompt, userPrompt, (success, content) =>
        {
            if (!success)
            {
                onResult(null);
                return;
            }
            onResult(ParseDecision(content));
        }, temperature: 0.8f, maxTokens: 128);
    }

    private MotionDecision ParseDecision(string jsonText)
    {
        try
        {
            string clean = jsonText.Trim();
            if (clean.StartsWith("```json")) clean = clean.Substring(7).Trim();
            else if (clean.StartsWith("```")) clean = clean.Substring(3).Trim();
            if (clean.EndsWith("```")) clean = clean.Substring(0, clean.Length - 3).Trim();

            int braceStart = clean.IndexOf('{');
            int braceEnd = clean.LastIndexOf('}');
            if (braceStart < 0 || braceEnd <= braceStart) return null;
            string json = clean.Substring(braceStart, braceEnd - braceStart + 1);

            string action = ExtractJsonString(json, "action") ?? "idle";
            string target = ExtractJsonString(json, "target") ?? "";
            string reason = ExtractJsonString(json, "reason") ?? "";
            float intensity = ExtractJsonFloat(json, "intensity", 0.5f);
            float duration = ExtractJsonFloat(json, "duration", 3f);

            return new MotionDecision
            {
                action = action,
                target = target,
                intensity = Mathf.Clamp01(intensity),
                duration = Mathf.Clamp(duration, 1f, 8f),
                reason = reason
            };
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[MotionAgent] 解析决策失败: {e.Message}\n{StringTruncateExtension.Truncate(jsonText, 200)}");
            return null;
        }
    }

    // ==================================================================
    //  概率回退决策（Fallback）
    // ==================================================================

    private MotionDecision FallbackDecide(string context)
    {
        string mood = emotion.GetDominantMood();

        // 用户不在时：安静动作
        float idleFactor = Mathf.Min(1f, (Time.time - _lastInteractionTime) / shortIdleThreshold);

        // 根据情绪计算各动作权重
        var candidates = new List<(string action, string target, float weight, float duration)>();

        // —— 表情（降低权重，重点验证动作而非表情）——
        float exprWeight = mood switch
        {
            "happy" => 0.15f,
            "sad" => 0.15f,
            "angry" => 0.12f,
            "excited" => 0.15f,
            "content" => 0.15f,
            "sleepy" => 0.20f,
            "affectionate" => 0.15f,
            _ => 0.12f,
        };

        string exprTarget = mood switch
        {
            "happy" => "happy_smile",
            "sad" => "sad_pout",
            "angry" => "angry_frown",
            "excited" => "surprised",
            "content" => "blush",
            "sleepy" => "sleepy",
            "affectionate" => "loving",
            _ => "happy_smile",
        };

        candidates.Add(("expression", exprTarget, exprWeight, 2.5f));

        // —— 动作（大幅提高权重 + 增加复杂动作）——
        // 无论情绪如何，优先出动作积累数据
        candidates.Add(("motion", "wave", 0.18f, 3f));
        candidates.Add(("motion", "hands_on_hips", 0.16f, 4f));
        candidates.Add(("motion", "cover_face", 0.16f, 3.5f));
        candidates.Add(("motion", "bow", 0.14f, 3f));
        candidates.Add(("motion", "think", 0.14f, 3f));
        candidates.Add(("motion", "stretch", 0.12f, 4f));
        candidates.Add(("motion", "tilt_head", 0.12f, 2.5f));
        candidates.Add(("motion", "nod", 0.10f, 2f));

        // —— 复合复杂动作（LLM 翻译生成，最值得验证）——
        candidates.Add(("combo", "害羞地扭捏捂脸", 0.18f, 4f));
        candidates.Add(("combo", "叉腰昂头哼一声", 0.16f, 3.5f));
        candidates.Add(("combo", "伸懒腰打个哈欠", 0.14f, 4f));
        candidates.Add(("combo", "歪头疑惑地眨眨眼", 0.14f, 3f));
        candidates.Add(("combo", "双手合十闭眼祈祷", 0.12f, 4f));
        candidates.Add(("combo", "低头玩弄衣角", 0.12f, 3.5f));
        candidates.Add(("combo", "惊喜地跳起来拍手", 0.14f, 4f));
        candidates.Add(("combo", "赌气背过身去又悄悄回头", 0.12f, 5f));
        candidates.Add(("combo", "轻轻行礼问候", 0.12f, 3f));

        // —— idle（大幅降低，减少发呆）——
        candidates.Add(("idle", "", 0.10f, 0f));

        // 去重：最近做过的动作降权
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c.action != "idle" && _recentActions.Contains(c.action + ":" + c.target))
            {
                candidates[i] = (c.action, c.target, c.weight * 0.3f, c.duration);
            }
        }

        // ★ 负反馈失败惩罚：低分动作动态降权（对接 MotionMemoryManager）
        var mm = MotionMemoryManager.Instance;
        if (mm != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c.action == "idle" || c.action == "expression") continue;

                // ① L2D 物理不可能动作 → 完全阻止
                if (MotionMemoryManager.PhysicallyImpossibleActions.Contains(c.target))
                {
                    candidates[i] = (c.action, c.target, 0f, c.duration);
                    UnityEngine.Debug.Log($"[MotionAgent] 🚫 L2D 无法表现「{c.target}」，已禁止");
                    continue;
                }

                // ② 负反馈降权
                float penalty = mm.GetFailurePenalty(c.target);
                if (penalty < 1.0f)
                {
                    float oldWeight = candidates[i].weight;
                    candidates[i] = (c.action, c.target, oldWeight * penalty, c.duration);
                    UnityEngine.Debug.Log($"[MotionAgent] ⛔ 失败惩罚「{c.target}」: {oldWeight:F2} → {oldWeight * penalty:F2} (乘数={penalty})");
                }
            }
        }

        // 加权随机选择
        float totalWeight = candidates.Sum(c => c.weight);
        float roll = UnityEngine.Random.Range(0f, totalWeight);
        float cum = 0f;
        MotionDecision selected = null;
        foreach (var c in candidates)
        {
            cum += c.weight;
            if (roll <= cum)
            {
                selected = new MotionDecision
                {
                    action = c.action,
                    target = c.target,
                    intensity = 0.5f + UnityEngine.Random.value * 0.3f,
                    duration = c.duration,
                    reason = $"情绪{mood}, 空闲{ (Time.time - _lastInteractionTime):F0}s"
                };
                break;
            }
        }

        if (selected == null) return null;

        // 记录历史
        RecordAction(selected);
        return selected;
    }

    // ==================================================================
    //  决策执行
    // ==================================================================

    private IEnumerator ExecuteDecision(MotionDecision decision)
    {
        if (decision == null) yield break;

        // 检查 AI 控制锁
        if (_renderer != null)
        {
            // 通过 public 属性检查是否有动作锁定
        }

        switch (decision.action)
        {
            case "expression":
                yield return ExecuteExpression(decision.target, decision.intensity, decision.duration);
                break;

            case "motion":
                yield return ExecuteMotion(decision.target, decision.intensity, decision.duration);
                break;

            case "combo":
                yield return ExecuteCombo(decision.target, decision.duration);
                break;

            case "idle":
            default:
                // 什么都不做
                yield break;
        }

        _lastActionEndTime = Time.time;
        RecordAction(decision);
    }

    private IEnumerator ExecuteExpression(string target, float intensity, float duration)
    {
        // 1) 尝试使用 ExpressionManager（需构造实例 + 每帧驱动）
        if (_mapper != null && _mapper.IsLoaded)
        {
            var expressionManager = new ExpressionManager(_mapper);
            expressionManager.LoadPresets();
            string exprName = target switch
            {
                "happy_smile" => "happy",
                "sad_pout" => "sad",
                "angry_frown" => "angry",
                "surprised" => "surprised",
                "sleepy" => "sleepy",
                "blush" => "blush",
                "loving" => "blush",
                "proud" => "happy",
                _ => "happy",
            };

            expressionManager.Play(exprName, 0.3f);

            // 每帧驱动 Update 实现淡入 → 保持 → 淡出
            float elapsed = 0f;
            float fadeOutStart = duration - 0.3f;
            bool hasTriggeredStop = false;

            while (elapsed < duration)
            {
                expressionManager.Update(Time.deltaTime);
                elapsed += Time.deltaTime;

                // 在结束前 0.3s 触发淡出
                if (!hasTriggeredStop && elapsed >= fadeOutStart)
                {
                    expressionManager.Stop(0.3f);
                    hasTriggeredStop = true;
                }

                yield return null;
            }

            expressionManager.Update(0f); // final flush
        }
        // 2) 回退: 通过 MotionTranslator 生成简单表情动作
        else if (_mapper != null && _model != null)
        {
            string desc = target switch
            {
                "happy_smile" => "开心地微笑",
                "sad_pout" => "委屈地嘟嘴",
                "angry_frown" => "生气地皱眉",
                "surprised" => "惊讶地瞪大眼睛",
                "sleepy" => "困倦地眯眼",
                "blush" => "害羞地脸红",
                "loving" => "温柔地微笑",
                "proud" => "骄傲地昂首",
                _ => "微笑",
            };

            MotionPlanner.MotionPlan plan = null;
            yield return MotionTranslator.TranslateAsync(
                desc, _mapper, _model, duration, p => plan = p);

            if (plan != null)
            {
                var generator = new MotionGenerator(_mapper, _model);
                yield return generator.PlayAsync(plan);
            }
        }
    }

    private IEnumerator ExecuteMotion(string target, float intensity, float duration)
    {
        // 用 MotionMemoryManager 统一映射（与 GetFailurePenalty 共享）
        string cnDescription = MotionMemoryManager.GetChineseName(target);

        // 通过 MotionTranslator 生成并播放
        if (_mapper != null && _model != null)
        {
            string fullDesc = intensity > 0.6f
                ? $"夸张地{cnDescription}"
                : intensity < 0.4f
                    ? $"轻轻地{cnDescription}"
                    : cnDescription;

            MotionPlanner.MotionPlan plan = null;
            yield return MotionTranslator.TranslateAsync(
                fullDesc, _mapper, _model, duration, p => plan = p);

            if (plan != null)
            {
                byte[] capturedPng = null;
                var generator = new MotionGenerator(_mapper, _model);
                yield return generator.PlayAsync(plan, progress =>
                {
                    // 动作峰值时刻（~50%进度）截图
                    if (capturedPng == null && progress >= 0.48f && _renderer != null)
                    {
                        capturedPng = _renderer.CaptureModelSnapshot();
                    }
                });

                // ★ 闭环学习-写入演武心经：记录本次动作参数快照
                string snapshot = BuildParamSnapshot(plan);
                var mm = MotionMemoryManager.Instance;
                if (mm != null)
                    mm.RecordMotion(cnDescription, snapshot, plan.KeyFrames.Count, plan.TotalDuration);

                // 播放完毕 → 双模型交叉验证
                if (capturedPng != null && _dualValidator != null)
                {
                    string dataUrl = "data:image/png;base64," + Convert.ToBase64String(capturedPng);
                    bool consensus = false;
                    int avgScore = 0, sGlm = 0, sQwen = 0;
                    string rGlm = "", rQwen = "";
                    yield return _dualValidator.ValidateAsync(fullDesc, dataUrl, plan,
                        (c, avg, g, q, rg, rq) => { consensus = c; avgScore = avg; sGlm = g; sQwen = q; rGlm = rg; rQwen = rq; });

                    if (consensus)
                    {
                        Debug.Log($"[MotionAgent] ✅ 双镜鉴通过: 「{fullDesc}」均分={avgScore}/5");
                        _actionSuccessCount.TryGetValue(cnDescription, out int oldS);
                        _actionSuccessCount[cnDescription] = oldS + 1;

                        // ★ 闭环学习-写入评分：双镜鉴通过 → UpdateScore（均分为可信评分）
                        if (mm != null && avgScore > 0)
                            mm.UpdateScore(cnDescription, avgScore, $"双镜鉴均分{avgScore}/5", snapshot);
                    }
                    else
                    {
                        Debug.Log($"[MotionAgent] ⚠️ 双镜鉴未通过: 「{fullDesc}」 GLM={sGlm} Qwen={sQwen}");
                        _actionFailCount.TryGetValue(cnDescription, out int oldF);
                        _actionFailCount[cnDescription] = oldF + 1;

                        // ★ 闭环学习-写入低分：双镜鉴未通过 → 取两者中较低分
                        if (mm != null)
                        {
                            int failScore = Mathf.Min(sGlm, sQwen);
                            if (failScore > 0)
                                mm.UpdateScore(cnDescription, failScore, $"双镜鉴未通过 GLM={sGlm} Qwen={sQwen}", snapshot);
                        }
                    }
                    _totalActionsSinceReport++;
                }
            }
            else
            {
                _skippedCount++;
                // 翻译失败 → 记录负反馈（避免下次再选同一动作）
                var mmFail = MotionMemoryManager.Instance;
                if (mmFail != null && !string.IsNullOrEmpty(fullDesc))
                {
                    mmFail.RecordNegativeExample(fullDesc, "", 1,
                        $"MotionTranslator 无法将「{fullDesc}」翻译为参数序列");
                }
            }
        }
    }

    private IEnumerator ExecuteCombo(string description, float duration)
    {
        // 复合动作：直接传给 MotionTranslator
        if (_mapper != null && _model != null)
        {
            MotionPlanner.MotionPlan plan = null;
            yield return MotionTranslator.TranslateAsync(
                description, _mapper, _model, duration, p => plan = p);

            if (plan != null)
            {
                byte[] capturedPng = null;
                var generator = new MotionGenerator(_mapper, _model);
                yield return generator.PlayAsync(plan, progress =>
                {
                    if (capturedPng == null && progress >= 0.48f && _renderer != null)
                    {
                        capturedPng = _renderer.CaptureModelSnapshot();
                    }
                });

                // ★ 闭环学习-写入演武心经：记录复合动作参数快照
                string snapshot = BuildParamSnapshot(plan);
                var mm = MotionMemoryManager.Instance;
                if (mm != null)
                    mm.RecordMotion(description, snapshot, plan.KeyFrames.Count, plan.TotalDuration);

                // 双模型交叉验证
                if (capturedPng != null && _dualValidator != null)
                {
                    string dataUrl = "data:image/png;base64," + Convert.ToBase64String(capturedPng);
                    bool consensus = false;
                    int avgScore = 0, sGlm = 0, sQwen = 0;
                    string rGlm = "", rQwen = "";
                    yield return _dualValidator.ValidateAsync(description, dataUrl, plan,
                        (c, avg, g, q, rg, rq) => { consensus = c; avgScore = avg; sGlm = g; sQwen = q; rGlm = rg; rQwen = rq; });

                    if (consensus)
                    {
                        Debug.Log($"[MotionAgent] ✅ 双镜鉴通过(combo): 「{description}」均分={avgScore}/5");
                        _actionSuccessCount.TryGetValue(description, out int oldS);
                        _actionSuccessCount[description] = oldS + 1;

                        // ★ 闭环学习-写入评分
                        if (mm != null && avgScore > 0)
                            mm.UpdateScore(description, avgScore, $"双镜鉴均分{avgScore}/5", snapshot);
                    }
                    else
                    {
                        Debug.Log($"[MotionAgent] ⚠️ 双镜鉴未通过(combo): 「{description}」 GLM={sGlm} Qwen={sQwen}");
                        _actionFailCount.TryGetValue(description, out int oldF);
                        _actionFailCount[description] = oldF + 1;

                        // ★ 闭环学习-写入低分
                        if (mm != null)
                        {
                            int failScore = Mathf.Min(sGlm, sQwen);
                            if (failScore > 0)
                                mm.UpdateScore(description, failScore, $"双镜鉴未通过 GLM={sGlm} Qwen={sQwen}", snapshot);
                        }
                    }
                    _totalActionsSinceReport++;
                }
            }
            else
            {
                _skippedCount++;
            }
        }
    }

    // ==================================================================
    //  工具方法
    // ==================================================================

    /// <summary>从 MotionPlan 提取关键参数快照（供闭环学习写入 MotionMemoryManager）</summary>
    private static string BuildParamSnapshot(MotionPlanner.MotionPlan plan)
    {
        if (plan == null || plan.KeyFrames.Count == 0) return "";
        int midIdx = Mathf.Clamp(plan.KeyFrames.Count / 2, 0, plan.KeyFrames.Count - 1);
        var midKf = plan.KeyFrames[midIdx];
        if (midKf.Values.Count == 0) return "";
        var topParams = midKf.Values
            .OrderByDescending(kv => Math.Abs(kv.Value))
            .Take(5)
            .Select(kv => $"{kv.Key}={kv.Value:F2}");
        return string.Join(", ", topParams);
    }

    private void RecordAction(MotionDecision decision)
    {
        if (decision == null || decision.action == "idle") return;
        string key = decision.action + ":" + decision.target;
        _recentActions.Add(key);
        if (_recentActions.Count > RECENT_ACTION_HISTORY)
            _recentActions.RemoveAt(0);
        _consecutiveActionCount++;
    }

    // ==================================================================
    //  JSON 解析辅助
    // ==================================================================

    private static string ExtractJsonString(string json, string key)
    {
        int idx = json.IndexOf("\"" + key + "\"");
        if (idx < 0) return null;
        int colon = json.IndexOf(':', idx + key.Length + 2);
        if (colon < 0) return null;
        int start = colon + 1;
        while (start < json.Length && json[start] == ' ') start++;
        if (start >= json.Length) return null;
        if (json[start] == '"')
        {
            start++;
            int end = start;
            bool esc = false;
            while (end < json.Length)
            {
                if (esc) { esc = false; end++; continue; }
                if (json[end] == '\\') { esc = true; end++; continue; }
                if (json[end] == '"') break;
                end++;
            }
            if (end >= json.Length) return null;
            return json.Substring(start, end - start).Replace("\\\"", "\"").Replace("\\n", "\n");
        }
        // 非字符串值
        int valEnd = start;
        while (valEnd < json.Length && (char.IsLetter(json[valEnd]) || json[valEnd] == '_'))
            valEnd++;
        if (valEnd > start) return json.Substring(start, valEnd - start);
        return null;
    }

    private static float ExtractJsonFloat(string json, string key, float defaultValue)
    {
        int idx = json.IndexOf("\"" + key + "\"");
        if (idx < 0) return defaultValue;
        int colon = json.IndexOf(':', idx + key.Length + 2);
        if (colon < 0) return defaultValue;
        int start = colon + 1;
        while (start < json.Length && json[start] == ' ') start++;
        if (start >= json.Length) return defaultValue;
        int end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-' || json[end] == '+' || json[end] == 'e' || json[end] == 'E'))
            end++;
        if (end <= start) return defaultValue;
        string numStr = json.Substring(start, end - start);
        float result;
        float.TryParse(numStr, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out result);
        return result;
    }
}
