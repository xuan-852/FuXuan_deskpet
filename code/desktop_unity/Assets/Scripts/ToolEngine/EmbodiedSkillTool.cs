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

    public bool IsAsync => false;

    public string Execute(string argsJson)
    {
        string skillId = ToolHelpers.JsonRead(argsJson, "skill_id");
        if (string.IsNullOrEmpty(skillId)) return "❌ 未指定 skill_id";
        if (!EmbodiedRuntimeAdmission.IsSkillAdmissible(skillId))
            return $"❌ 技能 {skillId} 未认证或不存在，请求被拒绝";
        var renderer = GameObject.FindObjectOfType<Live2DRenderer>();
        if (renderer == null) return "❌ 本座法身未现";
        return renderer.PlayCertifiedMotion(skillId);
    }

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        onResult?.Invoke(Execute(argsJson));
        yield break;
    }

    private static string BuildDescription()
    {
        var builder = new StringBuilder("【法身·具身】执行一项已认证的身体技能。仅在用户明确要求身体动作时调用，一次只执行一个技能。可用技能：");
        foreach (var motion in CertifiedMotionLibrary.Entries)
            builder.Append($"\n- {motion.SkillId}：{motion.SemanticBoundary}（约 {motion.DurationSeconds:F1} 秒）");
        builder.Append("\n- screen_side_arm_raise：画面侧单臂上抬后回落（约 2.4 秒）");
        return builder.ToString();
    }
}
