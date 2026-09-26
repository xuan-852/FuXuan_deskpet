using System;
using System.Collections;
using System.Text;
using UnityEngine;

/// <summary>
/// LLM 身体技能请求工具：只能选择认证动作库中已注册的技能，
/// 由渲染器经准入与租约执行；拒绝即终态，无原始参数入口（FR-L3-03/C-L3-06）。
/// </summary>
public class RequestBodySkillTool : IPetTool
{
    public string ToolName => "request_body_skill";

    public string ToolDescription => BuildDescription();

    public string ToolParametersJson => ToolSchema.Schema(
        ToolSchema.Req("skill_id", "string", "技能 ID，必须从描述列出的已认证技能中选择")
    );

    public bool IsAsync => true;

    public string Execute(string argsJson)
    {
        string skillId = ToolHelpers.JsonRead(argsJson, "skill_id");
        if (string.IsNullOrEmpty(skillId)) return "❌ 未指定 skill_id";
        if (!CertifiedMotionLibrary.IsLlmExposed(skillId))
            return $"❌ 技能 {skillId} 未获 AI 调用授权，请求被拒绝";
        if (!EmbodiedRuntimeAdmission.IsSkillAdmissible(skillId))
            return $"❌ 技能 {skillId} 未认证或不存在，请求被拒绝";
        var renderer = GameObject.FindObjectOfType<Live2DRenderer>();
        if (renderer == null) return "❌ 本座法身未现";
        return renderer.PlayCertifiedMotion(
            skillId,
            "request_body_skill",
            "user_requested_body_skill",
            true);
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        string first = Execute(argsJson);
        // 生产实测缺口（2026-09-27）：自主闲逛中收到显式身体请求会被拒——
        // 行走渐入窗口内语义门禁可能放行而由输入租约层拒绝（"动作通道被占用"），
        // 其余时刻由"稳定静止"门禁拒绝。用户显式请求优先于自主行走：仅当
        // 可让位（确实在行走、在地面、未拖拽、无其他动作占用）时停走、收步、重试。
        bool locomotionRejected = first != null
            && (first.Contains("稳定静止") || first.Contains("动作通道被占用"));
        if (!locomotionRejected)
        {
            onResult?.Invoke(first);
            yield break;
        }
        var yieldRenderer = GameObject.FindObjectOfType<Live2DRenderer>();
        if (yieldRenderer == null || !yieldRenderer.CanYieldLocomotionForUserRequest())
        {
            onResult?.Invoke(first);
            yield break;
        }
        yieldRenderer.StopIdleLocomotionForUserRequest();
        float deadline = Time.time + 2f;
        while (Time.time < deadline && !yieldRenderer.IsStationaryForBodyRequest())
            yield return null;
        onResult?.Invoke(Execute(argsJson));
    }

    private static string BuildDescription()
    {
        var builder = new StringBuilder("【法身·具身】执行一项已认证的身体技能。仅在用户明确要求身体动作时调用，一次只执行一个技能。可用技能：");
        foreach (var motion in CertifiedMotionLibrary.LlmExposedEntries)
            builder.Append($"\n- {motion.SkillId}：{motion.SemanticBoundary}（约 {motion.DurationSeconds:F1} 秒）");
        return builder.ToString();
    }
}
