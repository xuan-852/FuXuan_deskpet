using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 符玄「法眼」— 行为活动追踪器
///
/// 用户明确开启后才按前台进程类别统计活动；不读取窗口标题或浏览器标签。
///
/// 分类规则（可扩展）：
///   coding        — 编程/开发工具（VS Code、VS、JetBrains、Unity、终端等）
///   gaming        — 游戏进程
///   studying      — 学习/文档（Markdown、Office、PDF、词典、学习网站等）
///   browsing      — 普通网页浏览
///   entertainment — 视频/音乐（播放器、B站、YouTube 等）
///   communication — 社交/聊天（微信、QQ、Discord 等）
///   idle          — 锁屏/屏保/无人
///   other         — 未分类
/// </summary>
public class ActivityTracker : MonoBehaviour
{
    [Header("配置")]
    [Tooltip("轮询间隔（秒），默认 2 秒")]
    public float pollInterval = 2f;

    // ============================================================
    //  Win32 API
    // ============================================================

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetModuleBaseName(IntPtr hProcess, IntPtr hModule, StringBuilder lpBaseName, uint nSize);

    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    /// <summary>
    /// 用 Win32 API 取进程名，避免 Process.GetProcessById 在进程瞬逝时抛异常
    /// </summary>
    private static string GetProcessNameByPid(uint pid)
    {
        IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero)
            return null;
        try
        {
            StringBuilder sb = new StringBuilder(256);
            uint len = GetModuleBaseName(hProcess, IntPtr.Zero, sb, (uint)sb.Capacity);
            if (len > 0)
            {
                string name = sb.ToString(0, (int)len);
                // 去掉 .exe 后缀，使 Classify 中的 p=="code" 等匹配生效
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - 4);
                return name;
            }
            return null;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    // ============================================================
    //  数据结构
    // ============================================================

    [System.Serializable]
    private class CategoryEntry
    {
        public string category;
        public float seconds;
    }

    [System.Serializable]
    private class DayStats
    {
        public string date;
        public List<CategoryEntry> entries = new List<CategoryEntry>();

        /// <summary>运行时用的字典（非序列化，Save/Load 时与 entries 互转）</summary>
        [System.NonSerialized]
        public Dictionary<string, float> dict = new Dictionary<string, float>();
    }

    /// <summary>单例</summary>
    public static ActivityTracker Instance { get; private set; }

    private DayStats _today = new DayStats();
    private float _pollTimer = 0f;
    private float _saveTimer = 0f;
    private const float SAVE_INTERVAL = 30f;

    // 记录上一次轮询结果（去重用）
    private IntPtr _lastHwnd = IntPtr.Zero;
    private string _lastProcName = "";
    private string _lastCategory = "";
    private bool _trackingWasEnabled;

    public bool TrackingEnabled => PetConfig.Instance != null
        && PetConfig.Instance.data != null
        && PetConfig.Instance.data.activityCategoryTrackingEnabled;

    /// <summary>当前前台进程名（只在用户开启类别统计后更新）</summary>
    public string CurrentProcessName { get; private set; } = "";
    /// <summary>当前活动分类</summary>
    public string CurrentCategory => TrackingEnabled ? _lastCategory : "unknown";
    /// <summary>最近一次检测到用户活动的时间（Time.time），供 MotionAgent 等消费</summary>
    public float LastActivityTime { get; private set; } = float.MinValue;
    // ============================================================
    //  路径
    // ============================================================

    private string FilePath => Path.Combine(DataPathConfig.DataRoot, "activity_log.json");

    // ============================================================
    //  生命周期
    // ============================================================

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        _today.date = DateTime.Now.ToString("yyyy-MM-dd");
        Debug.Log("[ActivityTracker] 前台进程类别统计=" + (TrackingEnabled ? "已开启" : "已关闭"));
    }

    void Update()
    {
        if (!TrackingEnabled)
        {
            if (_trackingWasEnabled) OnTrackingPreferenceChanged();
            return;
        }
        if (!_trackingWasEnabled)
        {
            _trackingWasEnabled = true;
            Load();
        }
        if (_suspended) return; // 睡眠期间暂停轮询
        _pollTimer += Time.deltaTime;
        if (_pollTimer < pollInterval) return;
        _pollTimer = 0f;

        PollForeground();

    }

    public void OnTrackingPreferenceChanged()
    {
        if (TrackingEnabled) return;
        if (_trackingWasEnabled) Save();
        _trackingWasEnabled = false;
        _pollTimer = 0f;
        _saveTimer = 0f;
        _lastHwnd = IntPtr.Zero;
        _lastProcName = "";
        _lastCategory = "";
        CurrentProcessName = "";
        LastActivityTime = float.MinValue;
    }

    private bool _suspended = false;

    void OnApplicationPause(bool pauseStatus)
    {
        _suspended = pauseStatus;
        if (pauseStatus)
        {
            Debug.Log("[ActivityTracker] ⏸ 睡眠挂起，存档当前数据");
            if (TrackingEnabled) Save();
        }
        else
        {
            Debug.Log("[ActivityTracker] ▶ 唤醒恢复，继续轮询");
        }
    }

    void OnApplicationQuit()
    {
        if (TrackingEnabled) Save();
    }

    // ============================================================
    //  轮询
    // ============================================================

    private int _pollCount = 0;

    private void PollForeground()
    {
        _pollCount++;
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            if (_pollCount % 30 == 0)
                Debug.Log($"[ActivityTracker] 轮询#{_pollCount}: 前台窗口=null");
            // 也算 idle 时长，不 return
            _lastCategory = "idle";
            AccumulateAndSave();
            return;
        }

        // 获取进程名（用 Win32 API 避免 Process.GetProcessById 进程瞬逝异常）
        GetWindowThreadProcessId(hwnd, out uint pid);
        string procName = GetProcessNameByPid(pid);
        if (procName == null)
        {
            if (_pollCount % 30 == 0)
                Debug.Log($"[ActivityTracker] 轮询#{_pollCount}: OpenProcess({pid}) 失败，使用\"unknown\"继续");
            procName = "unknown";
        }

        // 窗口切换时更新记录
        if (hwnd != _lastHwnd || procName != _lastProcName)
        {
            _lastHwnd = hwnd;
            _lastProcName = procName;

            // 仅使用进程名分类；窗口标题属于私密内容，不读取。
            CurrentProcessName = procName;
            _lastCategory = Classify(procName);
            // ★ 窗口切换视为用户活动：更新时间戳供 MotionAgent 交互检测使用
            LastActivityTime = Time.time;
            if (_pollCount <= 5 || _pollCount % 30 == 0)
                Debug.Log($"[ActivityTracker] 轮询#{_pollCount}: 类别={_lastCategory}");
        }

        // ★ 每次轮询都累加当前窗口时长（不是只在切换时）
        AccumulateAndSave();

        // 首次诊断: 第 3 次轮询打印累计
        if (_pollCount == 3)
        {
            float total = SumDict(_today.dict);
            Debug.Log($"[ActivityTracker] 诊断: pollCount={_pollCount}, dict.Count={_today.dict.Count}, total={total:F0}s, categories={string.Join(",", _today.dict.Keys)}");
        }
    }

    /// <summary>累加当前类别的时长 + 按需保存</summary>
    private void AccumulateAndSave()
    {
        if (string.IsNullOrEmpty(_lastCategory))
            _lastCategory = "other";
        if (!_today.dict.ContainsKey(_lastCategory))
            _today.dict[_lastCategory] = 0f;
        _today.dict[_lastCategory] += pollInterval;

        // 定期保存
        _saveTimer += pollInterval;
        if (_saveTimer >= SAVE_INTERVAL)
        {
            _saveTimer = 0f;
            Debug.Log($"[ActivityTracker] 触发保存: {_today.dict.Count} categories, total={SumDict(_today.dict):F0}s");
            Save();
        }
    }

    // ============================================================
    //  活动分类
    // ============================================================

    private string Classify(string procName)
    {
        string p = procName.ToLowerInvariant();

        // ——— 屏保/锁屏/登录屏 ———
        if (p == "logonscreen" || p.Contains("screensaver") || p.StartsWith("scr") ||
            p.Contains("lockapp"))
            return "idle";

        // ——— 开发工具 ———
        // 主流编辑器/IDE
        if (p == "code" || p == "devenv" || p == "cursor" || p == "rider" ||
            p == "idea" || p == "clion" || p == "pycharm" || p == "webstorm" ||
            p == "phpstorm" || p == "goland" || p == "rubymine" ||
            p.Contains("jetbrains") || p.Contains("intellij"))
            return "coding";
        // Unity / 团结引擎编辑器
        if (p == "unity" || p == "tuanjie" || p.Contains("unityeditor"))
            return "coding";
        // 终端
        if (p == "pwsh" || p == "powershell" || p == "cmd" ||
            p == "windowsterminal" || p == "wt" || p == "terminal" ||
            p.Contains("conhost") || p == "bash" || p == "wsl")
            return "coding";
        // Git / 代码工具
        if (p.Contains("git") && !p.Contains("game"))
            return "coding";
        // 数据库工具
        if (p.Contains("dbeaver") || p.Contains("navicat") || p.Contains("datagrip") ||
            p.Contains("mysql") || p.Contains("postgres") || p.Contains("redis"))
            return "coding";

        // ——— 浏览器 ———
        if (p == "msedge" || p == "chrome" || p == "firefox" || p == "opera" ||
            p == "brave" || p == "vivaldi" || p == "safari")
            return "browsing";

        // ——— 学习/文档工具 ———
        if (p.Contains("onenote") || p == "notion" || p == "obsidian" ||
            p.Contains("markdown") || p == "typora" || p.Contains("logseq"))
            return "studying";
        if (p.Contains("winword") || p.Contains("word") ||
            p.Contains("powerpnt") || p.Contains("ppt") ||
            p.Contains("excel") || p.Contains("et") || p.Contains("wps"))
            return "studying";
        if (p.Contains("acrobat") || p.Contains("foxit") || p.Contains("pdf") ||
            p == "sumatra" || p.Contains("ebook") || p.Contains("kindle"))
            return "studying";
        if (p.Contains("eudic") || p.Contains("dict") || p.Contains("translate"))
            return "studying";
        if (p.Contains("anki") || p.Contains("quiz") || p.Contains("coursera"))
            return "studying";

        // ——— 娱乐（音视频播放器） ———
        if (p.Contains("potplayer") || p.Contains("vlc") || p.Contains("wmplayer") ||
            p.Contains("mpv") || p.Contains("kmplayer"))
            return "entertainment";
        if (p.Contains("cloudmusic") || p.Contains("netease") || p.Contains("music") ||
            p.Contains("spotify") || p.Contains("pandora"))
            return "entertainment";

        // ——— 社交 ———
        if (p.Contains("wechat") || p.Contains("qq") || p.Contains("discord") ||
            p.Contains("telegram") || p.Contains("slack") || p.Contains("dingtalk") ||
            p.Contains("lark") || p.Contains("feishu"))
            return "communication";

        // ——— 游戏进程 ———
        // 已知游戏进程名
        string[] gameProcs = { "game", "launcher", "steam", "epic", "origin", "ubisoft",
                               "wow", "lol", "dota", "csgo", "cs2", "valheim", "genshin",
                               "starrail", "honkai", "wuthering", "eldenring", "cyberpunk",
                               "gta", "reddead", "minecraft", "terraria", "factorio" };
        foreach (string gp in gameProcs)
        {
            if (p.Contains(gp))
                return "gaming";
        }
        // ——— 未分类 ———
        return "other";
    }

    // ============================================================
    //  辅助
    // ============================================================

    private static float SumDict(Dictionary<string, float> d)
    {
        float s = 0f;
        foreach (var kv in d) s += kv.Value;
        return s;
    }

    // ============================================================
    //  公开接口
    // ============================================================

    /// <summary>
    /// 获取已授权类别统计的今日摘要；不进入聊天 system prompt。
    /// 返回格式: 【法眼观测】🖥️ 编程2h30min 🌐 浏览45min 🎬 娱乐20min
    /// 若今日无数据或全是 <1min，返回空字符串
    /// </summary>
    public string GetSummary()
    {
        if (!TrackingEnabled) return "";
        // 日期切换保护
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        if (_today.date != today)
        {
            Save();                    // 存昨天数据
            _today = new DayStats { date = today };
            Load();                    // 尝试加载今天已有数据
        }

        if (_today.dict.Count == 0)
            return "";

        // 按时长降序排列
        var sorted = new List<KeyValuePair<string, float>>(_today.dict);
        sorted.Sort((a, b) => b.Value.CompareTo(a.Value));

        var sb = new StringBuilder("【法眼观测 | 今日行为】");
        bool hasData = false;

        foreach (var kv in sorted)
        {
            if (kv.Value < 60f) continue; // <1 分钟不展示
            hasData = true;

            string label = kv.Key switch
            {
                "coding"        => "🖥️ 编程",
                "gaming"        => "🎮 游戏",
                "studying"      => "📖 学习",
                "browsing"      => "🌐 浏览",
                "entertainment" => "🎬 娱乐",
                "communication" => "💬 社交",
                "idle"          => "💤 离开",
                _               => "📋 其他"
            };

            int totalSec = Mathf.RoundToInt(kv.Value);
            int minutes = totalSec / 60;
            int hours = minutes / 60;
            minutes %= 60;

            if (hours > 0)
                sb.Append($" {label}{hours}h{minutes}min");
            else
                sb.Append($" {label}{minutes}min");
        }

        if (!hasData) return "";

        return sb.ToString();
    }

    /// <summary>
    /// 获取指定类别今日累计分钟数
    /// </summary>
    public int GetCategoryMinutes(string category)
    {
        if (!TrackingEnabled) return 0;
        if (_today.dict.TryGetValue(category, out float sec))
            return Mathf.RoundToInt(sec / 60f);
        return 0;
    }

    // ============================================================ //
    //  持久化
    // ============================================================

    [System.Serializable]
    private class SaveData
    {
        public List<DayStats> days = new List<DayStats>();
    }

    private void Save()
    {
        if (ChatManager.IsTestMode) return; // ★ 测试模式不落盘：活动日志无污染（2026-08-15）
        try
        {
            Debug.Log($"[ActivityTracker] Save() 开始, dict.Count={_today.dict.Count}, 保存路径={FilePath}");

            // 先把运行时 dict → 序列化的 entries
            _today.entries.Clear();
            foreach (var kv in _today.dict)
                _today.entries.Add(new CategoryEntry { category = kv.Key, seconds = kv.Value });

            SaveData data;
            if (File.Exists(FilePath))
            {
                string rawJson = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(rawJson))
                    data = new SaveData();
                else
                    data = JsonUtility.FromJson<SaveData>(rawJson) ?? new SaveData();
            }
            else
                data = new SaveData();

            // 更新或追加今天的数据
            for (int i = 0; i < data.days.Count; i++)
            {
                if (data.days[i].date == _today.date)
                {
                    data.days[i] = _today;
                    File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
                    Debug.Log($"[ActivityTracker] Save() 完成 (更新), 文件大小={new System.IO.FileInfo(FilePath).Length}");
                    return;
                }
            }
            data.days.Add(_today);
            // 只保留最近 30 天
            if (data.days.Count > 30)
                data.days.RemoveRange(0, data.days.Count - 30);

            File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            Debug.Log($"[ActivityTracker] Save() 完成 (追加), days={data.days.Count}, 文件大小={new System.IO.FileInfo(FilePath).Length}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ActivityTracker] 保存失败: {e.Message}");
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            string json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json)) return;
            var data = JsonUtility.FromJson<SaveData>(json);
            if (data == null) return;

            // 找到今天的记录
            foreach (var day in data.days)
            {
                if (day.date == _today.date)
                {
                    _today = day;
                    // 反序列化 entries → 运行时 dict
                    _today.dict.Clear();
                    foreach (var e in _today.entries)
                        _today.dict[e.category] = e.seconds;
                    Debug.Log($"[ActivityTracker] ✅ 加载今日行为数据，{_today.dict.Count} 类活动");
                    return;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ActivityTracker] 载入失败: {e.Message}");
        }
    }
}
