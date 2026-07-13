using UnityEngine;

/// <summary>
/// 符玄「韬晦」— GPU 负载监控 & 游戏检测
///
/// 通过 ActivityTracker 的分类结果判断用户是否在玩游戏，
/// 检测到游戏时自动暂停本地 LLM，退出游戏冷却后自动恢复。
/// 确保 LLM 决策不占用 GPU 影响游戏性能。
///
/// 使用方式：自动实例化（挂载在 MotionAgent 同物体上）
/// </summary>
public class GpuLoadMonitor : MonoBehaviour
{
    [Header("配置")]
    [Tooltip("退出游戏后等待多久才恢复 LLM（秒）")]
    public float cooldownAfterGame = 30f;

    [Tooltip("检测到游戏后是否也停止自主动作决策（不止停 LLM）")]
    public bool alsoPauseMotionAgent = false;

    /// <summary>单例</summary>
    public static GpuLoadMonitor Instance { get; private set; }

    /// <summary>当前是否在游戏中</summary>
    public bool IsGaming { get; private set; } = false;

    private ActivityTracker _tracker;
    private float _lastGameEndTime = 0f;
    private bool _wasGaming = false;
    private MotionAgent _motionAgent;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        _tracker = ActivityTracker.Instance;
        _motionAgent = GetComponent<MotionAgent>();
        if (_tracker == null)
            Debug.LogWarning("[GpuLoadMonitor] ActivityTracker 未找到，游戏检测不可用");
    }

    void Update()
    {
        if (_tracker == null) return;

        bool isGaming = _tracker.CurrentCategory == "gaming";

        // 状态变化时触发
        if (isGaming != _wasGaming)
        {
            _wasGaming = isGaming;

            if (isGaming)
            {
                // ── 进入游戏 → 立即暂停 ──
                IsGaming = true;
                LocalLLMClient.Paused = true;

                if (alsoPauseMotionAgent && _motionAgent != null)
                    _motionAgent.enabled = false;

                Debug.Log("[GpuLoadMonitor] 🎮 检测到游戏 → 暂停本地 LLM");
            }
            else
            {
                // ── 退出游戏 → 进入冷却 ──
                _lastGameEndTime = Time.time;
                Debug.Log("[GpuLoadMonitor] ✅ 退出游戏 → 冷却后恢复 LLM");
            }
        }

        // 退出游戏后的冷却恢复
        if (!isGaming && IsGaming && Time.time - _lastGameEndTime > cooldownAfterGame)
        {
            IsGaming = false;
            LocalLLMClient.Paused = false;

            if (alsoPauseMotionAgent && _motionAgent != null)
                _motionAgent.enabled = true;

            Debug.Log("[GpuLoadMonitor] 🔄 冷却结束 → 恢复本地 LLM");
        }
    }
}
