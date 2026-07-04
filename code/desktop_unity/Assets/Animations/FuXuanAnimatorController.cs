using UnityEngine;
using System;

/// <summary>
/// 符玄 Animator Controller
/// 提供 Idle 和 Walk 动动画状态切换功能
/// </summary>
[RequireComponent(typeof(Animator))]
public class FuXuanAnimatorController : MonoBehaviour
{
    public enum State
    {
        Idle,
        Walk
    }

    [Header("动画参数")]
    [Tooltip("移动速度")]
    public float Speed = 1.0f;

    [Header("调试")]
    public bool debugMode = true;

    private Animator animator;
    private State currentState = State.Idle;
    private State previousState = State.Idle;
    private float _poseLockUntil = 0f;

    private void Start()
    {
        animator = GetComponent<Animator>();
    }

    private void Update()
    {
        if (animator != null && Time.time >= _poseLockUntil)
        {
            // 根据速度设置 Animator 参数
            float speedParam = Speed > 0.1f ? 1f : 0f;
            animator.SetFloat("Speed", speedParam);
        }
    }

    public void SetSpeed(float speed)
    {
        Speed = speed;
    }

    public void ForceState(State state)
    {
        if (state == State.Walk)
        {
            Speed = 1f;
        }
        else
        {
            Speed = 0f;
        }
    }
}