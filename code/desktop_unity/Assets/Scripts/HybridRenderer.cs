using UnityEngine;

/// <summary>
/// 混合渲染管理器 — 协调 Live2D(表情) 与 3D(动作) 切换
///
/// 切换规则：
/// - Idle / 点击 / 停着 → Live2D（精细表情+物理飘动）
/// - 走路 / 飞行/ 空中 → 3D（骨骼动画）
/// - 过渡方式 → Alpha 交叉淡入淡出（0.3s）
///
/// 用法：
/// 1. 挂到 DesktopPet 同一个 GameObject 上
/// 2. Live2DRenderer 和 Model3DRenderer 挂到同一个 GameObject 上
/// 3. HybridRenderer 自动接管 IPetRenderer 接口
/// </summary>
[RequireComponent(typeof(DesktopPet))]
public class HybridRenderer : MonoBehaviour, IPetRenderer
{
    [Header("渲染器引用")]
    public Live2DRenderer live2DRenderer;
    public Model3DRenderer model3DRenderer;

    [Header("过渡设置")]
    [Tooltip("交叉淡入淡出时长（秒）")]
    public float crossFadeDuration = 0.3f;

    private enum Mode { Live2D, Model3D, Transitioning }
    private Mode _currentMode = Mode.Live2D;
    private Mode _targetMode = Mode.Live2D;
    private float _transitionTimer = 0f;
    private float _live2DAlpha = 1f;
    private float _model3DAlpha = 0f;
    private bool _shouldUse3D = false;
    private bool _initialized = false;

    private void Start()
    {
        if (live2DRenderer == null) live2DRenderer = GetComponent<Live2DRenderer>();
        if (live2DRenderer == null)
        {
            live2DRenderer = gameObject.AddComponent<Live2DRenderer>();
            Debug.Log("[HybridRenderer] 自动添加了 Live2DRenderer");
        }

        if (model3DRenderer == null) model3DRenderer = GetComponent<Model3DRenderer>();
        if (model3DRenderer == null)
        {
            model3DRenderer = gameObject.AddComponent<Model3DRenderer>();
            Debug.Log("[HybridRenderer] 自动添加了 Model3DRenderer");
        }

        live2DRenderer.SetAlpha(1f);
        model3DRenderer.SetAlpha(0f);
        model3DRenderer.enabled = false;
        _initialized = true;
        Debug.Log("[HybridRenderer] 初始化完成，初始模式: Live2D");
    }

    private void Update()
    {
        if (!_initialized) return;

        // === 调试快捷键（按 1=Live2D, 2=3D, Play 模式下用）===
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            Debug.Log("[HybridRenderer] 调试: 强制切换到 Live2D");
            live2DRenderer.SetAlpha(1f);
            model3DRenderer.SetAlpha(0f);
            live2DRenderer.enabled = true;
            model3DRenderer.enabled = false;
            _currentMode = Mode.Live2D;
            _targetMode = Mode.Live2D;
            return;
        }
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            Debug.Log("[HybridRenderer] 调试: 强制切换到 3D");
            live2DRenderer.SetAlpha(0f);
            model3DRenderer.SetAlpha(1f);
            live2DRenderer.enabled = false;
            model3DRenderer.enabled = true;
            _currentMode = Mode.Model3D;
            _targetMode = Mode.Model3D;
            return;
        }

        if (_currentMode == Mode.Transitioning)
        {
            _transitionTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_transitionTimer / crossFadeDuration);
            float eased = t * t * (3f - 2f * t);

            float targetL2D = (_targetMode == Mode.Live2D) ? 1f : 0f;
            float target3D = (_targetMode == Mode.Model3D) ? 1f : 0f;

            _live2DAlpha = Mathf.Lerp(_live2DAlpha, targetL2D, eased);
            _model3DAlpha = Mathf.Lerp(_model3DAlpha, target3D, eased);

            live2DRenderer.SetAlpha(_live2DAlpha);
            model3DRenderer.SetAlpha(_model3DAlpha);

            if (t >= 1f)
            {
                _currentMode = _targetMode;
                _transitionTimer = 0f;

                if (_currentMode == Mode.Live2D)
                {
                    model3DRenderer.SetAlpha(0f);
                    model3DRenderer.enabled = false;
                }
                else
                {
                    live2DRenderer.SetAlpha(0f);
                    live2DRenderer.enabled = false;
                }
                Debug.Log($"[HybridRenderer] 切换完成 → {_currentMode}");
            }
        }
    }

    private void RequestSwitch(bool use3D)
    {
        if (use3D == _shouldUse3D && _currentMode == (use3D ? Mode.Model3D : Mode.Live2D))
            return;

        _shouldUse3D = use3D;
        Mode newTarget = use3D ? Mode.Model3D : Mode.Live2D;
        if (newTarget == _currentMode) return;

        if (!live2DRenderer.enabled) live2DRenderer.enabled = true;
        if (!model3DRenderer.enabled) model3DRenderer.enabled = true;

        _targetMode = newTarget;
        _currentMode = Mode.Transitioning;
        _transitionTimer = 0f;
    }

    #region IPetRenderer

    public void ShowDragPose() { RequestSwitch(false); live2DRenderer.ShowDragPose(); }
    public void ShowClickPose(Vector2 screenPos) { RequestSwitch(false); live2DRenderer.ShowClickPose(screenPos); }
    public void ShowLandPose() { RequestSwitch(false); live2DRenderer.ShowLandPose(); }
    public void ShowWalkPose() { RequestSwitch(false); live2DRenderer.ShowWalkPose(); }
    public void ShowStopPose(float lockSeconds) { RequestSwitch(false); live2DRenderer.ShowStopPose(lockSeconds); }

    public void ShowWallHitPose(int direction) { live2DRenderer.ShowWallHitPose(direction); }

    public void SetEyeTarget(float? targetX, float? targetY) { live2DRenderer.SetEyeTarget(targetX, targetY); }

    public void OnPetUpdate(int petX, int petY, int petWidth, int petHeight,
                            int petVx, int petVy, bool onGround, bool isDragging, bool isPaused)
    {
        if (!_initialized) return;

        if (!isDragging)
        {
            // TODO: 接入 3D 模型后可根据条件切 true：
            //   !onGround (空中) → 3D; Mathf.Abs(petVx) > 0 (行走) → 3D; 否则 → Live2D
            // 当前 3D 模型未接入，强制 Live2D
            bool shouldUse3D = false;
            RequestSwitch(shouldUse3D);
        }

        live2DRenderer.OnPetUpdate(petX, petY, petWidth, petHeight,
            petVx, petVy, onGround, isDragging, isPaused);
        model3DRenderer.OnPetUpdate(petX, petY, petWidth, petHeight,
            petVx, petVy, onGround, isDragging, isPaused);
    }

    #endregion
}
