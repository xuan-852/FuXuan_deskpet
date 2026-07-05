using System;
using System.Collections.Generic;

/// <summary>
/// 模型身体 Schema — 完整的参数知识结构化数据
///
/// 用途：为 AI Agent 提供对模型"身体"的完整认知。
/// 包含每个参数的语义名、范围、部位归属、关联关系等。
///
/// 生成方式：
/// - 运行时由 RuntimeModelAnalyzer 自动分析生成
/// - 也可手动编辑 JSON 后反序列化
///
/// 消费方：
/// - ParameterKnowledgeProvider → 生成 AI system prompt
/// - MotionPlanner → 规划动作时查询参数约束
/// - SafetyValidator → 验证参数值是否越界/冲突
/// </summary>
public class ModelBodySchema
{
    public string modelName;
    public string schemaVersion = "2.0";
    public string generatedAt;
    public List<ParameterDef> parameters = new List<ParameterDef>();
    public List<ParameterGroup> groups = new List<ParameterGroup>();
    public List<ParameterRelation> relations = new List<ParameterRelation>();

    /// <summary>按部位查找参数</summary>
    public List<ParameterDef> GetParamsByBodyPart(string bodyPart)
    {
        return parameters.FindAll(p => p.bodyPart == bodyPart);
    }

    /// <summary>按语义名查找参数</summary>
    public ParameterDef GetParamBySemantic(string semantic)
    {
        return parameters.Find(p => p.semantic == semantic);
    }

    /// <summary>按参数 ID 查找参数</summary>
    public ParameterDef GetParamById(string paramId)
    {
        return parameters.Find(p => p.paramId == paramId);
    }

    /// <summary>获取所有未映射的参数（semantic 为空或为占位符）</summary>
    public List<ParameterDef> GetUnmappedParams()
    {
        return parameters.FindAll(p => string.IsNullOrEmpty(p.semantic) || p.semantic.StartsWith("unmapped_"));
    }

    /// <summary>获取所有已映射参数</summary>
    public List<ParameterDef> GetMappedParams()
    {
        return parameters.FindAll(p => !string.IsNullOrEmpty(p.semantic) && !p.semantic.StartsWith("unmapped_"));
    }
}

/// <summary>单个参数定义</summary>
[Serializable]
public class ParameterDef
{
    /// <summary>语义名（如 "eye_l_open", "head_angle_x"）</summary>
    public string semantic;

    /// <summary>模型内部参数 ID（如 "ParamEyeLOpen", "ParamAngleX"）</summary>
    public string paramId;

    /// <summary>最小值</summary>
    public float min;

    /// <summary>最大值</summary>
    public float max;

    /// <summary>默认值</summary>
    public float defaultValue;

    /// <summary>所属身体部位分类</summary>
    public string bodyPart;

    /// <summary>来自 cdi3.json 的中文名</summary>
    public string cdiName;

    /// <summary>验证后的中文描述（人工填写或 GLM 分析）</summary>
    public string description;

    /// <summary>是否已人工验证</summary>
    public bool verified;

    /// <summary>验证备注</summary>
    public string verifiedNote;

    /// <summary>值域类型</summary>
    public ValueDomain valueDomain;

    /// <summary>该参数控制的主要方向/轴（用于 X/Y/Z 类参数）</summary>
    public string axis;

    public override string ToString()
    {
        return $"{semantic,-25} → {paramId,-15} [{min,7:F2}, {max,7:F2}] def={defaultValue:F3}  {bodyPart,-10} {cdiName}";
    }
}

/// <summary>值域类型 — 帮助 AI 理解这个参数怎么用</summary>
public enum ValueDomain
{
    /// <summary>归一化连续值（0~1 或 -1~1），如睁眼度</summary>
    Normalized,
    /// <summary>角度（度），如头部旋转</summary>
    Angle,
    /// <summary>位移（像素/单位），如镜头</summary>
    Position,
    /// <summary>开关（0 或 1），如特效显隐</summary>
    Toggle,
    /// <summary>缩放（乘法因子），如角色大小</summary>
    Scale,
    /// <summary>透明度</summary>
    Alpha,
    /// <summary>未知/其他</summary>
    Other
}

/// <summary>参数分组 — 按身体部位组织</summary>
[Serializable]
public class ParameterGroup
{
    /// <summary>分组标识（如 "head", "eyes", "left_arm"）</summary>
    public string groupName;

    /// <summary>显示名称（如 "头部", "眼睛"）</summary>
    public string displayName;

    /// <summary>分组描述</summary>
    public string description;

    /// <summary>属于此组的语义参数名列表</summary>
    public List<string> paramsList = new List<string>();

    /// <summary>特殊行为定义</summary>
    public List<SpecialBehavior> specialBehaviors = new List<SpecialBehavior>();
}

/// <summary>参数间的特殊行为定义</summary>
[Serializable]
public class SpecialBehavior
{
    /// <summary>行为标识（如 "blink", "gaze", "sword_finger"）</summary>
    public string behaviorName;

    /// <summary>涉及此行为的参数</summary>
    public List<string> involvedParams = new List<string>();

    /// <summary>前置条件参数（{语义名: 所需值}）</summary>
    public Dictionary<string, float> prerequisites = new Dictionary<string, float>();

    /// <summary>行为描述</summary>
    public string description;

    /// <summary>行为模式</summary>
    public string pattern; // "both_sync", "coupled", "sequential"
}

/// <summary>参数关联关系</summary>
[Serializable]
public class ParameterRelation
{
    /// <summary>关联类型</summary>
    public RelationType type;

    /// <summary>关联的语义参数列表</summary>
    public List<string> involvedSemantics = new List<string>();

    /// <summary>关联的原始参数 ID 列表</summary>
    public List<string> involvedParamIds = new List<string>();

    /// <summary>关系描述</summary>
    public string description;

    /// <summary>置信度（0~1），自动检测的关联可能不准确</summary>
    public float confidence;

    /// <summary>关联强度（0~1），1=强关联（如左右必须同步）</summary>
    public float strength;
}

/// <summary>关联类型</summary>
public enum RelationType
{
    /// <summary>对称关联（如 eye_l_open ↔ eye_r_open，值通常相同）</summary>
    Symmetrical,

    /// <summary>耦合关联（如 eye_ball_x + eye_ball_y 组合成眼珠位置）</summary>
    Coupled,

    /// <summary>前置条件（如 sword_finger_switch 必须先开启才能控制手指）</summary>
    Prerequisite,

    /// <summary>互斥（两个参数不应同时非零）</summary>
    MutuallyExclusive,

    /// <summary>从属（一个参数变化时另一个也随之变化）</summary>
    Dependent,

    /// <summary>同组范围近似（范围大小相似，可能属于同一部位族）</summary>
    SameRange
}
