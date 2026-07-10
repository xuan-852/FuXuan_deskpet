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
    private Vector2 _velocityBuffer;
    private int _velocityFrames;

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

    // 底部输入栏引用
    private BottomInputBar _bottomBar;

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

        // BottomInputBar 可能稍后才添加，Start 中找一次
        RefreshBottomBar();

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

        // ========== 0a. RightPanel 展开时 ==========
        if (_rightPanel != null)
        {
            Vector2 mousePos = GetMousePos();
            bool overRightPanel = _rightPanel.IsPointInInteractiveArea(mousePos);
            if (overRightPanel)
            {
                _lastFramePanelOpen = true;
                _window?.SetClickThrough(false); // 关穿透，让 Unity 接收点击
                _mouseOverPet = false;
                // 不 return，让拖拽/点击等继续
            }
        }

        // ========== 0b. BallPanel 子面板打开时 ==========
        if (_ballPanel != null && _ballPanel.IsOpen)
        {
            _lastFramePanelOpen = true;
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

        // ========== 面板关闭检测：前一帧还开着，这帧关了 ==========
        if (_lastFramePanelOpen)
        {
            _lastFramePanelOpen = false;
            _mouseOverPet = false;
            bool overPet = IsPointInPet(GetMousePos());
            _window?.SetClickThrough(!overPet);
            Debug.Log($"[DragHandler] 面板关闭，强制重置穿透: overPet={overPet}");
        }

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
                _isClickCandidate = true;
                _isDragging = false;
                _pet.isDragging = false;
                _dragStartMouse = mousePos;
                _lastMousePos = mousePos;
                _velocityBuffer = Vector2.zero;
                _velocityFrames = 0;
            }
        }

        // ========== 4. 鼠标移动（按住左键） ==========
        if (Input.GetMouseButton(0) && _isClickCandidate)
        {
            lastInteractionTime = Time.time;
            Vector2 mousePos = GetMousePos();
            Vector2 delta = mousePos - _dragStartMouse;

            if (delta.magnitude >= dragThreshold && !_isDragging)
            {
                _isDragging = true;
                _pet.isDragging = true;
                if (_renderer != null) _renderer.ShowDragPose();
            }

            if (_isDragging)
            {
                // 不移动窗口！窗口始终全屏固定在 (0,0)
                // 只更新宠物的渲染坐标 (petX/petY)
                Vector2 moveDelta = mousePos - _lastMousePos;
                _pet.petX += (int)moveDelta.x;
                _pet.petY += (int)moveDelta.y;

                // v1 行为：拖拽中更新 petVx 方向 → 渲染器切换左右的拖拽图
                if (moveDelta.x > 0)
                    _pet.petVx = 1;   // 朝右 → 显示 right 文件夹的 3.png
                else if (moveDelta.x < 0)
                    _pet.petVx = -1;  // 朝左 → 显示 left 文件夹的 3.png

                // 记录速度
                _velocityBuffer += (mousePos - _lastMousePos);
                _velocityFrames++;
                if (_velocityFrames > 5)
                {
                    _velocityBuffer -= _velocityBuffer * 0.5f;
                    _velocityFrames = 5;
                }

                _lastMousePos = mousePos;
            }
        }

        // ========== 5. 鼠标左键释放 ==========
        if (Input.GetMouseButtonUp(0))
        {
            if (_isDragging)
            {
                _pet.isDragging = false;
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
            else if (_isClickCandidate)
            {
                // ★ 传递鼠标屏幕位置给渲染器，由渲染器做精确射线检测
                Vector2 clickPos = _dragStartMouse;
                // 转换为 Unity 屏幕坐标（左下角原点，Y 向上）
                Vector2 unityScreenPos = new Vector2(clickPos.x, Screen.height - clickPos.y);
                if (_renderer != null) _renderer.ShowClickPose(unityScreenPos);
                _pet.Pause(clickPauseDuration);
                Debug.Log($"[DragHandler] 轻击宠物 pos=({unityScreenPos.x:F0},{unityScreenPos.y:F0})");
                OnPetClicked?.Invoke();
            }

            _isDragging = false;
            _isClickCandidate = false;
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
    /// 查找底部输入栏（每次更新前刷新，应对动态添加）
    /// </summary>
    private void RefreshBottomBar()
    {
        if (_bottomBar != null) return;
        _bottomBar = GetComponent<BottomInputBar>();
        if (_bottomBar == null) _bottomBar = FindObjectOfType<BottomInputBar>();
    }

    /// <summary>
    /// 每帧根据鼠标位置动态设置点击穿透
    /// </summary>
    /// <summary>
    /// 窗口就绪后调用，强制下一帧重算穿透状态（解决启动时序问题）
    /// </summary>
    public void ResetClickState()
    {
        _mouseOverPet = false; // 强制下一帧 UpdateClickThrough 重新评估
    }

    private void UpdateClickThrough()
    {
        if (_window == null) return;

        RefreshBottomBar(); // ★ 每帧重新确保引用

        Vector2 mousePos = GetMousePos();
        bool overPet = IsPointInPet(mousePos);
        // ★ 底部输入栏也接收点击（打字用）
        bool overBar = _bottomBar != null
            && mousePos.x >= _bottomBar.BarLeft
            && mousePos.x <= _bottomBar.BarRight
            && mousePos.y >= _bottomBar.BarTop
            && mousePos.y <= _bottomBar.BarBottom;

        // ★ BallPanel 区域也接收点击
        bool overPanel = _ballPanel != null
            && _ballPanel.IsMouseOverPanel(mousePos);

        // ★ RightPanel 区域也接收点击
        bool overRightPanel = _rightPanel != null
            && _rightPanel.IsPointInInteractiveArea(mousePos);

        bool needInput = overPet || overBar || overPanel || overRightPanel;

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
