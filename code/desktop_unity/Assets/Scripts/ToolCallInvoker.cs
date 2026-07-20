using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 符玄「法阵」— 工具调用执行器
/// 重构版：薄封装层，所有实际逻辑委托给 ToolRegistry。
/// </summary>
public class ToolCallInvoker : MonoBehaviour
{
    [Header("安全设置")]
    [Tooltip("危险操作需要人确认（已迁移到 ToolRegistry）")]
    public bool dangerousOpsNeedConfirm = true;

    [Header("当前执行状态")]
    public string lastToolResult = "";
    public string lastToolError = "";

    // 协程工具结果缓存（保留原接口）
    private string _coroutineResult;

    void Awake()
    {
        // 初始化 ToolRegistry（自动发现所有 IPetTool）
        ToolRegistry.Initialize();
        Debug.Log($"[ToolCallInvoker] 法阵已就绪，共 {ToolRegistry.ToolCount} 道术式");
    }

    /// <summary>保留旧版接口 — 判断工具是否需要协程执行</summary>
    public bool IsCoroutineTool(string name) => ToolRegistry.IsAsync(name) || ToolRegistry.HasTool(name);

    /// <summary>保留旧版接口 — 协程执行工具，结果通过 GetCoroutineResult() 获取</summary>
    public IEnumerator ExecuteCoroutine(string name, string argsJson)
    {
        _coroutineResult = null;

        if (ToolRegistry.IsAsync(name))
        {
            string result = null;
            yield return ToolRegistry.ExecuteAsync(name, argsJson ?? "{}", r => result = r);
            _coroutineResult = result;
        }
        else if (ToolRegistry.HasTool(name))
        {
            _coroutineResult = ToolRegistry.Execute(name, argsJson ?? "{}");
        }
        else
        {
            _coroutineResult = $"\u274C不识此术：「{name}」";
        }

        lastToolResult = _coroutineResult ?? "";
    }

    /// <summary>保留旧版接口 — 获取协程工具执行结果</summary>
    public string GetCoroutineResult() => _coroutineResult;

    /// <summary>保留旧版接口 — 同步执行工具</summary>
    public string Execute(string name, string argsJson, out string error)
    {
        lastToolResult = "";
        lastToolError = "";

        if (!ToolRegistry.HasTool(name))
        {
            error = $"\u274C不识此术：「{name}」";
            return error;
        }

        try
        {
            string result = ToolRegistry.Execute(name, argsJson ?? "{}");
            lastToolResult = result;
            error = null;
            return result;
        }
        catch (Exception e)
        {
            lastToolError = e.Message;
            error = $"\u274C施法失败：{e.Message}";
            return error;
        }
    }

    /// <summary>保留旧版接口 — 获取所有工具的 JSON Schema</summary>
    public string GetToolsJson() => ToolRegistry.GetToolsJson();
}
