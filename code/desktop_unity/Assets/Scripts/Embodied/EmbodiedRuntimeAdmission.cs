using System;
using System.Collections.Generic;
using UnityEngine;

// 生产运行时唯一认证技能准入汇点（FR-L3-02/C-L3-01）：注册表只接受四层
// 认证记录，ActionRequest 是唯一准入入口，没有原始参数字段。注册失败时
// 准入保持为空并报错，绝不降级放行。并行由 EmbodiedCoordinator 按资源
// 掩码仲裁（C-L3-02：不相交资源可并行）；完成与取消必须经 CompleteSkill。
// 当前仅隔离测试执行器（@@sim:gesture:param94，仅 .test_mode）经过本汇点；
// LLM 工具面仍为零。超时与取消由执行器生命周期负责。
public static class EmbodiedRuntimeAdmission
{
    // Update 中的超时检查发生在协程收尾之前；给已知时长的动作留出一小段收尾窗口，
    // 避免最后一帧与超时同刻时被误判。该窗口仍受统一超时保护，不能无限占用资源。
    public const float CompletionGraceSeconds = 0.25f;

    private static readonly CertifiedSkillRegistry Registry = CreateRegistry();
    private static readonly EmbodiedCoordinator Coordinator = new EmbodiedCoordinator(Registry);
    private static readonly HashSet<EmbodiedActionRequest> Active = new HashSet<EmbodiedActionRequest>();

    private static CertifiedSkillRegistry CreateRegistry()
    {
        var registry = new CertifiedSkillRegistry();
        if (!registry.TryRegister(ScreenSideArmRaiseCertification.CreateRecord(), out var reason))
            Debug.LogError($"[EmbodiedRuntimeAdmission] 认证技能注册被拒绝，准入保持为空：{reason}");
        foreach (var motion in CertifiedMotionLibrary.Entries)
        {
            if (registry.TryGet(motion.SkillId, out _)) continue;
            if (!registry.TryRegister(motion.CreateRecord(), out reason))
                Debug.LogError($"[EmbodiedRuntimeAdmission] 认证动作 {motion.SkillId} 注册被拒绝：{reason}");
        }
        if (RegistryHasNoSkills(registry)) Debug.LogError("[EmbodiedRuntimeAdmission] 准入注册表为空。");
        return registry;
    }

    private static bool RegistryHasNoSkills(CertifiedSkillRegistry registry) => registry.Count == 0;

    public static bool IsSkillAdmissible(string skillId) => Registry.TryGet(skillId ?? "", out _);

    // 认证技能的语义目标与时长计划；新认证技能在此登记后即可被准入。
    private static bool TryGetSkillPlan(string skillId, out string semanticTarget, out TimeSpan duration)
    {
        switch (skillId)
        {
            case ScreenSideArmRaiseCertification.SkillId:
                var arm = CandidateSkillCatalog.ScreenSideArmRaise;
                semanticTarget = arm.SemanticDescription;
                duration = arm.Duration;
                return true;
            case HeadSwayBlinkIdleCertification.SkillId:
                semanticTarget = HeadSwayBlinkIdleCertification.SemanticBoundary;
                duration = TimeSpan.FromSeconds(HeadSwayBlinkIdleCertification.DurationSeconds);
                return true;
            default:
                if (CertifiedMotionLibrary.TryGet(skillId, out var motion))
                {
                    semanticTarget = motion.SemanticBoundary;
                    duration = TimeSpan.FromSeconds(motion.DurationSeconds);
                    return true;
                }
                semanticTarget = null;
                duration = default;
                return false;
        }
    }

    public static bool TryBeginSkill(string skillId, out EmbodiedActionRequest request, out string reason)
    {
        request = null;
        if (!Registry.TryGet(skillId ?? "", out var skill)) { reason = "skill-not-certified"; return false; }
        if (!TryGetSkillPlan(skillId, out var semanticTarget, out var duration)) { reason = "skill-not-certified"; return false; }
        var begun = new EmbodiedActionRequest
        {
            SkillId = skillId,
            SemanticTarget = semanticTarget,
            Priority = 0,
            Timeout = TimeSpan.FromSeconds(duration.TotalSeconds + CompletionGraceSeconds),
            Resources = skill.Resources
        };
        if (Coordinator.TryBegin(begun, out reason) != EmbodiedActionStatus.Executing) return false;
        Active.Add(begun);
        request = begun;
        Debug.Log("[EmbodiedRuntimeAdmission] admitted: " + skillId);
        return true;
    }

    // 由运行时执行器每帧调用；到期请求已经由协调器释放资源，随后从准入活动集移除。
    public static int ExpireDue(DateTime nowUtc)
    {
        int expired = Coordinator.ExpireDue(nowUtc);
        if (expired == 0) return 0;
        var completed = new List<EmbodiedActionRequest>();
        foreach (var request in Active)
            if (request.Status == EmbodiedActionStatus.TimedOut) completed.Add(request);
        foreach (var request in completed)
        {
            Active.Remove(request);
            Debug.Log("[EmbodiedRuntimeAdmission] timed-out: " + request.SkillId);
        }
        return completed.Count;
    }

    // 完成与取消都必须经过这里释放资源；重复释放或释放非当前请求被忽略。
    public static void CompleteSkill(EmbodiedActionRequest request, string reason)
    {
        if (request == null || !Active.Remove(request)) return;
        Coordinator.Complete(request);
        Debug.Log("[EmbodiedRuntimeAdmission] released: " + reason);
    }

    // 取消不应被伪装成完成；超时请求已在 ExpireDue 中释放，随后调用此方法仅记录
    // 运行时收束，不会重复占用或改变其 TimedOut 终态。
    public static void CancelSkill(EmbodiedActionRequest request, string reason)
    {
        if (request == null || !Active.Remove(request)) return;
        Coordinator.Cancel(request, reason);
        Debug.Log("[EmbodiedRuntimeAdmission] cancelled: " + reason);
    }
}
