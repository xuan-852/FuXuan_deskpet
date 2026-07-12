using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 服务端轮询服务 — 符玄的「卜算传讯」
///
/// 功能：
/// - 定时轮询本地课表服务（D:\C\小程序\server）的推送消息
/// - 接收考试提醒、成绩更新等消息
/// - 自动将考试提醒加入 ReminderManager 待办
/// - 通过头顶气泡 + Windows 通知即时告知用户
///
/// 配置：
///   在 Unity Inspector 中设置 serverUrl 和 desktopToken
/// </summary>
public class ServerPollService : MonoBehaviour
{
    [Header("服务端配置")]
    [Tooltip("本地课表服务地址")]
    public string serverUrl = "http://localhost:3000";

    [Tooltip("桌面端 API Token（需与 .env 中的 DESKTOP_TOKEN 一致）")]
    public string desktopToken = "desktop_secret_token_here";

    [Header("轮询设置")]
    [Tooltip("轮询间隔（秒）")]
    public float pollInterval = 30f;

    [Header("考试提前提醒天数")]
    [Tooltip("考试前多少天开始每天提醒复习")]
    public int examRemindDays = 3;

    private HttpClient _http;
    private string _lastId = "";
    private float _timer = 0f;
    private bool _initialized = false;

    // 引用
    private ReminderManager _reminder;
    private ChatBubble _bubble;

    // 已处理的考试 ID 集合（避免重复添加待办）
    private HashSet<string> _processedExamIds = new HashSet<string>();

    // ================================================================
    //  生命周期
    // ================================================================

    /// <summary>
    /// 在 Awake 中初始化 HttpClient，确保 AddComponent 后立刻可用。
    /// 因为 ToolCallInvoker 可能在 Start() 之前就通过 QueryScoresAsync() 等
    /// 方法访问 _http，放在 Start() 里会导致 NullReferenceException。
    /// </summary>
    private void Awake()
    {
        _http = new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(5);
        if (!string.IsNullOrEmpty(desktopToken))
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {desktopToken}");
    }

    private void Start()
    {
        _reminder = GetComponent<ReminderManager>();
        if (_reminder == null) _reminder = FindObjectOfType<ReminderManager>();
        _bubble = GetComponent<ChatBubble>();
        if (_bubble == null) _bubble = FindObjectOfType<ChatBubble>();

        // 启动后马上拉一次
        _timer = pollInterval;
        _initialized = true;

        Debug.Log("[ServerPollService] 初始化完成，每 " + pollInterval + "s 轮询 " + serverUrl);
    }

    private void Update()
    {
        if (!_initialized) return;

        _timer += Time.deltaTime;
        if (_timer >= pollInterval)
        {
            _timer = 0f;
            PollMessages();
        }
    }

    private void OnDestroy()
    {
        _http?.Dispose();
    }

    // ================================================================
    //  轮询逻辑
    // ================================================================

    /// <summary>轮询推送消息队列</summary>
    private async void PollMessages()
    {
        try
        {
            string url = $"{serverUrl}/api/pet/poll?lastId={Uri.EscapeDataString(_lastId)}";
            var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                // 服务器未运行时不报错
                return;
            }

            string json = await response.Content.ReadAsStringAsync();
            var result = JsonUtility.FromJson<PollResponse>(json);

            if (result == null || result.data == null) return;

            if (result.data.Length > 0)
            {
                _lastId = result.lastId;

                foreach (var msg in result.data)
                {
                    HandleMessage(msg);
                }
            }
        }
        catch (HttpRequestException)
        {
            // 服务器没启动是正常的，静默忽略
        }
        catch (TaskCanceledException)
        {
            // 超时忽略
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ServerPollService] 轮询异常: {e.Message}");
        }
    }

    /// <summary>处理单条推送消息</summary>
    private void HandleMessage(PollMessage msg)
    {
        switch (msg.type)
        {
            case "exam_reminder":
                HandleExamReminder(msg);
                break;

            case "score_update":
                HandleScoreUpdate(msg);
                break;

            case "schedule_reminder":
                HandleScheduleReminder(msg);
                break;

            default:
                // 未知类型，直接显示气泡
                ShowBubble(msg.title + "\n" + msg.body);
                break;
        }
    }

    /// <summary>处理考试提醒：显示气泡 + 加入待办</summary>
    private void HandleExamReminder(PollMessage msg)
    {
        // 解析 payload
        ExamPayload payload = null;
        if (!string.IsNullOrEmpty(msg.payload))
        {
            try
            {
                payload = JsonUtility.FromJson<ExamPayload>(msg.payload);
            }
            catch
            {
                // payload 非 JSON 格式或无有效载荷，视为纯文本消息
            }
        }

        string examKey = payload?.examKey ?? msg.title;
        string courseName = payload?.courseName ?? msg.title;

        // 1. 显示角色气泡
        ShowBubble(msg.body);

        // 2. 如果是今明两天考试的提醒，加入 ReminderManager 待办
        if (!_processedExamIds.Contains(examKey))
        {
            _processedExamIds.Add(examKey);

            if (_reminder != null)
            {
                // 从消息中提取考试时间
                DateTime examTime = DateTime.Now.AddDays(1); // fallback 明天
                if (payload != null && !string.IsNullOrEmpty(payload.examDate))
                {
                    string dateStr = payload.examDate;
                    string timeStr = payload.startTime ?? "08:00";
                    DateTime.TryParse($"{dateStr} {timeStr}", out examTime);
                }

                // ═══ 去重：检查是否已有同课程名的待办提醒 ═══
                // 场景：用户可能在考试安排出来前就让 AI 设了提醒（如「提醒我高数考试」），
                // 服务器推送 exam_reminder 时再用同一课程名加提醒就会重复。
                // 规则：若有同课程名的未完成提醒，则跳过服务器推送的版本，保留用户原始设置。
                // 如需刷新时间（如考试日期变更），可考虑先删旧提醒再加新提醒。
                bool hasExistingReminder = _reminder.HasPendingReminderContaining(courseName);
                if (hasExistingReminder)
                {
                    Debug.Log($"[ServerPollService] 去重跳过: 已有包含「{courseName}」的待办提醒，不重复添加");
                }
                else
                {
                    // 添加多条复习提醒：examRemindDays 天前开始每天一条
                    for (int i = examRemindDays; i > 0; i--)
                    {
                        DateTime remindAt = examTime.Date.AddDays(-i).AddHours(19); // 每晚 19:00 提醒复习
                        if (remindAt <= DateTime.Now) continue; // 已过的时间不添加

                        string dayTag = i == 1 ? "明天" : $"{i}天后";
                        _reminder.AddReminder(
                            $"📖 复习「{courseName}」（{dayTag}考试）",
                            remindAt,
                            source: "server",
                            priority: "high"
                        );
                    }

                    // 考试当天提前 2 小时提醒
                    DateTime examDayRemind = examTime.AddHours(-2);
                    if (examDayRemind > DateTime.Now)
                    {
                        _reminder.AddReminder(
                            $"📝 「{courseName}」考试倒计时 2 小时！",
                            examDayRemind,
                            source: "server",
                            priority: "high"
                        );
                    }

                    Debug.Log($"[ServerPollService] 已添加复习提醒: {courseName} ({examTime:yyyy-MM-dd})");
                }
            }
        }
    }

    /// <summary>处理成绩更新</summary>
    private void HandleScoreUpdate(PollMessage msg)
    {
        // 直接显示气泡
        ShowBubble($"📊 {msg.title}\n{msg.body}");
    }

    /// <summary>处理课表提醒</summary>
    private void HandleScheduleReminder(PollMessage msg)
    {
        // 早上 6 点的课表推送，加入待办
        string[] lines = (msg.body ?? "").Split('\n');
        if (lines.Length > 0 && _reminder != null)
        {
            // 第一节课的时间
            if (lines[0].Length > 5)
            {
                string firstCourse = lines[0];
                _reminder.AddReminder(
                    $"📚 要上课了：{firstCourse}",
                    DateTime.Now.Date.AddHours(7).AddMinutes(30), // 早 7:30
                    source: "server",
                    priority: "normal"
                );
            }
        }

        ShowBubble($"🌅 {msg.title}\n{msg.body}");
    }

    /// <summary>显示角色头顶气泡</summary>
    private void ShowBubble(string message)
    {
        if (_bubble != null)
        {
            _bubble.ShowMessage(message, 8f, ChatBubble.MsgPriority.Normal);
        }
    }

    // ================================================================
    //  公开方法（供 AI Tool Calling 使用）
    // ================================================================

    /// <summary>从服务端查询即将到来的考试（供 AI Tool Calling 使用）</summary>
    public async Task<string> QueryUpcomingExamsAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{serverUrl}/api/pet/exams");
            if (!response.IsSuccessStatusCode) return "查询考试失败，服务端未响应";

            string json = await response.Content.ReadAsStringAsync();
            var result = JsonUtility.FromJson<ExamsResponse>(json);
            if (result?.data?.exams == null || result.data.exams.Length == 0)
                return "目前没有考试安排";

            var now = DateTime.Now;
            var lines = new List<string>();
            foreach (var exam in result.data.exams)
            {
                if (DateTime.TryParse(exam.exam_date, out var examDate))
                {
                    var diff = (examDate - now.Date).Days;
                    string prefix = diff < 0 ? "⚠️ 已过期" :
                                    diff == 0 ? "🔥 今天" :
                                    diff == 1 ? "🔔 明天" :
                                    diff <= 3 ? $"⏰ {diff}天后" :
                                    $"📅 {diff}天后";
                    lines.Add($"  {prefix} {exam.course_name} | {exam.exam_date} {exam.start_time}-{exam.end_time} @{exam.location}");
                }
            }

            return $"📝 考试安排（共 {result.data.exams.Length} 门）：\n" + string.Join("\n", lines);
        }
        catch (Exception e)
        {
            return $"查询考试出错: {e.Message}";
        }
    }

    /// <summary>查询学业成绩（供 AI Tool Calling 使用）</summary>
    public async Task<string> QueryScoresAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{serverUrl}/api/pet/scores");
            if (!response.IsSuccessStatusCode) return "查询成绩失败，服务端未响应";

            string json = await response.Content.ReadAsStringAsync();
            var result = JsonUtility.FromJson<ScoresResponse>(json);
            if (result?.data?.scores == null || result.data.scores.Length == 0)
                return "目前还没有录入成绩";

            var lines = new List<string>();
            string currentSem = "";
            foreach (var s in result.data.scores)
            {
                if (s.semester != currentSem)
                {
                    currentSem = s.semester;
                    lines.Add($"\n📚 【{currentSem}】");
                }
                string scoreStr = string.IsNullOrEmpty(s.score) ? "暂无" : s.score;
                string attr = string.IsNullOrEmpty(s.attribute) ? "" : $" ({s.attribute})";
                lines.Add($"  · {s.course_name}{attr}: {scoreStr} 分  | 学分 {s.credit}");
            }

            return $"📊 我的成绩单（共 {result.data.scores.Length} 门课程）：\n" + string.Join("\n", lines);
        }
        catch (Exception e)
        {
            return $"查询成绩出错: {e.Message}";
        }
    }

    /// <summary>查询课表（供 AI Tool Calling 使用）</summary>
    public async Task<string> QueryScheduleAsync(int week = 0)
    {
        try
        {
            string url = $"{serverUrl}/api/pet/schedule";
            if (week > 0) url += $"?week={week}";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return "查询课表失败，服务端未响应";

            string json = await response.Content.ReadAsStringAsync();
            var result = JsonUtility.FromJson<ScheduleResponse>(json);
            if (result?.data?.courses == null || result.data.courses.Length == 0)
                return week > 0 ? $"第 {week} 周没有课程安排" : "目前没有课程安排";

            string[] dayNames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
            var lines = new List<string>();

            if (week > 0)
            {
                lines.Add($"📅 第 {week} 周课表（共 {result.data.maxWeek} 周）：");
            }
            else
            {
                lines.Add($"📅 全部课表（共 {result.data.maxWeek} 周）：");
            }

            string currentDay = "";
            foreach (var c in result.data.courses)
            {
                string dayStr = c.day >= 0 && c.day < dayNames.Length ? dayNames[c.day] : $"周{c.day}";
                if (dayStr != currentDay)
                {
                    currentDay = dayStr;
                    if (week > 0) lines.Add($"\n  【{dayStr}】");
                }
                string weekStr = week > 0 ? "" : $"第{c.week}周 ";
                lines.Add($"    {weekStr}{c.name}  |  {c.start_slot}-{c.end_slot}节  {c.teacher}  {c.location}");
            }

            if (week > 0)
                return string.Join("\n", lines);
            else
                return string.Join("\n", lines) + $"\n\n💡 可对我说「看第X周的课表」查看具体某周";
        }
        catch (Exception e)
        {
            return $"查询课表出错: {e.Message}";
        }
    }

    /// <summary>查询用户绑定状态和学业概览（供 AI Tool Calling 使用）</summary>
    public async Task<string> QueryUserStatusAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{serverUrl}/api/pet/user/status");
            if (!response.IsSuccessStatusCode) return "查询用户信息失败，服务端未响应";

            string json = await response.Content.ReadAsStringAsync();
            var result = JsonUtility.FromJson<UserStatusResponse>(json);

            if (result?.data == null) return "查询用户信息失败";

            if (!result.data.bound)
                return "📭 尚未绑定教务账号。请在小程序中绑定你的学号密码，本座才能查看你的学业数据哦~";

            var d = result.data;
            var lines = new List<string>
            {
                $"👤 学号: {d.username}",
                $"📖 当前学期: {d.semester ?? "未知"}",
                $"🕐 最近登录: {d.lastLoginAt ?? "未知"}",
                "",
                "📊 学业概况：",
                $"  · 成绩: {d.stats?.scoresCount ?? 0} 门课程已出分",
                $"  · 考试: {d.stats?.examsCount ?? 0} 门考试已安排",
                $"  · 课表: {d.stats?.scheduleWeeks ?? 0} 周课程"
            };

            return string.Join("\n", lines);

        }
        catch (Exception e)
        {
            return $"查询用户信息出错: {e.Message}";
        }
    }

    // ================================================================
    //  JSON 模型
    // ================================================================

    [System.Serializable]
    private class PollResponse
    {
        public string status;
        public PollMessage[] data;
        public string lastId;
    }

    [System.Serializable]
    private class PollMessage
    {
        public string id;
        public string type;
        public string title;
        public string body;
        public string payload;
        public string created_at;
    }

    [System.Serializable]
    private class ExamPayload
    {
        public string examKey;
        public string courseName;
        public string examDate;
        public string startTime;
        public string endTime;
        public string location;
        public string seatNo;
    }

    [System.Serializable]
    private class ExamsResponse
    {
        public string status;
        public ExamsData data;
    }

    [System.Serializable]
    private class ExamsData
    {
        public ExamItem[] exams;
    }

    [System.Serializable]
    public class ExamItem
    {
        public string course_name;
        public string exam_date;
        public string start_time;
        public string end_time;
        public string location;
        public string seat_no;
    }

    // ============ 成绩响应模型 ============

    [System.Serializable]
    private class ScoresResponse
    {
        public string status;
        public ScoresData data;
    }

    [System.Serializable]
    private class ScoresData
    {
        public ScoreItem[] scores;
    }

    [System.Serializable]
    public class ScoreItem
    {
        public string semester;
        public string course_code;
        public string course_name;
        public string score;
        public float credit;
        public int hours;
        public string exam_type;
        public string attribute;
        public string nature;
    }

    // ============ 课表响应模型 ============

    [System.Serializable]
    private class ScheduleResponse
    {
        public string status;
        public ScheduleData data;
    }

    [System.Serializable]
    private class ScheduleData
    {
        public string week;
        public int maxWeek;
        public CourseItem[] courses;
    }

    [System.Serializable]
    public class CourseItem
    {
        public int week;
        public int day;
        public string name;
        public string teacher;
        public string location;
        public int start_slot;
        public int end_slot;
    }

    // ============ 用户状态响应模型 ============

    [System.Serializable]
    private class UserStatusResponse
    {
        public string status;
        public UserStatusData data;
    }

    [System.Serializable]
    private class UserStatusData
    {
        public bool bound;
        public string username;
        public string semester;
        public string lastLoginAt;
        public UserStats stats;
    }

    [System.Serializable]
    private class UserStats
    {
        public int scoresCount;
        public int examsCount;
        public int scheduleWeeks;
    }
}
