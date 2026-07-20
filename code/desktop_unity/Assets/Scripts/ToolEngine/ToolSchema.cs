using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Runtime.CompilerServices;

/// <summary>
/// 法阵铭文 — JSON Schema 构建器
/// 提供类型安全的方式构造 ToolParametersJson，取代手写转义 JSON 字符串。
/// 用法：
///   public string ToolParametersJson => ToolSchema.Schema(
///       ToolSchema.Req("url", "string", "要打开的完整网址")
///   );
/// </summary>
public static class ToolSchema
{
    /// <summary>空的参数 Schema（无参数工具使用）</summary>
    public static string Empty { get; } = new JObject
    {
        ["type"] = "object",
        ["properties"] = new JObject()
    }.ToString(Formatting.None);

    /// <summary>构建带参数的 JSON Schema</summary>
    public static string Schema(params ParamDef[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
            return Empty;

        var properties = new JObject();
        var required = new JArray();

        foreach (var p in parameters)
        {
            var propObj = new JObject
            {
                ["type"] = p.Type,
                ["description"] = p.Description
            };
            properties[p.Name] = propObj;

            if (p.Required)
                required.Add(p.Name);
        }

        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.HasValues)
            schema["required"] = required;

        return schema.ToString(Formatting.None);
    }

    /// <summary>定义一个必需参数</summary>
    public static ParamDef Req(string name, string type, string description)
        => new ParamDef(name, type, description, required: true);

    /// <summary>定义一个可选参数</summary>
    public static ParamDef Opt(string name, string type, string description)
        => new ParamDef(name, type, description, required: false);
}

/// <summary>参数定义值类型</summary>
public readonly struct ParamDef
{
    public readonly string Name;
    public readonly string Type;
    public readonly string Description;
    public readonly bool Required;

    public ParamDef(string name, string type, string description, bool required)
    {
        Name = name;
        Type = type;
        Description = description;
        Required = required;
    }
}
