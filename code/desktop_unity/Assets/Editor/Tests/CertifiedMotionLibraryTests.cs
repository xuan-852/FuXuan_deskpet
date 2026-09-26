using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

public class CertifiedMotionLibraryTests
{
    [Test] public void 库内条目唯一且基本有效()
    {
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (var motion in CertifiedMotionLibrary.Entries)
        {
            Assert.IsTrue(seen.Add(motion.SkillId), "duplicate skill id: " + motion.SkillId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(motion.SemanticBoundary), "missing semantic: " + motion.SkillId);
            Assert.Greater(motion.DurationSeconds, 0f, motion.SkillId);
            Assert.AreNotEqual(EmbodiedResource.None, motion.Resources, motion.SkillId);
            Assert.AreEqual(64, motion.PacketSha256.Length, motion.SkillId);
            Assert.AreEqual(64, motion.CurveSha256.Length, "missing curve hash: " + motion.SkillId);
            Assert.GreaterOrEqual(motion.NaturalnessScore, NaturalnessGate.MinimumScore, motion.SkillId);
        }
    }

    [Test]
    public void 被篡改的认证曲线必须被完整性校验拒绝()
    {
        Assert.IsTrue(CertifiedMotionLibrary.TryGet("external_Hiyori_Hiyori_m05", out var motion));
        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, "tampered-curve");
            Assert.IsFalse(CertifiedMotionLibrary.TryVerifyCurveFile(motion, path, out var reason));
            Assert.AreEqual("curve-hash-mismatch", reason);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Test]
    public void 自制单臂抬起评审曲线必须与登记哈希一致()
    {
        Assert.IsTrue(CertifiedMotionLibrary.TryGet("screen_side_arm_raise", out var motion));
        string path = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(path, "{\n  \"candidateId\": \"screen_side_arm_raise\",\n  \"durationSeconds\": 2.4,\n  \"curves\": [\n    {\n      \"parameterId\": \"Param94\",\n      \"segments\": [\n        0,\n        0,\n        1,\n        0.24,\n        0,\n        0.64,\n        15,\n        0.97,\n        15,\n        2,\n        1.2,\n        15,\n        1,\n        1.52,\n        15,\n        2.16,\n        0,\n        2.4,\n        0\n      ]\n    }\n  ]\n}\n", new UTF8Encoding(false));
            Assert.IsTrue(CertifiedMotionLibrary.TryVerifyCurveFile(motion, path, out var reason), reason);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Test] public void 库内全部技能都已注册进准入()
    {
        foreach (var motion in CertifiedMotionLibrary.Entries)
            Assert.IsTrue(EmbodiedRuntimeAdmission.IsSkillAdmissible(motion.SkillId), motion.SkillId);
        Assert.IsTrue(EmbodiedRuntimeAdmission.IsSkillAdmissible("screen_side_arm_raise"));
    }

    [Test]
    public void 点头候选曲线具有固定语义并回到基线()
    {
        Assert.IsTrue(CertifiedMotionLibrary.TryGet("acknowledge_nod", out var motion));
        // v4（2026-09-27）：ParamAngleY 低头 + ParamBodyAngleY 身体跟随（约 1/5 幅度滞后）；
        // 不写眼球参数——认证动作期间唯一的眼动来源，合成补偿会读作乱瞟（用户实测反馈）。
        Assert.AreEqual(EmbodiedResource.Body, motion.Resources);
        Assert.AreEqual(2.0f, motion.DurationSeconds, 0.001f);
        StringAssert.Contains("两次轻微向下点头", motion.SemanticBoundary);
        // 2026-09-27：v2 曲线通过真实可见窗口人工评审后向 AI 开放（truth 文档记录证据）。
        Assert.IsTrue(motion.LlmExposed);
        Assert.IsTrue(motion.IsBuiltIn);

        string path = System.IO.Path.Combine(Application.dataPath, "Resources", "Live2D", "CertifiedMotions", "acknowledge_nod.json");
        Assert.IsTrue(File.Exists(path), "built-in candidate missing: " + path);
        string json = File.ReadAllText(path);
        Assert.IsTrue(CertifiedMotionLibrary.TryVerifyCurveText(motion, json, out var reason), reason);
        TextAsset builtIn = Resources.Load<TextAsset>(motion.BuiltInResourcePath);
        Assert.IsNotNull(builtIn, "built-in Resources asset missing");
        Assert.IsTrue(CertifiedMotionLibrary.TryVerifyCurveText(motion, builtIn.text, out reason), reason);
        var candidate = JsonUtility.FromJson<EmbodiedMotionCandidate>(json);
        Assert.IsNotNull(candidate);
        Assert.AreEqual("acknowledge_nod", candidate.candidateId);
        Assert.AreEqual(2, candidate.curves.Length);
        Assert.AreEqual("ParamAngleY", candidate.curves[0].parameterId);
        Assert.AreEqual("ParamBodyAngleY", candidate.curves[1].parameterId);
        var curve = new EmbodiedMotionCurve(candidate.curves[0].parameterId,
            Array.ConvertAll(candidate.curves[0].segments, value => (double)value), candidate.durationSeconds);
        Assert.AreEqual(0f, curve.Evaluate(0.0), 0.001f);
        Assert.AreEqual(-13f, curve.Evaluate(0.35), 0.001f);
        Assert.AreEqual(0f, curve.Evaluate(0.7), 0.001f);
        Assert.AreEqual(-9f, curve.Evaluate(0.98), 0.001f);
        Assert.AreEqual(0f, curve.Evaluate(1.26), 0.001f);
        Assert.AreEqual(0f, curve.Evaluate(2.0), 0.001f);
        // v2 曲线（2026-09-26）：负角度低头两次（-13/-9，幅度递减），全程不得高于基线——
        // v1 正角度（+8/+6）实机读作“抬头”而非“点头”，人工评审否决。
        foreach (float sample in new[] { 0.15f, 0.2f, 0.5f, 0.85f, 1.1f })
        {
            float value = curve.Evaluate(sample);
            Assert.LessOrEqual(value, 0.001f, $"t={sample}: 点头曲线不得高于基线");
            Assert.GreaterOrEqual(value, -13.001f, $"t={sample}");
        }
        // v3 身体跟随：滞后于头部、幅度约 1/5、平滑单次起伏（官方动作配比 0.15~0.2）。
        var bodyCurve = new EmbodiedMotionCurve(candidate.curves[1].parameterId,
            Array.ConvertAll(candidate.curves[1].segments, value => (double)value), candidate.durationSeconds);
        Assert.AreEqual(0f, bodyCurve.Evaluate(0.0), 0.001f);
        Assert.AreEqual(-2.6f, bodyCurve.Evaluate(0.9), 0.001f);
        Assert.AreEqual(0f, bodyCurve.Evaluate(1.75), 0.001f);
        Assert.AreEqual(0f, bodyCurve.Evaluate(2.0), 0.001f);
    }

    [Test]
    public void 点头候选优先使用数据根覆盖并拒绝篡改()
    {
        Assert.IsTrue(CertifiedMotionLibrary.TryGet("acknowledge_nod", out var motion));
        string dir = Path.Combine(Path.GetTempPath(), "fuxuan-certified-motion-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "acknowledge_nod.json");
        string assetPath = Path.Combine(Application.dataPath, "Resources", "Live2D", "CertifiedMotions", "acknowledge_nod.json");
        string json = File.ReadAllText(assetPath);
        try
        {
            File.WriteAllText(path, json, new UTF8Encoding(false));
            Assert.IsTrue(CertifiedMotionLibrary.TryLoadCandidateJson(motion, path, out string loaded, out string source, out string reason), reason);
            Assert.AreEqual("data-root", source);
            Assert.AreEqual(json, loaded);
            File.WriteAllText(path, json + " ", new UTF8Encoding(false));
            Assert.IsFalse(CertifiedMotionLibrary.TryLoadCandidateJson(motion, path, out _, out _, out reason));
            Assert.AreEqual("curve-hash-mismatch", reason);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Test] public void HiyoriM06只以已评审的头面技能语义登记()
    {
        Assert.IsTrue(CertifiedMotionLibrary.TryGet("external_Hiyori_Hiyori_m06", out var motion));
        Assert.AreEqual(EmbodiedResource.Face | EmbodiedResource.Body, motion.Resources);
        Assert.AreEqual("1c5b4f6edb705658b1648a3b5a1b2de5cce5d764183d47b21637ca0cb1b29c74", motion.PacketSha256);
        Assert.AreEqual(82, motion.NaturalnessScore);
        StringAssert.Contains("不是手臂动作或位移动作", motion.SemanticBoundary);
    }

    [Test] public void request_body_skill已注册且仅在身体意图白名单()
    {
        ToolRegistry.Initialize();
        Assert.IsTrue(ToolRegistry.HasTool("request_body_skill"));
        Assert.IsFalse(ToolRegistry.IsDangerous("request_body_skill"));
        Assert.IsTrue(LocalToolRouter.TryGetStrictIntentTools("body", out var allowed));
        CollectionAssert.Contains(allowed, "request_body_skill");
        Assert.IsFalse(LocalToolRouter.IsAllowed("request_body_skill", "operation"));
    }

    [Test] public void 身体提示词只声明认证技能且禁止原始参数()
    {
        string prompt = ChatManager.BuildCertifiedBodySkillBoundary();
        StringAssert.Contains("request_body_skill", prompt);
        StringAssert.Contains("原始参数", prompt);
        StringAssert.DoesNotContain("当前没有可调用", prompt);
        StringAssert.DoesNotContain("screen_side_arm_raise", prompt);
        foreach (var motion in CertifiedMotionLibrary.LlmExposedEntries)
            StringAssert.Contains(motion.SkillId, prompt);
    }

    [Test] public void 无生产曲线执行器的候选不得暴露给AI()
    {
        Assert.IsTrue(EmbodiedRuntimeAdmission.IsSkillAdmissible("screen_side_arm_raise"));
        Assert.IsFalse(CertifiedMotionLibrary.IsLlmExposed("screen_side_arm_raise"));
        var tool = new RequestBodySkillTool();
        StringAssert.DoesNotContain("screen_side_arm_raise", tool.ToolDescription);
    }

    [Test]
    public void 认证与AI暴露必须是两道独立白名单()
    {
        var hidden = new CertifiedMotionLibrary.Entry { SkillId = "internal_candidate" };
        Assert.IsFalse(hidden.LlmExposed);
        Assert.IsFalse(CertifiedMotionLibrary.IsLlmExposed("not_registered"));
        foreach (var motion in CertifiedMotionLibrary.Entries)
        {
            if (motion.SkillId == "screen_side_arm_raise")
            {
                Assert.IsFalse(motion.LlmExposed, "candidate must remain hidden until exposure evidence is complete");
                Assert.IsFalse(CertifiedMotionLibrary.IsLlmExposed(motion.SkillId));
            }
            else
            {
                Assert.IsTrue(motion.LlmExposed, "existing behavior must remain explicit: " + motion.SkillId);
                Assert.IsTrue(CertifiedMotionLibrary.IsLlmExposed(motion.SkillId));
            }
        }
    }
}
