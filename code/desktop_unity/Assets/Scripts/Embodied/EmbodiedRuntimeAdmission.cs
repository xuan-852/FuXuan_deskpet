using System;
using UnityEngine;

// 生产运行时唯一认证技能准入汇点（FR-L3-02/C-L3-01）：注册表只接受四层
// 认证记录，ActionRequest 是唯一准入入口，没有原始参数字段。注册失败时
// 准入保持为空并报错，绝不降级放行。当前仅隔离测试执行器
// （@@sim:gesture:param94，仅 .test_mode）经过本汇点；LLM 工具面仍为零。
// 超时与取消由执行器生命周期负责，本汇点只保证同一请求单占用、完成即释放。
public static class EmbodiedRuntimeAdmission
{
    private static readonly CertifiedSkillRegistry Registry = CreateRegistry();
    private static readonly EmbodiedCoordinator Coordinator = new EmbodiedCoordinator(Registry);
    private static EmbodiedActionRequest _activeRequest;

    private static CertifiedSkillRegistry CreateRegistry()
    {
        var registry = new CertifiedSkillRegistry();
        if (!registry.TryRegister(ScreenSideArmRaiseCertification.CreateRecord(), out var reason))
            Debug.LogError($"[EmbodiedRuntimeAdmission] 首个认证技能注册被拒绝，准入保持为空：{reason}");
        return registry;
    }

    public static bool IsSkillAdmissible(string skillId) => Registry.TryGet(skillId ?? "", out _);

    public static bool TryBeginSkill(string skillId, out EmbodiedActionRequest request, out string reason)
    {
        request = null;
        if (_activeRequest != null) { reason = "admission-request-active"; return false; }
        if (!Registry.TryGet(skillId ?? "", out var skill)) { reason = "skill-not-certified"; return false; }
        var candidate = CandidateSkillCatalog.ScreenSideArmRaise;
        if (candidate.SkillId != skillId || !candidate.HasPassedAllEvidenceLayers()) { reason = "skill-not-certified"; return false; }
        var begun = new EmbodiedActionRequest
        {
            SkillId = skillId,
            SemanticTarget = candidate.SemanticDescription,
            Priority = 0,
            Timeout = candidate.Duration,
            Resources = skill.Resources
        };
        if (Coordinator.TryBegin(begun, out reason) != EmbodiedActionStatus.Executing) return false;
        _activeRequest = begun;
        request = begun;
        Debug.Log("[EmbodiedRuntimeAdmission] admitted: " + skillId);
        return true;
    }

    // 完成与取消都必须经过这里释放资源；重复释放或释放非当前请求被忽略。
    public static void CompleteSkill(EmbodiedActionRequest request, string reason)
    {
        if (request == null || !ReferenceEquals(request, _activeRequest)) return;
        Coordinator.Complete(request);
        _activeRequest = null;
        Debug.Log("[EmbodiedRuntimeAdmission] released: " + reason);
    }
}
