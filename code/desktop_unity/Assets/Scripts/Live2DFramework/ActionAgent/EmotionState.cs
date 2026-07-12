using System;
using UnityEngine;

/// <summary>
/// 符玄「心绪」— 四维情绪模型
///
/// 借鉴 Nexus 的 Emotion Model，用于 MotionAgent 的情感驱动：
/// - Valence (效价): -1..1  负面↔正面情绪
/// - Arousal (激活度): 0..1  平静↔兴奋
/// - Warmth (温暖度): -1..1  冷淡↔亲切
/// - Energy (精力): 0..1  疲惫↔活力充沛
///
/// 特性：
/// - 指数衰减，自动回归基线
/// - 信号源可叠加（空闲时间、用户互动、时间等）
/// - 格式化为文本注入 LLM prompt
/// </summary>
[Serializable]
public class EmotionState
{
    [Header("当前情绪值")]
    [Range(-1f, 1f)] public float valence = 0.2f;   // 效价
    [Range(0f, 1f)]  public float arousal = 0.3f;   // 激活度
    [Range(-1f, 1f)] public float warmth = 0.3f;    // 温暖度
    [Range(0f, 1f)]  public float energy = 0.5f;    // 精力

    [Header("基线值（情绪最终回归到此）")]
    [Range(-1f, 1f)] public float baseValence = 0.3f;
    [Range(0f, 1f)]  public float baseArousal = 0.3f;
    [Range(-1f, 1f)] public float baseWarmth = 0.3f;
    [Range(0f, 1f)]  public float baseEnergy = 0.5f;

    [Header("衰减速度（越小衰减越慢）")]
    public float decayHalfLife = 120f; // 半衰期（秒），默认2分钟回归一半

    /// <summary>上次情绪更新时间</summary>
    private float _lastUpdateTime = 0f;

    /// <summary>设置更新计时</summary>
    public void Init() { _lastUpdateTime = Time.time; }

    /// <summary>
    /// 每帧自动衰减
    /// </summary>
    public void TickDecay()
    {
        if (decayHalfLife <= 0f) return;
        float now = Time.time;
        float dt = now - _lastUpdateTime;
        if (dt <= 0f) return;
        _lastUpdateTime = now;

        // 指数衰减: value = base + (value - base) * 0.5^(dt/halfLife)
        float factor = Mathf.Pow(0.5f, dt / decayHalfLife);

        valence = baseValence + (valence - baseValence) * factor;
        arousal = baseArousal + (arousal - baseArousal) * factor;
        warmth = baseWarmth + (warmth - baseWarmth) * factor;
        energy = baseEnergy + (energy - baseEnergy) * factor;
    }

    /// <summary>
    /// 施加情绪信号（叠加偏移）
    /// </summary>
    public void ApplySignal(float dValence, float dArousal, float dWarmth, float dEnergy)
    {
        valence = Mathf.Clamp(valence + dValence, -1f, 1f);
        arousal = Mathf.Clamp(arousal + dArousal, 0f, 1f);
        warmth = Mathf.Clamp(warmth + dWarmth, -1f, 1f);
        energy = Mathf.Clamp(energy + dEnergy, 0f, 1f);
        _lastUpdateTime = Time.time;
    }

    /// <summary>
    /// 格式化为 LLM prompt 注入文本
    /// </summary>
    public string FormatForPrompt()
    {
        string vDesc = Describe(valence, "非常低落", "低落", "平和", "愉悦", "非常开心");
        string aDesc = arousal < 0.3f ? "安静" : arousal < 0.6f ? "平静" : "兴奋";
        string wDesc = Describe(warmth, "冷淡", "疏离", "友善", "温暖", "亲切");
        string eDesc = energy < 0.3f ? "疲惫" : energy < 0.6f ? "有精神" : "精力充沛";

        return $"【本座当前心境】效价={valence:+0.00;-0.00}({vDesc})，激活度={arousal:0.00}({aDesc})，温暖度={warmth:+0.00;-0.00}({wDesc})，精力={energy:0.00}({eDesc})";
    }

    /// <summary>获取情绪主导类型（用于动作风格选择）</summary>
    public string GetDominantMood()
    {
        if (valence < -0.3f && energy < 0.4f) return "sad";
        if (valence < -0.3f && arousal > 0.6f) return "angry";
        if (valence > 0.4f && arousal > 0.6f) return "excited";
        if (valence > 0.3f && energy < 0.4f) return "content";
        if (arousal < 0.3f && energy < 0.3f) return "sleepy";
        if (warmth > 0.4f) return "affectionate";
        return "neutral";
    }

    /// <summary>获取情绪色（用于 UI 指示）</summary>
    public Color GetEmotionColor()
    {
        if (valence > 0.3f) return Color.Lerp(Color.white, new Color(1f, 0.8f, 0.6f), arousal);
        if (valence < -0.3f) return Color.Lerp(new Color(0.6f, 0.7f, 1f), new Color(0.4f, 0.4f, 0.8f), arousal);
        return Color.Lerp(Color.white, new Color(0.9f, 0.9f, 1f), arousal);
    }

    private static string Describe(float val, params string[] labels)
    {
        int n = labels.Length;
        float step = 2f / n;
        int idx = Mathf.Clamp(Mathf.FloorToInt((val + 1f) / step), 0, n - 1);
        return labels[idx];
    }
}
