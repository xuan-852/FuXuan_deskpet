using System;
using UnityEngine;

/// <summary>
/// 表情预设 — 一组语义化参数值
/// 通过 ExpressionManager 使用 Live2DParameterMapper 驱动
/// </summary>
[CreateAssetMenu(fileName = "NewExpression", menuName = "Live2D/Expression Preset")]
public class ExpressionPreset : ScriptableObject
{
    [Header("标识")]
    public string expressionName;

    [Header("过渡")]
    public float fadeInTime = 0.3f;
    public float fadeOutTime = 0.3f;

    [Header("参数")]
    public ExpressionParamEntry[] parameters = Array.Empty<ExpressionParamEntry>();
}

[Serializable]
public struct ExpressionParamEntry
{
    /// <summary>语义化参数名（对应 Live2DParameterMapper 的 key）</summary>
    public string semantic;

    /// <summary>目标值</summary>
    public float value;
}
