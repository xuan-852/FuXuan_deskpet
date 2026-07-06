using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// 符玄「演武心经」— 运动记忆管理器（闭环学习的强化/覆盖/淘汰引擎）
///
/// 核心原则：
///   1. 按动作名索引，每个动作只保留一个最佳参数模板
///   2. 高分覆盖低分——GLM-4V 评分 ≥ 当前最佳时，用新参数替换旧参数
///   3. 差的记忆也会记录（尝试次数+1），但不会污染最佳模板
///   4. 全局上限 30 条，超出时自动淘汰最低分/最久远条目
///   5. 独立持久化，不挤占 PetMemory 忆境
/// </summary>
public class MotionMemoryManager : MonoBehaviour
{
    [Header("记忆配置")]
    [Tooltip("最多保留多少种动作的最佳模板")]
    public int maxEntries = 30;

    [Tooltip("低于此分的动作视为「未掌握」，不注入 prompt")]
    public int minInjectionScore = 3;

    [Tooltip("尝试次数超过此值且最高分仍≤2，视为「无望动作」，优先淘汰")]
    public int hopelessAttempts = 5;

    // ==================================================================

    [Serializable]
    public class MotionMemoryEntry
    {
        /// <summary>动作名（如 "开心地挥手"）</summary>
        public string actionName;
        /// <summary>最佳中间帧参数 JSON（按绝对值排序的前 N 个参数）</summary>
        public string bestParamJson;
        /// <summary>最佳评分 1-5</summary>
        public int bestScore;
        /// <summary>最佳的 GLM-4V 评语摘要</summary>
        public string bestReview;
        /// <summary>最后更新的参数 JSON（完整关键帧序列压缩版）</summary>
        public string lastParamSnapshot;
        /// <summary>上次更新时间</summary>
        public string timestamp;
        /// <summary>尝试总次数</summary>
        public int attempts;
        /// <summary>动作持续时长</summary>
        public float totalDuration;
        /// <summary>关键帧数</summary>
        public int keyframeCount;
        /// <summary>历史评分（用于退化检测: 如果近期持续低分则降低注入优先级）</summary>
        public List<int> scoreHistory;
    }

    [Serializable]
    private class StorageData
    {
        public List<MotionMemoryEntry> entries = new List<MotionMemoryEntry>();
    }

    // ==================================================================

    private StorageData _data = new StorageData();
    private string FilePath => Path.Combine(Application.persistentDataPath, "motion_memory.json");

    public static MotionMemoryManager Instance { get; private set; }

    void Awake()
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
    //  写入接口
    // ==================================================================

    /// <summary>记录一次演武的参数快照（MotionTranslator 翻译成功时调用）</summary>
    public void RecordMotion(string actionName, string paramSnapshot, int keyframeCount, float duration)
    {
        var entry = GetOrCreateEntry(actionName);
        entry.lastParamSnapshot = paramSnapshot;
        entry.keyframeCount = keyframeCount;
        entry.totalDuration = duration;
        entry.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        entry.attempts++;
        Save();

        UnityEngine.Debug.Log($"[MotionMemoryManager] 📝 记录演武「{actionName}」(第{entry.attempts}次)");
    }

    /// <summary>更新动作评分（GLM-4V 自评后调用），高分覆盖低分</summary>
    /// <param name="actionName">动作名</param>
    /// <param name="score">GLM-4V 打分 1-5</param>
    /// <param name="review">GLM-4V 评语</param>
    /// <param name="paramSnapshot">本次的参数快照</param>
    /// <returns>true=新记录打破最佳, false=未超越最佳</returns>
    public bool UpdateScore(string actionName, int score, string review, string paramSnapshot)
    {
        var entry = GetOrCreateEntry(actionName);
        score = Mathf.Clamp(score, 1, 5);

        // 记录历史
        if (entry.scoreHistory == null)
            entry.scoreHistory = new List<int>();
        entry.scoreHistory.Add(score);
        // 只保留最近 10 次
        if (entry.scoreHistory.Count > 10)
            entry.scoreHistory.RemoveRange(0, entry.scoreHistory.Count - 10);

        bool isNewBest = score > entry.bestScore;

        if (isNewBest)
        {
            // ★ 高分覆盖低分：用新参数替换最佳模板
            string oldScoreInfo = entry.bestScore > 0 ? $"({entry.bestScore}/5 → {score}/5)" : "";
            entry.bestScore = score;
            entry.bestParamJson = paramSnapshot;
            entry.bestReview = TruncateReview(review);
            entry.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            UnityEngine.Debug.Log($"[MotionMemoryManager] 🏆 打破纪录！「{actionName}」{oldScoreInfo} 新最佳={score}/5");
        }
        else if (score >= 3)
        {
            // 及格但没超越最佳 → 仅更新时间戳（保持活跃）
            entry.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            // 更新 lastParamSnapshot 但保留最佳参数
            entry.lastParamSnapshot = paramSnapshot;
        }
        else
        {
            // 低分（≤2）→ 记录但不动最佳模板
            UnityEngine.Debug.Log($"[MotionMemoryManager] ⚠️ 「{actionName}」得分偏低 ({score}/5)，最佳仍为 {entry.bestScore}/5");
        }

        entry.attempts++;
        Save();

        // 自动淘汰检查
        EnforceMaxEntries();

        return isNewBest;
    }

    // ==================================================================
    //  读取接口
    // ==================================================================

    /// <summary>获取适合注入 system prompt 的运动记忆文本（仅最佳模板）</summary>
    public string GetFormattedMemories()
    {
        var injectable = GetInjectableEntries();
        if (injectable.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        sb.Append("📚 本座从过往演武中习得的经验（供本次演武参考）：\n");

        foreach (var e in injectable)
        {
            string bestParamHint = "";
            if (!string.IsNullOrEmpty(e.bestParamJson))
            {
                bestParamHint = $" [{e.bestParamJson}]";
            }
            string scoreStars = new string('⭐', Mathf.Clamp(e.bestScore, 1, 5));
            sb.Append($"  • 「{e.actionName}」{scoreStars}({e.bestScore}/5) {bestParamHint}\n");
        }

        sb.Append("请参考以上最佳演武参数，在本次生成中有意识地模仿成功经验。");
        return sb.ToString();
    }

    /// <summary>获取内存状态摘要（用于 inspect_motion_memory 工具）</summary>
    public string GetStatistics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("📊 演武心经 — 闭环修为统计");
        sb.AppendLine($"▸ 已掌握动作数: {_data.entries.Count}/{maxEntries}");

        var mastered = _data.entries.Where(e => e.bestScore >= minInjectionScore).ToList();
        var learning = _data.entries.Where(e => e.bestScore > 0 && e.bestScore < minInjectionScore).ToList();
        var untried = _data.entries.Where(e => e.bestScore == 0).ToList();

        sb.AppendLine($"▸ 已精通(≥{minInjectionScore}/5): {mastered.Count}");
        sb.AppendLine($"▸ 待精进(<{minInjectionScore}/5): {learning.Count}");
        sb.AppendLine($"▸ 尚未自评: {untried.Count}");
        sb.AppendLine();

        if (mastered.Count > 0)
        {
            sb.AppendLine("【已精通的动作】");
            foreach (var e in mastered.OrderByDescending(x => x.bestScore))
            {
                string stars = new string('⭐', e.bestScore);
                sb.AppendLine($"  {stars} 「{e.actionName}」({e.bestScore}/5) 尝试{e.attempts}次");
                if (!string.IsNullOrEmpty(e.bestReview))
                    sb.AppendLine($"    ↳ {e.bestReview}");
            }
        }

        if (learning.Count > 0)
        {
            sb.AppendLine("【尚待精进的动作】");
            foreach (var e in learning.OrderByDescending(x => x.bestScore))
            {
                sb.AppendLine($"  🔶 「{e.actionName}」({e.bestScore}/5) 尝试{e.attempts}次");
            }
        }

        // 低分退化检测
        var degrading = _data.entries
            .Where(e => e.scoreHistory != null && e.scoreHistory.Count >= 3
                        && e.scoreHistory.TakeLast(3).Average() <= 2)
            .ToList();
        if (degrading.Count > 0)
        {
            sb.AppendLine("【⚠️ 退步预警 — 最近3次评分持续≤2】");
            foreach (var e in degrading)
                sb.AppendLine($"  🔴 「{e.actionName}」(最佳{e.bestScore}/5) 最近{e.scoreHistory.Count}次评分: [{string.Join(",", e.scoreHistory)}]");
        }

        return sb.ToString();
    }

    // ==================================================================
    //  内部逻辑
    // ==================================================================

    /// <summary>获取适合注入的条目（评分 ≥ minInjectionScore）</summary>
    private List<MotionMemoryEntry> GetInjectableEntries()
    {
        return _data.entries
            .Where(e => e.bestScore >= minInjectionScore && !string.IsNullOrEmpty(e.bestParamJson))
            .OrderByDescending(e => e.bestScore)
            .ThenByDescending(e => e.timestamp)
            .Take(5) // 最多注入 5 条
            .ToList();
    }

    /// <summary>获取或创建指定动作的条目</summary>
    private MotionMemoryEntry GetOrCreateEntry(string actionName)
    {
        var existing = _data.entries.FirstOrDefault(e => e.actionName == actionName);
        if (existing != null) return existing;

        var newEntry = new MotionMemoryEntry
        {
            actionName = actionName,
            bestScore = 0,
            attempts = 0,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            scoreHistory = new List<int>()
        };
        _data.entries.Add(newEntry);
        return newEntry;
    }

    /// <summary>强制上限：超出时淘汰最低分/最久远条目</summary>
    private void EnforceMaxEntries()
    {
        if (_data.entries.Count <= maxEntries) return;

        // 淘汰策略：优先淘汰 "无望动作"（尝试≥hopelessAttempts 且最高分≤2）
        // 其次淘汰最低分中最久未更新的
        var hopeless = _data.entries
            .Where(e => e.attempts >= hopelessAttempts && e.bestScore <= 2)
            .OrderBy(e => e.timestamp)
            .ToList();

        int toRemove = _data.entries.Count - maxEntries;

        if (hopeless.Count > 0)
        {
            // 淘汰无望动作
            int removeCount = Mathf.Min(hopeless.Count, toRemove);
            for (int i = 0; i < removeCount; i++)
            {
                var removed = hopeless[i];
                _data.entries.Remove(removed);
                UnityEngine.Debug.Log($"[MotionMemoryManager] 🗑️ 淘汰无望动作「{removed.actionName}」(尝试{removed.attempts}次, 最高{removed.bestScore}/5)");
                toRemove--;
            }
        }

        // 如果还超出，淘汰最低分中最久未更新的
        while (toRemove > 0 && _data.entries.Count > maxEntries)
        {
            var victim = _data.entries
                .OrderBy(e => e.bestScore)
                .ThenBy(e => e.timestamp)
                .First();
            _data.entries.Remove(victim);
            UnityEngine.Debug.Log($"[MotionMemoryManager] 🗑️ 自动淘汰「{victim.actionName}」(评分{victim.bestScore}/5)");
            toRemove--;
        }
    }

    /// <summary>截断评语</summary>
    private static string TruncateReview(string review)
    {
        if (string.IsNullOrEmpty(review)) return "";
        string flat = review.Replace("\n", " ").Trim();
        return flat.Length > 100 ? flat.Substring(0, 100) + "…" : flat;
    }

    // ==================================================================
    //  持久化
    // ==================================================================

    private void Save()
    {
        try
        {
            string json = JsonUtility.ToJson(_data, prettyPrint: true);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[MotionMemoryManager] 保存失败: {e.Message}");
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                _data = JsonUtility.FromJson<StorageData>(json) ?? new StorageData();
                // 确保 scoreHistory 不为 null
                foreach (var e in _data.entries)
                {
                    if (e.scoreHistory == null)
                        e.scoreHistory = new List<int>();
                }
                UnityEngine.Debug.Log($"[MotionMemoryManager] 💾 已加载 {_data.entries.Count} 条运动记忆");
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[MotionMemoryManager] 加载失败: {e.Message}");
            _data = new StorageData();
        }
    }

    /// <summary>清空所有运动记忆（用于调试）</summary>
    public void Clear()
    {
        _data.entries.Clear();
        Save();
        UnityEngine.Debug.Log("[MotionMemoryManager] 🧹 已清空所有运动记忆");
    }
}
