using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 复合动作播放器 — 用 Live2DParameterMapper 驱动时序动作
///
/// 从 Resources/Live2D/ActionPresets/ 加载 JSON 动作定义，
/// 每个动作可包含多个阶段（Phase），每个阶段有持续时间和参数目标。
///
/// 用法：
///   var player = new ActionPresetPlayer(mapper, monoBehaviour);
///   player.LoadPresets();
///   player.Play("stretch", onComplete: () => Debug.Log("done"));
///   player.Stop();
/// </summary>
public class ActionPresetPlayer
{
    private readonly Live2DParameterMapper _mapper;
    private readonly MonoBehaviour _coroutineHost;
    private readonly Dictionary<string, ActionData> _presets = new Dictionary<string, ActionData>();

    private Coroutine _currentCoroutine;
    private bool _isPlaying;

    /// <summary>是否正在播放</summary>
    public bool IsPlaying => _isPlaying;

    /// <summary>当前动作名</summary>
    public string CurrentAction { get; private set; }

    /// <summary>已加载的动作列表</summary>
    public IReadOnlyCollection<string> AvailableActions => _presets.Keys;

    public ActionPresetPlayer(Live2DParameterMapper mapper, MonoBehaviour coroutineHost)
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _coroutineHost = coroutineHost ?? throw new ArgumentNullException(nameof(coroutineHost));
    }

    // ================================================================
    //  加载
    // ================================================================

    /// <summary>从 Resources 路径加载所有动作 JSON</summary>
    public void LoadPresets(string resourcesFolder = "Live2D/ActionPresets")
    {
        _presets.Clear();

        try
        {
            TextAsset[] assets = Resources.LoadAll<TextAsset>(resourcesFolder);
            if (assets == null || assets.Length == 0)
            {
                Debug.Log($"[ActionPresetPlayer] Resources/{resourcesFolder} 下无动作预设");
                return;
            }

            int count = 0;
            foreach (var asset in assets)
            {
                try
                {
                    var data = ParseActionJson(asset.text, asset.name);
                    if (data != null)
                    {
                        _presets[data.name] = data;
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ActionPresetPlayer] 解析 {asset.name} 失败: {ex.Message}");
                }
            }

            Debug.Log($"[ActionPresetPlayer] 已加载 {count} 个动作预设");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ActionPresetPlayer] 加载动作预设失败: {ex.Message}");
        }
    }

    /// <summary>注册一个运行时动作</summary>
    public void RegisterPreset(string name, ActionData data)
    {
        _presets[name] = data;
    }

    // ================================================================
    //  播放控制
    // ================================================================

    /// <summary>播放指定动作</summary>
    public void Play(string name, Action onComplete = null)
    {
        if (!_presets.TryGetValue(name, out var data))
        {
            Debug.LogWarning($"[ActionPresetPlayer] 未找到动作: {name}");
            onComplete?.Invoke();
            return;
        }

        // 停止当前动作
        Stop();

        CurrentAction = name;
        _currentCoroutine = _coroutineHost.StartCoroutine(PlayRoutine(data, onComplete));
    }

    /// <summary>停止当前动作</summary>
    public void Stop()
    {
        if (_currentCoroutine != null)
        {
            _coroutineHost.StopCoroutine(_currentCoroutine);
            _currentCoroutine = null;
        }
        _isPlaying = false;
        CurrentAction = null;
    }

    /// <summary>立即停止并清空参数（带淡出）</summary>
    public void StopWithFade(float fadeOut = 0.2f)
    {
        if (_isPlaying)
        {
            _currentCoroutine = _coroutineHost.StartCoroutine(FadeOutRoutine(fadeOut));
        }
        else
        {
            Stop();
        }
    }

    // ================================================================
    //  播放协程
    // ================================================================

    private IEnumerator PlayRoutine(ActionData data, Action onComplete)
    {
        _isPlaying = true;

        // 手动驱动子迭代器 GetPlaySteps，以便能捕获异常
        // 注意：C# 不允许在 try-catch 块内使用 yield return (CS1626)，
        // 因此用 yield return current; 在 try 块之外，MoveNext() 在 try 块之内
        var steps = GetPlaySteps(data);
        if (steps != null)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!steps.MoveNext())
                        break;
                    current = steps.Current;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ActionPresetPlayer] 动作 '{data.name}' 播放异常: {ex.Message}\n{ex.StackTrace}");
                    break;
                }
                yield return current;
            }
        }

        // ★ 必须释放锁：无论是否异常，都确保 onComplete 被调用，防止宠物永久卡死
        _isPlaying = false;
        CurrentAction = null;
        _currentCoroutine = null;
        onComplete?.Invoke();
    }

    /// <summary>实际的播放步骤（可抛异常，由 PlayRoutine 捕获）</summary>
    private IEnumerator GetPlaySteps(ActionData data)
    {
        // 1. Global fade in
        if (data.globalFadeIn > 0.001f && data.phases.Length > 0 && data.phases[0].targets.Length > 0)
        {
            yield return FadeToTargets(data.phases[0], data.globalFadeIn);
        }

        // 2. 依次播放每个 Phase
        for (int i = 0; i < data.phases.Length; i++)
        {
            yield return PlayPhase(data.phases[i]);
        }

        // 3. Global fade out
        if (data.globalFadeOut > 0.001f)
        {
            yield return FadeToDefault(data, data.globalFadeOut);
        }
    }

    /// <summary>淡入到 Phase 的目标值</summary>
    private IEnumerator FadeToTargets(ActionPhaseData phase, float duration)
    {
        float elapsed = 0f;
        var snapshot = CaptureCurrentValues(phase);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float weight = EaseOutQuad(t);

            foreach (var target in phase.targets)
            {
                if (snapshot.TryGetValue(target.semantic, out float startVal))
                {
                    float val = Mathf.Lerp(startVal, target.value, weight);
                    _mapper.Set(target.semantic, val);
                }
            }

            yield return null;
        }

        // 保证最终值精确
        foreach (var target in phase.targets)
            _mapper.Set(target.semantic, target.value);
    }

    /// <summary>播放单个 Phase（用自定义曲线）</summary>
    private IEnumerator PlayPhase(ActionPhaseData phase)
    {
        float elapsed = 0f;
        var snapshot = CaptureCurrentValues(phase);

        while (elapsed < phase.duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(phase.duration, 0.001f));
            float curveVal = phase.curve.Evaluate(t);

            foreach (var target in phase.targets)
            {
                float curve = target.curveOverride?.Evaluate(t) ?? curveVal;
                // ★ 修复：从当前捕获值开始过渡，而非从 0（否则每个 Phase 都会瞬间跳回 0 重播）
                float startVal = snapshot.TryGetValue(target.semantic, out var sv) ? sv : 0f;
                float val = Mathf.Lerp(startVal, target.value, curve);
                _mapper.Set(target.semantic, val);
            }

            yield return null;
        }

        // 保证最终值精确
        foreach (var target in phase.targets)
            _mapper.Set(target.semantic, target.value);
    }

    /// <summary>所有受影响的参数淡出到 0</summary>
    private IEnumerator FadeToDefault(ActionData data, float duration)
    {
        // 收集所有用到的语义参数
        var allTargets = new Dictionary<string, float>();
        foreach (var phase in data.phases)
        {
            foreach (var t in phase.targets)
                allTargets[t.semantic] = 0f;
        }

        if (allTargets.Count == 0) yield break;

        float elapsed = 0f;
        var snapshot = CaptureCurrentValues(allTargets.Keys);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float weight = 1f - EaseOutQuad(t); // 1→0

            foreach (var kv in snapshot)
            {
                float val = kv.Value * weight;
                _mapper.Set(kv.Key, val);
            }

            yield return null;
        }

        foreach (var key in allTargets.Keys)
            _mapper.Set(key, 0f);
    }

    private IEnumerator FadeOutRoutine(float duration)
    {
        // 快速淡出所有当前动作参数
        // 由于我们不知道哪些参数被修改了，Safe 做法：只用 _isPlaying 做标志不实际清参数
        // 实际场景中，下一帧的 Idle 动画会覆盖回去
        _isPlaying = false;
        CurrentAction = null;
        _currentCoroutine = null;
        yield break;
    }

    // ================================================================
    //  辅助
    // ================================================================

    /// <summary>从 mapper 捕获 Phase 中所有参数的当前值</summary>
    private Dictionary<string, float> CaptureCurrentValues(ActionPhaseData phase)
    {
        var result = new Dictionary<string, float>();
        foreach (var t in phase.targets)
        {
            result[t.semantic] = _mapper.Get(t.semantic);
        }
        return result;
    }

    /// <summary>从 mapper 捕获一批参数的当前值</summary>
    private Dictionary<string, float> CaptureCurrentValues(IEnumerable<string> semantics)
    {
        var result = new Dictionary<string, float>();
        foreach (var s in semantics)
            result[s] = _mapper.Get(s);
        return result;
    }

    // ================================================================
    //  Easing
    // ================================================================

    private static float EaseOutQuad(float t) => t * (2f - t);
    private static float EaseInQuad(float t) => t * t;
    private static float EaseInCubic(float t) => t * t * t;

    // ================================================================
    //  JSON 解析
    // ================================================================

    [Serializable]
    private class ActionJson
    {
        public string name;
        public bool lockMovement = true;
        public float globalFadeIn = 0.2f;
        public float globalFadeOut = 0.3f;
        public ActionPhaseJson[] phases;
    }

    [Serializable]
    private class ActionPhaseJson
    {
        public float duration = 1f;
        public ParamTargetJson[] targets;
        // curve 用 key 字符串表示： "easeOutQuad", "easeInQuad", "easeInCubic", "linear", "sin"
        public string curve = "easeOutQuad";
    }

    [Serializable]
    private class ParamTargetJson
    {
        public string s;  // semantic
        public float v;   // value
        // 可选 per-param curve
        public string curve;
    }

    /// <summary>解析动作 JSON</summary>
    public static ActionData ParseActionJson(string json, string fallbackName)
    {
        var obj = JsonUtility.FromJson<ActionJson>(json);
        if (obj == null) return null;

        if (string.IsNullOrEmpty(obj.name)) obj.name = fallbackName;

        var data = new ActionData
        {
            name = obj.name,
            lockMovement = obj.lockMovement,
            globalFadeIn = obj.globalFadeIn,
            globalFadeOut = obj.globalFadeOut,
            phases = new ActionPhaseData[obj.phases?.Length ?? 0]
        };

        if (obj.phases != null)
        {
            for (int i = 0; i < obj.phases.Length; i++)
            {
                var pj = obj.phases[i];
                var phase = new ActionPhaseData
                {
                    duration = pj.duration,
                    curve = ResolveCurve(pj.curve),
                    targets = new ParamTargetData[pj.targets?.Length ?? 0]
                };

                if (pj.targets != null)
                {
                    for (int j = 0; j < pj.targets.Length; j++)
                    {
                        var tj = pj.targets[j];
                        phase.targets[j] = new ParamTargetData
                        {
                            semantic = tj.s,
                            value = tj.v,
                            curveOverride = !string.IsNullOrEmpty(tj.curve) ? ResolveCurve(tj.curve) : null
                        };
                    }
                }

                data.phases[i] = phase;
            }
        }

        return data;
    }

    private static AnimationCurve ResolveCurve(string name)
    {
        return name?.ToLowerInvariant() switch
        {
            "linear" => AnimationCurve.Linear(0f, 0f, 1f, 1f),
            "easeinquad" => AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),  // 近似
            "easeoutquad" => new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0f, 0f)),
            "easeincubic" => new AnimationCurve(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 0f)),
            "sin" => new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.25f, 1f), new Keyframe(0.75f, 0f), new Keyframe(1f, 0f)),
            "triangle" => new AnimationCurve(new Keyframe(0f, 0f, 1f, 1f), new Keyframe(0.5f, 1f, 0f, 0f), new Keyframe(1f, 0f, -1f, -1f)),
            "trapezoid" => CreateTrapezoidCurve(),
            _ => AnimationCurve.EaseInOut(0f, 0f, 1f, 1f)
        };

        static AnimationCurve CreateTrapezoidCurve()
        {
            var c = new AnimationCurve();
            c.AddKey(new Keyframe(0f, 0f, 0f, 4f));
            c.AddKey(new Keyframe(0.25f, 1f, 0f, 0f));
            c.AddKey(new Keyframe(0.75f, 1f, 0f, 0f));
            c.AddKey(new Keyframe(1f, 0f, -4f, 0f));
            return c;
        }
    }
}

/// <summary>运行时动作数据</summary>
public class ActionData
{
    public string name;
    public bool lockMovement = true;
    public float globalFadeIn = 0.2f;
    public float globalFadeOut = 0.3f;
    public ActionPhaseData[] phases = Array.Empty<ActionPhaseData>();
}

public class ActionPhaseData
{
    public float duration = 1f;
    public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public ParamTargetData[] targets = Array.Empty<ParamTargetData>();
}

public class ParamTargetData
{
    public string semantic;
    public float value;
    public AnimationCurve curveOverride;
}
