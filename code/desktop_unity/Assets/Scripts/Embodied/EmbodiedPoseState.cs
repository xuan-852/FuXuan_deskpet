using System;
using System.Collections.Generic;

public sealed class EmbodiedPoseSnapshot
{
    public long Version { get; internal set; }
    public long ActiveRequestId { get; internal set; }
    public string ActiveSource { get; internal set; }
    public string CorrelationId { get; internal set; }
    public string ActiveSkillId { get; internal set; }
    public EmbodiedResource OccupiedResources { get; internal set; }
    public EmbodiedActionStatus ActionStatus { get; internal set; }
    public string TerminalReason { get; internal set; }
    public string[] PendingParameterIds { get; internal set; }
}

// 具身姿势状态最小集：只跟踪已认证技能执行器写入的参数还原记录。
// 完整虚拟骨架 PoseState 属后续阶段；本类保证 SafeRecovery 可以把每个
// 具身写入恢复到执行前基线，且重复恢复幂等。
public sealed class EmbodiedPoseState
{
    private readonly Dictionary<string, float> _baselines = new Dictionary<string, float>();
    private readonly Dictionary<string, float> _lastWritten = new Dictionary<string, float>();
    private long _version;
    private long _activeRequestId;
    private string _activeSource;
    private string _correlationId;
    private string _activeSkillId;
    private EmbodiedResource _occupiedResources;
    private EmbodiedActionStatus _actionStatus;
    private string _terminalReason;

    public void RecordWrite(string parameterId, float value, float restoreValue)
    {
        if (string.IsNullOrWhiteSpace(parameterId)) throw new ArgumentException("parameter id required", nameof(parameterId));
        // The first write owns the entry baseline. Later frames must not replace
        // it with a transient value or a caller's placeholder restore value.
        if (!_baselines.ContainsKey(parameterId))
            _baselines[parameterId] = restoreValue;
        _lastWritten[parameterId] = value;
        _version++;
    }

    public void BeginAction(EmbodiedActionRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.SkillId)) throw new ArgumentException("certified request required", nameof(request));
        _activeRequestId = request.RequestId;
        _activeSource = string.IsNullOrWhiteSpace(request.Source) ? "legacy/unknown" : request.Source;
        _correlationId = request.CorrelationId;
        _activeSkillId = request.SkillId;
        _occupiedResources = request.Resources;
        _actionStatus = request.Status;
        _terminalReason = null;
        _version++;
    }

    public void FinishAction(EmbodiedActionStatus terminalStatus, string reason = null)
    {
        _activeRequestId = 0;
        _activeSource = null;
        _correlationId = null;
        _activeSkillId = null;
        _occupiedResources = EmbodiedResource.None;
        _actionStatus = terminalStatus;
        _terminalReason = string.IsNullOrWhiteSpace(reason) ? terminalStatus.ToString() : reason;
        _version++;
    }

    public EmbodiedPoseSnapshot CaptureSnapshot()
    {
        var ids = new string[_lastWritten.Count]; _lastWritten.Keys.CopyTo(ids, 0);
        return new EmbodiedPoseSnapshot
        {
            Version = _version,
            ActiveRequestId = _activeRequestId,
            ActiveSource = _activeSource,
            CorrelationId = _correlationId,
            ActiveSkillId = _activeSkillId,
            OccupiedResources = _occupiedResources,
            ActionStatus = _actionStatus,
            TerminalReason = _terminalReason,
            PendingParameterIds = ids
        };
    }

    public bool HasPendingRestore => _lastWritten.Count > 0;
    public int PendingCount => _lastWritten.Count;
    public float LastWritten(string parameterId) => _lastWritten.TryGetValue(parameterId ?? "", out var v) ? v : 0f;

    // 对每个仍待还原的参数调用 apply(参数, 基线值)，随后清空待还原集合；
    // 重复调用不再产生还原动作（幂等）。
    public List<string> RestoreAll(Action<string, float> apply)
    {
        var restored = new List<string>();
        if (apply == null) throw new ArgumentNullException(nameof(apply));
        var pending = new List<string>(_lastWritten.Keys);
        Exception firstFailure = null;
        foreach (var parameterId in pending)
        {
            try
            {
                apply(parameterId, _baselines[parameterId]);
                _lastWritten.Remove(parameterId);
                restored.Add(parameterId);
            }
            catch (Exception error)
            {
                // Keep failed entries pending so a later recovery pass can retry them.
                firstFailure = firstFailure ?? error;
            }
        }
        if (restored.Count > 0) _version++;
        if (firstFailure != null) throw firstFailure;
        return restored;
    }
}

// Phase A 的写入者清册。它不是参数写入白名单，更不授予任何路径控制权；
// 作用是把仍存在的生产写入路径显式暴露给迁移和测试，避免将“有输入租约”
// 误述成“已经过 EmbodiedCoordinator 统一仲裁”。
// PhysicsRoot 与 Live2D BodyRoot 必须分离：这个快照仅描述桌面位置、速度和
// 交互状态，绝不保存或推导任何 Cubism Param* 值。
public enum DesktopBodyMode { Idle, Walking, Airborne, Dragging, Paused, ActionMovementLocked }

public sealed class DesktopBodySnapshot
{
    public long Version { get; internal set; }
    public int X { get; internal set; }
    public int Y { get; internal set; }
    public int VelocityX { get; internal set; }
    public int VelocityY { get; internal set; }
    public bool OnGround { get; internal set; }
    public bool IsDragging { get; internal set; }
    public bool IsPaused { get; internal set; }
    public bool IsActionMovementLocked { get; internal set; }
    public string GroundTask { get; internal set; }
    public DesktopBodyMode Mode { get; internal set; }
}

public sealed class DesktopBodyState
{
    private long _version;
    private int _x, _y, _velocityX, _velocityY;
    private bool _onGround, _isDragging, _isPaused, _isActionMovementLocked;
    private string _groundTask;
    private DesktopBodyMode _mode;

    public void Update(int x, int y, int velocityX, int velocityY, bool onGround,
        bool isDragging, bool isPaused, bool isActionMovementLocked, string groundTask)
    {
        var task = groundTask ?? string.Empty;
        var mode = ResolveMode(velocityX, velocityY, onGround, isDragging, isPaused, isActionMovementLocked);
        if (_x == x && _y == y && _velocityX == velocityX && _velocityY == velocityY &&
            _onGround == onGround && _isDragging == isDragging && _isPaused == isPaused &&
            _isActionMovementLocked == isActionMovementLocked && _groundTask == task && _mode == mode) return;
        _x = x; _y = y; _velocityX = velocityX; _velocityY = velocityY;
        _onGround = onGround; _isDragging = isDragging; _isPaused = isPaused;
        _isActionMovementLocked = isActionMovementLocked; _groundTask = task; _mode = mode; _version++;
    }

    public DesktopBodySnapshot CaptureSnapshot() => new DesktopBodySnapshot
    {
        Version = _version, X = _x, Y = _y, VelocityX = _velocityX, VelocityY = _velocityY,
        OnGround = _onGround, IsDragging = _isDragging, IsPaused = _isPaused,
        IsActionMovementLocked = _isActionMovementLocked, GroundTask = _groundTask, Mode = _mode
    };

    private static DesktopBodyMode ResolveMode(int velocityX, int velocityY, bool onGround,
        bool isDragging, bool isPaused, bool isActionMovementLocked)
    {
        if (isDragging) return DesktopBodyMode.Dragging;
        if (isActionMovementLocked) return DesktopBodyMode.ActionMovementLocked;
        if (isPaused) return DesktopBodyMode.Paused;
        if (!onGround || velocityY != 0) return DesktopBodyMode.Airborne;
        return velocityX == 0 ? DesktopBodyMode.Idle : DesktopBodyMode.Walking;
    }
}

public enum BodyWriterControlLevel { CertifiedCoordinator, InputLeaseOnly, LegacyUnmanaged, InternalOnly }

public enum BodyWriterRole
{
    ExternalLeaseWriter,
    LeaseGatedOverlay,
    DesktopStateLease,
    InternalParameterLayer
}

public sealed class BodyWriterDescriptor
{
    public string WriterId { get; internal set; }
    public EmbodiedResource Resources { get; internal set; }
    public BodyWriterControlLevel ControlLevel { get; internal set; }
    public BodyWriterRole Role { get; internal set; }
    public string RecoveryOwner { get; internal set; }
}

public static class BodyWriterInventory
{
    private static readonly BodyWriterDescriptor[] Descriptors =
    {
        new BodyWriterDescriptor { WriterId = "certified-motion", Resources = EmbodiedResource.Body | EmbodiedResource.Face | EmbodiedResource.LeftArm | EmbodiedResource.RightArm, ControlLevel = BodyWriterControlLevel.CertifiedCoordinator, Role = BodyWriterRole.ExternalLeaseWriter, RecoveryOwner = "EmbodiedSafeRecovery" },
        new BodyWriterDescriptor { WriterId = "expression", Resources = EmbodiedResource.Face, ControlLevel = BodyWriterControlLevel.InputLeaseOnly, Role = BodyWriterRole.ExternalLeaseWriter, RecoveryOwner = "ReleaseExpressionInputLease" },
        new BodyWriterDescriptor { WriterId = "legacy-action", Resources = EmbodiedResource.Body | EmbodiedResource.Face | EmbodiedResource.LeftArm | EmbodiedResource.RightArm, ControlLevel = BodyWriterControlLevel.InputLeaseOnly, Role = BodyWriterRole.ExternalLeaseWriter, RecoveryOwner = "ReleaseActionLock" },
        new BodyWriterDescriptor { WriterId = "generated-motion", Resources = EmbodiedResource.Body | EmbodiedResource.Face | EmbodiedResource.LeftArm | EmbodiedResource.RightArm, ControlLevel = BodyWriterControlLevel.InputLeaseOnly, Role = BodyWriterRole.ExternalLeaseWriter, RecoveryOwner = "EndGeneratedMotion" },
        new BodyWriterDescriptor { WriterId = "idle-action", Resources = EmbodiedResource.Body | EmbodiedResource.Face | EmbodiedResource.LeftArm | EmbodiedResource.RightArm | EmbodiedResource.Effect, ControlLevel = BodyWriterControlLevel.InputLeaseOnly, Role = BodyWriterRole.ExternalLeaseWriter, RecoveryOwner = "ResetIdleAction" },
        new BodyWriterDescriptor { WriterId = "walk-pose", Resources = EmbodiedResource.Movement | EmbodiedResource.Body | EmbodiedResource.LeftArm | EmbodiedResource.RightArm, ControlLevel = BodyWriterControlLevel.InputLeaseOnly, Role = BodyWriterRole.ExternalLeaseWriter, RecoveryOwner = "walking-state" },
        new BodyWriterDescriptor { WriterId = "desktop-physics", Resources = EmbodiedResource.Movement | EmbodiedResource.Body | EmbodiedResource.Effect, ControlLevel = BodyWriterControlLevel.InputLeaseOnly, Role = BodyWriterRole.DesktopStateLease, RecoveryOwner = "physics-state" },
        new BodyWriterDescriptor { WriterId = "mouse-gaze", Resources = EmbodiedResource.Face, ControlLevel = BodyWriterControlLevel.InternalOnly, Role = BodyWriterRole.LeaseGatedOverlay, RecoveryOwner = "gaze-update" },
        new BodyWriterDescriptor { WriterId = "renderer-parameter-commit", Resources = EmbodiedResource.None, ControlLevel = BodyWriterControlLevel.InternalOnly, Role = BodyWriterRole.InternalParameterLayer, RecoveryOwner = "ParameterCommitBridge" },
        new BodyWriterDescriptor { WriterId = "drag-response", Resources = EmbodiedResource.Movement | EmbodiedResource.Body | EmbodiedResource.Face | EmbodiedResource.LeftArm | EmbodiedResource.RightArm | EmbodiedResource.Effect, ControlLevel = BodyWriterControlLevel.InputLeaseOnly, Role = BodyWriterRole.ExternalLeaseWriter, RecoveryOwner = "ReleaseDragResponseInputLease" }
    };

    public static BodyWriterDescriptor[] Snapshot()
    {
        var snapshot = new BodyWriterDescriptor[Descriptors.Length];
        for (var index = 0; index < Descriptors.Length; index++)
        {
            var item = Descriptors[index];
            snapshot[index] = Copy(item);
        }
        return snapshot;
    }

    public static bool TryGet(string writerId, out BodyWriterDescriptor descriptor)
    {
        descriptor = null;
        if (string.IsNullOrWhiteSpace(writerId)) return false;
        for (var index = 0; index < Descriptors.Length; index++)
        {
            if (Descriptors[index].WriterId != writerId) continue;
            descriptor = Copy(Descriptors[index]);
            return true;
        }
        return false;
    }

    private static BodyWriterDescriptor Copy(BodyWriterDescriptor item)
    {
        return new BodyWriterDescriptor
        {
            WriterId = item.WriterId,
            Resources = item.Resources,
            ControlLevel = item.ControlLevel,
            Role = item.Role,
            RecoveryOwner = item.RecoveryOwner
        };
    }
}
