using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 安全校验器 — ActionAgent 阶段四
///
/// 职责：校验参数操作的安全性，包括：
/// 1. 参数值范围钳制（沿用 Mapper 的自动钳制）
/// 2. 参数间的冲突检测（如左右对称参数不一致）
/// 3. 前提条件检查（如某个参数需要另一个参数先置位）
/// 4. 危险操作告警（参数组合可能导致视觉异常）
/// </summary>
public static class SafetyValidator
{
    /// <summary>校验结果</summary>
    public struct ValidationResult
    {
        /// <summary>是否通过校验（警告也视为通过）</summary>
        public bool IsValid;
        /// <summary>警告列表</summary>
        public List<string> Warnings;
        /// <summary>风险等级: 0=安全, 1=警告, 2=危险</summary>
        public int RiskLevel;
    }

    // ──────────────────────────────────────────────
    //  已知的冲突规则
    // ──────────────────────────────────────────────

    /// <summary>互斥参数组 — 同一组内不能同时设置到非默认值</summary>
    private static readonly string[][] EXCLUSIVE_GROUPS =
    {
        new[] { "eye_l_open", "eye_l_smile" },
        new[] { "eye_r_open", "eye_r_smile" },
    };

    /// <summary>对称对 — 应该一起设置的左右对称参数</summary>
    private static readonly (string left, string right)[] SYMMETRIC_PAIRS =
    {
        ("eye_l_open", "eye_r_open"),
        ("eye_l_smile", "eye_r_smile"),
        ("brow_l_y", "brow_r_y"),
        ("brow_l_x", "brow_r_x"),
        ("brow_l_form", "brow_r_form"),
    };

    /// <summary>参数极限值保护 — 某些参数在极端值可能有风险</summary>
    private static readonly Dictionary<string, (float min, float max, string warning)> EXTREME_WARNINGS = new()
    {
        ["eye_l_open"] = (0f, 1f, "眼睛完全闭合(0)会使眼睛消失"),
        ["eye_r_open"] = (0f, 1f, "眼睛完全闭合(0)会使眼睛消失"),
        ["mouth_open_y"] = (0.8f, 1f, "嘴巴张太大可能不自然"),
        ["mouth_jaw"] = (0.8f, 1f, "下颌张太大可能不自然"),
        ["head_angle_y"] = (-25f, 25f, "过度低头/抬头"),
        ["head_angle_x"] = (-30f, 30f, "过度转头"),
    };

    // ──────────────────────────────────────────────
    //  公开 API
    // ──────────────────────────────────────────────

    /// <summary>校验单个参数（范围钳制）</summary>
    public static ValidationResult Validate(string semantic, ref float value, Live2DParameterMapper mapper)
    {
        var result = new ValidationResult { IsValid = true, Warnings = new List<string>(), RiskLevel = 0 };

        if (!mapper.TryGetRange(semantic, out var range))
        {
            result.Warnings.Add($"未知参数 {semantic}，无法校验范围");
            return result;
        }

        // 钳制到有效范围
        float clamped = Mathf.Clamp(value, range.Min, range.Max);
        if (Mathf.Abs(clamped - value) > 0.001f)
        {
            result.Warnings.Add($"{semantic} 值 {value:F2} 超出范围 [{range.Min:F1}~{range.Max:F1}]，已钳制为 {clamped:F2}");
            value = clamped;
        }

        // 极端值警告
        if (EXTREME_WARNINGS.TryGetValue(semantic, out var extreme))
        {
            if (value <= extreme.min + 0.05f || value >= extreme.max - 0.05f)
            {
                result.Warnings.Add($"⚠ {semantic}={value:F2}: {extreme.warning}");
                result.RiskLevel = 1;
            }
        }

        return result;
    }

    /// <summary>校验批量参数</summary>
    public static List<ValidationResult> ValidateBulk(Dictionary<string, float> paramValues, Live2DParameterMapper mapper)
    {
        var results = new List<ValidationResult>();
        var clampedCopy = new Dictionary<string, float>(paramValues);

        // 1. 逐个参数校验
        foreach (var kv in clampedCopy)
        {
            float val = kv.Value;
            var r = Validate(kv.Key, ref val, mapper);
            clampedCopy[kv.Key] = val;
            results.Add(r);
        }

        // 2. 互斥组检查
        for (int g = 0; g < EXCLUSIVE_GROUPS.Length; g++)
        {
            var group = EXCLUSIVE_GROUPS[g];
            bool anyActive = false;
            foreach (var sem in group)
            {
                if (clampedCopy.ContainsKey(sem))
                {
                    if (anyActive)
                    {
                        results.Add(new ValidationResult
                        {
                            IsValid = true,
                            Warnings = new List<string> { $"⚠ 互斥参数冲突: {group[0]} 和 {group[1]} 同时设置可能产生异常" },
                            RiskLevel = 1
                        });
                        break;
                    }
                    anyActive = true;
                }
            }
        }

        // 3. 对称参数建议
        foreach (var (left, right) in SYMMETRIC_PAIRS)
        {
            bool hasLeft = clampedCopy.ContainsKey(left);
            bool hasRight = clampedCopy.ContainsKey(right);
            if (hasLeft && !hasRight)
            {
                float leftVal = clampedCopy[left];
                results.Add(new ValidationResult
                {
                    IsValid = true,
                    Warnings = new List<string> { $"💡 建议同时设置对称参数 {right} ≈ {leftVal:F2}" },
                    RiskLevel = 0
                });
            }
            else if (!hasLeft && hasRight)
            {
                float rightVal = clampedCopy[right];
                results.Add(new ValidationResult
                {
                    IsValid = true,
                    Warnings = new List<string> { $"💡 建议同时设置对称参数 {left} ≈ {rightVal:F2}" },
                    RiskLevel = 0
                });
            }
        }

        return results;
    }

    /// <summary>获取参数安全范围描述</summary>
    public static string GetSafeRangeDescription(string semantic, Live2DParameterMapper mapper)
    {
        if (!mapper.TryGetRange(semantic, out var range))
            return $"未知范围";

        if (EXTREME_WARNINGS.TryGetValue(semantic, out var extreme))
        {
            float safeMin = Mathf.Max(range.Min, extreme.min);
            float safeMax = Mathf.Min(range.Max, extreme.max);
            return $"[{safeMin:F1}~{safeMax:F1}]（推荐） / [{range.Min:F1}~{range.Max:F1}]（物理极限）";
        }

        return $"[{range.Min:F1}~{range.Max:F1}]";
    }
}
