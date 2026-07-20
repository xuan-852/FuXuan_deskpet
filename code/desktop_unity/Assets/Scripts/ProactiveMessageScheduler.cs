using UnityEngine;

/// <summary>
/// 主动关心调度器 — 符玄的「体贴天机」
///
/// 零 LLM 成本：基于 ActivityTracker 的本地规则引擎，在合适时机弹出关心气泡。
/// 无需任何 API 调用，仅使用 ChatBubble.ShowMessage() 本地渲染。
///
/// 触发规则（全部可配）：
/// - longCodingMin:   累计编程超过阈值 → 提醒休息
/// - lateNightHour:   过了该时间且仍在活动 → 提醒睡觉
/// - mealTime:        饭点检测（午餐/晚餐）
/// - idleReturn:      离开后回来 → 打招呼
/// - longGamingMin:   累计游戏超时 → 护眼提醒
///
/// 每条消息独立冷却，不重复刷屏。
/// </summary>
public class ProactiveMessageScheduler : MonoBehaviour
{
    [Header("⏰ 触发开关")]
    public bool enableLongCodingReminder = true;
    public bool enableLateNightReminder = true;
    public bool enableMealTimeReminder = true;
    public bool enableIdleReturnGreeting = true;
    public bool enableLongGamingReminder = true;

    [Header("⏱ 阈值（分钟）")]
    [Tooltip("累计编程超过此分钟数时提醒休息")]
    public float longCodingMin = 90f;

    [Tooltip("累计游戏超过此分钟数时提醒护眼")]
    public float longGamingMin = 120f;

    [Tooltip(">= 此小时（24h）视为深夜")]
    public int lateNightHour = 23;

    [Header("🍚 饭点（小时，24h）")]
    public int lunchHour = 12;
    public int dinnerHour = 18;

    [Header("🧊 冷却（分钟）")]
    [Tooltip("同类别消息的最小间隔")]
    public float cooldownMinutes = 60f;

    [Header("🫧 气泡设置")]
    public float bubbleDuration = 6f;

    // ——— 内部状态 ———
    private ChatBubble _bubble;
    private float _lastCodingTime = -999f;
    private float _lastNightTime = -999f;
    private float _lastMealTime = -999f;
    private float _lastGamingTime = -999f;
    private float _lastIdleReturnTime = -999f;

    // idle 状态跟踪
    private bool _wasIdle = false;
    private bool _firstCheckDone = false; // 跳过第一次避免启动时误触发

    private float _checkTimer = 0f;
    private const float CHECK_INTERVAL = 30f; // 每 30 秒检查一次

    void Start()
    {
        _bubble = GetComponent<ChatBubble>();
        if (_bubble == null)
            _bubble = gameObject.AddComponent<ChatBubble>();
        Debug.Log("[ProactiveScheduler] 🌸 体贴天机已启动");
    }

    void Update()
    {
        if (ActivityTracker.Instance == null) return;

        _checkTimer += Time.deltaTime;
        if (_checkTimer < CHECK_INTERVAL) return;
        _checkTimer = 0f;

        // 跳过首次检查，给 ActivityTracker 足够的轮询时间
        if (!_firstCheckDone)
        {
            _firstCheckDone = true;
            _wasIdle = ActivityTracker.Instance.CurrentCategory == "idle";
            return;
        }

        string category = ActivityTracker.Instance.CurrentCategory;
        int codingMin = ActivityTracker.Instance.GetCategoryMinutes("coding");
        int gamingMin = ActivityTracker.Instance.GetCategoryMinutes("gaming");
        int hour = System.DateTime.Now.Hour;
        float now = Time.time;
        float cd = cooldownMinutes * 60f;

        // ——— 1. 长时间编程提醒 ———
        if (enableLongCodingReminder && codingMin >= longCodingMin && now - _lastCodingTime > cd)
        {
            _lastCodingTime = now;
            string[] msgs = {
                "高强度编了一上午了，起来活动一下吧～",
                "代码不会自己跑掉的，先休息会儿 💫",
                "符玄大人掐指一算，你该起来走走了 ✨"
            };
            ShowMsg(msgs[Random.Range(0, msgs.Length)]);
            return;
        }

        // ——— 2. 深夜提醒 ———
        if (enableLateNightReminder && hour >= lateNightHour && category != "idle" && now - _lastNightTime > cd)
        {
            _lastNightTime = now;
            string[] msgs = {
                "夜深了，该休息了，别累坏了身子 🌙",
                "丑时将至，符玄大人劝你早些歇息 🏮",
                "代码是改不完的，身体要紧，去睡吧～"
            };
            ShowMsg(msgs[Random.Range(0, msgs.Length)]);
            return;
        }

        // ——— 3. 饭点提醒（分钟 < 2 避免反复触发） ———
        if (enableMealTimeReminder && (hour == lunchHour || hour == dinnerHour))
        {
            int min = System.DateTime.Now.Minute;
            if (min < 2 && now - _lastMealTime > cd)
            {
                _lastMealTime = now;
                string[] msgs = {
                    "到饭点了，快去吃饭吧 🍚",
                    "人是铁饭是钢，符玄大人准你用膳了 🥢",
                    "别饿着了，先吃饭再继续吧～"
                };
                ShowMsg(msgs[Random.Range(0, msgs.Length)]);
                return;
            }
        }

        // ——— 4. 长时间游戏提醒 ———
        if (enableLongGamingReminder && gamingMin >= longGamingMin && now - _lastGamingTime > cd)
        {
            _lastGamingTime = now;
            string[] msgs = {
                "玩了好久了，歇会儿眼睛吧 👀",
                "适度游戏益脑，过度游戏伤身～",
                "符玄大人提醒你：看看远方，放松一下 🏔️"
            };
            ShowMsg(msgs[Random.Range(0, msgs.Length)]);
            return;
        }

        // ——— 5. idle return 打招呼 ———
        if (enableIdleReturnGreeting)
        {
            if (_wasIdle && category != "idle" && !string.IsNullOrEmpty(category))
            {
                if (now - _lastIdleReturnTime > cd)
                {
                    _lastIdleReturnTime = now;
                    string[] msgs = {
                        "欢迎回来～",
                        "回来啦？本座等你好久了 😊",
                        "你回来啦，要继续忙了吗？"
                    };
                    ShowMsg(msgs[Random.Range(0, msgs.Length)]);
                }
            }
            _wasIdle = category == "idle";
        }
    }

    private void ShowMsg(string msg)
    {
        if (_bubble != null)
            _bubble.ShowMessage("🌸 " + msg, bubbleDuration, ChatBubble.MsgPriority.Low);
    }
}
