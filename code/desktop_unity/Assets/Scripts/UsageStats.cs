using System;
using System.Collections.Generic;

/// <summary>
/// Token 消耗统计（内存累计）— 供 RightPanel「消耗」面板展示
/// 每次 API 响应带 usage 时由 ApiClient.ExtractUsageSummary 记录（时间戳采样）。
/// 显示「累计」与「近 1 小时」两个口径，方便观察真实小时消耗量。
///
/// 价格常数为 DeepSeek 估算（2026-08-15，非高峰）：输入未命中 ¥2/M、命中 ¥0.5/M、输出 ¥3/M。
/// ⚠️ 8-17 起峰谷定价（峰值输出最高 ¥27/M），此处用非高峰价，面板标注"估算"。
/// </summary>
public static class UsageStats
{
    public const float PRICE_INPUT_MISS_YUAN_PER_M = 2f;   // 输入缓存未命中（元/百万 tokens）
    public const float PRICE_INPUT_HIT_YUAN_PER_M = 0.5f;  // 输入缓存命中
    public const float PRICE_OUTPUT_YUAN_PER_M = 3f;       // 输出

    private class Sample
    {
        public float Time;      // Time.realtimeSinceStartup（秒）
        public int Prompt;
        public int CacheHit;
        public int Completion;
    }

    private static readonly List<Sample> _samples = new List<Sample>();
    private static readonly object _lock = new object();
    private const int MAX_SAMPLES = 5000;   // 防内存膨胀：超出丢弃最旧

    public static long TotalCalls { get; private set; }
    public static long TotalPrompt { get; private set; }
    public static long TotalCacheHit { get; private set; }
    public static long TotalCompletion { get; private set; }

    /// <summary>记录一次 API 消耗（由 ApiClient 调用）</summary>
    public static void Record(int prompt, int cacheHit, int completion)
    {
        lock (_lock)
        {
            TotalCalls++;
            TotalPrompt += prompt;
            TotalCacheHit += cacheHit;
            TotalCompletion += completion;
            _samples.Add(new Sample
            {
                Time = UnityEngine.Time.realtimeSinceStartup,
                Prompt = prompt,
                CacheHit = cacheHit,
                Completion = completion
            });
            while (_samples.Count > MAX_SAMPLES) _samples.RemoveAt(0);
        }
    }

    /// <summary>回灌持久化历史（UsageLogger 启动时调用；近 1 小时口径不含历史，仅累计口径）</summary>
    public static void LoadPersisted(long calls, long prompt, long cacheHit, long completion)
    {
        lock (_lock)
        {
            TotalCalls += calls;
            TotalPrompt += prompt;
            TotalCacheHit += cacheHit;
            TotalCompletion += completion;
            // 不塞入 _samples（那是"近1小时"采样队列，历史数据不应污染实时窗口）
        }
    }

    /// <summary>近 seconds 秒内的统计（如 3600 = 近 1 小时）</summary>
    public static (long calls, long prompt, long cacheHit, long completion) GetRecent(float seconds)
    {
        lock (_lock)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            long c = 0, p = 0, h = 0, o = 0;
            for (int i = _samples.Count - 1; i >= 0; i--)
            {
                var s = _samples[i];
                if (now - s.Time <= seconds) { c++; p += s.Prompt; h += s.CacheHit; o += s.Completion; }
                else break; // 列表按时间有序，最旧的在前面
            }
            return (c, p, h, o);
        }
    }

    /// <summary>估算费用（元）：按非高峰价格，未命中 = prompt - cacheHit</summary>
    public static float EstimateCostYuan(long prompt, long cacheHit, long completion)
    {
        long miss = Math.Max(0, prompt - cacheHit);
        return (miss * PRICE_INPUT_MISS_YUAN_PER_M
              + cacheHit * PRICE_INPUT_HIT_YUAN_PER_M
              + completion * PRICE_OUTPUT_YUAN_PER_M) / 1_000_000f;
    }

    /// <summary>缓存命中率（0~1）</summary>
    public static float HitRate(long prompt, long cacheHit)
    {
        return prompt > 0 ? (float)cacheHit / prompt : 0f;
    }
}
