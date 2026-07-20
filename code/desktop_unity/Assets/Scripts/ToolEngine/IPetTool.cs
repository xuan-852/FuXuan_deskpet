using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 法阵术式接口 — 所有工具实现此接口即可自动注册。
/// 新增工具只需新建一个类实现此接口，ToolRegistry 会自动发现它。
/// </summary>
public interface IPetTool
{
    /// <summary>工具名称（snake_case，如 "open_url", "file_create"）</summary>
    string ToolName { get; }

    /// <summary>工具描述（用于 OpenAI Function Calling）</summary>
    string ToolDescription { get; }

    /// <summary>工具参数 JSON Schema（完整的 parameters 对象 JSON，不含外层 function 包装）</summary>
    string ToolParametersJson { get; }

    /// <summary>是否需要协程执行（网络/IO 类工具返回 true）</summary>
    bool IsAsync { get; }

    /// <summary>
    /// 同步执行（IsAsync=false 时调用）
    /// </summary>
    string Execute(string argsJson);

    /// <summary>
    /// 异步协程执行（IsAsync=true 时调用），通过 onResult 回调返回结果
    /// </summary>
    IEnumerator ExecuteAsync(string argsJson, Action<string> onResult);
}
