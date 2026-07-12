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

    [Header("负反馈配置")]
    [Tooltip("最多保留多少个负反馈例子")]
    public int maxNegativeExamples = 10;

    [Tooltip("低于此分的动作视为「负反馈例子」")]
    public int negativeThreshold = 2;

    // ==================================================================

    /// <summary>
    /// 负反馈条目 — 记录低分动作的参数快照，作为"不要这样做"的反面教材
    /// </summary>
    [Serializable]
    public class NegativeExample
    {
        /// <summary>动作描述</summary>
        public string actionDescription;
        /// <summary>低分参数快照（人工可读）</summary>
        public string paramSnapshot;
        /// <summary>评分</summary>
        public int score;
        /// <summary>评语/原因</summary>
        public string review;
        /// <summary>记录时间</summary>
        public string timestamp;
        /// <summary>此反例被引用的次数</summary>
        public int referenceCount;
    }

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
        /// <summary>负反馈例子列表</summary>
        public List<NegativeExample> negativeExamples = new List<NegativeExample>();
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
        entry.bestParamJson = paramSnapshot;  // 确保 GetInjectableEntries 能读到参数
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
    //  负反馈接口
    // ==================================================================

    /// <summary>
    /// 记录一个负反馈例子 — 低分动作的参数快照，作为"不要这样做"的反面教材
    /// </summary>
    /// <param name="actionDescription">动作描述</param>
    /// <param name="paramSnapshot">参数快照</param>
    /// <param name="score">评分 (1-5)</param>
    /// <param name="review">评语/失败原因</param>
    public void RecordNegativeExample(string actionDescription, string paramSnapshot, int score, string review)
    {
        // 如果已经有相同动作的负反馈，合并更新
        var existing = _data.negativeExamples.FirstOrDefault(e => e.actionDescription == actionDescription);
        if (existing != null)
        {
            existing.paramSnapshot = paramSnapshot;
            existing.score = score;
            existing.review = review;
            existing.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            existing.referenceCount++;
        }
        else
        {
            var example = new NegativeExample
            {
                actionDescription = actionDescription,
                paramSnapshot = paramSnapshot,
                score = score,
                review = TruncateReview(review),
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                referenceCount = 1
            };
            _data.negativeExamples.Add(example);
        }

        // 强制上限
        while (_data.negativeExamples.Count > maxNegativeExamples)
        {
            // 淘汰引用次数最少 + 最旧的
            var victim = _data.negativeExamples
                .OrderBy(e => e.referenceCount)
                .ThenBy(e => e.timestamp)
                .First();
            _data.negativeExamples.Remove(victim);
            UnityEngine.Debug.Log($"[MotionMemoryManager] 🗑️ 淘汰负反馈「{victim.actionDescription}」");
        }

        Save();
        UnityEngine.Debug.Log($"[MotionMemoryManager] ⛔ 记录负反馈「{actionDescription}」({score}/5) — {TruncateReview(review)}");
    }

    /// <summary>
    /// 获取格式化的负反馈文本，注入 system prompt 作为"不要这样做"的指导
    /// </summary>
    public string GetFormattedNegativeExamples()
    {
        if (_data.negativeExamples.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("⛔ 本座从失败中吸取的教训（避免重现以下不良动作）：");

        // 取引用次数最多的前 5 条（说明近期反复出现）
        var topNegatives = _data.negativeExamples
            .OrderByDescending(e => e.referenceCount)
            .ThenByDescending(e => e.score) // 越低分越需要避免
            .Take(5)
            .ToList();

        foreach (var e in topNegatives)
        {
            string paramHint = "";
            if (!string.IsNullOrEmpty(e.paramSnapshot))
            {
                paramHint = $" [{e.paramSnapshot}]";
            }
            sb.AppendLine($"  ⛔ 「{e.actionDescription}」({e.score}/5) {paramHint}");
            if (!string.IsNullOrEmpty(e.review))
            {
                // 只取第一句评语
                string firstSentence = e.review.Split('.', '。')[0];
                sb.AppendLine($"    ↳ 失败原因: {firstSentence}");
            }
        }

        sb.Append("请以上述失败案例为鉴，避免生成类似的参数模式。");

        return sb.ToString();
    }

    /// <summary>
    /// 将英文动作键名映射为中文描述（与 MotionAgent.ExecuteMotion 同步）
    /// </summary>
    public static string GetChineseName(string actionName)
    {
        return actionName switch
        {
            "wave" => "开心挥手",
            "nod" => "轻轻点头",
            "shake_head" => "摇头",
            "bow" => "行礼",
            "stretch" => "伸懒腰舒展身体",
            "think" => "歪头思考",
            "cover_face" => "害羞捂脸",
            "hands_on_hips" => "挺胸叉腰",
            "tilt_head" => "歪头",
            "prayer" => "合十祈祷",
            "proud_lift_head" => "骄傲抬头",
            _ => actionName, // combo actions 已经是中文
        };
    }

    /// <summary>
    /// 已知 Live2D 无法表现的动作列表
    /// (2D 正面模型无法实现转身/跳跃/背对等)
    /// </summary>
    public static readonly HashSet<string> PhysicallyImpossibleActions = new HashSet<string>
    {
        "惊喜地跳起来拍手",    // L2D 无法跳跃
        "赌气背过身去又悄悄回头", // L2D 正面模型无法转身背对
    };

    /// <summary>
    /// 获取动作的失败惩罚乘数 — 供 MotionAgent Fallback 动态降权
    /// </summary>
    /// <param name="actionName">动作名（如 "wave", "害羞地扭捏捂脸"）</param>
    /// <returns>惩罚乘数: 0.0=完全禁用, 0.3=大幅降权, 0.7=轻微降权, 1.0=正常</returns>
    public float GetFailurePenalty(string actionName)
    {
        if (string.IsNullOrEmpty(actionName)) return 1.0f;

        // 将英文键转为中文描述（否则匹配不到 entries 中的中文名）
        string lookupName = GetChineseName(actionName);

        // ① 检查负反馈 — 低分动作降低权重
        var negative = _data.negativeExamples.FirstOrDefault(e =>
            e.actionDescription == actionName || e.actionDescription == lookupName);
        if (negative != null && negative.score <= negativeThreshold)
            return 0.3f;

        // ② 检查 memory entries — 最近3次持续低分则降权
        var entry = _data.entries.FirstOrDefault(e =>
            e.actionName == actionName || e.actionName == lookupName);
        if (entry != null && entry.scoreHistory != null && entry.scoreHistory.Count >= 3)
        {
            float recentAvg = 0f;
            for (int i = Mathf.Max(0, entry.scoreHistory.Count - 3); i < entry.scoreHistory.Count; i++)
                recentAvg += entry.scoreHistory[i];
            recentAvg /= 3f;

            if (recentAvg <= 2.0f) return 0.4f;   // 持续低分，大幅降权
            if (recentAvg <= 2.5f) return 0.7f;   // 偏低，轻微降权
        }

        return 1.0f; // 正常权重
    }

    // ==================================================================
    //  读取接口
    // ==================================================================

    /// <summary>获取适合注入 system prompt 的运动记忆文本（最佳模板 + 负反馈反面教材）</summary>
    public string GetFormattedMemories()
    {
        var injectable = GetInjectableEntries();
        if (injectable.Count == 0 && _data.negativeExamples.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("📚 本座从过往演武中习得的经验（供本次演武参考）：");

        if (injectable.Count > 0)
        {
            foreach (var e in injectable)
            {
                string bestParamHint = "";
                if (!string.IsNullOrEmpty(e.bestParamJson))
                {
                    bestParamHint = $" [{e.bestParamJson}]";
                }
                string scoreStars = new string('⭐', Mathf.Clamp(e.bestScore, 1, 5));
                sb.AppendLine($"  ✅ 「{e.actionName}」{scoreStars}({e.bestScore}/5) {bestParamHint}");
            }
        }
        else
        {
            sb.AppendLine("  （暂无已验证的高分经验）");
        }

        sb.Append("请参考以上最佳演武参数，在本次生成中有意识地模仿成功经验。");

        // ── 追加负反馈反面教材 ──
        string negatives = GetFormattedNegativeExamples();
        if (!string.IsNullOrEmpty(negatives))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(negatives);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 获取全链路运行报告
    /// </summary>
    public string GetPipelineReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("╔══════════════════════════════════════╗");
        sb.AppendLine("║  符玄「演武心经」— 全链路报告        ║");
        sb.AppendLine("╚══════════════════════════════════════╝");

        // ── 1. 基础统计 ──
        sb.AppendLine();
        sb.AppendLine("📊 一、基础统计");
        sb.AppendLine($"▸ 已掌握动作数: {_data.entries.Count}/{maxEntries}");

        var mastered = _data.entries.Where(e => e.bestScore >= minInjectionScore).ToList();
        var learning = _data.entries.Where(e => e.bestScore > 0 && e.bestScore < minInjectionScore).ToList();
        var untried = _data.entries.Where(e => e.bestScore == 0).ToList();

        sb.AppendLine($"▸ 已精通(≥{minInjectionScore}/5): {mastered.Count}");
        sb.AppendLine($"▸ 待精进(<{minInjectionScore}/5): {learning.Count}");
        sb.AppendLine($"▸ 尚未自评: {untried.Count}");

        // ── 2. 负反馈统计 ──
        sb.AppendLine();
        sb.AppendLine("⛔ 二、负反馈统计");
        sb.AppendLine($"▸ 负反馈记录: {_data.negativeExamples.Count}/{maxNegativeExamples}");
        if (_data.negativeExamples.Count > 0)
        {
            var byRef = _data.negativeExamples.OrderByDescending(x => x.referenceCount).Take(5);
            sb.AppendLine("▸ 最常失败的 5 个动作:");
            foreach (var e in byRef)
                sb.AppendLine($"  ⛔ 「{e.actionDescription}」score={e.score} ref={e.referenceCount}");
        }

        // ── 3. 退化检测 ──
        var degrading = _data.entries
            .Where(e => e.scoreHistory != null && e.scoreHistory.Count >= 3
                        && e.scoreHistory.TakeLast(3).Average() <= 2)
            .ToList();
        if (degrading.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("🔴 三、退步预警");
            foreach (var e in degrading)
                sb.AppendLine($"  🔴 「{e.actionName}」最佳{e.bestScore}/5 最近评分: [{string.Join(",", e.scoreHistory)}]");
        }

        // ── 4. 已知不可能动作 ──
        sb.AppendLine();
        sb.AppendLine("🚫 四、L2D 物理不可能动作");
        sb.AppendLine($"▸ 已标记 {PhysicallyImpossibleActions.Count} 个:");
        foreach (var a in PhysicallyImpossibleActions)
            sb.AppendLine($"  🚫 {a}");
        sb.AppendLine("▸ MotionAgent FallbackDecide 已自动过滤这些动作");

        // ── 5. 改善建议 ──
        sb.AppendLine();
        sb.AppendLine("💡 五、改善建议");

        if (learning.Count > 0)
        {
            sb.AppendLine($"▸ 待精进动作 ({learning.Count} 个)：需要 GLM-4V 验证更多次以提升评分");
        }
        if (mastered.Count == 0)
        {
            sb.AppendLine("▸ ⚠️ 尚无精通动作——确保 testMode 开启且验证链路畅通");
        }
        if (_data.negativeExamples.Count >= maxNegativeExamples)
        {
            sb.AppendLine($"▸ 负反馈已满 ({maxNegativeExamples}条)，建议审查是否有些动作描述不现实（如跳跃/转身）");
        }
        // 检查是否某些 impossible action 仍出现在负反馈中
        var blockedInNegatives = _data.negativeExamples
            .Where(n => PhysicallyImpossibleActions.Any(imp => n.actionDescription.Contains(imp)))
            .ToList();
        if (blockedInNegatives.Count > 0)
        {
            sb.AppendLine($"▸ 负反馈中 {blockedInNegatives.Count} 条属于不可能动作，已被自动过滤");
        }

        sb.AppendLine("▸ 继续运行即可自动积累更多正例——GLM 单模型验证通道已畅通");

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

        // ── 负反馈统计 ──
        if (_data.negativeExamples.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"【⛔ 负反馈反面教材 — {_data.negativeExamples.Count}/{maxNegativeExamples} 条】");
            foreach (var e in _data.negativeExamples.OrderByDescending(x => x.referenceCount))
            {
                sb.AppendLine($"  ⛔ 「{e.actionDescription}」({e.score}/5) 引用{e.referenceCount}次");
                if (!string.IsNullOrEmpty(e.review))
                    sb.AppendLine($"    ↳ {e.review}");
            }
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
                // 兼容旧版 JSON — 确保 negativeExamples 不为 null
                if (_data.negativeExamples == null)
                    _data.negativeExamples = new List<NegativeExample>();
                UnityEngine.Debug.Log($"[MotionMemoryManager] 💾 已加载 {_data.entries.Count} 条运动记忆, {_data.negativeExamples.Count} 条负反馈");
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
