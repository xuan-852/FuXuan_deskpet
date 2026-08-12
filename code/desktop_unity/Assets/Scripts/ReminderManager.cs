using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using UnityEngine;

/// <summary>
/// 便签/提醒管理器 — 符玄的「卜算记事簿」
///
/// 功能：
/// - 增删改查提醒事项（本地 JSON 持久化）
/// - 每帧检查是否有到期提醒
/// - 到期时触发气泡 + Windows 系统通知
/// - 支持每日重复提醒
/// - 数据存于 D:\DesktopPetData\reminders.json
///
/// 对接 DeepSeek Function Calling：
///   set_reminder(text, remindAt)   → 新增提醒
///   query_reminders(status?)       → 查询提醒列表
///   mark_done(id)                  → 标记完成
///   delete_reminder(id)            → 删除提醒
/// </summary>
public class ReminderManager : MonoBehaviour
{
    [System.Serializable]
    public class Reminder
    {
        public string id;
        public string text;
        public string createdAt;       // ISO 8601
        public string remindAt;        // ISO 8601, 到期时间
        public bool done;
        public string recurring;       // null / "daily" / "weekday" / "weekly"
        public string source;          // "user" / "ai"
        public string priority;        // "low" / "normal" / "high"
        public ReminderAction action;  // P2: 到期要执行的动作（null = 纯提醒）
    }

    /// <summary>定时动作 — 提醒到期时自动执行（P2 定时动作框架）</summary>
    [System.Serializable]
    public class ReminderAction
    {
        public string type;      // "local_tool" | "openclaw_task"
        public string toolName;  // local_tool 时的工具名（如 open_app）
        public string args;      // local_tool 时的参数 JSON
        public string task;      // openclaw_task 时的任务描述
        public string mode;      // openclaw_task 模式: "agent" / "browser"
        public string result;    // 最近一次执行结果摘要（供查询）
        public string lastRunAt; // 最近一次执行时间（ISO 8601）
    }

    [System.Serializable]
    public class ReminderList
    {
        public List<Reminder> reminders = new List<Reminder>();
    }

    // ——— 单例 ———
    private static ReminderManager _instance;
    public static ReminderManager Instance => _instance;

    [Header("提醒设置")]
    [Tooltip("到期提醒提前多少秒触发（0=准时）")]
    public float preTriggerSeconds = 0f;

    [Tooltip("提醒气泡显示时长（秒）")]
    public float bubbleDuration = 6f;

    [Tooltip("检查间隔（秒）")]
    public float checkInterval = 1f;

    [Header("Server酱³ 推送")]
    [Tooltip("Server酱³ SendKey（从环境变量 SERVERCHAN_SENDKEY 读取，无需在此填写）")]
    public string serverChanSendKey = "";

    /// <summary>从环境变量构建 Server酱³ 推送 URL</summary>
    private string ServerChanUrl
    {
        get
        {
            string sendKey = serverChanSendKey;
            if (string.IsNullOrEmpty(sendKey))
                sendKey = System.Environment.GetEnvironmentVariable("SERVERCHAN_SENDKEY") ?? "";
            if (string.IsNullOrEmpty(sendKey))
                return null;
            return $"https://sctapi.ft07.com/send/{sendKey}";
        }
    }

    // 组件引用
    private ChatBubble _bubble;
    private ChatManager _chat;

    // 数据
    private ReminderList _data = new ReminderList();
    private string _filePath;
    private float _checkTimer = 0f;

    // 已触发的提醒 ID 集合（防止重复触发）
    private HashSet<string> _triggeredIds = new HashSet<string>();

    // 公开事件
    public System.Action<Reminder> OnReminderDue;
    public System.Action<Reminder> OnReminderAdded;
    public System.Action<string> OnReminderDone;
    public System.Action<List<Reminder>> OnDataChanged;

    // ================================================================
    //  生命周期
    // ================================================================

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[ReminderManager] 检测到重复实例，销毁自身");
            Destroy(this);
            return;
        }
        _instance = this;
    }

    private void Start()
    {
        _filePath = Path.Combine(DataPathConfig.DataRoot, "reminders.json");
        _bubble = GetComponent<ChatBubble>();
        if (_bubble == null) _bubble = FindObjectOfType<ChatBubble>();
        _chat = GetComponent<ChatManager>();
        if (_chat == null) _chat = FindObjectOfType<ChatManager>();

        Load();

        // 数据变更后自动更新托盘悬浮提示
        OnReminderAdded += _ => UpdateTrayBadge();
        OnReminderDone += _ => UpdateTrayBadge();
        OnDataChanged += _ => UpdateTrayBadge();
        UpdateTrayBadge();

        Debug.Log($"[ReminderManager] 已加载 {_data.reminders.Count} 条提醒，数据路径: {_filePath}");
    }

    /// <summary>更新托盘图标悬浮提示中的待办数量</summary>
    private void UpdateTrayBadge()
    {
        int pending = PendingCount;
        var tray = GetComponent<SystemTrayManager>();
        if (tray == null) tray = FindObjectOfType<SystemTrayManager>();
        if (tray != null)
        {
            string tip = pending > 0
                ? $"符玄桌面宠物 · {pending} 条待办提醒"
                : "符玄桌面宠物";
            tray.UpdateTooltip(tip);
        }
    }

    private void Update()
    {
        _checkTimer += Time.deltaTime;
        if (_checkTimer < checkInterval) return;
        _checkTimer = 0f;

        CheckDueReminders();

        // 定期清理已触发 ID 集合（防止无限增长）
        if (Time.frameCount % 600 == 0) // 约每 10 秒（60fps下）清理一次
            CleanupTriggeredIds();
    }

    /// <summary>清理 _triggeredIds 中已完成或已删除的 ID</summary>
    private void CleanupTriggeredIds()
    {
        var activeIds = new HashSet<string>();
        foreach (var r in _data.reminders)
        {
            if (r.done && string.IsNullOrEmpty(r.recurring))
                continue; // 一次性已完成的不保留
            activeIds.Add(r.id);
        }
        _triggeredIds.IntersectWith(activeIds);
    }

    private void OnApplicationQuit()
    {
        Save();
    }

    // ================================================================
    //  公开 CRUD
    // ================================================================

    /// <summary>新增一条提醒（可带到期动作）</summary>
    public Reminder AddReminder(string text, DateTime remindAt,
        string recurring = null, string priority = "normal", string source = "user",
        ReminderAction action = null)
    {
        var r = new Reminder
        {
            id = Guid.NewGuid().ToString("N"),
            text = text,
            createdAt = DateTime.Now.ToString("O"),
            remindAt = remindAt.ToString("O"),
            done = false,
            recurring = recurring,
            source = source,
            priority = priority,
            action = action
        };
        _data.reminders.Add(r);
        Save();
        OnReminderAdded?.Invoke(r);
        NotifyDataChanged();
        string actionTag = action != null && !string.IsNullOrEmpty(action.type)
            ? $" [动作:{action.type}]" : "";
        Debug.Log($"[ReminderManager] 新增提醒: [{r.id}] {text} @ {remindAt:yyyy-MM-dd HH:mm}{actionTag}");
        return r;
    }

    /// <summary>标记提醒为已完成</summary>
    public bool MarkDone(string id)
    {
        var r = _data.reminders.Find(x => x.id == id);
        if (r == null) return false;
        r.done = true;
        Save();
        OnReminderDone?.Invoke(id);
        NotifyDataChanged();
        Debug.Log($"[ReminderManager] 标记完成: [{id}] {r.text}");
        return true;
    }

    /// <summary>删除提醒</summary>
    public bool DeleteReminder(string id)
    {
        int removed = _data.reminders.RemoveAll(x => x.id == id);
        if (removed > 0)
        {
            Save();
            NotifyDataChanged();
            Debug.Log($"[ReminderManager] 删除提醒: [{id}]");
            return true;
        }
        return false;
    }

    /// <summary>获取所有未完成的提醒（按时间升序）</summary>
    public List<Reminder> GetPendingReminders()
    {
        var list = _data.reminders.FindAll(r => !r.done);
        list.Sort((a, b) => string.Compare(a.remindAt, b.remindAt, StringComparison.Ordinal));
        return list;
    }

    /// <summary>获取所有提醒（含已完成的，按创建时间降序）</summary>
    public List<Reminder> GetAllReminders()
    {
        var list = new List<Reminder>(_data.reminders);
        list.Sort((a, b) => string.Compare(b.createdAt, a.createdAt, StringComparison.Ordinal));
        return list;
    }

    /// <summary>获取未完成提醒数量</summary>
    public int PendingCount => _data.reminders.FindAll(r => !r.done).Count;

    /// <summary>获取已完成的提醒（按完成时间降序）</summary>
    public List<Reminder> GetDoneReminders()
    {
        var list = _data.reminders.FindAll(r => r.done);
        list.Sort((a, b) => string.Compare(b.createdAt, a.createdAt, StringComparison.Ordinal));
        return list;
    }

    // ================================================================
    //  去重
    // ================================================================

    /// <summary>
    /// 检查是否有未完成提醒包含指定关键词（用于去重，避免服务器推送与用户手动设置重复）
    /// </summary>
    /// <param name="keyword">用于匹配的关键词，如课程名</param>
    /// <returns>存在匹配的未完成提醒返回 true</returns>
    public bool HasPendingReminderContaining(string keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return false;
        return _data.reminders.Exists(r =>
            !r.done && r.text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>
    /// 删除所有包含指定关键词的未完成提醒（用于去重后替换）
    /// </summary>
    /// <param name="keyword">用于匹配的关键词</param>
    /// <returns>实际删除的条数</returns>
    public int DeletePendingRemindersContaining(string keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return 0;
        int count = _data.reminders.RemoveAll(r =>
            !r.done && r.text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        if (count > 0)
        {
            Save();
            NotifyDataChanged();
            Debug.Log($"[ReminderManager] 去重清理: 关键词「{keyword}」删除了 {count} 条重复提醒");
        }
        return count;
    }

    /// <summary>获取待办列表的格式化文本（供 AI 读取）</summary>
    public string GetPendingText()
    {
        var pending = GetPendingReminders();
        if (pending.Count == 0) return "暂无待办事项";
        var lines = new List<string>();
        foreach (var r in pending)
        {
            if (DateTime.TryParse(r.remindAt, out var dt))
            {
                string timeStr = dt.ToString("MM-dd HH:mm");
                string recurringTag = r.recurring == "daily" ? " [每日]" :
                                      r.recurring == "weekday" ? " [工作日]" :
                                      r.recurring == "weekly" ? " [每周]" : "";
                string actionTag = HasAction(r) ? " [⚡到时执行动作]" : "";
                lines.Add($"  • [{r.id.Substring(0, 6)}] {timeStr} {r.text}{recurringTag}{actionTag}");
            }
        }
        return $"你共有 {pending.Count} 项待办：\n" + string.Join("\n", lines);
    }

    // ================================================================
    //  到期检查
    // ================================================================

    private void CheckDueReminders()
    {
        var now = DateTime.Now;
        bool changed = false;
        foreach (var r in _data.reminders.FindAll(r => !r.done))
        {
            if (_triggeredIds.Contains(r.id)) continue;
            if (!DateTime.TryParse(r.remindAt, out var dt)) continue;

            // 到期时间在 preTriggerSeconds 窗口内
            double diff = (now - dt).TotalSeconds;
            if (diff >= -preTriggerSeconds && diff <= checkInterval + 1f)
            {
                TriggerReminder(r);
            }
            else if (diff > checkInterval + 1f && string.IsNullOrEmpty(r.recurring))
            {
                // ★ 一次性提醒错过触发窗口（如桌宠当时未运行）：归档为已勾销，
                //   避免永久卡在待办列表（原逻辑只等重启时 Load 兜底清理 >1 天的）
                r.done = true;
                changed = true;
                Debug.Log($"[ReminderManager] ⏳ 一次性提醒「{r.text}」已错过（{((int)(diff / 60))} 分钟），自动归档");
                OnReminderDone?.Invoke(r.id);
            }
        }
        if (changed) Save();
    }

    private void TriggerReminder(Reminder r)
    {
        _triggeredIds.Add(r.id);

        // 1. 角色头顶气泡
        string msg = FormatReminderMessage(r);
        if (_bubble != null)
        {
            _bubble.ShowMessage(msg, bubbleDuration, ChatBubble.MsgPriority.Normal);
        }

        // 2. Windows 系统通知
        ShowSystemNotification("符玄 · 卜算提醒", $"{r.text}");

        // 3. Server酱³ 推送手机
        PushToServerChan("符玄 · 卜算提醒", r.text);

        // 3.5 ★ P2 定时动作：有 action → 异步执行（危险工具需确认，见 ExecuteReminderAction）
        if (HasAction(r))
        {
            StartCoroutine(ExecuteReminderAction(r));
        }

        // 4. 触发事件
        OnReminderDue?.Invoke(r);

        // 5. 如果是每日/工作日/每周重复，自动重置
        //    否则（一次性提醒）自动标记完成，从待办中移除
        if (r.recurring == "daily")
        {
            r.remindAt = dtTomorrow(r.remindAt);
            _triggeredIds.Remove(r.id);
            Save();
        }
        else if (r.recurring == "weekday")
        {
            // 跳到下一个工作日
            var next = DateTime.Now.AddDays(1);
            while (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)
                next = next.AddDays(1);
            r.remindAt = next.Date.Add(DateTime.Parse(r.remindAt).TimeOfDay).ToString("O");
            _triggeredIds.Remove(r.id);
            Save();
        }
        else if (r.recurring == "weekly")
        {
            r.remindAt = DateTime.Parse(r.remindAt).AddDays(7).ToString("O");
            _triggeredIds.Remove(r.id);
            Save();
        }
        else
        {
            // 一次性提醒 — 触发后自动勾销
            r.done = true;
            OnReminderDone?.Invoke(r.id);
            Save();
        }

        Debug.Log($"[ReminderManager] 触发提醒: {r.text}");
    }

    private string dtTomorrow(string remindAt)
    {
        if (DateTime.TryParse(remindAt, out var dt))
            return dt.AddDays(1).ToString("O");
        return remindAt;
    }

    /// <summary>根据优先级和来源生成角色风格的消息</summary>
    private string FormatReminderMessage(Reminder r)
    {
        string prefix = "";
        if (r.priority == "high") prefix = "⚠️ ";
        else if (r.priority == "low") prefix = "☕ ";

        string[] templates = new string[]
        {
            $"{prefix}本座算了一卦，你该「{r.text}」了！",
            $"{prefix}卜象显示——{r.text}，时辰已到。",
            $"{prefix}记得{r.text}哦，别让本座催第二次~",
        };

        if (r.recurring == "daily")
        {
            templates = new string[]
            {
                $"{prefix}每日提醒：{r.text}，该动身了~",
                $"{prefix}又到这个时辰了，{r.text}！",
            };
        }

        return templates[UnityEngine.Random.Range(0, templates.Length)];
    }

    // ================================================================
    //  P2 定时动作执行
    // ================================================================

    /// <summary>判断提醒是否带有效动作（兼容旧数据：action 为 null 或空对象时视为纯提醒）</summary>
    public static bool HasAction(Reminder r)
    {
        return r != null && r.action != null && !string.IsNullOrEmpty(r.action.type);
    }

    /// <summary>
    /// 执行提醒的定时动作。
    /// 安全规则：
    /// - local_tool 安全工具（open_app/open_url/notify 等）→ 直接执行，无需确认
    /// - 危险工具（file_delete/power/run_command/openclaw_task 等）→ 弹确认（点宠物=允许/ESC=拒绝/60s 超时拒绝），
    ///   无人值守时不静默执行（守住 ToolRegistry 危险动作铁律）
    /// </summary>
    private IEnumerator ExecuteReminderAction(Reminder r)
    {
        var action = r.action;
        string result;

        if (action.type == "local_tool")
        {
            bool needsConfirm = ToolRegistry.IsDangerous(action.toolName);
            if (needsConfirm)
            {
                string desc = ToolRegistry.GetDangerDescription(action.toolName);
                bool confirmed = false;
                yield return StartCoroutine(ConfirmReminderAction(r, desc, ok => confirmed = ok));
                if (!confirmed)
                {
                    result = "⏭️ 定时动作属危险操作，未获确认，未执行";
                    FinishAction(r, result, false);
                    yield break;
                }
            }
            result = null;
            yield return StartCoroutine(ExecuteLocalTool(action.toolName, action.args, res => result = res));
            if (result == null) result = "❌ 工具无返回结果";
        }
        else if (action.type == "openclaw_task")
        {
            string desc = "外包给 OpenClaw 智能体执行任务（可能涉及浏览器操作、命令执行）";
            bool confirmed = false;
            yield return StartCoroutine(ConfirmReminderAction(r, desc, ok => confirmed = ok));
            if (!confirmed)
            {
                result = "⏭️ 外包任务属危险操作，未获确认，未执行";
                FinishAction(r, result, false);
                yield break;
            }
            result = null;
            yield return StartCoroutine(ExecuteOpenClawTask(action, res => result = res));
            if (result == null) result = "❌ 外包任务无返回结果";
        }
        else
        {
            result = $"❌ 未知动作类型：{action.type}";
        }

        FinishAction(r, result, true);
    }

    /// <summary>危险定时动作的用户确认流（复用 ToolConfirmManager；60s 超时自动拒绝）</summary>
    private IEnumerator ConfirmReminderAction(Reminder r, string description, Action<bool> onResult)
    {
        // 已有确认请求在排队（如用户聊天中正触发危险工具）→ 本次跳过，不抢占
        if (ToolConfirmManager.HasPending)
        {
            Debug.LogWarning($"[ReminderManager] 定时动作「{r.text}」跳过确认：已有确认请求在排队");
            onResult(false);
            yield break;
        }

        bool confirmed = false;
        bool resolved = false;
        var confirmBubble = _bubble != null ? _bubble : FindObjectOfType<ChatBubble>();
        if (confirmBubble != null)
        {
            confirmBubble.ShowMessage(
                $"⚠️ 定时动作「{r.text}」欲执行「{r.action.toolName}」——{description}。\n点一下本座 = 允许，按 ESC = 拒绝。",
                60f, ChatBubble.MsgPriority.High);
        }

        ToolConfirmManager.Request(r.action.toolName, r.action.args, description,
            ok => { confirmed = ok; resolved = true; });

        float confirmTimeout = Time.time + 60f;
        while (!resolved)
        {
            if (Time.time > confirmTimeout)
            {
                ToolConfirmManager.Resolve(false); // 触发回调 → resolved=true, confirmed=false
                break;
            }
            yield return null;
        }

        Debug.Log($"[ReminderManager] 定时动作确认结果: {(confirmed ? "允许" : "拒绝")}");
        onResult(confirmed);
    }

    /// <summary>执行本地工具（同步/异步统一入口）</summary>
    private IEnumerator ExecuteLocalTool(string toolName, string argsJson, Action<string> onResult)
    {
        if (ToolRegistry.IsAsync(toolName))
        {
            string result = null;
            yield return ToolRegistry.ExecuteAsync(toolName, argsJson ?? "{}", res => result = res);
            onResult(result ?? "❌ 工具无返回结果");
        }
        else if (ToolRegistry.HasTool(toolName))
        {
            onResult(ToolRegistry.Execute(toolName, argsJson ?? "{}"));
        }
        else
        {
            onResult($"❌ 不识此术：「{toolName}」");
        }
    }

    /// <summary>执行 openclaw_task 外包任务（后台 Task + 轮询，参考 OpenClawTaskTool）</summary>
    private IEnumerator ExecuteOpenClawTask(ReminderAction action, Action<string> onResult)
    {
        string task = string.IsNullOrEmpty(action.task) ? "请总结当前提醒文本对应的待办" : action.task;
        string mode = string.IsNullOrEmpty(action.mode) ? "agent" : action.mode;

        var taskRunner = System.Threading.Tasks.Task.Run(async () =>
        {
            bool healthy = await OpenClawBridge.CheckHealthAsync();
            if (!healthy)
                return $"❌ 定时外包任务无法发动：未检测到通神阵法（{OpenClawBridge.LastError}）。请先运行 openclaw_bridge.js";
            // 定时任务给足预算，但限制步骤防烧 token
            return await OpenClawBridge.ExecuteTaskAndWaitAsync(task, mode,
                totalTimeoutSeconds: 600, maxSteps: 20, heartbeatSeconds: 60, maxIdleHeartbeats: 5);
        });

        yield return new WaitUntil(() => taskRunner.IsCompleted);

        if (taskRunner.IsFaulted)
            onResult($"❌ 定时外包任务出错: {taskRunner.Exception?.InnerException?.Message}");
        else
            onResult(taskRunner.Result);
    }

    /// <summary>记录动作执行结果并通知用户</summary>
    private void FinishAction(Reminder r, string result, bool executed)
    {
        if (r.action == null) return;
        r.action.result = result;
        r.action.lastRunAt = DateTime.Now.ToString("O");
        Save();
        NotifyDataChanged();

        string prefix = executed ? "✅" : "⚠️";
        if (_bubble != null)
        {
            _bubble.ShowMessage($"{prefix} 定时动作「{r.text}」完成：{Truncate(result, 60)}",
                bubbleDuration, ChatBubble.MsgPriority.Normal);
        }
        Debug.Log($"[ReminderManager] 定时动作执行完成: [{r.id}] {r.text} → {result}");
    }

    /// <summary>截断过长的结果文本（气泡展示用）</summary>
    private static string Truncate(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "…";
    }

    // ================================================================
    //  持久化
    // ================================================================

    private void Save()
    {
        try
        {
            string json = JsonUtility.ToJson(_data, true);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[ReminderManager] 保存失败: {e.Message}");
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                string json = File.ReadAllText(_filePath);
                _data = JsonUtility.FromJson<ReminderList>(json);
                if (_data == null) _data = new ReminderList();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ReminderManager] 加载失败，将使用空数据: {e.Message}");
            _data = new ReminderList();
        }

        // 兜底清理：过期超过 1 天的一次性提醒（正常应触发即删，此逻辑防异常残留）
        try
        {
            int cleaned = _data.reminders.RemoveAll(r =>
                string.IsNullOrEmpty(r.recurring)
                && DateTime.TryParse(r.remindAt, out var dt)
                && (DateTime.Now - dt).TotalDays > 1);
            if (cleaned > 0)
                Debug.Log($"[ReminderManager] 加载时兜底清理了 {cleaned} 条过期一次性提醒");
        }
        catch
        {
            // 清理异常不影响主流程
        }
    }

    private void NotifyDataChanged()
    {
        OnDataChanged?.Invoke(_data.reminders);
    }

    // ================================================================
    //  Server酱³ 手机推送
    // ================================================================

    /// <summary>通过 Server酱³ 向手机推送提醒</summary>
    private void PushToServerChan(string title, string message)
    {
        string url = ServerChanUrl;
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            url = $"{url}?title={UriEscape(title)}&desp={UriEscape(message)}";
            var req = new System.Net.Http.HttpClient();
            req.Timeout = TimeSpan.FromSeconds(5);
            req.GetAsync(url).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Debug.LogWarning($"[ReminderManager] Server酱³ 推送失败: {t.Exception?.InnerException?.Message}");
                else if (t.IsCompletedSuccessfully)
                    Debug.Log($"[ReminderManager] Server酱³ 推送成功: {title}");
            });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ReminderManager] Server酱³ 推送异常: {e.Message}");
        }
    }

    private static string UriEscape(string s) => System.Uri.EscapeDataString(s ?? "");

    // ================================================================
    //  系统通知
    // ================================================================

    private static void ShowSystemNotification(string title, string message)
    {
        try
        {
            // ★ 注入面修复：标题/消息不拼进脚本（原实现 \" 转义在 PowerShell 中无效，
            //   且内容可直接拼入 -Command 字符串形成注入），改为环境变量传递——
            //   脚本内只读 $env:，用户内容无论含什么字符都无法执行代码
            const string ENV_TITLE = "FX_NOTIF_TITLE";
            const string ENV_MSG = "FX_NOTIF_MSG";
            System.Environment.SetEnvironmentVariable(ENV_TITLE, title ?? "");
            System.Environment.SetEnvironmentVariable(ENV_MSG, message ?? "");

            string script =
                "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null\n" +
                "$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)\n" +
                "$textNodes = $template.GetElementsByTagName('text')\n" +
                "$textNodes.Item(0).AppendChild($template.CreateTextNode($env:" + ENV_TITLE + ")) > $null\n" +
                "$textNodes.Item(1).AppendChild($template.CreateTextNode($env:" + ENV_MSG + ")) > $null\n" +
                "$toast = [Windows.UI.Notifications.ToastNotification]::new($template)\n" +
                "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('符玄').Show($toast)";

            // 脚本内不含双引号与用户内容 → 手工加双引号包裹参数是安全的
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell",
                "-NoProfile -Command \"" + script + "\"")
            { UseShellExecute = false, CreateNoWindow = true });
        }
        catch
        {
            // 系统通知不可用时（如 Windows 版本不支持），静默忽略
        }
    }
}
