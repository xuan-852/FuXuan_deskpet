using System;
using UnityEngine;

/// <summary>
/// 复合动作预设 — 多参数多阶段的时序动作
///
/// 一个 ActionPreset 由多个 Phase 组成，每个 Phase 定义一组参数目标值
/// 和一个曲线形状（如 sin / easeInOut / trapezoid 等）。
///
/// 播放器按顺序执行每个 Phase，用 curve 计算强度乘子，再乘 targetValue。
/// </summary>
[CreateAssetMenu(fileName = "NewAction", menuName = "Live2D/Action Preset")]
public class ActionPreset : ScriptableObject
{
    [Header("标识")]
    public string actionName;

    [Tooltip("用于 AI 匹配的标签")]
    public string[] tags = Array.Empty<string>();

    [Header("行为")]
    [Tooltip("播放时是否锁定移动（不被走路覆盖）")]
    public bool lockMovement = true;

    [Header("参数过渡")]
    [Tooltip("动作开始前将所有受影响的参数淡入到起始值的时间")]
    public float globalFadeIn = 0.2f;

    [Tooltip("动作结束后将所有参数淡出到默认值的时间")]
    public float globalFadeOut = 0.3f;

    [Header("阶段序列")]
    public ActionPhase[] phases = Array.Empty<ActionPhase>();
}

/// <summary>动作阶段 — 持续一段时间，平滑过渡到目标参数</summary>
[Serializable]
public class ActionPhase
{
    [Header("时长")]
    public float duration = 1f;

    [Header("曲线形状")]
    [Tooltip("强度随时间的变化曲线（横轴 0~1 归一化时间，纵轴 0~1 强度）")]
    public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("参数目标")]
    public ParamTarget[] targets = Array.Empty<ParamTarget>();
}

/// <summary>单个参数的目标值</summary>
[Serializable]
public class ParamTarget
{
    /// <summary>语义化参数名</summary>
    public string semantic;

    /// <summary>目标值（乘 curve 强度后设入）</summary>
    public float value;

    [Tooltip("可选自定义曲线，为 null 则使用 Phase.curve")]
    public AnimationCurve curveOverride;
}
