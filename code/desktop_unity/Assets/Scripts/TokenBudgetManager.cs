using System;
using System.Collections.Generic;

/// <summary>
/// 云端请求成本闸门。
///
/// 只限制后台来源的请求频率；普通用户聊天和本地模型默认只观测、不拒绝。
/// 组件不写磁盘，避免把成本控制状态混入生产记忆；显式时间参数便于 EditMode
/// 测试在不等待真实时间的情况下验证滑动窗口和冷却逻辑。
/// </summary>
public static class TokenBudgetManager
{
    /// <summary>成本闸门拒绝结果前缀；调用方不得把它当作可重试网络错误。</summary>
    public const string BUDGET_ERROR_PREFIX = "成本闸门已拦截：";

    /// <summary>单个来源的频率策略。</summary>
    public sealed class SourcePolicy
    {
        public string Source { get; private set; }
        public int MaxCalls { get; private set; }
        public TimeSpan Window { get; private set; }
        public TimeSpan MinInterval { get; private set; }

        public SourcePolicy(string source, int maxCalls, TimeSpan window, TimeSpan minInterval)
        {
            Source = source;
            MaxCalls = maxCalls;
            Window = window;
            MinInterval = minInterval;
        }
    }

    private sealed class SourceState
    {
        public readonly Queue<DateTime> AcquiredAtUtc = new Queue<DateTime>();
        public DateTime LastAcquiredAtUtc;
        public bool HasLastAcquired;
    }

    private static readonly object SyncRoot = new object();
    private static readonly Dictionary<string, SourcePolicy> Policies =
        new Dictionary<string, SourcePolicy>(StringComparer.OrdinalIgnoreCase)
        {
            { "idle", new SourcePolicy("idle", 4, TimeSpan.FromHours(1), TimeSpan.FromMinutes(10)) },
            { "reflect", new SourcePolicy("reflect", 2, TimeSpan.FromHours(1), TimeSpan.FromMinutes(20)) },
            { "weather", new SourcePolicy("weather", 4, TimeSpan.FromHours(1), TimeSpan.FromMinutes(10)) },
            { "motion", new SourcePolicy("motion", 12, TimeSpan.FromHours(1), TimeSpan.FromMinutes(2)) },
            { "glm", new SourcePolicy("glm", 12, TimeSpan.FromHours(1), TimeSpan.FromMinutes(5)) },
        };
    private static readonly Dictionary<string, SourceState> States =
        new Dictionary<string, SourceState>(StringComparer.OrdinalIgnoreCase);

    /// <summary>使用当前 UTC 时间尝试放行一个云端来源。</summary>
    public static bool TryAcquire(string source, out string reason)
    {
        return TryAcquire(source, DateTime.UtcNow, out reason);
    }

    /// <summary>
    /// 使用指定 UTC 时间尝试放行一个云端来源。
    /// 未配置策略的来源（例如 chat、local、server）默认只观测，不拒绝。
    /// </summary>
    public static bool TryAcquire(string source, DateTime nowUtc, out string reason)
    {
        string normalized = NormalizeSource(source);
        reason = "";

        SourcePolicy policy;
        if (!Policies.TryGetValue(normalized, out policy))
            return true;

        DateTime now = nowUtc.Kind == DateTimeKind.Utc
            ? nowUtc
            : nowUtc.ToUniversalTime();

        lock (SyncRoot)
        {
            SourceState state;
            if (!States.TryGetValue(normalized, out state))
            {
                state = new SourceState();
                States[normalized] = state;
            }

            while (state.AcquiredAtUtc.Count > 0
                && now - state.AcquiredAtUtc.Peek() >= policy.Window)
            {
                state.AcquiredAtUtc.Dequeue();
            }

            if (state.HasLastAcquired && now - state.LastAcquiredAtUtc < policy.MinInterval)
            {
                TimeSpan wait = policy.MinInterval - (now - state.LastAcquiredAtUtc);
                reason = $"来源 {normalized} 进入成本闸门：距离上次放行不足 {Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))} 秒，请稍后再试。";
                return false;
            }

            if (state.AcquiredAtUtc.Count >= policy.MaxCalls)
            {
                reason = $"来源 {normalized} 已达到成本闸门上限：{policy.MaxCalls} 次/{FormatWindow(policy.Window)}，请稍后再试。";
                return false;
            }

            state.AcquiredAtUtc.Enqueue(now);
            state.LastAcquiredAtUtc = now;
            state.HasLastAcquired = true;
            return true;
        }
    }

    /// <summary>返回来源当前滑动窗口内已放行的请求数，供测试和诊断面板使用。</summary>
    public static int GetAcquiredCount(string source, DateTime nowUtc)
    {
        string normalized = NormalizeSource(source);
        SourcePolicy policy;
        if (!Policies.TryGetValue(normalized, out policy))
            return 0;

        DateTime now = nowUtc.Kind == DateTimeKind.Utc
            ? nowUtc
            : nowUtc.ToUniversalTime();

        lock (SyncRoot)
        {
            SourceState state;
            if (!States.TryGetValue(normalized, out state))
                return 0;

            while (state.AcquiredAtUtc.Count > 0
                && now - state.AcquiredAtUtc.Peek() >= policy.Window)
            {
                state.AcquiredAtUtc.Dequeue();
            }
            return state.AcquiredAtUtc.Count;
        }
    }

    /// <summary>获取来源策略；未知来源返回 null。</summary>
    public static SourcePolicy GetPolicy(string source)
    {
        SourcePolicy policy;
        return Policies.TryGetValue(NormalizeSource(source), out policy) ? policy : null;
    }

    /// <summary>清空进程内预算状态。仅供测试/应用重启初始化使用。</summary>
    public static void Reset()
    {
        lock (SyncRoot)
        {
            States.Clear();
        }
    }

    /// <summary>判断错误是否来自成本闸门。</summary>
    public static bool IsBudgetRejection(string error)
    {
        return !string.IsNullOrEmpty(error) && error.StartsWith(BUDGET_ERROR_PREFIX, StringComparison.Ordinal);
    }

    private static string NormalizeSource(string source)
    {
        return string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim().ToLowerInvariant();
    }

    private static string FormatWindow(TimeSpan window)
    {
        if (window.TotalHours >= 1)
            return $"{(int)window.TotalHours} 小时";
        return $"{(int)window.TotalMinutes} 分钟";
    }
}
