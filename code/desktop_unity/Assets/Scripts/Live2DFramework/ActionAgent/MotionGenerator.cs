using System.Collections;
using System.Collections.Generic;
using Live2D.Cubism.Core;
using UnityEngine;

/// <summary>
/// 动作生成器 — ActionAgent 阶段四
///
/// 职责：接收 MotionPlanner 产出的 MotionPlan（关键帧序列），
/// 通过协程逐帧插值播放，将参数值应用到 Live2DParameterMapper。
///
/// 功能：
/// - 协程驱动播放（支持 Start/Stop/Pause）
/// - 多种插值曲线（Linear/Smooth/EaseOut/EaseIn/Hold/Bounce）
/// - 实时状态查询（播放中/已暂停/已完成）
/// - 播放完成回调
///
/// 使用方式：
///   var gen = new MotionGenerator(mapper);
///   gen.StartCoroutine(gen.PlayAsync(plan, onComplete));
/// </summary>
public class MotionGenerator
{
    private Live2DParameterMapper _mapper;
    private CubismModel _model;

    /// <summary>当前播放状态</summary>
    public MotionState State { get; private set; } = MotionState.Idle;

    /// <summary>当前播放的动作描述</summary>
    public string CurrentDescription { get; private set; }

    /// <summary>已完成的关键帧数</summary>
    public int CompletedFrames { get; private set; }

    /// <summary>当前播放进度 (0~1)</summary>
    public float Progress { get; private set; }

    /// <summary>播放完成事件</summary>
    public event System.Action OnMotionComplete;

    public enum MotionState
    {
        Idle,
        Playing,
        Paused,
    }

    public MotionGenerator(Live2DParameterMapper mapper, CubismModel model)
    {
        _mapper = mapper;
        _model = model;
    }

    // ──────────────────────────────────────────────
    //  播放控制
    // ──────────────────────────────────────────────

    /// <summary>重置状态（新计划前调用）</summary>
    public void ResetState(string description)
    {
        State = MotionState.Playing;
        CurrentDescription = description ?? "未知动作";
        CompletedFrames = 0;
        Progress = 0f;
    }

    /// <summary>标记完成</summary>
    public void Complete()
    {
        State = MotionState.Idle;
        Progress = 1f;
        OnMotionComplete?.Invoke();
    }

    /// <summary>强制停止</summary>
    public void Stop()
    {
        State = MotionState.Idle;
        Progress = 0f;
    }

    /// <summary>暂停/恢复</summary>
    public void TogglePause()
    {
        if (State == MotionState.Playing)
            State = MotionState.Paused;
        else if (State == MotionState.Paused)
            State = MotionState.Playing;
    }

    // ──────────────────────────────────────────────
    //  核心协程
    // ──────────────────────────────────────────────

    /// <summary>播放动作计划（协程）</summary>
    public IEnumerator PlayAsync(MotionPlanner.MotionPlan plan)
    {
        if (_mapper == null || plan == null || plan.KeyFrames.Count == 0)
        {
            State = MotionState.Idle;
            OnMotionComplete?.Invoke();
            yield break;
        }

        ResetState(plan.Description);

        // 排序关键帧
        plan.KeyFrames.Sort((a, b) => a.Time.CompareTo(b.Time));

        float elapsed = 0f;
        float totalDuration = plan.TotalDuration;
        int frameIndex = 0;

        // 记录初始参数值
        var startValues = new Dictionary<string, float>();
        foreach (var kf in plan.KeyFrames)
        {
            foreach (var kv in kf.Values)
            {
                if (!startValues.ContainsKey(kv.Key))
                    startValues[kv.Key] = _mapper.Get(kv.Key);
            }
        }

        // 如果没有关键帧在 t=0，插入一个当前状态帧
        if (plan.KeyFrames[0].Time > 0.01f)
        {
            var zeroFrame = new MotionPlanner.KeyFrame { Time = 0f, Curve = MotionPlanner.InterpolationType.Linear };
            foreach (var kv in startValues)
                zeroFrame.Values[kv.Key] = kv.Value;
            plan.KeyFrames.Insert(0, zeroFrame);
        }

        while (elapsed < totalDuration + 0.1f)
        {
            // 暂停状态等待
            while (State == MotionState.Paused)
                yield return null;

            // 如果被外部停止了
            if (State != MotionState.Playing)
                yield break;

            // 找当前帧和下一帧
            MotionPlanner.KeyFrame current = null;
            MotionPlanner.KeyFrame next = null;

            for (int i = 0; i < plan.KeyFrames.Count - 1; i++)
            {
                if (elapsed >= plan.KeyFrames[i].Time && elapsed < plan.KeyFrames[i + 1].Time)
                {
                    current = plan.KeyFrames[i];
                    next = plan.KeyFrames[i + 1];
                    frameIndex = i;
                    break;
                }
            }

            // 如果超过最后一帧
            if (next == null)
            {
                var last = plan.KeyFrames[plan.KeyFrames.Count - 1];
                if (elapsed >= last.Time)
                {
                    foreach (var kv in last.Values)
                        _mapper.Set(kv.Key, kv.Value);
                    break;
                }
            }

            if (current != null && next != null)
            {
                // 计算插值进度
                float segmentDuration = next.Time - current.Time;
                float localT = segmentDuration > 0.001f
                    ? Mathf.Clamp01((elapsed - current.Time) / segmentDuration)
                    : 1f;

                float easedT = Ease(localT, next.Curve);

                // 插值所有参数
                var allKeys = new HashSet<string>(current.Values.Keys);
                allKeys.UnionWith(next.Values.Keys);

                foreach (var key in allKeys)
                {
                    float fromVal = current.Values.ContainsKey(key) ? current.Values[key] : _mapper.Get(key);
                    float toVal = next.Values.ContainsKey(key) ? next.Values[key] : _mapper.Get(key);
                    float interpolated = Mathf.Lerp(fromVal, toVal, easedT);
                    _mapper.Set(key, interpolated);
                }

                CompletedFrames = frameIndex;
            }

            Progress = Mathf.Clamp01(elapsed / totalDuration);
            yield return null;
            elapsed += Time.deltaTime;
        }

        // 确保最终状态
        var final = plan.KeyFrames[plan.KeyFrames.Count - 1];
        foreach (var kv in final.Values)
            _mapper.Set(kv.Key, kv.Value);

        Complete();
    }

    // ──────────────────────────────────────────────
    //  插值曲线
    // ──────────────────────────────────────────────

    /// <summary>应用插值曲线</summary>
    private static float Ease(float t, MotionPlanner.InterpolationType curve)
    {
        return curve switch
        {
            MotionPlanner.InterpolationType.Linear => t,
            MotionPlanner.InterpolationType.Smooth => t * t * (3f - 2f * t), // smoothstep
            MotionPlanner.InterpolationType.EaseOut => 1f - (1f - t) * (1f - t), // quad out
            MotionPlanner.InterpolationType.EaseIn => t * t, // quad in
            MotionPlanner.InterpolationType.Hold => 0f, // 保持到最后一刻跳变
            MotionPlanner.InterpolationType.Bounce => BounceEaseOut(t),
            _ => t
        };
    }

    private static float BounceEaseOut(float t)
    {
        if (t < 1f / 2.75f)
            return 7.5625f * t * t;
        else if (t < 2f / 2.75f)
        {
            t -= 1.5f / 2.75f;
            return 7.5625f * t * t + 0.75f;
        }
        else if (t < 2.5f / 2.75f)
        {
            t -= 2.25f / 2.75f;
            return 7.5625f * t * t + 0.9375f;
        }
        else
        {
            t -= 2.625f / 2.75f;
            return 7.5625f * t * t + 0.984375f;
        }
    }
}
