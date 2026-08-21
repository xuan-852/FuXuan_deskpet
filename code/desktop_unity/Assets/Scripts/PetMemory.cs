using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// 符玄「忆境」— 分层长期记忆系统 + 反思反射 (Phase 3)
/// 
/// 改进对比旧版:
///   ✅ 写入闸门: 候选、稳定性、来源、置信度分开判断
///   ✅ 分层容量: 稳定事实、短期事件、工具轨迹、反思各有配额
///   ✅ 查询注入: 只注入与当前问题有明确关系的少量记忆
///   ✅ 对话记录: 不只记工具调用，也记日常对话内容
///   ✅ 对话摘要: 持续更新的近日印象
///   ✅ 话题冷却: 每个话题独立冷却，冷却中跳过
///   ✅ 反思反射: 重要性积分累计达阈值时触发 LLM 提炼高层次洞察
/// </summary>
public class PetMemory : MonoBehaviour
{
    [Header("记忆配置")]
    [Tooltip("所有长期记忆的硬上限；各层还会有独立配额")]
    public int maxMemories = 30;

    [Tooltip("稳定事实/偏好层上限")]
    public int maxDurableMemories = 12;

    [Tooltip("短期事件层上限")]
    public int maxEpisodicMemories = 8;

    [Tooltip("工具轨迹层上限")]
    public int maxToolMemories = 4;

    [Tooltip("反思洞察层上限")]
    public int maxReflectionMemories = 6;

    [Tooltip("核心事实上限；核心事实不再无限常驻")]
    public int maxCoreFacts = 12;

    [Tooltip("同一话题冷却（秒），防止短时间内反复记录")]
    public float memoryCooldown = 120f;

    [Tooltip("一次对话最多注入的相关长期记忆条数")]
    public int injectionCount = 3;

    [Header("反思反射")]
    [Tooltip("重要性积分累计达到此值时触发反思")]
    public int reflectionThreshold = 30;

    [Tooltip("离上次反思的最低冷却（秒），防止频繁反思（加大以降本）")]
    public float reflectionCooldown = 600f; // 10 分钟

    // ==================================================================

    [System.Serializable]
    public class MemoryEntry
    {
        /// <summary>稳定标识；旧版 JSON 没有此字段，载入时自动补齐。</summary>
        public string id = "";
        /// <summary>记忆摘要文本</summary>
        public string summary;
        /// <summary>记录时间 (yyyy-MM-dd HH:mm)</summary>
        public string timestamp;
        /// <summary>话题标签，用于去重</summary>
        public string topic;
        /// <summary>重要性 1-10，10=极重要</summary>
        public int importance = 5;
        /// <summary>类别: tool / conversation / observation / reflection</summary>
        public string category = "conversation";
        /// <summary>来源: user / local_model / tool / reflection / system</summary>
        public string source = "system";
        /// <summary>可信度 0-1，明确告知通常高于模型推断。</summary>
        public float confidence = MemoryGovernance.DefaultConfidence;
        /// <summary>最近被检索到的时间；不等同于创建时间。</summary>
        public string lastAccessAt = "";
        /// <summary>被检索次数，用于治理与后续淘汰策略。</summary>
        public int accessCount = 0;
        /// <summary>可选过期时间；空值表示不过期。</summary>
        public string expiresAt = "";
        /// <summary>存储层级：durable / episodic / tool / reflection。</summary>
        public string tier = "episodic";
        /// <summary>被不同证据确认的次数；用于容量淘汰。</summary>
        public int evidenceCount = 1;
        /// <summary>最近一次被新证据确认的时间。</summary>
        public string lastConfirmedAt = "";
    }

    [System.Serializable]
    public class MemoryData
    {
        public List<MemoryEntry> entries = new List<MemoryEntry>();
        /// <summary>核心事实（始终注入 system prompt）</summary>
        public List<string> coreFacts = new List<string>();
        /// <summary>近日对话摘要（始终注入）</summary>
        public string conversationSummary = "";
        /// <summary>近日摘要的时间；旧数据为空时按普通摘要处理。</summary>
        public string conversationSummaryAt = "";
        /// <summary>上次反思时已处理的记忆索引</summary>
        public int lastReflectionIndex = 0;
        /// <summary>新版反思游标：避免容量淘汰后索引漂移。</summary>
        public string lastReflectionAt = "";
        public string lastReflectionId = "";
    }

    private MemoryData _data = new MemoryData();
    private Dictionary<string, float> _topicCooldowns = new Dictionary<string, float>();
    private float _lastReflectionTime = -999f;
    /// <summary>从上一次反思后累计的重要性积分</summary>
    private int _reflectionAccum = 0;
    // 注：反思已由 ChatManager.SendRequestCoroutine → CheckReflection → DoReflection 驱动；
    // 曾有的 OnReflectRequest 回调字段从未被调用（恒 null），已删除。

    public static PetMemory Instance { get; private set; }

    private string FilePath => Path.Combine(DataPathConfig.DataRoot, "pet_memory.json");

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
        // 启动时根据 lastReflectionIndex 重建积分
        RebuildReflectionAccum();
    }

    /// <summary>重建积分（重启后恢复之前积累的积分）</summary>
    private void RebuildReflectionAccum()
    {
        _reflectionAccum = 0;
        IEnumerable<MemoryEntry> pending = GetPendingReflectionEntries();
        foreach (MemoryEntry entry in pending)
        {
            if (entry.category != "reflection")
                _reflectionAccum += entry.importance;
        }
    }

    private IEnumerable<MemoryEntry> GetPendingReflectionEntries()
    {
        if (!string.IsNullOrEmpty(_data.lastReflectionId))
        {
            int markerIndex = _data.entries.FindIndex(e => e.id == _data.lastReflectionId);
            if (markerIndex >= 0)
                return _data.entries.Skip(markerIndex + 1);
        }

        if (MemoryGovernance.TryParseTimestamp(_data.lastReflectionAt, out DateTime markerTime))
        {
            return _data.entries.Where(e =>
                MemoryGovernance.TryParseTimestamp(e.timestamp, out DateTime entryTime)
                && entryTime >= markerTime);
        }

        int start = Mathf.Clamp(_data.lastReflectionIndex, 0, _data.entries.Count);
        return _data.entries.Skip(start);
    }

    // ==================================================================
    //  公开接口
    // ==================================================================

    /// <summary>添加记忆（自动评分重要性）</summary>
    /// <param name="summary">记忆摘要</param>
    /// <param name="topic">话题标签（用于去重）</param>
    /// <param name="category">类别: tool / conversation / observation / reflection</param>
    public void AddMemory(string summary, string topic = "", string category = "conversation")
    {
        int importance = CalculateImportance(summary, topic, category);
        string source = string.Equals(category, "tool", StringComparison.OrdinalIgnoreCase) ? "tool" : "system";
        AddMemoryWithMetadata(summary, topic, category, importance, source, MemoryGovernance.DefaultConfidence);
    }

    /// <summary>添加记忆（指定重要性）</summary>
    public void AddMemoryWithImportance(string summary, string topic, string category, int importance)
    {
        AddMemoryWithMetadata(summary, topic, category, importance, "system", MemoryGovernance.DefaultConfidence);
    }

    /// <summary>
    /// 经过治理闸门后添加记忆。返回 true 表示新增，false 表示被过滤或与已有记忆合并。
    /// 旧调用点继续使用 AddMemory/AddMemoryWithImportance，因此迁移不破坏现有功能。
    /// </summary>
    public bool AddMemoryWithMetadata(
        string summary,
        string topic,
        string category,
        int importance,
        string source,
        float confidence,
        int expiresAfterDays = 0)
    {
        if (!MemoryGovernance.ShouldPersist(summary, category, source, importance, confidence, out string rejectReason))
        {
            Debug.Log($"[PetMemory] ⏭️ 写入闸门拒绝: {rejectReason}");
            return false;
        }

        string cleanSummary = summary.Trim();
        if (cleanSummary.Length > MemoryGovernance.MaxSummaryLength)
            cleanSummary = cleanSummary.Substring(0, MemoryGovernance.MaxSummaryLength) + "…";

        importance = Mathf.Clamp(importance, 1, 10);
        confidence = Mathf.Clamp01(confidence <= 0f ? MemoryGovernance.DefaultConfidence : confidence);
        topic = topic ?? "";
        category = string.IsNullOrEmpty(category) ? "conversation" : category;
        source = string.IsNullOrEmpty(source) ? "system" : source;
        string tier = MemoryGovernance.GetTier(category, source, importance, expiresAfterDays);
        DateTime now = DateTime.Now;

        // 先合并同类近似记忆，避免“主人喜欢音乐”被重复写入几十次。
        MemoryEntry duplicate = _data.entries.FirstOrDefault(e =>
            string.Equals(e.category, category, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrEmpty(topic) || string.IsNullOrEmpty(e.topic)
                || string.Equals(e.topic, topic, StringComparison.OrdinalIgnoreCase))
            && MemoryGovernance.IsNearDuplicate(e.summary, cleanSummary));
        if (duplicate != null)
        {
            bool changed = false;
            if (importance > duplicate.importance)
            {
                duplicate.importance = importance;
                changed = true;
            }
            if (confidence > duplicate.confidence)
            {
                duplicate.confidence = confidence;
                changed = true;
            }
            duplicate.evidenceCount = Mathf.Max(1, duplicate.evidenceCount) + 1;
            duplicate.lastConfirmedAt = MemoryGovernance.Timestamp(now);
            duplicate.tier = tier;
            changed = true;
            if (cleanSummary.Length > (duplicate.summary ?? "").Length && confidence >= duplicate.confidence)
            {
                duplicate.summary = cleanSummary;
                changed = true;
            }
            duplicate.lastAccessAt = MemoryGovernance.Timestamp(now);
            duplicate.accessCount++;
            TrimCapacity();
            if (changed) Save();
            return false;
        }

        // 话题冷却检查：放在去重之后，允许新证据提升已有记忆的可信度/重要度。
        if (!string.IsNullOrEmpty(topic) && _topicCooldowns.TryGetValue(topic, out float lastTime))
        {
            if (Time.time - lastTime < memoryCooldown)
                return false; // 冷却中，跳过
        }

        _data.entries.Add(new MemoryEntry
        {
            id = Guid.NewGuid().ToString("N"),
            summary = cleanSummary,
            timestamp = MemoryGovernance.Timestamp(now),
            topic = topic,
            importance = importance,
            category = category,
            source = source,
            confidence = confidence,
            lastAccessAt = MemoryGovernance.Timestamp(now),
            accessCount = 0,
            tier = tier,
            evidenceCount = 1,
            lastConfirmedAt = MemoryGovernance.Timestamp(now),
            expiresAt = expiresAfterDays > 0
                ? MemoryGovernance.Timestamp(now.AddDays(expiresAfterDays))
                : ""
        });

        // 先清理过期项，再按层级配额和保留分淘汰。
        TrimCapacity();

        // 更新冷却
        if (!string.IsNullOrEmpty(topic))
            _topicCooldowns[topic] = Time.time;

        // ——— 非 reflection 记忆计入积分 ———
        if (category != "reflection")
            _reflectionAccum += importance;

        Save();
        return true;
    }

    /// <summary>智能重要性计算（根据话题和关键词自动评分）</summary>
    private int CalculateImportance(string summary, string topic, string category)
    {
        if (category == "tool")
        {
            if (topic == "考试" || topic == "成绩") return 8;
            if (topic == "提醒")                   return 7;
            if (topic == "文件搜索")                return 6;
            if (topic == "搜索" || topic == "截屏") return 5;
            if (topic == "天气")                    return 4;
            return 5;
        }

        if (category == "reflection") return 7;

        // 对话内容关键词检测
        string s = summary.ToLower();
        if (s.Contains("喜欢")   || s.Contains("讨厌")  || s.Contains("最爱") ||
            s.Contains("习惯")   || s.Contains("生日")  || s.Contains("名字") ||
            s.Contains("禁忌")   || s.Contains("秘密")) return 8;
        if (s.Contains("工作")   || s.Contains("考试")  || s.Contains("学习") ||
            s.Contains("项目")   || s.Contains("代码")  || s.Contains("毕业")) return 6;
        if (s.Contains("游戏")   || s.Contains("动漫")  || s.Contains("音乐") ||
            s.Contains("电影")   || s.Contains("小说")) return 5;
        return 3; // 一般闲聊
    }

    /// <summary>按层级配额和总容量淘汰最弱条目。</summary>
    private void TrimCapacity()
    {
        DateTime now = DateTime.Now;
        _data.entries.RemoveAll(e => MemoryGovernance.IsExpired(e, now));

        TrimTier(MemoryGovernance.DurableTier, maxDurableMemories, now);
        TrimTier(MemoryGovernance.EpisodicTier, maxEpisodicMemories, now);
        TrimTier(MemoryGovernance.ToolTier, maxToolMemories, now);
        TrimTier(MemoryGovernance.ReflectionTier, maxReflectionMemories, now);

        while (_data.entries.Count > Math.Max(1, maxMemories))
        {
            MemoryEntry victim = _data.entries
                .OrderBy(e => MemoryGovernance.RetentionScore(e, now))
                .ThenBy(e => e.timestamp)
                .FirstOrDefault();
            if (victim == null) break;
            _data.entries.Remove(victim);
        }
    }

    private void TrimTier(string tier, int limit, DateTime now)
    {
        if (limit < 0) limit = 0;
        var tierEntries = _data.entries.Where(e => string.Equals(e.tier, tier, StringComparison.OrdinalIgnoreCase)).ToList();
        while (tierEntries.Count > limit)
        {
            MemoryEntry victim = tierEntries
                .OrderBy(e => MemoryGovernance.RetentionScore(e, now))
                .ThenBy(e => e.timestamp)
                .First();
            _data.entries.Remove(victim);
            tierEntries.Remove(victim);
        }
    }

    /// <summary>添加核心事实（始终注入，不会被裁剪）</summary>
    public void AddCoreFact(string fact)
    {
        if (!MemoryGovernance.IsValidSummary(fact)) return;
        fact = fact.Trim();
        if (fact.Length > 120) fact = fact.Substring(0, 120) + "…";
        bool duplicate = _data.coreFacts.Any(existing =>
            MemoryGovernance.IsNearDuplicate(existing, fact));
        if (!duplicate)
        {
            _data.coreFacts.Add(fact);
            while (_data.coreFacts.Count > Math.Max(1, maxCoreFacts))
                _data.coreFacts.RemoveAt(0);
            Save();
        }
    }

    /// <summary>移除核心事实</summary>
    public void RemoveCoreFact(string fact)
    {
        _data.coreFacts.Remove(fact);
        Save();
    }

    /// <summary>更新近日对话摘要</summary>
    public void UpdateConversationSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return;
        summary = summary.Trim();
        if (summary.Length > 180) summary = summary.Substring(0, 180) + "…";
        _data.conversationSummary = summary;
        _data.conversationSummaryAt = MemoryGovernance.Timestamp(DateTime.Now);
        Save();
    }

    public string GetConversationSummary() => _data.conversationSummary;

    // ==================================================================
    //  反思反射 (Reflection)
    // ==================================================================

    /// <summary>
    /// 检查是否应该触发反思。
    /// 如果积分达标且冷却已过，返回待反思的记忆列表；
    /// 否则返回 null。
    /// </summary>
    public List<MemoryEntry> CheckReflection()
    {
        if (_reflectionAccum < reflectionThreshold) return null;
        if (Time.time - _lastReflectionTime < reflectionCooldown) return null;

        // 取出从上次反思到现在的非 reflection 记忆；游标不会因容量淘汰而失真。
        var candidates = GetPendingReflectionEntries()
            .Where(e => e.category != "reflection" && !MemoryGovernance.IsExpired(e, DateTime.Now))
            .OrderByDescending(e => MemoryGovernance.RetentionScore(e, DateTime.Now))
            .Take(6)
            .ToList();

        if (candidates.Count < 2) return null; // 太少，没意义
        return candidates;
    }

    /// <summary>
    /// 完成一次反思循环：将 LLM 返回的提炼写入记忆，
    /// 重置积分计数器。
    /// </summary>
    public void CommitReflection(string reflectionText)
    {
        if (!MemoryGovernance.IsValidSummary(reflectionText)) return;

        // 反思也必须走统一写入/容量治理，避免洞察无限挤占稳定事实。
        bool added = AddMemoryWithMetadata(
            reflectionText, "反思", "reflection", 8, "reflection", 0.75f, 30);
        if (!added) return;

        // 更新游标和积分
        DateTime reflectionAt = DateTime.Now;
        _data.lastReflectionAt = MemoryGovernance.Timestamp(reflectionAt);
        MemoryEntry reflectionEntry = _data.entries
            .Where(e => e.category == "reflection" && e.summary == reflectionText)
            .OrderByDescending(e => e.timestamp)
            .FirstOrDefault();
        _data.lastReflectionId = reflectionEntry?.id ?? "";
        _data.lastReflectionIndex = _data.entries.Count;
        _reflectionAccum = 0;
        _lastReflectionTime = Time.time;

        Save();
        Debug.Log($"[PetMemory] 🧠 反思完成: {reflectionText}");
    }

    /// <summary>构建反思 prompt，供 ChatManager 调 LLM 用</summary>
    public string BuildReflectionPrompt(List<MemoryEntry> candidates)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("你是符玄，仙舟「罗浮」太卜司之首。");
        sb.AppendLine("以下是你的忆境中最近积累的一些观察和记忆碎片：");
        sb.AppendLine();

        foreach (MemoryEntry e in candidates
            .Where(e => e != null && !MemoryGovernance.IsExpired(e, DateTime.Now))
            .OrderByDescending(e => MemoryGovernance.RetentionScore(e, DateTime.Now))
            .Take(6))
        {
            sb.AppendLine($"- [{e.topic}] {e.summary}");
        }

        sb.AppendLine();
        sb.AppendLine("请根据以上碎片提炼出 1-2 条高层次观察结论——");
        sb.AppendLine("这些结论应能反映主人的近况、习惯、偏好或情绪变化。");
        sb.AppendLine("用第一人称（本座），每条一句话，不要列举事实，而是给出洞察。");
        sb.AppendLine("例如：「本座观主人近日勤于温习，必有要考。」");
        sb.AppendLine("例如：「主人似乎对音乐颇有兴致，可常与其论之。」");
        sb.AppendLine("直接输出结论，不要前缀，每条一行。");
        return sb.ToString();
    }

    // ==================================================================
    //  记忆检索（分层注入 system prompt）
    // ==================================================================

    /// <summary>
    /// 获取格式化的记忆文本。
    /// 非空 query 只返回直接相关的少量线索；空 query 仅供闲聊取少量稳定记忆。
    /// 记忆正文不再携带时间戳/重要度等管理元数据，减少 token 和模型误读。
    /// </summary>
    public string GetFormattedMemories()
    {
        return GetFormattedMemories("");
    }

    /// <summary>
    /// 按当前问题选择记忆。普通记忆没有关键词命中时不再因为“重要度高”而强行注入。
    /// </summary>
    public string GetFormattedMemories(string query)
    {
        if (_data.entries.Count == 0 && _data.coreFacts.Count == 0
            && string.IsNullOrEmpty(_data.conversationSummary))
            return "";

        DateTime now = DateTime.Now;
        bool hasQuery = !string.IsNullOrWhiteSpace(query);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n【本座的忆境线索】");
        sb.AppendLine("（仅作相关背景；若与主人当前话题无关，不得主动提及。）");

        int factCount = 0;
        foreach (string fact in _data.coreFacts)
        {
            // 核心事实是极小的身份/称呼层，最多 3 条常驻；普通事件仍必须相关。
            sb.AppendLine($"- {fact}");
            if (++factCount >= 3) break;
        }

        bool summaryIncluded = false;
        if (!string.IsNullOrEmpty(_data.conversationSummary))
        {
            float summaryOverlap = hasQuery
                ? MemoryGovernance.TermOverlap(query, _data.conversationSummary)
                : 1f;
            if (!hasQuery || summaryOverlap >= 0.20f)
            {
                sb.AppendLine($"- 近日印象：{_data.conversationSummary}");
                summaryIncluded = true;
            }
        }

        List<MemoryEntry> candidates = GetRelevantMemories(query, Math.Max(1, injectionCount));
        foreach (MemoryEntry entry in candidates)
            sb.AppendLine($"- {entry.summary}");

        if (factCount == 0 && !summaryIncluded && candidates.Count == 0)
            return "";

        // 固定的小预算是第二道保险，ChatManager 还会按全局段落预算再裁一次。
        const int maxInjectionChars = 1400;
        if (sb.Length > maxInjectionChars)
            return sb.ToString(0, maxInjectionChars) + "\n…【忆境线索已收束】…";
        return sb.ToString();
    }

    /// <summary>
    /// 返回与当前问题最相关的记忆，并在内存中记录访问，不在每次请求时落盘。
    /// 非空 query 必须有明确词元重叠；空 query 才允许返回少量稳定层记忆。
    /// </summary>
    public List<MemoryEntry> GetRelevantMemories(string query, int maxCount = 8)
    {
        if (maxCount <= 0) return new List<MemoryEntry>();
        DateTime now = DateTime.Now;
        bool hasQuery = !string.IsNullOrWhiteSpace(query);

        var ranked = _data.entries
            .Where(e => !MemoryGovernance.IsExpired(e, now))
            .Select(e => new
            {
                Entry = e,
                Score = MemoryGovernance.Relevance(e, query, now),
                Overlap = hasQuery
                    ? MemoryGovernance.TermOverlap(query, (e.topic ?? "") + " " + (e.summary ?? ""))
                    : 0f
            })
            .Where(x => !hasQuery
                ? x.Entry.tier == MemoryGovernance.DurableTier
                    || x.Entry.tier == MemoryGovernance.ReflectionTier
                : x.Overlap >= 0.12f)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.importance)
            .ThenByDescending(x => x.Entry.timestamp)
            .Take(maxCount)
            .Select(x => x.Entry)
            .ToList();

        foreach (MemoryEntry entry in ranked)
        {
            entry.accessCount++;
            entry.lastAccessAt = MemoryGovernance.Timestamp(now);
        }
        return ranked;
    }

    /// <summary>获取所有记忆（按时间倒序）</summary>
    public List<MemoryEntry> GetAllMemories()
    {
        return _data.entries.OrderByDescending(e => e.timestamp).ToList();
    }

    /// <summary>返回核心事实副本，供记忆管理 UI 只读展示。</summary>
    public List<string> GetCoreFacts()
    {
        return new List<string>(_data.coreFacts);
    }

    /// <summary>清理已过期记忆，返回实际移除条数。</summary>
    public int RemoveExpiredMemories()
    {
        DateTime now = DateTime.Now;
        int removed = _data.entries.RemoveAll(e => MemoryGovernance.IsExpired(e, now));
        if (removed > 0)
        {
            _data.lastReflectionIndex = Mathf.Min(_data.lastReflectionIndex, _data.entries.Count);
            RebuildReflectionAccum();
            Save();
            Debug.Log($"[PetMemory] 🧹 已清理过期记忆: {removed} 条");
        }
        return removed;
    }

    /// <summary>按关键词搜索记忆</summary>
    public List<MemoryEntry> SearchMemories(string keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return new List<MemoryEntry>();
        string kw = keyword.ToLower();
        return _data.entries
            .Where(e => e.summary.ToLower().Contains(kw) || e.topic.ToLower().Contains(kw))
            .OrderByDescending(e => e.importance)
            .ThenByDescending(e => e.timestamp)
            .Take(5)
            .ToList();
    }

    /// <summary>清空所有记忆</summary>
    public void ClearMemories()
    {
        _data.entries.Clear();
        _data.coreFacts.Clear();
        _data.conversationSummary = "";
        _data.conversationSummaryAt = "";
        _data.lastReflectionIndex = 0;
        _data.lastReflectionAt = "";
        _data.lastReflectionId = "";
        _reflectionAccum = 0;
        _topicCooldowns.Clear();
        Save();
        Debug.Log("[PetMemory] 🧹 忆境已清空");
    }

    /// <summary>获取记忆条数</summary>
    public int MemoryCount => _data.entries.Count;

    // ==================================================================
    //  持久化
    // ==================================================================

    public void Save()
    {
        // 测试模式允许内存态验证治理逻辑，但禁止任何记忆落盘。
        if (System.IO.File.Exists(DataPathConfig.TestModeFile)) return;
        try
        {
            string json = JsonUtility.ToJson(_data, prettyPrint: true);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[PetMemory] ❌ 保存失败: {e.Message}");
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                Debug.Log("[PetMemory] 无已有记忆，从零开始");
                return;
            }
            string json = File.ReadAllText(FilePath);
            var loaded = JsonUtility.FromJson<MemoryData>(json);
            if (loaded != null)
            {
                _data = loaded;
                if (_data.entries == null) _data.entries = new List<MemoryEntry>();
                if (_data.coreFacts == null) _data.coreFacts = new List<string>();
                bool migrated = false;
                int removedLegacyLowValue = 0;
                var legacyLowValueEntries = new List<MemoryEntry>();
                // 兼容旧格式：旧记录没有 importance，默认 5
                foreach (var e in _data.entries)
                {
                    if (e.importance == 0) e.importance = 5;
                    if (string.IsNullOrEmpty(e.category)) e.category = "tool";
                    if (string.IsNullOrEmpty(e.id))
                    {
                        e.id = Guid.NewGuid().ToString("N");
                        migrated = true;
                    }
                    if (string.IsNullOrEmpty(e.source))
                    {
                        e.source = e.category == "conversation" ? "system" : e.category;
                        migrated = true;
                    }
                    if (e.confidence <= 0f)
                    {
                        e.confidence = MemoryGovernance.DefaultConfidence;
                        migrated = true;
                    }
                    if (string.IsNullOrEmpty(e.lastAccessAt))
                    {
                        e.lastAccessAt = e.timestamp;
                        migrated = true;
                    }
                    if (string.IsNullOrEmpty(e.tier))
                    {
                        e.tier = MemoryGovernance.GetTier(e.category, e.source, e.importance, 0);
                        migrated = true;
                    }
                    if (e.evidenceCount <= 0)
                    {
                        e.evidenceCount = 1;
                        migrated = true;
                    }
                    if (string.IsNullOrEmpty(e.lastConfirmedAt))
                    {
                        e.lastConfirmedAt = e.timestamp;
                        migrated = true;
                    }

                    // 旧版随机闲聊/低分系统对话不再占用新架构的容量。
                    if (e.category == "conversation" && e.source == "system" && e.importance <= 3)
                    {
                        legacyLowValueEntries.Add(e);
                        removedLegacyLowValue++;
                    }
                }
                foreach (MemoryEntry legacyEntry in legacyLowValueEntries)
                    _data.entries.Remove(legacyEntry);
                if (_data.coreFacts.Count > Math.Max(1, maxCoreFacts))
                {
                    _data.coreFacts = _data.coreFacts
                        .Skip(Math.Max(0, _data.coreFacts.Count - Math.Max(1, maxCoreFacts)))
                        .ToList();
                    migrated = true;
                }
                TrimCapacity();
                if (removedLegacyLowValue > 0) migrated = true;
                if (migrated) Save();
                Debug.Log($"[PetMemory] ✅ 忆境已载入，共 {_data.entries.Count} 条记忆");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[PetMemory] ❌ 载入失败: {e.Message}");
        }
    }
}
