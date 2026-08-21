using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// 符玄「忆境」— 分层长期记忆系统 + 反思反射 (Phase 2)
/// 
/// 改进对比旧版:
///   ✅ 分层注入: 核心事实始终保留 + Top-N 重要记忆 + 近期琐事
///   ✅ 重要性评分: 每条记忆 1-10 分，自动根据话题/内容计算
///   ✅ 重要性裁剪: 超出上限时丢弃最不重要的，而非最早加入的
///   ✅ 对话记录: 不只记工具调用，也记日常对话内容
///   ✅ 对话摘要: 持续更新的近日印象
///   ✅ 话题冷却: 每个话题独立冷却，冷却中跳过
///   ✅ 反思反射: 重要性积分累计达阈值时触发 LLM 提炼高层次洞察
/// </summary>
public class PetMemory : MonoBehaviour
{
    [Header("记忆配置")]
    [Tooltip("最多保留多少条普通记忆")]
    public int maxMemories = 30;

    [Tooltip("同一话题冷却（秒），防止短时间内反复记录")]
    public float memoryCooldown = 120f;

    [Tooltip("注入 prompt 时保留的高重要性记忆条数")]
    public int topImportantCount = 5;

    [Tooltip("注入 prompt 时保留的最近记忆条数")]
    public int topRecentCount = 3;

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
    }

    [System.Serializable]
    public class MemoryData
    {
        public List<MemoryEntry> entries = new List<MemoryEntry>();
        /// <summary>核心事实（始终注入 system prompt）</summary>
        public List<string> coreFacts = new List<string>();
        /// <summary>近日对话摘要（始终注入）</summary>
        public string conversationSummary = "";
        /// <summary>上次反思时已处理的记忆索引</summary>
        public int lastReflectionIndex = 0;
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
        for (int i = _data.lastReflectionIndex; i < _data.entries.Count; i++)
        {
            if (_data.entries[i].category != "reflection")
                _reflectionAccum += _data.entries[i].importance;
        }
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
        AddMemoryWithMetadata(summary, topic, category, importance, "system", MemoryGovernance.DefaultConfidence);
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
        if (!MemoryGovernance.IsValidSummary(summary))
        {
            Debug.Log("[PetMemory] ⏭️ 忽略过短或空白记忆");
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
            if (cleanSummary.Length > (duplicate.summary ?? "").Length && confidence >= duplicate.confidence)
            {
                duplicate.summary = cleanSummary;
                changed = true;
            }
            duplicate.lastAccessAt = MemoryGovernance.Timestamp(now);
            duplicate.accessCount++;
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
            expiresAt = expiresAfterDays > 0
                ? MemoryGovernance.Timestamp(now.AddDays(expiresAfterDays))
                : ""
        });

        // 超出上限时，丢弃重要性最低的（而非最早的）
        while (_data.entries.Count > maxMemories)
        {
            var lowest = _data.entries.OrderBy(e => e.importance).ThenBy(e => e.timestamp).First();
            _data.entries.Remove(lowest);
        }

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

    /// <summary>添加核心事实（始终注入，不会被裁剪）</summary>
    public void AddCoreFact(string fact)
    {
        if (!_data.coreFacts.Contains(fact))
        {
            _data.coreFacts.Add(fact);
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
        _data.conversationSummary = summary;
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

        // 取出从上次反思到现在的非 reflection 记忆
        var candidates = new List<MemoryEntry>();
        for (int i = _data.lastReflectionIndex; i < _data.entries.Count; i++)
        {
            if (_data.entries[i].category != "reflection")
                candidates.Add(_data.entries[i]);
        }

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

        // 写入作为高重要性 reflection 记忆
        _data.entries.Add(new MemoryEntry
        {
            id = Guid.NewGuid().ToString("N"),
            summary = reflectionText,
            timestamp = MemoryGovernance.Timestamp(DateTime.Now),
            topic = "反思",
            importance = 8,
            category = "reflection",
            source = "reflection",
            confidence = 0.75f,
            lastAccessAt = MemoryGovernance.Timestamp(DateTime.Now)
        });

        // 更新索引和积分
        _data.lastReflectionIndex = _data.entries.Count - 1;
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

        for (int i = 0; i < candidates.Count; i++)
        {
            var e = candidates[i];
            sb.AppendLine($"{i + 1}. ({e.timestamp}) [{e.topic}] {e.summary}");
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
    /// 获取格式化的记忆文本（分层注入）
    /// 第一层: 核心事实（始终保留）
    /// 第二层: 近日对话摘要
    /// 第三层: Top-N 高重要性记忆
    /// 第四层: 最近几条记忆（补充）
    /// </summary>
    public string GetFormattedMemories()
    {
        return GetFormattedMemories("");
    }

    /// <summary>
    /// 按当前问题选择记忆。空 query 保留旧版全局摘要行为，非空 query 启用相关性排序。
    /// </summary>
    public string GetFormattedMemories(string query)
    {
        if (_data.entries.Count == 0 && _data.coreFacts.Count == 0
            && string.IsNullOrEmpty(_data.conversationSummary))
            return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n【本座的忆境残象】");

        // ——— 第一层：核心事实 ———
        if (_data.coreFacts.Count > 0)
        {
            sb.AppendLine("┌─ 本座确定知晓之事 ─┐");
            foreach (var fact in _data.coreFacts)
                sb.AppendLine($"  ✦ {fact}");
            sb.AppendLine("└────────────────────┘");
        }

        // ——— 第二层：对话摘要 ———
        if (!string.IsNullOrEmpty(_data.conversationSummary))
        {
            sb.AppendLine("【近日印象】" + _data.conversationSummary);
        }

        List<MemoryEntry> candidates = GetRelevantMemories(query, Math.Max(8, topImportantCount + topRecentCount));

        // ——— 第三层：高重要性记忆（Top-N）———
        var important = candidates
            .OrderByDescending(e => e.importance * Math.Max(e.confidence, MemoryGovernance.DefaultConfidence))
            .ThenByDescending(e => e.timestamp)
            .Take(topImportantCount)
            .ToList();

        if (important.Count > 0)
        {
            sb.AppendLine("【印象深刻之事】");
            for (int i = 0; i < important.Count; i++)
            {
                var e = important[i];
                string imp = new string('✦', Mathf.Clamp(e.importance / 2, 1, 5));
                sb.AppendLine($"  ({e.timestamp}) [{imp}] {e.summary}");
            }
        }

        // ——— 第四层：最近记忆（去重后补充）———
        var recent = candidates
            .OrderByDescending(e => MemoryGovernance.Relevance(e, query, DateTime.Now))
            .Where(r => !important.Contains(r))
            .Take(topRecentCount)
            .ToList();

        if (recent.Count > 0)
        {
            sb.AppendLine("【近日琐事】");
            foreach (var e in recent)
                sb.AppendLine($"  ({e.timestamp}) {e.summary}");
        }

        sb.AppendLine("（忆境残象或有模糊，若主人提及相关之事，本座自可据此推演。）");
        return sb.ToString();
    }

    /// <summary>
    /// 返回与当前问题最相关的记忆，并在内存中记录访问，不在每次请求时落盘。
    /// 重要度很高的记忆即使没有关键词命中也保留少量，避免核心近况完全消失。
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
            .Where(x => !hasQuery || x.Overlap > 0f || x.Entry.importance >= 8)
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
        _data.lastReflectionIndex = 0;
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
                }
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
