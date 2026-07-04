using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Live2D 动作控制器 — 统一管理表情和复合动作
///
/// 职责：
/// - 持有 ExpressionManager（表情）和 ActionPresetPlayer（复合动作）
/// - 提供 PlayExpression / PlayAction / StopAll 等高层 API
/// - 管理表情/动作的优先级和互斥
/// - 管理旧式硬编码动作的向后兼容适配器
/// </summary>
public class Live2DActionController
{
    // ================================================================
    //  子管理器
    // ================================================================

    public ExpressionManager Expressions { get; private set; }
    public ActionPresetPlayer Actions { get; private set; }

    // ================================================================
    //  状态
    // ================================================================

    /// <summary>当前是否在播放任何动作（表情或复合动作）</summary>
    public bool IsAnythingPlaying => Expressions != null && Expressions.IsTransitioning;

    /// <summary>当前复合动作名</summary>
    public string CurrentActionName => Actions?.CurrentAction;

    /// <summary>当前表情名</summary>
    public string CurrentExpressionName => Expressions?.CurrentExpression;

    // 旧式动作播放状态（从 Live2DRenderer 迁移过来的硬编码动作用）
    public bool IsLegacyActionPlaying { get; private set; }
    public int CurrentLegacyActionId { get; private set; }

    // 回调
    public event Action OnActionFinished;
    public event Action OnExpressionChanged;

    // ================================================================
    //  构造
    // ================================================================

    private readonly MonoBehaviour _coroutineHost;

    public Live2DActionController(Live2DParameterMapper mapper, MonoBehaviour coroutineHost)
    {
        _coroutineHost = coroutineHost ?? throw new ArgumentNullException(nameof(coroutineHost));

        Expressions = new ExpressionManager(mapper);
        Actions = new ActionPresetPlayer(mapper, coroutineHost);

        // 子管理器的事件桥接
        // Actions 完成时通知上层
    }

    /// <summary>加载所有预设（从 Resources）</summary>
    public void LoadAllPresets()
    {
        Expressions.LoadPresets();
        Actions.LoadPresets();
    }

    // ================================================================
    //  高层 API — 供 AI / 右键菜单 / 外部调用
    // ================================================================

    /// <summary>播放表情（淡入当前，淡出旧表情）</summary>
    public void PlayExpression(string name, float fadeTime = -1f)
    {
        if (Expressions == null) return;
        Expressions.Play(name, fadeTime);
        OnExpressionChanged?.Invoke();
    }

    /// <summary>停止表情（淡出）</summary>
    public void StopExpression(float fadeTime = -1f)
    {
        Expressions?.Stop(fadeTime);
        OnExpressionChanged?.Invoke();
    }

    /// <summary>播放复合动作</summary>
    public void PlayAction(string name, Action onComplete = null)
    {
        if (Actions == null)
        {
            onComplete?.Invoke();
            return;
        }

        // 停止旧式动作
        if (IsLegacyActionPlaying)
            StopLegacyAction();

        Actions.Play(name, () =>
        {
            OnActionFinished?.Invoke();
            onComplete?.Invoke();
        });
    }

    /// <summary>停止所有动作和表情</summary>
    public void StopAll()
    {
        Actions?.Stop();
        Expressions?.StopImmediate();
        StopLegacyAction();
    }

    /// <summary>停止所有并淡出</summary>
    public void StopAllWithFade(float fadeMs = 0.2f)
    {
        Actions?.StopWithFade(fadeMs);
        Expressions?.Stop(fadeMs);
        StopLegacyAction();
    }

    // ================================================================
    //  旧式硬编码动作适配器（向后兼容 ContextMenu / AutoChat）
    // ================================================================

    /// <summary>播放旧式硬编码动作（由 Live2DRenderer 实现）</summary>
    public void PlayLegacyAction(int actionId, Action onComplete = null)
    {
        // 停止复合动作
        Actions?.Stop();

        CurrentLegacyActionId = actionId;
        IsLegacyActionPlaying = true;
        // 实际播放由 Live2DRenderer 的 UpdateIdleAnimation() 驱动
        // 这个只是状态通知
    }

    /// <summary>停止旧式动作</summary>
    public void StopLegacyAction()
    {
        if (IsLegacyActionPlaying)
        {
            IsLegacyActionPlaying = false;
            CurrentLegacyActionId = 0;
            OnActionFinished?.Invoke();
        }
    }

    // ================================================================
    //  每帧更新
    // ================================================================

    public void Update(float deltaTime)
    {
        Expressions?.Update(deltaTime);
    }
}
