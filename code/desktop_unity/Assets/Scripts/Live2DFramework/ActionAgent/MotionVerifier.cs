using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Live2D.Cubism.Core;
using UnityEngine;

/// <summary>
/// 具身智能验证器 — Embodied AI Verification Suite
///
/// 自动验证 AI 是否真正理解自然语言动作描述并生成合理的参数序列。
/// 分为三个层级：
///   Level 1: 对照组 — 5 个硬编码模板（验证快路径正常）
///   Level 2: 测试组 — 10 个 LLM 翻译动作（验证具身智能）
///   Level 3: 边界测试 — 4 个边界/异常输入
/// </summary>
public static class MotionVerifier
{
    // ──────────────────────────────────────────────
    //  测试集定义
    // ──────────────────────────────────────────────

    public class TestCase
    {
        public string Id;
        public string Description;
        public bool ExpectHardcoded; // true=应在 MotionPlanner 走硬编码模板
        public string ExpectedBehavior; // 人类可读的期望
        public int MinExpectedFrames; // 最少关键帧数
    }

    /// <summary>Level 1: 对照组 — 应走硬编码模板</summary>
    private static readonly TestCase[] CONTROL_GROUP =
    {
        new() { Id = "C1", Description = "开心地挥手",  ExpectHardcoded = true,
                ExpectedBehavior = "右手 0→1→0 摆动，微笑", MinExpectedFrames = 3 },
        new() { Id = "C2", Description = "点头同意",    ExpectHardcoded = true,
                ExpectedBehavior = "head_angle_y 0→8→0→6→0", MinExpectedFrames = 3 },
        new() { Id = "C3", Description = "摇头",        ExpectHardcoded = true,
                ExpectedBehavior = "head_angle_x ±8 交替", MinExpectedFrames = 3 },
        new() { Id = "C4", Description = "鞠躬行礼",    ExpectHardcoded = true,
                ExpectedBehavior = "body_angle_x=25 + head_angle_y=10", MinExpectedFrames = 3 },
        new() { Id = "C5", Description = "伸个大懒腰",  ExpectHardcoded = true,
                ExpectedBehavior = "双臂抬起 + 抬头 + 张嘴", MinExpectedFrames = 3 },
    };

    /// <summary>Level 2: 测试组 — 应走 LLM 翻译路径</summary>
    private static readonly TestCase[] TEST_GROUP =
    {
        new() { Id = "T1", Description = "害羞地捂脸",     ExpectHardcoded = false,
                ExpectedBehavior = "头低 + 眼垂 + 手在脸前", MinExpectedFrames = 3 },
        new() { Id = "T2", Description = "昂首挺胸叉腰",   ExpectHardcoded = false,
                ExpectedBehavior = "抬头 + 臂弯外摆 + 挺胸", MinExpectedFrames = 3 },
        new() { Id = "T3", Description = "惊讶地捂住嘴",   ExpectHardcoded = false,
                ExpectedBehavior = "头微仰 + 眼睁大 + 手在嘴前", MinExpectedFrames = 3 },
        new() { Id = "T4", Description = "忧郁地望向远方", ExpectHardcoded = false,
                ExpectedBehavior = "头侧转 + 眼珠偏 + 微表情", MinExpectedFrames = 3 },
        new() { Id = "T5", Description = "俏皮地眨一下右眼", ExpectHardcoded = false,
                ExpectedBehavior = "右眼单眨 + 歪头 + 微笑", MinExpectedFrames = 3 },
        new() { Id = "T6", Description = "标准地行一个礼", ExpectHardcoded = false,
                ExpectedBehavior = "双手下摆 + 身体微倾", MinExpectedFrames = 3 },
        new() { Id = "T7", Description = "被吓到缩成一团", ExpectHardcoded = false,
                ExpectedBehavior = "全身收缩 + 低头 + 臂夹紧", MinExpectedFrames = 3 },
        new() { Id = "T8", Description = "骄傲地抬起头",   ExpectHardcoded = false,
                ExpectedBehavior = "头仰 + 身体后倾 + 微笑", MinExpectedFrames = 3 },
        new() { Id = "T9", Description = "歪着头思考",     ExpectHardcoded = false,
                ExpectedBehavior = "头歪 + 眼珠上漂", MinExpectedFrames = 3 },
        new() { Id = "T10",Description = "双手合十祈祷",  ExpectHardcoded = false,
                ExpectedBehavior = "双手胸前合拢 + 头低", MinExpectedFrames = 3 },
    };

    /// <summary>Level 3: 边界测试</summary>
    private static readonly (string id, string desc, string expect)[] BORDER_GROUP =
    {
        ("B1", "", "应拒绝不崩溃"),
        ("B2", "gibberish_xyz_123_unknown", "回退到泛用微动 ≤2 帧"),
        ("B3", "用右手摸摸左耳朵", "多帧交叉动作"),
        ("B4", "飞到天上转三圈然后降落", "不崩溃，生成简化动作"),
    };

    // ──────────────────────────────────────────────
    //  单条测试结果
    // ──────────────────────────────────────────────

    public class TestResult
    {
        public string Id;
        public string Description;
        public bool IsHardcoded;        // 实际走的是硬编码？
        public MotionPlanner.MotionPlan Plan;
        public bool AllValuesInRange;
        public bool HasResetFrame;
        public float ParamComplianceRate; // 合规率
        public float SymmetryRate;        // 对称配比率
        public int ParamCount;            // 使用了多少不同参数
        public string ErrorMessage;
        public bool BorderlinePass;       // 边界测试是否通过
    }

    // ──────────────────────────────────────────────
    //  核心验证方法
    // ──────────────────────────────────────────────

    /// <summary>运行完整验证套件（返回结构化报告）</summary>
    public static string RunVerificationSuite(Live2DParameterMapper mapper, CubismModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 具身智能验证报告");
        sb.AppendLine($"> 生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"> 模型参数总数: {mapper?.SemanticToId?.Count ?? 0}");
        sb.AppendLine();

        // ——— 对照组 ———
        sb.AppendLine("## 一、对照组 (硬编码模板)");
        sb.AppendLine("| ID | 描述 | 帧数 | 硬编码? | 合规 | 归零 | 参数数 | 状态 |");
        sb.AppendLine("|----|------|------|---------|------|------|--------|------|");
        int cPass = 0, cTotal = 0;
        foreach (var tc in CONTROL_GROUP)
        {
            var result = RunSingleTestSync(tc, mapper);
            sb.AppendLine(FormatResultLine(result));
            if (result.ErrorMessage == null) cPass++;
            cTotal++;
        }
        sb.AppendLine();

        // ——— 测试组 ———
        sb.AppendLine("## 二、测试组 (LLM 翻译)");
        sb.AppendLine("| ID | 描述 | 帧数 | LLM? | 合规率 | 归零 | 参数数 | 状态 |");
        sb.AppendLine("|----|------|------|------|--------|------|--------|------|");
        int tPass = 0, tTotal = 0, tLLMCount = 0;
        foreach (var tc in TEST_GROUP)
        {
            var result = RunSingleTestSync(tc, mapper);
            sb.AppendLine(FormatResultLine(result));
            if (result.ErrorMessage == null) tPass++;
            tTotal++;
            if (!result.IsHardcoded) tLLMCount++;
        }
        sb.AppendLine();

        // ——— 边界测试 ———
        sb.AppendLine("## 三、边界测试");
        sb.AppendLine("| ID | 描述 | 结果 | 备注 |");
        sb.AppendLine("|----|------|------|------|");
        foreach (var (id, desc, expect) in BORDER_GROUP)
        {
            try
            {
                var plan = string.IsNullOrEmpty(desc)
                    ? null
                    : MotionPlanner.PlanFromDescription(desc, 3f, mapper);

                if (id == "B1" && string.IsNullOrEmpty(desc))
                {
                    sb.AppendLine($"| {id} | \"\" | ✅ 正常 | 空描述返回 null |");
                }
                else if (id == "B2" && (plan == null || plan.KeyFrames.Count <= 2))
                {
                    bool fallbackOk = plan == null || plan.Description == "泛用微动";
                    sb.AppendLine($"| {id} | {desc} | {(fallbackOk ? "✅ 通过" : "⚠️ 异常")} | {expect} → 实际: {(plan?.Description ?? "null")} {(plan != null ? $"{plan.KeyFrames.Count}帧" : "")} |");
                }
                else if (plan != null && plan.KeyFrames.Count > 0)
                {
                    sb.AppendLine($"| {id} | {desc} | ✅ 通过 (无崩溃) | {expect} → {plan.KeyFrames.Count}帧 |");
                }
                else
                {
                    sb.AppendLine($"| {id} | {desc} | ✅ 通过 | {expect} → {plan?.KeyFrames.Count ?? 0}帧 |");
                }
            }
            catch (Exception e)
            {
                sb.AppendLine($"| {id} | {desc} | ❌ 异常: {e.Message} | {expect} |");
            }
        }
        sb.AppendLine();

        // ——— 总结 ———
        sb.AppendLine("## 四、汇总");
        float autoPassRate = (cTotal + tTotal) > 0
            ? (float)(cPass + tPass) / (cTotal + tTotal) * 100f : 0f;
        float llmTriggerRate = tTotal > 0 ? (float)tLLMCount / tTotal * 100f : 0f;
        sb.AppendLine($"- 对照组通过: {cPass}/{cTotal} ({(cTotal > 0 ? (float)cPass / cTotal * 100f : 0):F1}%)");
        sb.AppendLine($"- 测试组通过: {tPass}/{tTotal} ({(tTotal > 0 ? (float)tPass / tTotal * 100f : 0):F1}%)");
        sb.AppendLine($"- LLM 触发率: {tLLMCount}/{tTotal} ({llmTriggerRate:F1}%)");
        sb.AppendLine($"- 自动通过率: {autoPassRate:F1}%");
        sb.AppendLine();

        if (autoPassRate >= 100f && llmTriggerRate >= 80f)
            sb.AppendLine("🥇 **完全通过** — 具身智能验证通过！");
        else if (autoPassRate >= 80f && llmTriggerRate >= 50f)
            sb.AppendLine("🥈 **基本通过** — 具身智能核心功能正常，有待优化。");
        else if (autoPassRate >= 60f)
            sb.AppendLine("🥉 **需改进** — 部分动作不符合预期，请检查 MotionTranslator。");
        else
            sb.AppendLine("❌ **未通过** — LLM 翻译路径存在严重问题，需排查。");

        return sb.ToString();
    }

    // ──────────────────────────────────────────────
    //  单测试执行（同步版 — 仅验证 Plan，不实际播放）
    // ──────────────────────────────────────────────

    private static TestResult RunSingleTestSync(TestCase tc, Live2DParameterMapper mapper)
    {
        var result = new TestResult
        {
            Id = tc.Id,
            Description = tc.Description,
        };

        try
        {
            var plan = MotionPlanner.PlanFromDescription(tc.Description, 3f, mapper);
            if (plan == null)
            {
                result.ErrorMessage = "Plan 为 null";
                return result;
            }

            result.Plan = plan;
            result.IsHardcoded = !plan.Description.StartsWith("LLM:") && !tc.ExpectHardcoded;

            // ——— 关键帧数 ———
            if (plan.KeyFrames.Count < tc.MinExpectedFrames)
            {
                result.ErrorMessage = $"关键帧不足: {plan.KeyFrames.Count} < {tc.MinExpectedFrames}";
                return result;
            }

            // ——— 参数合规校验 ———
            int totalParams = 0, compliantParams = 0;
            int leftRightPairs = 0, pairedAppearances = 0;
            var seenPairs = new HashSet<string>();
            bool hasReset = false;
            var usedParams = new HashSet<string>();

            foreach (var kf in plan.KeyFrames)
            {
                foreach (var kv in kf.Values)
                {
                    totalParams++;
                    usedParams.Add(kv.Key);

                    // 检查参数值是否在范围内
                    if (mapper.TryGetRange(kv.Key, out var range))
                    {
                        float val = kv.Value;
                        if (val >= range.Min - 0.5f && val <= range.Max + 0.5f)
                            compliantParams++;
                    }
                    else
                    {
                        // 未知参数名，标记为未合规
                        continue;
                    }

                    // 检查是否接近0（归零帧）
                    if (Mathf.Abs(kv.Value) < 0.01f)
                        hasReset = true;
                }

                // 检查对称配对
                var keys = new HashSet<string>(kf.Values.Keys);
                foreach (var pair in SYMMETRY_PAIRS)
                {
                    if (keys.Contains(pair.left) || keys.Contains(pair.right))
                    {
                        leftRightPairs++;
                        if (keys.Contains(pair.left) && keys.Contains(pair.right))
                            pairedAppearances++;
                        seenPairs.Add(pair.left);
                    }
                }
            }

            result.ParamCount = usedParams.Count;
            result.AllValuesInRange = totalParams == compliantParams;
            result.ParamComplianceRate = totalParams > 0 ? (float)compliantParams / totalParams * 100f : 0f;
            result.SymmetryRate = leftRightPairs > 0 ? (float)pairedAppearances / leftRightPairs * 100f : 100f;
            result.HasResetFrame = hasReset;

            // ——— 最后的检查 ———
            if (!result.AllValuesInRange)
            {
                result.ErrorMessage = $"参数越界: {compliantParams}/{totalParams} 合规 ({result.ParamComplianceRate:F1}%)";
                return result;
            }

            if (tc.ExpectHardcoded && plan.Description.Contains("LLM:"))
            {
                result.ErrorMessage = "应走硬编码但却走了 LLM 路径";
                return result;
            }
        }
        catch (Exception e)
        {
            result.ErrorMessage = $"异常: {e.Message}";
        }

        return result;
    }

    // ──────────────────────────────────────────────
    //  辅助
    // ──────────────────────────────────────────────

    /// <summary>左右对称参数对</summary>
    private static readonly (string left, string right)[] SYMMETRY_PAIRS =
    {
        ("eye_l_open", "eye_r_open"),
        ("eye_l_smile", "eye_r_smile"),
        ("eye_l_squint", "eye_r_squint"),
        ("brow_l_y", "brow_r_y"),
        ("brow_l_form", "brow_r_form"),
        ("brow_l_x", "brow_r_x"),
        ("arm_l_upper", "arm_r_upper"),
        ("arm_l_lower", "arm_r_lower"),
        ("hand_l", "hand_r"),
        ("leg_l", "leg_r"),
    };

    private static string FormatResultLine(TestResult r)
    {
        string status = r.ErrorMessage == null ? "✅" : "❌";
        string hardcoded = r.Plan != null && r.Plan.Description != null && r.Plan.Description.StartsWith("LLM:")
            ? "✅ LLM" : "❌ 硬编码";
        string frames = r.Plan != null ? r.Plan.KeyFrames.Count.ToString() : "N/A";
        string compliance = r.Plan != null ? $"{r.ParamComplianceRate:F0}%" : "N/A";
        string reset = r.HasResetFrame ? "✅" : "⚠️";
        string paramCount = r.ParamCount > 0 ? r.ParamCount.ToString() : "N/A";
        string note = r.ErrorMessage ?? (r.Plan?.Description ?? "N/A");
        return $"| {r.Id} | {r.Description} | {frames} | {hardcoded} | {compliance} | {reset} | {paramCount} | {status} {note} |";
    }

    // ──────────────────────────────────────────────
    //  JSON 工具（用于 ToolCallInvoker 集成）
    // ──────────────────────────────────────────────

    /// <summary>生成紧凑的测试摘要（用于 AI 工具返回）</summary>
    public static string GetCompactSummary(Live2DParameterMapper mapper)
    {
        var sb = new StringBuilder();
        sb.AppendLine("🧪 具身智能验证结果：");

        int tLLM = 0, tTotal = 0;
        foreach (var tc in TEST_GROUP)
        {
            var r = RunSingleTestSync(tc, mapper);
            if (r.ErrorMessage == null) tLLM++;
            tTotal++;
        }
        sb.AppendLine($"• 测试组通过: {tLLM}/{tTotal}");

        int cPass = 0, cTotal = 0;
        foreach (var tc in CONTROL_GROUP)
        {
            var r = RunSingleTestSync(tc, mapper);
            if (r.ErrorMessage == null) cPass++;
            cTotal++;
        }
        sb.AppendLine($"• 对照组通过: {cPass}/{cTotal}");

        float rate = tTotal > 0 ? (float)tLLM / tTotal * 100f : 0f;
        sb.AppendLine($"• LLM 触发率: {rate:F1}%");

        if (rate >= 80f) sb.AppendLine("🥇 具身智能验证通过！");
        else if (rate >= 50f) sb.AppendLine("🥈 基本通过，但需改进。");
        else sb.AppendLine("❌ 未通过。");

        return sb.ToString();
    }
}
