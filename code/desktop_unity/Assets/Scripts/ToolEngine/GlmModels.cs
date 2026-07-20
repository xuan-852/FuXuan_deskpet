using System;
using UnityEngine;

// ================================================================
//  GLM 响应模型（供视觉分析工具使用）
// ================================================================

[System.Serializable]
public class GlmVisionResponse
{
    public GlmChoice[] choices;
}

[System.Serializable]
public class GlmChoice
{
    public GlmMessage message;
}

[System.Serializable]
public class GlmMessage
{
    public string content;
}

[System.Serializable]
public class GlmErrorResponse
{
    public GlmErrorDetail error;
}

[System.Serializable]
public class GlmErrorDetail
{
    public string message;
}

// ---- fuxuan_map.json 反序列化（用于 explore_body 的部位分组） ----

[System.Serializable]
public class FuxuanMapData
{
    public FuxuanMapEntry[] entries;
}

[System.Serializable]
public class FuxuanMapEntry
{
    public string s;     // semantic
    public string p;     // paramId
    public string part;  // bodyPart
}
