using System;

// 认证动作候选定义（与 l3-external-motion-candidate/v1 JSON 对应）。
// 曲线数据不入版本库（许可约束）；本地存放于数据根 certified_motions/。
[Serializable]
public sealed class EmbodiedMotionCandidate
{
    public string candidateId;
    public float durationSeconds;
    public EmbodiedMotionCurveEntry[] curves;

    public bool IsValid => !string.IsNullOrEmpty(candidateId) && durationSeconds > 0f && curves != null && curves.Length > 0;
}

[Serializable]
public sealed class EmbodiedMotionCurveEntry
{
    public string parameterId;
    public float[] segments;
}
