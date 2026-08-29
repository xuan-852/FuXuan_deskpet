using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 拖拽处理 — 鼠标拖拽宠物 + 抛掷
///
/// 核心设计：
/// - 每帧根据鼠标位置动态设置点击穿透
///   → 鼠标在宠物范围内：关穿透，Unity 接收事件
///   → 鼠标在宠物范围外：开穿透，点击穿透到桌面
/// - 拖拽时用 SetWindowPos 移动整个窗口
/// - 松开时根据拖拽速度计算抛掷初速度
/// </summary>
[RequireComponent(typeof(DesktopPet))]
public class DragHandler : MonoBehaviour
{
    #region Win32 API

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(System.IntPtr hWnd, System.IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(System.IntPtr hWnd, out RECT lpRect);

    private static readonly System.IntPtr HWND_TOPMOST = new System.IntPtr(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    #endregion

    private DesktopPet _pet;
    private WindowOverlay _window;
    private IPetRenderer _renderer;

    [Header("拖拽设置")]
    [Tooltip("触发拖拽的最小移动像素")]
    public int dragThreshold = 5;

    [Tooltip("抛掷速度系数（像素/帧）")]
    public float throwScale = 0.5f;

    [Tooltip("最大抛掷速度")]
    public int maxThrowSpeed = 12;

    [Header("点击设置")]
    [Tooltip("点击后强制暂停时间（秒）")]
    public float clickPauseDuration = 1.0f;

    [Header("眼睛跟随")]
    [Tooltip("鼠标在此距离内触发眼睛跟随（像素）")]
    public float eyeFollowDistance = 150f;

    // 拖拽状态
    private bool _isDragging = false;
    private bool _isClickCandidate = false;
    private Vector2 _dragStartMouse;
    private Vector2 _lastMousePos;
    private Vector2 _dragPosition;
    private Vector2 _velocityBuffer;
    private int _velocityFrames;
    private readonly Vector2[] _velocitySamples = new Vector2[5];
    private int _velocitySampleIndex;
    private int _velocitySampleCount;
    private Coroutine _simulatedDragRoutine;

    // 公开事件（供 AutoChat 监听）
    public System.Action OnPetClicked;
    public System.Action OnDragEnded;

    // 上次交互时间（供睡觉系统检测无交互时长）
    [System.NonSerialized]
    public float lastInteractionTime = 0f;

    // BallPanel 引用（悬浮球子面板）
    private BallPanel _ballPanel;

    // 鼠标在宠物范围内的状态（每帧更新）
    private bool _mouseOverPet = false;

    // 右面板引用
    private RightPanel _rightPanel;

    // 面板打开状态追踪（用于关闭后强制重算穿透）
    private bool _lastFramePanelOpen = false;

    private void Start()
    {
        _pet = GetComponent<DesktopPet>();
        _window = GetComponent<WindowOverlay>();
        if (_window == null)
            _window = FindObjectOfType<WindowOverlay>();
        _renderer = GetComponent<IPetRenderer>();

        _ballPanel = GetComponent<BallPanel>();
        if (_ballPanel == null) _ballPanel = FindObjectOfType<BallPanel>();

        // 获取 RightPanel 引用
        _rightPanel = GetComponent<RightPanel>();
        if (_rightPanel == null) _rightPanel = FindObjectOfType<RightPanel>();
    }

    private void Update()
    {
        // ★ 动态刷新 BallPanel / RightPanel 引用（可能在 Start 之后才被添加）
        if (_ballPanel == null)
        {
            _ballPanel = GetComponent<BallPanel>() ?? FindObjectOfType<BallPanel>();
            if (_ballPanel != null)
                Debug.Log("[DragHandler] 延迟获取 BallPanel 引用成功");
        }
        if (_rightPanel == null)
        {
            _rightPanel = GetComponent<RightPanel>() ?? FindObjectOfType<RightPanel>();
        }

        // 本帧是否有面板需要接收输入（RightPanel 交互区 / BallPanel 打开）
        bool panelOpenNow = false;

        // ========== 0a. RightPanel 展开时 ==========
        if (_rightPanel != null)
        {
            Vector2 mousePos = GetMousePos();
            // 外置模式下聊天面板已移到独立窗口。Unity 主窗口保持置顶，
            // 但不能再让整块旧面板区域关闭穿透，否则会遮住非置顶外置窗口。
            bool overRightPanel = !_rightPanel.IsExternalMode
                && _rightPanel.IsPointInInteractiveArea(mousePos);
            if (overRightPanel)
            {
                panelOpenNow = true;
                _window?.SetClickThrough(false); // 关穿透，让 Unity 接收点击
                // 不重置 _mouseOverPet：让 UpdateClickThrough 统一管理缓存，避免每帧刷状态变更日志
                // 不 return，让拖拽/点击等继续
            }
        }

        // ========== 0b. BallPanel 子面板打开时 ==========
        if (_ballPanel != null && _ballPanel.IsOpen)
        {
            panelOpenNow = true;
            _lastFramePanelOpen = true; // 保持标记：BallPanel 打开期间（含 return 分支）确保关闭时触发穿透重置
            Vector2 mousePos = GetMousePos();
            bool overPanel = _ballPanel.IsMouseOverPanel(mousePos);
            _window?.SetClickThrough(!overPanel);
            _mouseOverPet = false;

            // 右键关闭面板
            if (Input.GetMouseButtonDown(1))
            {
                _ballPanel.Close();
            }
            else
            {
                return; // 不处理拖拽
            }
        }

        // ========== 面板关闭检测：上一帧开着、本帧关了 → 强制重置穿透 ==========
        if (_lastFramePanelOpen && !panelOpenNow)
        {
            _mouseOverPet = false;
            bool overPet = IsPointInPet(GetMousePos());
            _window?.SetClickThrough(!overPet);
            Debug.Log($"[DragHandler] 面板关闭，强制重置穿透: overPet={overPet}");
        }
        _lastFramePanelOpen = panelOpenNow;

        // ========== 1. 每帧更新点击穿透 ==========
        UpdateClickThrough();

        // ========== 2. 右键：关闭 BallPanel / 悬浮球菜单 ==========
        if (Input.GetMouseButtonDown(1))
        {
            if (_ballPanel != null && _ballPanel.IsOpen)
            {
                _ballPanel.Close();
            }
                // ★ 不再打开旧 ContextMenu
        }

        if (_pet.isPaused)
            return;

        // ========== 3. 鼠标左键按下 ==========
        if (Input.GetMouseButtonDown(0))
        {
            lastInteractionTime = Time.time;
            Vector2 mousePos = GetMousePos();
            if (IsPointInPet(mousePos))
            {
                BeginPointerInteraction(mousePos);
            }
        }

        // ========== 4. 鼠标移动（按住左键） ==========
        if (Input.GetMouseButton(0) && _isClickCandidate)
        {
            lastInteractionTime = Time.time;
            Vector2 mousePos = GetMousePos();
            ApplyPointerPosition(mousePos);
        }

        // ========== 5. 鼠标左键释放 ==========
        if (Input.GetMouseButtonUp(0))
        {
            EndPointerInteraction(true);
        }

        // ========== 6. 眼睛跟随鼠标（鼠标靠近宠物时眼睛看鼠标方向）==========
        UpdateEyeFollow();
    }

    /// <summary>
    /// 计算鼠标到宠物中心的距离，靠近时通知渲染器眼睛跟随
    /// </summary>
    private void UpdateEyeFollow()
    {
        if (_renderer == null || _pet == null) return;

        // 菜单打开或暂停时不跟随
        if (_ballPanel != null && _ballPanel.IsOpen) { _renderer.SetEyeTarget(null, null); return; }
        if (_pet.isPaused) { _renderer.SetEyeTarget(null, null); return; }
        if (_pet.isDragging) { _renderer.SetEyeTarget(null, null); return; }

        Vector2 mousePos = GetMousePos();

        // 宠物中心
        float centerX = _pet.petX + _pet.petWidth / 2f;
        float centerY = _pet.petY + _pet.petHeight / 2f;

        float dx = mousePos.x - centerX;
        float dy = mousePos.y - centerY;
        float dist = Mathf.Sqrt(dx * dx + dy * dy);

        if (dist <= eyeFollowDistance && dist > 0.1f)
        {
            // 鼠标靠近也算交互，重置无交互计时
            lastInteractionTime = Time.time;

            // 归一化方向向量，距离越近越满
            float t = 1f - Mathf.Clamp01(dist / eyeFollowDistance);
            float eased = t * t; // 平方让靠近时更快看向鼠标
            float targetX = (dx / dist) * eased;
            float targetY = (dy / dist) * eased;
            _renderer.SetEyeTarget(targetX, targetY);
        }
        else
        {
            // 超出距离，恢复默认眼球动画
            _renderer.SetEyeTarget(null, null);
        }
    }

    /// <summary>
    /// 窗口就绪后调用，强制下一帧重算穿透状态（解决启动时序问题）
    /// </summary>
    public void ResetClickState()
    {
        _mouseOverPet = false; // 强制下一帧 UpdateClickThrough 重新评估
    }

    /// <summary>
    /// 供测试命令使用的宠物屏幕区域（左上角原点，与 inbox 坐标一致）。
    /// </summary>
    public Rect GetDebugPetRect()
    {
        if (_pet == null) return new Rect();
        return new Rect(_pet.petX, _pet.petY, _pet.petWidth, _pet.petHeight);
    }

    /// <summary>
    /// 供测试命令使用的状态快照。只读，不写入记忆或 PlayerPrefs。
    /// </summary>
    public string GetDebugState()
    {
        if (_pet == null) return "pet=uninitialized";
        return $"pet=({_pet.petX},{_pet.petY},{_pet.petWidth},{_pet.petHeight}) "
            + $"dragging={_isDragging} candidate={_isClickCandidate} "
            + $"petDragging={_pet.isDragging} velocity=({_pet.petVx},{_pet.petVy}) "
            + $"paused={_pet.isPaused}";
    }

    /// <summary>
    /// 测试模式：模拟一次真实点击。坐标使用屏幕左上角原点。
    /// </summary>
    public bool SimulateClick(Vector2 screenPos)
    {
        if (_pet == null || _pet.isPaused)
        {
            Debug.LogWarning("[DragHandler] 模拟点击忽略：宠物未就绪或当前暂停");
            return false;
        }
        if (!IsPointInPet(screenPos))
        {
            Debug.LogWarning($"[DragHandler] 模拟点击忽略：坐标不在宠物区域 ({screenPos.x:F0},{screenPos.y:F0})");
            return false;
        }

        lastInteractionTime = Time.time;
        ApplyClick(screenPos, true);
        return true;
    }

    /// <summary>
    /// 测试模式：模拟按住左键从 start 拖到 end。每一步让 Unity 有机会渲染，
    /// 因而既能验证位置跟随，也能观察挣扎动画，而不是只改最终坐标。
    /// </summary>
    public bool SimulateDrag(Vector2 start, Vector2 end, int steps = 12)
    {
        if (_pet == null || _pet.isPaused)
        {
            Debug.LogWarning("[DragHandler] 模拟拖动忽略：宠物未就绪或当前暂停");
            return false;
        }
        if (!IsPointInPet(start))
        {
            Debug.LogWarning($"[DragHandler] 模拟拖动忽略：起点不在宠物区域 ({start.x:F0},{start.y:F0})");
            return false;
        }

        if (_simulatedDragRoutine != null)
        {
            StopCoroutine(_simulatedDragRoutine);
            AbortPointerInteraction();
        }

        int safeSteps = Mathf.Clamp(steps, 2, 60);
        _simulatedDragRoutine = StartCoroutine(SimulateDragRoutine(start, end, safeSteps));
        Debug.Log($"[DragHandler] 模拟拖动开始: ({start.x:F0},{start.y:F0})→({end.x:F0},{end.y:F0}), steps={safeSteps}");
        return true;
    }

    /// <summary>测试模式：中止模拟或丢失焦点后的拖动，不产生抛掷。</summary>
    public void ResetInputSimulation()
    {
        if (_simulatedDragRoutine != null)
        {
            StopCoroutine(_simulatedDragRoutine);
            _simulatedDragRoutine = null;
        }
        AbortPointerInteraction();
        Debug.Log("[DragHandler] 模拟输入状态已重置");
    }

    private IEnumerator SimulateDragRoutine(Vector2 start, Vector2 end, int steps)
    {
        BeginPointerInteraction(start);
        for (int i = 1; i <= steps; i++)
        {
            ApplyPointerPosition(Vector2.Lerp(start, end, i / (float)steps));
            yield return null;
        }
        EndPointerInteraction(true);
        _simulatedDragRoutine = null;
    }

    private void BeginPointerInteraction(Vector2 mousePos)
    {
        _isClickCandidate = true;
        _isDragging = false;
        _pet.isDragging = false;
        _dragStartMouse = mousePos;
        _lastMousePos = mousePos;
        _dragPosition = new Vector2(_pet.petX, _pet.petY);
        _velocityBuffer = Vector2.zero;
        _velocityFrames = 0;
        _velocitySampleIndex = 0;
        _velocitySampleCount = 0;
        lastInteractionTime = Time.time;

        // 按下后锁住输入接收，避免鼠标刚一离开宠物矩形就把窗口重新设为穿透。
        _window?.SetClickThrough(false);
    }

    private void ApplyPointerPosition(Vector2 mousePos)
    {
        if (!_isClickCandidate) return;

        Vector2 deltaFromStart = mousePos - _dragStartMouse;
        if (!_isDragging && deltaFromStart.magnitude >= dragThreshold)
        {
            _isDragging = true;
            _pet.isDragging = true;
            if (_renderer != null) _renderer.ShowDragPose();
            Debug.Log("[DragHandler] 拖动已启动");
        }

        if (!_isDragging) return;

        lastInteractionTime = Time.time;
        Vector2 moveDelta = mousePos - _lastMousePos;
        _dragPosition += moveDelta;
        _dragPosition.x = Mathf.Clamp(_dragPosition.x, 0f, Mathf.Max(0f, Screen.width - _pet.petWidth));
        _dragPosition.y = Mathf.Clamp(_dragPosition.y, 0f, Mathf.Max(0f, Screen.height - _pet.petHeight));
        _pet.petX = Mathf.RoundToInt(_dragPosition.x);
        _pet.petY = Mathf.RoundToInt(_dragPosition.y);

        if (moveDelta.x > 0f) _pet.petVx = 1;
        else if (moveDelta.x < 0f) _pet.petVx = -1;

        AddVelocitySample(moveDelta);
        _lastMousePos = mousePos;
    }

    private void AddVelocitySample(Vector2 delta)
    {
        _velocitySamples[_velocitySampleIndex] = delta;
        _velocitySampleIndex = (_velocitySampleIndex + 1) % _velocitySamples.Length;
        _velocitySampleCount = Mathf.Min(_velocitySampleCount + 1, _velocitySamples.Length);
        _velocityBuffer = Vector2.zero;
        for (int i = 0; i < _velocitySampleCount; i++) _velocityBuffer += _velocitySamples[i];
        _velocityFrames = _velocitySampleCount;
    }

    private void EndPointerInteraction(bool applyThrow)
    {
        if (_isDragging)
        {
            _pet.isDragging = false;
            if (applyThrow)
            {
                Vector2 avgVelocity = _velocityFrames > 0
                    ? _velocityBuffer / _velocityFrames
                    : Vector2.zero;
                int vx = Mathf.RoundToInt(Mathf.Clamp(avgVelocity.x * throwScale,
                    -maxThrowSpeed, maxThrowSpeed));
                int vy = Mathf.RoundToInt(Mathf.Clamp(avgVelocity.y * throwScale,
                    -maxThrowSpeed, maxThrowSpeed));
                _pet.ApplyDragVelocity(vx, vy);
                Debug.Log($"[DragHandler] 抛掷: ({vx}, {vy})");
                OnDragEnded?.Invoke();
            }
        }
        else if (_isClickCandidate && applyThrow)
        {
            ApplyClick(_dragStartMouse, false);
        }

        _isDragging = false;
        _isClickCandidate = false;
        _velocityBuffer = Vector2.zero;
        _velocityFrames = 0;
    }

    private void ApplyClick(Vector2 screenPos, bool simulated)
    {
        Vector2 unityScreenPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        if (_renderer != null) _renderer.ShowClickPose(unityScreenPos);
        _pet.Pause(clickPauseDuration);
        Debug.Log($"[DragHandler] {(simulated ? "模拟点击" : "轻击宠物")}: screen=({screenPos.x:F0},{screenPos.y:F0})");
        OnPetClicked?.Invoke();
    }

    private void AbortPointerInteraction()
    {
        _pet.isDragging = false;
        _isDragging = false;
        _isClickCandidate = false;
        _velocityBuffer = Vector2.zero;
        _velocityFrames = 0;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && (_isClickCandidate || _isDragging))
        {
            AbortPointerInteraction();
            Debug.Log("[DragHandler] 窗口失去焦点，已中止拖动并恢复输入状态");
        }
    }

    private void UpdateClickThrough()
    {
        if (_window == null) return;

        Vector2 mousePos = GetMousePos();
        bool overPet = IsPointInPet(mousePos);

        // ★ BallPanel 区域也接收点击
        bool overPanel = _ballPanel != null
            && _ballPanel.IsMouseOverPanel(mousePos);

        // ★ RightPanel 区域也接收点击
        bool overRightPanel = _rightPanel != null
            && !_rightPanel.IsExternalMode
            && _rightPanel.IsPointInInteractiveArea(mousePos);

        // ★ 审批弹窗打开时全屏接收点击（模态遮罩居中绘制，鼠标常不在宠物/面板上）
        bool approvalDialogOpen = _rightPanel != null
            && _rightPanel.IsApprovalDialogOpen;

        // 点击候选/拖动期间必须保持接收输入，即使鼠标已经离开宠物当前矩形；
        // 否则 WS_EX_TRANSPARENT 会在拖动中途吞掉后续 MouseDrag/MouseUp，表现为“挣扎/卡住”。
        bool needInput = _isClickCandidate || _isDragging
            || overPet || overPanel || overRightPanel || approvalDialogOpen;

        if (needInput != _mouseOverPet)
        {
            UnityEngine.Debug.Log($"[DragHandler] 穿透状态变更: mouseOverPet={_mouseOverPet}→{needInput}, mouse=({mousePos.x},{mousePos.y}), pet=({_pet.petX},{_pet.petY},{_pet.petWidth},{_pet.petHeight})");
            _mouseOverPet = needInput;
            _window.SetClickThrough(!needInput);
        }
    }

    private Vector2 GetMousePos()
    {
        Vector2 p = Input.mousePosition;
        p.y = Screen.height - p.y;
        return p;
    }

    private bool IsPointInPet(Vector2 mousePos)
    {
        return mousePos.x >= _pet.petX &&
               mousePos.x <= _pet.petX + _pet.petWidth &&
               mousePos.y >= _pet.petY &&
               mousePos.y <= _pet.petY + _pet.petHeight;
    }

    private System.IntPtr GetUnityWindowHandle()
    {
        return _window != null ? _window.WindowHandle : System.IntPtr.Zero;
    }
}
