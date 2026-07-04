using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 表情管理器 — 用 Live2DParameterMapper 驱动表情预设
///
/// 职责：
/// - 从 Resources/Live2D/ExpressionPresets/ 加载 JSON 表情定义
/// - 提供 SetExpression(name, fadeOverride?) 方法
/// - 每帧混合当前表情（带淡入淡出）
/// - 支持表情堆叠（优先级系统）或直接切换
///
/// 用法：
///   var em = new ExpressionManager(mapper);
///   em.LoadPresets();
///   em.Play("happy", 0.5f);  // 0.5秒淡入
///   em.Stop(0.3f);           // 0.3秒淡出
/// </summary>
public class ExpressionManager
{
    private readonly Live2DParameterMapper _mapper;
    private readonly Dictionary<string, ExpressionData> _presets = new Dictionary<string, ExpressionData>();

    // 当前表情状态
    private ExpressionData _current;
    private string _currentName;
    private float _fadeTimer;
    private float _fadeDuration;
    private float _fadeFromWeight;  // 0=纯旧表情, 1=纯新表情
    private bool _isPlaying;
    private bool _isFading;

    // 淡出中的旧表情（叠加混合用）
    private ExpressionData _oldPreset;
    private float _oldFadeTimer;
    private float _oldFadeDuration;

    /// <summary>当前表情名（null=无）</summary>
    public string CurrentExpression => _isPlaying ? _currentName : null;

    /// <summary>预设列表</summary>
    public IReadOnlyCollection<string> AvailableExpressions => _presets.Keys;

    public ExpressionManager(Live2DParameterMapper mapper)
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    // ================================================================
    //  加载
    // ================================================================

    /// <summary>从 Resources 路径加载所有 .exp3.json 文件</summary>
    public void LoadPresets(string resourcesFolder = "Live2D/ExpressionPresets")
    {
        _presets.Clear();

        try
        {
            TextAsset[] assets = Resources.LoadAll<TextAsset>(resourcesFolder);
            if (assets == null || assets.Length == 0)
            {
                Debug.Log($"[ExpressionManager] Resources/{resourcesFolder} 下无表情预设");
                return;
            }

            int count = 0;
            foreach (var asset in assets)
            {
                try
                {
                    var expData = ParseExpressionJson(asset.text);
                    if (expData != null && !string.IsNullOrEmpty(expData.name))
                    {
                        _presets[expData.name] = expData;
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ExpressionManager] 解析 {asset.name} 失败: {ex.Message}");
                }
            }

            Debug.Log($"[ExpressionManager] 已加载 {count} 个表情预设");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ExpressionManager] 加载表情预设失败: {ex.Message}");
        }
    }

    /// <summary>添加/注册一个运行时表情</summary>
    public void RegisterPreset(string name, ExpressionData data)
    {
        _presets[name] = data;
    }

    // ================================================================
    //  控制
    // ================================================================

    /// <summary>
    /// 播放指定表情。如果已有表情，先淡出当前再淡入新表情。
    /// </summary>
    public void Play(string name, float fadeTime = -1f)
    {
        if (!_presets.TryGetValue(name, out var data))
        {
            Debug.LogWarning($"[ExpressionManager] 未找到表情: {name}");
            return;
        }

        // 相同表情不重复切换
        if (_isPlaying && _currentName == name && !_isFading) return;

        float fade = fadeTime >= 0f ? fadeTime : data.fadeIn;

        // 保存旧表情用于淡出
        if (_isPlaying && _current != null)
        {
            _oldPreset = _current;
            _oldFadeTimer = 0f;
            _oldFadeDuration = Mathf.Min(_current.fadeOut, fade * 0.5f);
        }
        else
        {
            _oldPreset = null;
        }

        _current = data;
        _currentName = name;
        _fadeTimer = 0f;
        _fadeDuration = fade;
        _fadeFromWeight = 0f;
        _isPlaying = true;
        _isFading = true;
    }

    /// <summary>停止当前表情（淡出）</summary>
    public void Stop(float fadeTime = -1f)
    {
        if (!_isPlaying) return;

        if (_current != null)
        {
            _oldPreset = _current;
            _oldFadeTimer = 0f;
            _oldFadeDuration = fadeTime >= 0f ? fadeTime : _current.fadeOut;
        }

        _current = null;
        _currentName = null;
        _isPlaying = false;
        _isFading = false;
        _fadeTimer = 0f;
    }

    /// <summary>立即停止（无淡出）</summary>
    public void StopImmediate()
    {
        _current = null;
        _currentName = null;
        _oldPreset = null;
        _isPlaying = false;
        _isFading = false;
        _fadeTimer = 0f;
    }

    // ================================================================
    //  每帧更新 — 在 LateUpdate 中调用
    // ================================================================

    public void Update(float deltaTime)
    {
        if (_mapper == null) return;

        // 1. 更新旧表情淡出
        if (_oldPreset != null)
        {
            _oldFadeTimer += deltaTime;
            float t = Mathf.Clamp01(_oldFadeTimer / Mathf.Max(_oldFadeDuration, 0.001f));
            float weight = 1f - EaseOutQuad(t); // 新→0 淡出

            if (weight > 0.001f)
            {
                ApplyPreset(_oldPreset, weight);
            }
            else
            {
                _oldPreset = null;
            }
        }

        // 2. 更新新表情淡入
        if (_isFading && _current != null)
        {
            _fadeTimer += deltaTime;
            float t = Mathf.Clamp01(_fadeTimer / Mathf.Max(_fadeDuration, 0.001f));
            _fadeFromWeight = EaseOutQuad(t);

            if (t >= 1f)
            {
                _fadeFromWeight = 1f;
                _isFading = false;
            }
        }

        // 3. 应用当前表情
        if (_isPlaying && _current != null)
        {
            ApplyPreset(_current, _fadeFromWeight);
        }
    }

    /// <summary>是否有表情正在淡入/淡出</summary>
    public bool IsTransitioning => _isFading || _oldPreset != null;

    // ================================================================
    //  内部
    // ================================================================

    private void ApplyPreset(ExpressionData data, float weight)
    {
        if (weight <= 0f) return;

        foreach (var p in data.parameters)
        {
            float currentVal = _mapper.Get(p.semantic);
            float targetVal = p.value * weight;
            // 淡入时线性插值：0→target
            float finalVal = currentVal * (1f - weight) + targetVal;
            // 但这里不能用当前值做 lerp base，因为参数可能被其他系统（物理、idle）驱动
            // 正确方式：直接用 weight * target
            _mapper.Set(p.semantic, p.value * weight);
        }
    }

    // ================================================================
    //  JSON 解析
    // ================================================================

    [Serializable]
    private class ExpressionJson
    {
        public string name;
        public float fadeIn = 0.3f;
        public float fadeOut = 0.3f;
        public ExpressionJsonParam[] parameters;
    }

    [Serializable]
    private class ExpressionJsonParam
    {
        public string s;
        public float v;
    }

    /// <summary>解析单条表情 JSON</summary>
    public static ExpressionData ParseExpressionJson(string json)
    {
        var obj = JsonUtility.FromJson<ExpressionJson>(json);
        if (obj == null || string.IsNullOrEmpty(obj.name)) return null;

        var data = new ExpressionData
        {
            name = obj.name,
            fadeIn = obj.fadeIn,
            fadeOut = obj.fadeOut,
            parameters = new ExpressionParam[obj.parameters?.Length ?? 0]
        };

        if (obj.parameters != null)
        {
            for (int i = 0; i < obj.parameters.Length; i++)
            {
                data.parameters[i] = new ExpressionParam
                {
                    semantic = obj.parameters[i].s,
                    value = obj.parameters[i].v
                };
            }
        }

        return data;
    }

    // ================================================================
    //  Easing
    // ================================================================

    private static float EaseOutQuad(float t) => t * (2f - t);
}

/// <summary>运行时表情数据</summary>
public class ExpressionData
{
    public string name;
    public float fadeIn = 0.3f;
    public float fadeOut = 0.3f;
    public ExpressionParam[] parameters = Array.Empty<ExpressionParam>();
}

public struct ExpressionParam
{
    public string semantic;
    public float value;
}
