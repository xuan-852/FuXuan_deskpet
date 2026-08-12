using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// 符玄「太卜手札」— 任务执行轨迹库（P5.2）
///
/// 设计目标：
///   ▸ 把每次 openclaw_task 外包任务的执行轨迹（成功/失败/结果摘要/耗时）持久化
///   ▸ 后续同类任务提交前检索相似轨迹，把「成功经验 / 失败教训」附加到任务描述，
///     让 OpenClaw 直接参考（机制类似 MotionMemory 对动作的闭环学习）
///   ▸ 数据文件 task_trajectories.json（DataPathConfig.DataRoot 下）
///   ▸ 上限 maxEntries 条，超出淘汰最旧/最少参考条目
///
/// 条目字段：
///   taskKey         —— 任务关键词指纹（bigram 集合的紧凑串，用于相似检索）
///   taskDescription —— 任务描述（截断存储）
///   mode            —— 执行模式 agent / browser
///   success         —— 是否成功
///   resultSummary   —— 成功结果摘要（截断）
///   error           —— 失败原因（截断）
///   durationSeconds —— 耗时（秒）
///   timestamp       —— 记录时间 yyyy-MM-dd HH:mm
///   referenceCount  —— 被检索引用的次数（淘汰时优先保留高引用）
/// </summary>
[Serializable]
public class TaskTrajectoryEntry
{
    /// <summary>任务关键词指纹（bigram 集合排序拼接，用于相似检索）</summary>
    public string taskKey = "";

    /// <summary>任务描述（截断存储）</summary>
    public string taskDescription = "";

    /// <summary>执行模式 agent / browser</summary>
    public string mode = "agent";

    /// <summary>是否成功</summary>
    public bool success = false;

    /// <summary>成功结果摘要（截断）</summary>
    public string resultSummary = "";

    /// <summary>失败原因（截断）</summary>
    public string error = "";

    /// <summary>耗时（秒）</summary>
    public int durationSeconds = 0;

    /// <summary>记录时间 yyyy-MM-dd HH:mm</summary>
    public string timestamp = "";

    /// <summary>被检索引用的次数</summary>
    public int referenceCount = 0;
}

[Serializable]
public class TaskTrajectoryData
{
    public List<TaskTrajectoryEntry> entries = new List<TaskTrajectoryEntry>();
}

public class TaskTrajectoryManager : MonoBehaviour
{
    [Header("◈ 任务轨迹库配置")]
    [Tooltip("最大保留轨迹条数（超出淘汰最旧/最少参考）")]
    public int maxEntries = 30;

    [Tooltip("相似度阈值（0~1，bigram Jaccard），高于此值视为同类任务")]
    public float similarityThreshold = 0.2f;

    [Tooltip("提交任务前最多附加几条成功轨迹经验")]
    public int maxSuccessRefs = 2;

    [Tooltip("提交任务前最多附加几条失败轨迹教训")]
    public int maxFailureRefs = 1;

    /// <summary>任务描述存储截断长度</summary>
    private const int MAX_DESC_LEN = 200;

    /// <summary>结果摘要存储截断长度</summary>
    private const int MAX_SUMMARY_LEN = 300;

    private TaskTrajectoryData _data = new TaskTrajectoryData();

    public static TaskTrajectoryManager Instance { get; private set; }

    private string FilePath => Path.Combine(DataPathConfig.DataRoot, "task_trajectories.json");

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    // ==================================================================
    //  公开接口 — 记录 / 检索
    // ==================================================================

    /// <summary>
    /// 记录一次任务执行轨迹（openclaw_task 执行完毕后调用）
    /// </summary>
    /// <param name="task">任务描述（原始文本）</param>
    /// <param name="mode">执行模式 agent / browser</param>
    /// <param name="success">是否成功</param>
    /// <param name="resultSummary">成功结果摘要（可为空）</param>
    /// <param name="error">失败原因（可为空）</param>
    /// <param name="durationSeconds">耗时（秒）</param>
    public void RecordTrajectory(string task, string mode, bool success,
        string resultSummary, string error, int durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(task))
            return;

        var entry = new TaskTrajectoryEntry
        {
            taskKey = BuildKey(task),
            taskDescription = Truncate(task.Trim(), MAX_DESC_LEN),
            mode = string.IsNullOrEmpty(mode) ? "agent" : mode,
            success = success,
            resultSummary = Truncate(resultSummary ?? "", MAX_SUMMARY_LEN),
            error = Truncate(error ?? "", MAX_SUMMARY_LEN),
            durationSeconds = Math.Max(0, durationSeconds),
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
        };
        _data.entries.Add(entry);

        EnforceMaxEntries();
        Save();
        Debug.Log($"[TaskTrajectoryManager] 📜 轨迹已记录: {(success ? "✅" : "❌")} {entry.taskDescription}");
    }

    /// <summary>
    /// 检索与任务描述相似的历史轨迹（按 bigram Jaccard 相似度降序）
    /// </summary>
    /// <param name="task">任务描述</param>
    /// <param name="maxResults">最多返回条数</param>
    /// <returns>相似轨迹列表（已按相似度降序）</returns>
    public List<TaskTrajectoryEntry> GetSimilarTrajectories(string task, int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(task) || _data.entries.Count == 0)
            return new List<TaskTrajectoryEntry>();

        var target = BuildKey(task);
        if (string.IsNullOrEmpty(target))
            return new List<TaskTrajectoryEntry>();

        var scored = new List<KeyValuePair<TaskTrajectoryEntry, float>>();
        foreach (var e in _data.entries)
        {
            if (string.IsNullOrEmpty(e.taskKey)) continue;
            float sim = Jaccard(target, e.taskKey);
            if (sim >= similarityThreshold)
                scored.Add(new KeyValuePair<TaskTrajectoryEntry, float>(e, sim));
        }

        return scored
            .OrderByDescending(kv => kv.Value)
            .Take(maxResults)
            .Select(kv => kv.Key)
            .ToList();
    }

    /// <summary>
    /// 生成「参考经验」文本，供 OpenClawTaskTool 提交任务前附加到 task 描述。
    /// 成功轨迹作正面参考，失败轨迹作避坑提示。
    /// </summary>
    /// <param name="task">本次任务描述</param>
    /// <returns>附加文本（无相似轨迹返回空串）</returns>
    public string BuildReferenceText(string task)
    {
        if (string.IsNullOrWhiteSpace(task))
            return "";

        var similar = GetSimilarTrajectories(task, maxSuccessRefs + maxFailureRefs);
        if (similar.Count == 0)
            return "";

        var sb = new StringBuilder();
        int successCount = 0;
        int failureCount = 0;
        foreach (var e in similar)
        {
            if (e.success && successCount < maxSuccessRefs)
            {
                successCount++;
                e.referenceCount++;
                sb.AppendLine($"- [成功经验] 本座曾完成过类似任务：「{e.taskDescription}」");
                if (!string.IsNullOrEmpty(e.resultSummary))
                    sb.AppendLine($"  结果摘要: {e.resultSummary}");
            }
            else if (!e.success && failureCount < maxFailureRefs)
            {
                failureCount++;
                e.referenceCount++;
                sb.AppendLine($"- [失败教训] 本座曾执行类似任务失败：「{e.taskDescription}」");
                if (!string.IsNullOrEmpty(e.error))
                    sb.AppendLine($"  失败原因: {e.error}");
            }
        }

        if (sb.Length == 0)
            return "";

        Save(); // referenceCount 变更落盘
        return sb.ToString();
    }

    /// <summary>
    /// 生成轨迹库摘要，注入 ChatManager SystemPrompt（空库返回空串）
    /// </summary>
    public string FormatForPrompt()
    {
        if (_data.entries.Count == 0) return "";

        var sb = new StringBuilder();
        sb.AppendLine("\n【太卜手札 · 任务轨迹】");
        sb.AppendLine("（本座过往外包任务的成败记录，同类任务可参考）");

        // 最近 5 条按时间倒序
        var recent = _data.entries
            .OrderByDescending(e => e.timestamp)
            .Take(5)
            .ToList();
        foreach (var e in recent)
        {
            string mark = e.success ? "✅" : "❌";
            string detail = e.success
                ? Truncate(e.resultSummary, 60)
                : Truncate(e.error, 60);
            sb.AppendLine($"  {mark} {e.taskDescription}（{e.mode}，{e.timestamp}）");
            if (!string.IsNullOrEmpty(detail))
                sb.AppendLine($"     ↳ {detail}");
        }

        sb.AppendLine("（轨迹仅作参考，勿直接向主人复述其内容。）");
        return sb.ToString();
    }

    /// <summary>轨迹总数</summary>
    public int Count => _data.entries.Count;

    /// <summary>清空全部轨迹（测试用）</summary>
    public void ClearAll()
    {
        _data.entries.Clear();
        Save();
    }

    // ==================================================================
    //  相似度算法 — 字符 bigram Jaccard
    // ==================================================================

    /// <summary>把文本转成 bigram 集合的排序串（指纹）</summary>
    public static string BuildKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var bigrams = new SortedSet<string>(StringComparer.Ordinal);
        string t = text.Trim().ToLowerInvariant();
        // 过滤掉标点/空白，只保留字母数字（中文也保留，因为 char 是 letter）
        var chars = new List<char>();
        foreach (char c in t)
        {
            if (char.IsLetterOrDigit(c))
                chars.Add(c);
            else
                chars.Add(' '); // 标点/空白当作分隔符
        }

        // 以空格为界切词，对每个长度≥2 的词取 bigram
        var words = new string(chars.ToArray()).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var w in words)
        {
            if (w.Length < 2) continue;
            for (int i = 0; i < w.Length - 1; i++)
                bigrams.Add(w.Substring(i, 2));
        }
        return string.Join("|", bigrams);
    }

    /// <summary>计算两个 bigram 指纹的 Jaccard 相似度</summary>
    public static float Jaccard(string keyA, string keyB)
    {
        if (string.IsNullOrEmpty(keyA) || string.IsNullOrEmpty(keyB)) return 0f;
        var setA = new HashSet<string>(keyA.Split('|'), StringComparer.Ordinal);
        var setB = new HashSet<string>(keyB.Split('|'), StringComparer.Ordinal);
        if (setA.Count == 0 || setB.Count == 0) return 0f;

        int inter = setA.Count(x => setB.Contains(x));
        int union = setA.Count + setB.Count - inter;
        return union == 0 ? 0f : (float)inter / union;
    }

    // ==================================================================
    //  内部辅助
    // ==================================================================

    private void EnforceMaxEntries()
    {
        if (_data.entries.Count <= maxEntries) return;
        int excess = _data.entries.Count - maxEntries;
        // 优先淘汰：引用少 → 更旧 → 失败（失败轨迹价值低，保留少量作教训即可）
        var victims = _data.entries
            .OrderBy(e => e.referenceCount)
            .ThenBy(e => e.timestamp)
            .ThenBy(e => e.success) // false 排前 → 失败先淘汰
            .Take(excess)
            .ToList();
        foreach (var v in victims)
            _data.entries.Remove(v);
    }

    private static string Truncate(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s;
        return s.Substring(0, maxLen) + "…";
    }

    // ==================================================================
    //  持久化
    // ==================================================================

    public void Save()
    {
        try
        {
            string json = JsonUtility.ToJson(_data, prettyPrint: true);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[TaskTrajectoryManager] ❌ 保存失败: {e.Message}");
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                Save();
                return;
            }
            string json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json)) { Save(); return; }
            _data = JsonUtility.FromJson<TaskTrajectoryData>(json);
            if (_data == null) _data = new TaskTrajectoryData();
        }
        catch (Exception e)
        {
            Debug.LogError($"[TaskTrajectoryManager] ❌ 载入失败: {e.Message}");
            _data = new TaskTrajectoryData();
        }
    }
}
