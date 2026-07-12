using System;
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
/// - 数据存于 Application.persistentDataPath/reminders.json
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
    }

    [System.Serializable]
    private class ReminderList
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
    [Tooltip("Server酱³ 推送 URL（在 sc3.ft07.com 获取）")]
    public string serverChanUrl = "https://22900.push.ft07.com/send/sctp22900taqpvdfaudumkja8wybmjq8.send";

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
        _filePath = Path.Combine(Application.persistentDataPath, "reminders.json");
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
    }

    private void OnApplicationQuit()
    {
        Save();
    }

    // ================================================================
    //  公开 CRUD
    // ================================================================

    /// <summary>新增一条提醒</summary>
    public Reminder AddReminder(string text, DateTime remindAt,
        string recurring = null, string priority = "normal", string source = "user")
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
            priority = priority
        };
        _data.reminders.Add(r);
        Save();
        OnReminderAdded?.Invoke(r);
        NotifyDataChanged();
        Debug.Log($"[ReminderManager] 新增提醒: [{r.id}] {text} @ {remindAt:yyyy-MM-dd HH:mm}");
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
                lines.Add($"  • [{r.id.Substring(0, 6)}] {timeStr} {r.text}{recurringTag}");
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
        }
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
        if (string.IsNullOrEmpty(serverChanUrl)) return;

        try
        {
            // UnityWebRequest 需要放在协程里，用简单的异步委托即可
            string url = $"{serverChanUrl}?title={UriEscape(title)}&desp={UriEscape(message)}";
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
            string ps = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null
$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)
$textNodes = $template.GetElementsByTagName('text')
$textNodes.Item(0).AppendChild($template.CreateTextNode('{title.Replace("'", "''")}')) > $null
$textNodes.Item(1).AppendChild($template.CreateTextNode('{message.Replace("'", "''")}')) > $null
$toast = [Windows.UI.Notifications.ToastNotification]::new($template)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('符玄').Show($toast)
";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell",
                $"-NoProfile -Command \"{ps.Replace("\"", "\\\"")}\"")
            { UseShellExecute = false, CreateNoWindow = true });
        }
        catch
        {
            // 系统通知不可用时（如 Windows 版本不支持），静默忽略
        }
    }
}
