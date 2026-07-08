using System;
using System.Collections.Concurrent;
using UnityEngine;

/// <summary>
/// 在 Unity 主线程上执行后台线程回调的调度器
/// </summary>
public class MainThreadDispatcher : MonoBehaviour
{
    private static MainThreadDispatcher _instance;
    private static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }
        _instance = this;
    }

    void Update()
    {
        while (_queue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Debug.LogError($"[MainThreadDispatcher] {e.Message}"); }
        }
    }

    /// <summary>在主线程上执行回调（从后台线程调用）</summary>
    public static void Run(Action action)
    {
        _queue.Enqueue(action);
    }
}
