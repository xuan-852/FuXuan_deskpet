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

    [Tooltip("离上次反思的最低冷却（秒），防止频繁反思")]
    public float reflectionCooldown = 300f; // 5 分钟

    // ==================================================================

    [System.Serializable]
    public class MemoryEntry
    {
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
    /// <summary>外部注入的反思回调（由 ChatManager 注册）</summary>
    public System.Func<List<MemoryEntry>, string> OnReflectRequest;

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
        AddMemoryWithImportance(summary, topic, category, importance);
    }

    /// <summary>添加记忆（指定重要性）</summary>
    public void AddMemoryWithImportance(string summary, string topic, string category, int importance)
    {
        importance = Mathf.Clamp(importance, 1, 10);

        // 话题冷却检查
        if (!string.IsNullOrEmpty(topic) && _topicCooldowns.TryGetValue(topic, out float lastTime))
        {
            if (Time.time - lastTime < memoryCooldown)
                return; // 冷却中，跳过
        }

        _data.entries.Add(new MemoryEntry
        {
            summary = summary,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            topic = topic,
            importance = importance,
            category = category
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
        // 写入作为高重要性 reflection 记忆
        _data.entries.Add(new MemoryEntry
        {
            summary = reflectionText,
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            topic = "反思",
            importance = 8,
            category = "reflection"
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

        // ——— 第三层：高重要性记忆（Top-N）———
        var important = _data.entries
            .OrderByDescending(e => e.importance)
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
        var recent = _data.entries
            .OrderByDescending(e => e.timestamp)
            .Take(topRecentCount + important.Count)
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

    /// <summary>获取所有记忆（按时间倒序）</summary>
    public List<MemoryEntry> GetAllMemories()
    {
        return _data.entries.OrderByDescending(e => e.timestamp).ToList();
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
                // 兼容旧格式：旧记录没有 importance，默认 5
                foreach (var e in _data.entries)
                {
                    if (e.importance == 0) e.importance = 5;
                    if (string.IsNullOrEmpty(e.category)) e.category = "tool";
                }
                Debug.Log($"[PetMemory] ✅ 忆境已载入，共 {_data.entries.Count} 条记忆");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[PetMemory] ❌ 载入失败: {e.Message}");
        }
    }
}
