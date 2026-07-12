using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 动作规划器 — ActionAgent 阶段四
///
/// 职责：将自然语言动作意图或指定的参数目标拆解为时序参数序列。
/// 输入：动作描述（"开心地挥手"）或参数目标列表
/// 输出：MotionPlan → 一系列时间点上的参数状态快照
///
/// 与 MotionGenerator 的分工：
/// - MotionPlanner：决定"做什么、什么时候做"（时序编排）
/// - MotionGenerator：决定"怎么做"（具体参数值 + 插值曲线）
/// </summary>
public static class MotionPlanner
{
    // ──────────────────────────────────────────────
    //  公共类型
    // ──────────────────────────────────────────────

    /// <summary>动作计划 — 一系列按时间排列的参数关键帧</summary>
    public class MotionPlan
    {
        /// <summary>计划总持续时间（秒）</summary>
        public float TotalDuration;
        /// <summary>关键帧列表</summary>
        public List<KeyFrame> KeyFrames = new List<KeyFrame>();
        /// <summary>动作描述（用于调试/日志）</summary>
        public string Description;
        /// <summary>是否循环</summary>
        public bool Looping;
    }

    /// <summary>关键帧 — 在特定时间点的参数快照</summary>
    public class KeyFrame
    {
        /// <summary>时间点（秒，从动作开始算）</summary>
        public float Time;
        /// <summary>此时间点的参数值快照</summary>
        public Dictionary<string, float> Values = new Dictionary<string, float>();
        /// <summary>插值曲线类型（从前一帧到此帧）</summary>
        public InterpolationType Curve = InterpolationType.Smooth;
    }

    /// <summary>插值曲线类型</summary>
    public enum InterpolationType
    {
        /// <summary>线性</summary>
        Linear,
        /// <summary>平滑（默认，类似 smoothstep）</summary>
        Smooth,
        /// <summary>淡入（缓出）</summary>
        EaseOut,
        /// <summary>淡出（缓入）</summary>
        EaseIn,
        /// <summary>保持（突然跳变后保持）</summary>
        Hold,
        /// <summary>弹跳</summary>
        Bounce,
    }

    // ──────────────────────────────────────────────
    //  表情模板 — 常用表情的参数快照
    // ──────────────────────────────────────────────

    private static readonly Dictionary<string, Dictionary<string, float>> EXPRESSION_TEMPLATES = new()
    {
        ["happy"] = new() {
            {"eye_l_smile", 0.5f}, {"eye_r_smile", 0.5f},
            {"mouth_form", 0.3f}, {"brow_l_y", 0.1f}, {"brow_r_y", 0.1f}
        },
        ["sad"] = new() {
            {"brow_l_y", -0.2f}, {"brow_r_y", -0.2f},
            {"mouth_form", -0.3f}, {"eye_l_open", 0.7f}, {"eye_r_open", 0.7f}
        },
        ["angry"] = new() {
            {"brow_l_y", -0.3f}, {"brow_r_y", -0.3f},
            {"brow_l_form", 0.5f}, {"brow_r_form", 0.5f},
            {"eye_l_open", 0.9f}, {"eye_r_open", 0.9f},
            {"mouth_form", -0.2f}
        },
        ["surprised"] = new() {
            {"brow_l_y", 0.4f}, {"brow_r_y", 0.4f},
            {"eye_l_open", 1f}, {"eye_r_open", 1f},
            {"mouth_open_y", 0.4f}, {"head_angle_y", -5f}
        },
        ["sleepy"] = new() {
            {"eye_l_open", 0.2f}, {"eye_r_open", 0.2f},
            {"brow_l_y", -0.1f}, {"brow_r_y", -0.1f},
            {"mouth_open_y", 0.1f}, {"head_angle_y", 3f}
        },
        ["blush"] = new() {
            {"eye_l_smile", 0.3f}, {"eye_r_smile", 0.3f},
            {"head_angle_x", -2f}, {"head_angle_y", 2f},
            {"mouth_form", 0.2f}
        },
    };

    // ──────────────────────────────────────────────
    //  公开 API
    // ──────────────────────────────────────────────

    /// <summary>
    /// 从指定的参数目标创建一个简单动作计划
    /// </summary>
    /// <param name="targetParams">参数目标（语义名→值）</param>
    /// <param name="duration">动作持续时间（秒）</param>
    /// <param name="expression">可选的表情叠加</param>
    /// <returns>三阶段计划：从当前值→目标值→保持→回到默认</returns>
    public static MotionPlan PlanFromTargets(
        Dictionary<string, float> targetParams,
        float duration,
        string expression = null)
    {
        var plan = new MotionPlan
        {
            TotalDuration = duration + 0.5f,
            Description = $"control_body: {string.Join(", ", targetParams.Select(kv => $"{kv.Key}={kv.Value:F2}"))}",
            Looping = false
        };

        float rampUp = Mathf.Min(duration * 0.2f, 0.5f);
        float holdStart = rampUp;
        float holdEnd = rampUp + Mathf.Max(duration * 0.6f, 0.5f);

        // Phase 1: 从默认→目标（淡入）
        var phase1 = new KeyFrame { Time = rampUp, Curve = InterpolationType.EaseOut };
        foreach (var kv in targetParams)
            phase1.Values[kv.Key] = kv.Value;

        // 叠加表情
        if (!string.IsNullOrEmpty(expression) && EXPRESSION_TEMPLATES.TryGetValue(expression, out var expr))
        {
            foreach (var kv in expr)
                if (!phase1.Values.ContainsKey(kv.Key))
                    phase1.Values[kv.Key] = kv.Value;
        }

        plan.KeyFrames.Add(phase1);

        // Phase 2: 保持
        var phase2 = new KeyFrame { Time = holdEnd, Curve = InterpolationType.Smooth };
        foreach (var kv in phase1.Values)
            phase2.Values[kv.Key] = kv.Value;
        plan.KeyFrames.Add(phase2);

        // Phase 3: 回到默认（所有参数归零，即回到空闲态）
        if (duration > 1f)
        {
            var phase3 = new KeyFrame { Time = plan.TotalDuration, Curve = InterpolationType.EaseIn };
            foreach (var kv in phase1.Values)
                phase3.Values[kv.Key] = 0f;
            plan.KeyFrames.Add(phase3);
        }

        return plan;
    }

    /// <summary>
    /// 从自然语言描述创建一个动作计划
    /// </summary>
    /// <remarks>
    /// 这是一个基础实现。完整版应接 GLM API 将描述转为结构化参数序列。
    /// 当前使用预定义的"语义动作模板"。
    /// </remarks>
    public static MotionPlan PlanFromDescription(
        string description,
        float duration,
        Live2DParameterMapper mapper)
    {
        // 尝试匹配已知动作模板
        var knownTemplate = MatchKnownMotion(description, mapper);
        if (knownTemplate != null)
            return knownTemplate;

        // 回退：生成一个通用动作（微摇头/晃动）
        return GenerateGenericMotion(duration);
    }

    // ──────────────────────────────────────────────
    //  内部方法
    // ──────────────────────────────────────────────

    /// <summary>匹配已知动作模板</summary>
    private static MotionPlan MatchKnownMotion(string description, Live2DParameterMapper mapper)
    {
        string desc = description.ToLowerInvariant();

        // 挥手
        if (desc.Contains("挥手") || desc.Contains("wave") || desc.Contains("打招呼"))
        {
            var plan = new MotionPlan { TotalDuration = 3f, Description = "挥手", Looping = false };
            float[] times = { 0f, 0.3f, 0.8f, 1.3f, 1.8f, 2.3f, 2.8f, 3f };
            float[] armValues = { 0f, 0.6f, 1f, 0.6f, 1f, 0.6f, 0.3f, 0f };

            for (int i = 0; i < times.Length; i++)
            {
                var kf = new KeyFrame { Time = times[i], Curve = InterpolationType.Smooth };
                kf.Values["arm_right_upper"] = armValues[i];
                kf.Values["arm_right_lower"] = armValues[i] * 0.5f;
                kf.Values["eye_l_smile"] = Mathf.Lerp(0f, 0.5f, armValues[i]);
                kf.Values["eye_r_smile"] = Mathf.Lerp(0f, 0.5f, armValues[i]);
                plan.KeyFrames.Add(kf);
            }
            return plan;
        }

        // 点头
        if (desc.Contains("点头") || desc.Contains("nod"))
        {
            var plan = new MotionPlan { TotalDuration = 2f, Description = "点头", Looping = false };
            plan.KeyFrames.Add(new KeyFrame { Time = 0.2f, Curve = InterpolationType.EaseOut, Values = { {"head_angle_y", 8f} } });
            plan.KeyFrames.Add(new KeyFrame { Time = 0.5f, Curve = InterpolationType.EaseIn, Values = { {"head_angle_y", 0f} } });
            plan.KeyFrames.Add(new KeyFrame { Time = 0.8f, Curve = InterpolationType.EaseOut, Values = { {"head_angle_y", 6f} } });
            plan.KeyFrames.Add(new KeyFrame { Time = 1.2f, Curve = InterpolationType.EaseIn, Values = { {"head_angle_y", 0f} } });
            plan.KeyFrames.Add(new KeyFrame { Time = 2f, Curve = InterpolationType.Smooth, Values = { {"head_angle_y", 0f} } });
            return plan;
        }

        // 摇头
        if (desc.Contains("摇头") || desc.Contains("摇") || desc.Contains("shak"))
        {
            var plan = new MotionPlan { TotalDuration = 2.5f, Description = "摇头", Looping = false };
            for (int i = 0; i < 5; i++)
            {
                float t = i * 0.4f + 0.2f;
                float sign = (i % 2 == 0) ? 1f : -1f;
                plan.KeyFrames.Add(new KeyFrame
                {
                    Time = t,
                    Curve = InterpolationType.Smooth,
                    Values = { {"head_angle_x", sign * 8f} }
                });
            }
            plan.KeyFrames.Add(new KeyFrame { Time = 2.2f, Curve = InterpolationType.EaseIn, Values = { {"head_angle_x", 0f} } });
            return plan;
        }

        // 鞠躬
        if (desc.Contains("鞠躬") || desc.Contains("bow"))
        {
            var plan = new MotionPlan { TotalDuration = 2.5f, Description = "鞠躬", Looping = false };
            plan.KeyFrames.Add(new KeyFrame { Time = 0.5f, Curve = InterpolationType.EaseOut, Values = {
                {"body_angle_x", 25f}, {"head_angle_y", 10f}
            } });
            plan.KeyFrames.Add(new KeyFrame { Time = 1.5f, Curve = InterpolationType.Hold, Values = {
                {"body_angle_x", 25f}, {"head_angle_y", 10f}
            } });
            plan.KeyFrames.Add(new KeyFrame { Time = 2.5f, Curve = InterpolationType.EaseIn, Values = {
                {"body_angle_x", 0f}, {"head_angle_y", 0f}
            } });
            return plan;
        }

        // 伸懒腰
        if (desc.Contains("伸懒腰") || desc.Contains("stretch"))
        {
            var plan = new MotionPlan { TotalDuration = 4f, Description = "伸懒腰", Looping = false };
            plan.KeyFrames.Add(new KeyFrame { Time = 0.5f, Curve = InterpolationType.EaseOut, Values = {
                {"arm_right_upper", 1f}, {"arm_left_upper", 1f},
                {"arm_right_lower", 0.3f}, {"arm_left_lower", 0.3f},
                {"head_angle_y", 5f}, {"mouth_open_y", 0.2f}
            } });
            plan.KeyFrames.Add(new KeyFrame { Time = 2f, Curve = InterpolationType.Hold, Values = {
                {"arm_right_upper", 1f}, {"arm_left_upper", 1f},
                {"arm_right_lower", 0.3f}, {"arm_left_lower", 0.3f},
                {"head_angle_y", 5f}, {"mouth_open_y", 0.2f}
            } });
            plan.KeyFrames.Add(new KeyFrame { Time = 3.5f, Curve = InterpolationType.EaseIn, Values = {
                {"arm_right_upper", 0f}, {"arm_left_upper", 0f},
                {"arm_right_lower", 0f}, {"arm_left_lower", 0f},
                {"head_angle_y", 0f}, {"mouth_open_y", 0f}
            } });
            plan.KeyFrames.Add(new KeyFrame { Time = 4f, Curve = InterpolationType.Smooth, Values = {
                {"arm_right_upper", 0f}, {"arm_left_upper", 0f}
            } });
            return plan;
        }

        return null;
    }

    /// <summary>生成通用微动作（未匹配时的回退）</summary>
    private static MotionPlan GenerateGenericMotion(float duration)
    {
        var plan = new MotionPlan
        {
            TotalDuration = duration,
            Description = "泛用微动",
            Looping = false
        };

        float mid = duration * 0.4f;
        float end = duration;

        plan.KeyFrames.Add(new KeyFrame
        {
            Time = mid,
            Curve = InterpolationType.Smooth,
            Values = {
                {"head_angle_x", 3f},
                {"head_angle_y", 2f},
                {"eye_ball_x", 0.2f},
                {"eye_ball_y", -0.1f}
            }
        });

        plan.KeyFrames.Add(new KeyFrame
        {
            Time = end,
            Curve = InterpolationType.Smooth,
            Values = {
                {"head_angle_x", 0f},
                {"head_angle_y", 0f},
                {"eye_ball_x", 0f},
                {"eye_ball_y", 0f}
            }
        });

        return plan;
    }
}
