using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// 符玄「本心」— 人格特质与关系模型（持久化演化）
///
/// 设计目标：
///   ▸ 五维人格特质：勤勉/亲和/活泼/自信/求知
///   ▸ 三维关系模型：信任/亲密/熟悉
///   ▸ 所有维度 0~1 渐变，随交互缓缓演化
///   ▸ 里程碑系统：关键节点记录人格成长
///
/// 演化规则（每次对话后微调）：
///   diligence:  主人工作/学习/写代码 ↗，游戏/娱乐 ↘
///   warmth:     友好对话 ↗，长期无互动 ↘（缓慢回归中性）
///   playfulness: 游戏/娱乐 ↗，工作/严肃话题 ↘
///   confidence:  工具调用成功 ↗，失败 ↘
///   curiosity:  询问知识/搜索新信息 ↗，重复日常 ↘
///
/// 关系演化：
///   trust:     主人遵守提醒/正向反馈 ↗，忽视提醒/负向反馈 ↘
///   intimacy:  频繁深入对话 ↗，只问工具性需求 ↘
///   familiarity: 交互次数累积
/// </summary>
[Serializable]
public class PersonalityTraits
{
    [Range(0f, 1f)] public float diligence = 0.5f;    // 勤勉 vs 慵懒
    [Range(0f, 1f)] public float warmth = 0.6f;       // 亲和 vs 高冷
    [Range(0f, 1f)] public float playfulness = 0.5f;  // 活泼 vs 稳重
    [Range(0f, 1f)] public float confidence = 0.5f;   // 自信 vs 谦逊
    [Range(0f, 1f)] public float curiosity = 0.6f;    // 求知 vs 淡然
}

[Serializable]
public class RelationshipState
{
    [Range(0f, 1f)] public float trust = 0.3f;        // 信任度
    [Range(0f, 1f)] public float intimacy = 0.2f;     // 亲密度
    [Range(0f, 1f)] public float familiarity = 0.1f;  // 熟悉度
}

[Serializable]
public class PersonalityData
{
    public PersonalityTraits traits = new PersonalityTraits();
    public RelationshipState relationship = new RelationshipState();
    public int totalInteractions = 0;
    public string firstSeen = "";
    public string lastInteractionDate = "";
    public List<string> milestones = new List<string>();
}

public class PersonalityManager : MonoBehaviour
{
    [Header("◈ 人格演化配置")]
    [Tooltip("每次交互的微调量")]
    public float learningRate = 0.01f;
    [Tooltip("达到多少交互数触发一次里程碑")]
    public int milestoneInterval = 20;

    // ==================================================================

    private PersonalityData _data = new PersonalityData();
    public static PersonalityManager Instance { get; private set; }

    /// <summary>最近几次工具调用的成功/失败记录，用于 confidence 演化</summary>
    private Queue<bool> _recentToolResults = new Queue<bool>();
    private const int TOOL_HISTORY_SIZE = 10;

    private string FilePath => Path.Combine(DataPathConfig.DataRoot, "pet_personality.json");

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
    //  公开接口
    // ==================================================================

    /// <summary>获取人格特质</summary>
    public PersonalityTraits Traits => _data.traits;
    /// <summary>获取关系状态</summary>
    public RelationshipState Relationship => _data.relationship;
    /// <summary>总交互次数</summary>
    public int TotalInteractions => _data.totalInteractions;
    /// <summary>人格里程碑列表</summary>
    public List<string> Milestones => _data.milestones;

    // ==================================================================
    //  交互记录 & 演化
    // ==================================================================

    /// <summary>
    /// 记录一次用户交互，触发人格微调
    /// 由 ChatManager 在每次对话后调用
    /// </summary>
    public void RecordInteraction(string userMessage, string aiReply,
        string currentActivity, List<string> toolResults)
    {
        _data.totalInteractions++;
        _data.lastInteractionDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        // ——— 熟悉度自动增长（对数型，越往后越慢）———
        _data.relationship.familiarity = Mathf.Clamp01(
            1f - Mathf.Exp(-_data.totalInteractions / 50f));

        // ——— 根据 ActivityTracker 观测调整人格 ———
        AdjustTraitsByActivity(currentActivity);

        // ——— 根据对话内容微调 ———
        AdjustTraitsByConversation(userMessage, aiReply);

        // ——— 关系演化 ———
        AdjustRelationshipByConversation(userMessage);

        // ——— 记录工具结果用于 confidence ———
        if (toolResults != null)
        {
            foreach (var r in toolResults)
            {
                bool success = !string.IsNullOrEmpty(r)
                    && !r.StartsWith("❌")
                    && r != "法阵未就绪";
                _recentToolResults.Enqueue(success);
                while (_recentToolResults.Count > TOOL_HISTORY_SIZE)
                    _recentToolResults.Dequeue();
            }
            UpdateConfidenceFromTools();
        }

        // ——— 里程碑检查 ———
        CheckMilestones();

        Save();
    }

    /// <summary>外部直接记录工具结果（用于协程工具）</summary>
    public void RecordToolResult(string toolName, bool success)
    {
        _recentToolResults.Enqueue(success);
        while (_recentToolResults.Count > TOOL_HISTORY_SIZE)
            _recentToolResults.Dequeue();
        UpdateConfidenceFromTools();

        // 修正：置信微调只在 RecordInteraction 时持久化
        // 这里只更新队列，不触发 Save
    }

    /// <summary>由 EmotionState 获取与人格特质联动的基线偏移</summary>
    public void ApplyPersonalityToEmotion(EmotionState emotion)
    {
        // 自信 → 基线效价略高
        emotion.baseValence = 0.2f + _data.traits.confidence * 0.2f;
        // 活泼 → 基线激活度高
        emotion.baseArousal = 0.2f + _data.traits.playfulness * 0.3f;
        // 亲和 → 基线温暖度高
        emotion.baseWarmth = 0.1f + _data.traits.warmth * 0.4f;
        // 勤勉 → 基线精力高
        emotion.baseEnergy = 0.3f + _data.traits.diligence * 0.4f;
    }

    // ==================================================================
    //  为 SystemPrompt 构建人格描述
    // ==================================================================

    /// <summary>生成人格特征摘要，注入 ChatManager SystemPrompt</summary>
    public string FormatForPrompt()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("\n【本座的本心 · 人格与关系】");

        // ——— 人格特质描述 ———
        sb.AppendLine($"┌─ 本座性情 ─┐");
        sb.AppendLine($"  ✦ 勤勉度: {_data.traits.diligence * 100:F0}% — {DescribeTrait(_data.traits.diligence, "勤勉好学", "随性而为", "慵懒怠惰")}");
        sb.AppendLine($"  ✦ 亲和度: {_data.traits.warmth * 100:F0}% — {DescribeTrait(_data.traits.warmth, "温暖亲切", "淡然处之", "冷若冰霜")}");
        sb.AppendLine($"  ✦ 活泼度: {_data.traits.playfulness * 100:F0}% — {DescribeTrait(_data.traits.playfulness, "活泼灵动", "端庄稳重", "古板沉闷")}");
        sb.AppendLine($"  ✦ 自信度: {_data.traits.confidence * 100:F0}% — {DescribeTrait(_data.traits.confidence, "自信满满", "不卑不亢", "谦逊自省")}");
        sb.AppendLine($"  ✦ 求知欲: {_data.traits.curiosity * 100:F0}% — {DescribeTrait(_data.traits.curiosity, "求知若渴", "安于故常", "漠不关心")}");
        sb.AppendLine($"└────────────────────┘");

        // ——— 与主人的关系 ———
        string trustLabel = DescribeTrait(_data.relationship.trust, "深信不疑", "将信将疑", "心存戒备");
        string intimacyLabel = DescribeTrait(_data.relationship.intimacy, "亲密无间", "君子之交", "形同陌路");
        string familiarLabel = _data.relationship.familiarity > 0.7f ? "已十分熟悉" :
                               _data.relationship.familiarity > 0.3f ? "日渐熟稔" : "尚在相知";

        sb.AppendLine($"【与主人之缘】");
        sb.AppendLine($"  信任: {_data.relationship.trust * 100:F0}% ({trustLabel})");
        sb.AppendLine($"  亲密: {_data.relationship.intimacy * 100:F0}% ({intimacyLabel})");
        sb.AppendLine($"  相知: {familiarLabel}（共{_data.totalInteractions}次交谈）");
        sb.AppendLine($"  初遇: {_data.firstSeen}");

        // ——— 里程碑（仅最近3条）———
        if (_data.milestones.Count > 0)
        {
            sb.AppendLine("【成长印记】");
            int start = Math.Max(0, _data.milestones.Count - 3);
            for (int i = start; i < _data.milestones.Count; i++)
                sb.AppendLine($"  ✦ {_data.milestones[i]}");
        }

        sb.AppendLine("（本座之性非一成不变——与主人相处的点滴，皆在潜移默化中塑造着本座。）");
        return sb.ToString();
    }

    /// <summary>仅返回人格特质关键信息（给 MotionAgent 短 prompt 用）</summary>
    public string FormatShortForPrompt()
    {
        string moodDesc = DescribeTrait(_data.traits.warmth, "亲和", "中性", "清冷");
        string energyDesc = DescribeTrait(_data.traits.diligence, "精力充沛", "平常", "慵懒");
        return $"【本心】{moodDesc}，{energyDesc}（勤勉{_data.traits.diligence * 100:F0}%/亲和{_data.traits.warmth * 100:F0}%/活泼{_data.traits.playfulness * 100:F0}%）";
    }

    // ==================================================================
    //  内部演化逻辑
    // ==================================================================

    private void AdjustTraitsByActivity(string activity)
    {
        if (string.IsNullOrEmpty(activity)) return;

        string a = activity.ToLower();

        // ——— 勤勉度 ———
        if (a.Contains("coding") || a.Contains("编程") || a.Contains("studying")
            || a.Contains("学习") || a.Contains("working") || a.Contains("工作"))
            ShiftTrait(ref _data.traits.diligence, learningRate);
        else if (a.Contains("gaming") || a.Contains("游戏") || a.Contains("entertainment")
                 || a.Contains("娱乐"))
            ShiftTrait(ref _data.traits.diligence, -learningRate * 0.5f);

        // ——— 活泼度 ———
        if (a.Contains("gaming") || a.Contains("游戏") || a.Contains("entertainment")
            || a.Contains("娱乐") || a.Contains("music") || a.Contains("音乐"))
            ShiftTrait(ref _data.traits.playfulness, learningRate * 0.8f);
        else if (a.Contains("studying") || a.Contains("学习"))
            ShiftTrait(ref _data.traits.playfulness, -learningRate * 0.5f);

        // ——— 求知欲 ———
        if (a.Contains("studying") || a.Contains("学习") || a.Contains("browsing")
            || a.Contains("浏览") || a.Contains("reading") || a.Contains("阅读"))
            ShiftTrait(ref _data.traits.curiosity, learningRate * 0.6f);
    }

    private void AdjustTraitsByConversation(string userMsg, string aiReply)
    {
        if (string.IsNullOrEmpty(userMsg)) return;

        string u = userMsg.ToLower();
        string a = aiReply?.ToLower() ?? "";

        // ——— 亲和度（根据对话情感）———
        bool userWarm = u.Contains("谢谢") || u.Contains("感谢") || u.Contains("好棒")
            || u.Contains("可爱") || u.Contains("喜欢") || u.Contains("开心")
            || u.Contains("棒") || u.Contains("厉害");
        bool userCold = u.Contains("烦") || u.Contains("走开") || u.Contains("别烦")
            || u.Contains("闭嘴") || u.Contains("不想");

        if (userWarm) ShiftTrait(ref _data.traits.warmth, learningRate * 0.5f);
        if (userCold) ShiftTrait(ref _data.traits.warmth, -learningRate * 1.5f);

        // ——— 求知欲（用户问问题）———
        bool userAsks = u.Contains("什么") || u.Contains("怎么") || u.Contains("为什么")
            || u.Contains("如何") || u.Contains("?" ) || u.Contains("？");
        if (userAsks) ShiftTrait(ref _data.traits.curiosity, learningRate * 0.5f);

        // ——— 活泼度（用户使用语气词/表情）———
        bool userPlayful = u.Contains("哈哈") || u.Contains("嘿嘿") || u.Contains("233")
            || u.Contains("hh") || u.Contains("~") || u.Contains("～");
        if (userPlayful) ShiftTrait(ref _data.traits.playfulness, learningRate * 0.5f);

        // ——— 勤勉度（用户谈正事）———
        bool userDiligent = u.Contains("工作") || u.Contains("学习") || u.Contains("代码")
            || u.Contains("project") || u.Contains("项目") || u.Contains("作业")
            || u.Contains("复习") || u.Contains("考试");
        if (userDiligent) ShiftTrait(ref _data.traits.diligence, learningRate * 0.8f);

        // ——— 长期无互动时缓慢回归中性（此处不处理，由外部定时器触发）———
    }

    /// <summary>人格回归中性（长时间无交互时调用）</summary>
    public void DriftTowardNeutral(float driftRate = 0.005f)
    {
        ShiftTrait(ref _data.traits.warmth, (0.5f - _data.traits.warmth) * driftRate);
        ShiftTrait(ref _data.traits.diligence, (0.5f - _data.traits.diligence) * driftRate);
        ShiftTrait(ref _data.traits.playfulness, (0.5f - _data.traits.playfulness) * driftRate);
    }

    private void AdjustRelationshipByConversation(string userMsg)
    {
        if (string.IsNullOrEmpty(userMsg)) return;
        string u = userMsg.ToLower();

        // ——— 亲密度（talk 类内容）———
        bool personalTalk = u.Contains("今天") || u.Contains("我") || u.Contains("我的")
            || u.Contains("感觉") || u.Contains("心情") || u.Contains("想法")
            || u.Contains("喜欢") || u.Contains("讨厌") || u.Contains("最近");
        if (personalTalk)
            ShiftTrait(ref _data.relationship.intimacy, learningRate * 1.2f);
        else
            ShiftTrait(ref _data.relationship.intimacy, -learningRate * 0.2f);

        // ——— 信任度 ———
        bool positiveFeedback = u.Contains("谢谢") || u.Contains("好用") || u.Contains("不错");
        bool negativeFeedback = u.Contains("错了") || u.Contains("不对") || u.Contains("没用");
        if (positiveFeedback) ShiftTrait(ref _data.relationship.trust, learningRate * 0.8f);
        if (negativeFeedback) ShiftTrait(ref _data.relationship.trust, -learningRate * 1.2f);

        // 熟悉度由总交互次数驱动（已在 RecordInteraction 中处理）
    }

    private void UpdateConfidenceFromTools()
    {
        if (_recentToolResults.Count == 0) return;
        float successRate = (float)_recentToolResults.Count(r => r) / _recentToolResults.Count;
        // confidence 应接近成功率，但有 0.2 的基础保底
        float target = Mathf.Lerp(0.2f, 1f, successRate);
        _data.traits.confidence = Mathf.Lerp(_data.traits.confidence, target, 0.3f);
    }

    // ==================================================================
    //  里程碑
    // ==================================================================

    private void CheckMilestones()
    {
        // 每 milestoneInterval 次交互触发里程碑
        if (_data.totalInteractions > 0
            && _data.totalInteractions % milestoneInterval == 0)
        {
            string milestone = $"与主人相识以来已交流{_data.totalInteractions}次";

            // 根据当前人格特质生成个性化里程碑描述
            if (_data.traits.warmth > 0.7f)
                milestone += "，心中日渐温暖";
            else if (_data.traits.confidence > 0.7f)
                milestone += "，法阵术式愈发娴熟";
            else if (_data.traits.curiosity > 0.7f)
                milestone += "，对人间诸事兴致盎然";
            else if (_data.traits.diligence > 0.7f)
                milestone += "，见主人勤勉，本座亦不敢懈怠";

            // 记录里程碑（去重）
            if (!_data.milestones.Contains(milestone))
            {
                _data.milestones.Add(milestone);
                Debug.Log($"[PersonalityManager] 🏆 里程碑达成: {milestone}");
            }
        }

        // 亲密度里程碑
        if (_data.relationship.intimacy > 0.6f
            && !_data.milestones.Any(m => m.Contains("敞开心扉")))
        {
            _data.milestones.Add("本座渐渐对主人敞开心扉，信任日增");
            Debug.Log($"[PersonalityManager] 🏆 里程碑达成: 敞开心扉");
        }
    }

    // ==================================================================
    //  工具方法
    // ==================================================================

    private static void ShiftTrait(ref float value, float delta)
    {
        value = Mathf.Clamp01(value + delta);
    }

    private static string DescribeTrait(float value, string high, string mid, string low)
    {
        if (value > 0.66f) return high;
        if (value > 0.33f) return mid;
        return low;
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
            Debug.LogError($"[PersonalityManager] ❌ 保存失败: {e.Message}");
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                _data.firstSeen = DateTime.Now.ToString("yyyy-MM-dd");
                Save();
                Debug.Log("[PersonalityManager] 无已有存档，初始化新人格");
                return;
            }
            string json = File.ReadAllText(FilePath);
            var loaded = JsonUtility.FromJson<PersonalityData>(json);
            if (loaded != null)
            {
                _data = loaded;
                Debug.Log($"[PersonalityManager] ✅ 人格已载入，交互{_data.totalInteractions}次，里程碑{_data.milestones.Count}个");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[PersonalityManager] ❌ 载入失败: {e.Message}");
        }
    }
}
