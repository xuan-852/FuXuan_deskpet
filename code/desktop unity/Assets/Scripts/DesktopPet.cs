using UnityEngine;

/// <summary>
/// 桌面宠物 — 主控脚本
///
/// 职责：
/// 1. 管理宠物的物理状态 (位置、速度、尺寸)
/// 2. 驱动物理步进（重力、碰撞、落地检测）
/// 3. 管理地面状态机（行走、停止等任务）
/// 4. 协调 DragHandler、TouchController 等交互模块
///
/// v2 架构设计：
/// - 分离交互逻辑到独立的 Handler 脚本
/// - 用 IPetRenderer 接口抽象渲染层
/// - 状态机可扩展，方便添加新行为
/// </summary>
public class DesktopPet : MonoBehaviour
{
    #region 物理状态

    [Header("物理属性")]
    [Tooltip("宠物初始X位置")]
    public int startX = 50;

    [Tooltip("宠物初始Y位置")]
    public int startY = 25;

    [Tooltip("重力加速度（像素/帧²）")]
    public int gravity = 1;

    [Tooltip("最大下落速度（像素/帧）")]
    public int maxFallSpeed = 15;

    [Tooltip("水平速度范围")]
    public int maxHorizontalSpeed = 12;

    // 宠物物理状态（仿 v1 PetState）
    [System.NonSerialized]
    public int petX;
    [System.NonSerialized]
    public int petY;
    [System.NonSerialized]
    public int petVx;
    [System.NonSerialized]
    public int petVy;
    [System.NonSerialized]
    public int petWidth;
    [System.NonSerialized]
    public int petHeight;

    [System.NonSerialized]
    public bool onGround = false;

    [System.NonSerialized]
    public bool isPaused = false;

    // 屏幕尺寸缓存
    private int _screenWidth;
    private int _screenHeight;

    #endregion

    #region 地面任务状态机

    /// <summary>
    /// 地面行为枚举（与 v1 GroundTask 对应）
    /// </summary>
    public enum GroundTask
    {
        None,
        MoveLeftEdge,      // 向左走到边缘
        MoveRightEdge,     // 向右走到边缘
        MoveLeftTime,      // 向左走固定时长
        MoveRightTime,     // 向右走固定时长
        StopTime           // 停止固定时长
    }

    [Header("地面任务配置")]
    [Tooltip("向左走到边缘权重")]
    public int taskWeightMoveLeftEdge = 3;
    [Tooltip("向右走到边缘权重")]
    public int taskWeightMoveRightEdge = 3;
    [Tooltip("向左走定时权重")]
    public int taskWeightMoveLeftTime = 20;
    [Tooltip("向右走定时权重")]
    public int taskWeightMoveRightTime = 20;
    [Tooltip("停止定时权重")]
    public int taskWeightStopTime = 10;

    [Tooltip("地面任务移动持续时间（毫秒）")]
    public int taskMoveTimeMs = 5000;

    [Tooltip("停止持续时间（毫秒）")]
    public int taskStopTimeMs = 1500;

    [System.NonSerialized]
    public GroundTask currentTask = GroundTask.None;

    [System.NonSerialized]
    public GroundTask lastTask = GroundTask.None;

    private float _taskEndTime = 0f;

    #endregion

    #region 组件引用

    private WindowOverlay _windowOverlay;
    private DragHandler _dragHandler;

    #endregion

    #region Unity 生命周期

    private void Start()
    {
        // 获取屏幕尺寸
        _screenWidth = Screen.width;
        _screenHeight = Screen.height;

        // 初始化物理状态
        petX = startX;
        petY = startY;
        petVx = 0;
        petVy = 0;
        petWidth = 128;   // 默认尺寸，后续由 Sprite 覆盖
        petHeight = 128;

        // 查找依赖组件
        _windowOverlay = GetComponent<WindowOverlay>();
        _dragHandler = GetComponent<DragHandler>();

        if (_windowOverlay == null)
        {
            Debug.LogWarning("[DesktopPet] 未找到 WindowOverlay 组件");
        }

        Debug.Log($"[DesktopPet] 初始化完成 @ ({petX},{petY}), 屏幕: {_screenWidth}x{_screenHeight}");
    }

    private void Update()
    {
        // 暂停时不更新物理
        if (isPaused)
            return;

        // 物理步进
        StepPet();

        // 地面状态机更新
        if (onGround && !isPaused)
        {
            UpdateGroundTask();
        }
    }

    #endregion

    #region 物理步进

    /// <summary>
    /// 物理步进：位置更新、重力、边界碰撞、落地检测
    /// </summary>
    private void StepPet()
    {
        // 1. 应用速度
        petX += petVx;
        petY += petVy;

        // 2. 重力（空中时）
        if (!onGround)
        {
            petVy += gravity;
            if (petVy > maxFallSpeed)
                petVy = maxFallSpeed;
        }

        // 3. 左右边界碰撞
        if (petX <= 0)
        {
            petX = 0;
            if (petVx < 0)
            {
                if (onGround && currentTask == GroundTask.MoveLeftEdge)
                {
                    petVx = 0;
                }
                else
                {
                    petVx = -petVx;
                }
            }
        }
        else if (petX + petWidth >= _screenWidth)
        {
            petX = _screenWidth - petWidth;
            if (petVx > 0)
            {
                if (onGround && currentTask == GroundTask.MoveRightEdge)
                {
                    petVx = 0;
                }
                else
                {
                    petVx = -petVx;
                }
            }
        }

        // 4. 顶部边界
        if (petY <= 0)
        {
            petY = 0;
            if (petVy < 0)
                petVy = -petVy;
        }

        // 5. 底部落地检测
        if (petY + petHeight >= _screenHeight)
        {
            petY = _screenHeight - petHeight;
            if (petVy > 0)
            {
                petVy = 0;
                onGround = true;
                OnLand();
            }
        }
        else
        {
            onGround = false;
        }
    }

    /// <summary>
    /// 落地回调
    /// </summary>
    private void OnLand()
    {
        Debug.Log("[DesktopPet] 落地");
        // 后续扩展：播放落地动画/音效

        // 落地后开始地面任务
        StartNextGroundTask();
    }

    #endregion

    #region 地面状态机

    /// <summary>
    /// 选择下一个地面任务（加权随机）
    /// </summary>
    private GroundTask PickNextGroundTask()
    {
        int w1 = taskWeightMoveLeftEdge;
        int w2 = taskWeightMoveRightEdge;
        int w3 = taskWeightMoveLeftTime;
        int w4 = taskWeightMoveRightTime;
        int w5 = taskWeightStopTime;

        // 停止只能在上次是定时移动后触发
        bool allowStop = (lastTask == GroundTask.MoveLeftTime ||
                          lastTask == GroundTask.MoveRightTime);
        if (!allowStop) w5 = 0;

        int total = w1 + w2 + w3 + w4 + w5;
        if (total <= 0) return GroundTask.MoveLeftEdge;

        int r = Random.Range(0, total);
        if (r < w1) return GroundTask.MoveLeftEdge;
        r -= w1;
        if (r < w2) return GroundTask.MoveRightEdge;
        r -= w2;
        if (r < w3) return GroundTask.MoveLeftTime;
        r -= w3;
        if (r < w4) return GroundTask.MoveRightTime;
        return GroundTask.StopTime;
    }

    private GroundTask PickNextFromLeftEdge()
    {
        int wEdge = taskWeightMoveRightEdge;
        int wTime = taskWeightMoveRightTime;
        int total = wEdge + wTime;
        if (total <= 0) return GroundTask.MoveRightEdge;
        return Random.Range(0, total) < wEdge ?
            GroundTask.MoveRightEdge : GroundTask.MoveRightTime;
    }

    private GroundTask PickNextFromRightEdge()
    {
        int wEdge = taskWeightMoveLeftEdge;
        int wTime = taskWeightMoveLeftTime;
        int total = wEdge + wTime;
        if (total <= 0) return GroundTask.MoveLeftEdge;
        return Random.Range(0, total) < wEdge ?
            GroundTask.MoveLeftEdge : GroundTask.MoveLeftTime;
    }

    /// <summary>
    /// 启动一个地面任务
    /// </summary>
    public void StartGroundTask(GroundTask task)
    {
        currentTask = task;
        lastTask = task;
        _taskEndTime = 0f;

        int speed = Random.Range(1, 4); // 1~3

        switch (task)
        {
            case GroundTask.MoveLeftEdge:
                petVx = -speed;
                break;
            case GroundTask.MoveRightEdge:
                petVx = speed;
                break;
            case GroundTask.MoveLeftTime:
                petVx = -speed;
                _taskEndTime = Time.time + taskMoveTimeMs / 1000f;
                break;
            case GroundTask.MoveRightTime:
                petVx = speed;
                _taskEndTime = Time.time + taskMoveTimeMs / 1000f;
                break;
            case GroundTask.StopTime:
                petVx = 0;
                _taskEndTime = Time.time + taskStopTimeMs / 1000f;
                break;
        }
    }

    public void StartNextGroundTask()
    {
        StartGroundTask(PickNextGroundTask());
    }

    private void StartNextFromLeftEdge()
    {
        StartGroundTask(PickNextFromLeftEdge());
    }

    private void StartNextFromRightEdge()
    {
        StartGroundTask(PickNextFromRightEdge());
    }

    /// <summary>
    /// 每帧检查当前地面任务是否需要切换
    /// </summary>
    private void UpdateGroundTask()
    {
        if (currentTask == GroundTask.None)
        {
            StartNextGroundTask();
            return;
        }

        switch (currentTask)
        {
            case GroundTask.MoveLeftEdge:
                if (petX <= 0)
                    StartNextFromLeftEdge();
                break;

            case GroundTask.MoveRightEdge:
                if (petX + petWidth >= _screenWidth)
                    StartNextFromRightEdge();
                break;

            case GroundTask.MoveLeftTime:
            case GroundTask.MoveRightTime:
            case GroundTask.StopTime:
                if (_taskEndTime > 0f && Time.time >= _taskEndTime)
                    StartNextGroundTask();
                break;
        }
    }

    #endregion

    #region 交互接口

    /// <summary>
    /// 从拖拽释放设置初速度
    /// </summary>
    public void ApplyDragVelocity(int vx, int vy)
    {
        petVx = Mathf.Clamp(vx, -maxHorizontalSpeed, maxHorizontalSpeed);
        petVy = Mathf.Clamp(vy, -maxFallSpeed, maxFallSpeed);
        onGround = false;
        currentTask = GroundTask.None;
    }

    /// <summary>
    /// 暂停宠物运动
    /// </summary>
    public void Pause(float durationSeconds)
    {
        isPaused = true;
        if (durationSeconds > 0)
        {
            Invoke(nameof(Resume), durationSeconds);
        }
    }

    /// <summary>
    /// 恢复宠物运动
    /// </summary>
    public void Resume()
    {
        isPaused = false;
        if (onGround && currentTask == GroundTask.None)
        {
            StartNextGroundTask();
        }
    }

    /// <summary>
    /// 重置宠物位置
    /// </summary>
    public void TeleportTo(int x, int y)
    {
        petX = x;
        petY = y;
    }

    #endregion
}
