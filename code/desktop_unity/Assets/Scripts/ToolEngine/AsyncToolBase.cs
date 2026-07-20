using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 异步术式基类 — 提供将 async Task 包装为协程的通用逻辑。
/// 继承此类并实现 ExecuteAsyncTask，自动获得协程执行能力。
/// </summary>
public abstract class AsyncToolBase : IPetTool
{
    public abstract string ToolName { get; }
    public abstract string ToolDescription { get; }
    public abstract string ToolParametersJson { get; }
    public bool IsAsync => true;

    /// <summary>子类实现此方法返回异步结果</summary>
    protected abstract Task<string> ExecuteAsyncTask(string argsJson);

    public string Execute(string argsJson) => "⏳ 术式施展中，请稍候……";

    public IEnumerator ExecuteAsync(string argsJson, Action<string> onResult)
    {
        var task = ExecuteAsyncTask(argsJson ?? "{}");
        while (!task.IsCompleted) yield return null;

        if (task.IsFaulted)
            onResult?.Invoke($"❌ 执行出错: {task.Exception?.InnerException?.Message ?? task.Exception?.Message}");
        else
            onResult?.Invoke(task.Result);
    }
}
