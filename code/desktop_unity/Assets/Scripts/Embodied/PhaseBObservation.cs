using System;
using System.Collections.Generic;

/// <summary>
/// 无敏感数据的具身观测事件。事件只携带短标识、枚举状态和摘要哈希，绝不承载原文、窗口标题或截图。
/// </summary>
public struct EmbodiedEvent
{
    public DateTime UtcTimestamp { get; private set; }
    public long RequestId { get; private set; }
    public string Source { get; private set; }
    public string Kind { get; private set; }
    public string SkillId { get; private set; }
    public string State { get; private set; }
    public string Reason { get; private set; }
    public int DurationMilliseconds { get; private set; }
    public EmbodiedResource Resources { get; private set; }
    public long StateVersion { get; private set; }
    public string ParameterSummaryHash { get; private set; }

    public EmbodiedEvent(DateTime utcTimestamp, long requestId, string source, string kind,
        string skillId, string state, string reason, int durationMilliseconds,
        EmbodiedResource resources, long stateVersion, string parameterSummaryHash)
    {
        if (utcTimestamp.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC timestamp required", nameof(utcTimestamp));
        ValidateToken(source, nameof(source));
        ValidateToken(kind, nameof(kind));
        ValidateToken(skillId, nameof(skillId));
        ValidateToken(state, nameof(state));
        ValidateOptionalToken(reason, nameof(reason));
        ValidateOptionalToken(parameterSummaryHash, nameof(parameterSummaryHash));
        if (requestId < 0) throw new ArgumentOutOfRangeException(nameof(requestId));
        if (durationMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));
        if (stateVersion < 0) throw new ArgumentOutOfRangeException(nameof(stateVersion));
        UtcTimestamp = utcTimestamp;
        RequestId = requestId;
        Source = source;
        Kind = kind;
        SkillId = skillId;
        State = state;
        Reason = reason;
        DurationMilliseconds = durationMilliseconds;
        Resources = resources;
        StateVersion = stateVersion;
        ParameterSummaryHash = parameterSummaryHash;
    }

    private static void ValidateToken(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 96) throw new ArgumentException("short non-sensitive identifier required", name);
        ValidateOptionalToken(value, name);
    }

    private static void ValidateOptionalToken(string value, string name)
    {
        if (value == null) return;
        if (value.Length > 256 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
            throw new ArgumentException("bounded single-line summary required", name);
    }
}

/// <summary>进程内有界事件环形存储；不写磁盘、不联网。</summary>
public sealed class EmbodiedEventStore
{
    private readonly EmbodiedEvent[] _buffer;
    private int _next;
    private int _count;

    public EmbodiedEventStore(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new EmbodiedEvent[capacity];
    }

    public int Capacity { get { return _buffer.Length; } }
    public int Count { get { return _count; } }

    public void Append(EmbodiedEvent embodiedEvent)
    {
        _buffer[_next] = embodiedEvent;
        _next = (_next + 1) % _buffer.Length;
        if (_count < _buffer.Length) _count++;
    }

    public EmbodiedEvent[] Snapshot()
    {
        var result = new EmbodiedEvent[_count];
        var start = (_next - _count + _buffer.Length) % _buffer.Length;
        for (var index = 0; index < _count; index++) result[index] = _buffer[(start + index) % _buffer.Length];
        return result;
    }
}

/// <summary>组合后的只读身体状态；姿势与桌面物理状态保持独立分层。</summary>
public sealed class BodyStateSnapshot
{
    public long Version { get; private set; }
    public DateTime UtcTimestamp { get; private set; }
    public bool RendererReady { get; private set; }
    public EmbodiedPoseSnapshot Pose { get; private set; }
    public DesktopBodySnapshot Desktop { get; private set; }

    internal BodyStateSnapshot(long version, DateTime utcTimestamp, bool rendererReady,
        EmbodiedPoseSnapshot pose, DesktopBodySnapshot desktop)
    {
        Version = version;
        UtcTimestamp = utcTimestamp;
        RendererReady = rendererReady;
        Pose = pose;
        Desktop = desktop;
    }
}

/// <summary>只在内存中发布身体状态版本，并返回防修改快照。</summary>
public sealed class BodyStateStore
{
    private long _version;
    private EmbodiedPoseSnapshot _pose;
    private DesktopBodySnapshot _desktop;
    private bool _rendererReady;
    private DateTime _utcTimestamp;

    public BodyStateSnapshot Publish(EmbodiedPoseSnapshot pose, DesktopBodySnapshot desktop, bool rendererReady, DateTime utcTimestamp)
    {
        if (utcTimestamp.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC timestamp required", nameof(utcTimestamp));
        _pose = CopyPose(pose);
        _desktop = CopyDesktop(desktop);
        _rendererReady = rendererReady;
        _utcTimestamp = utcTimestamp;
        _version++;
        return CaptureSnapshot();
    }

    public BodyStateSnapshot CaptureSnapshot()
    {
        return new BodyStateSnapshot(_version, _utcTimestamp, _rendererReady, CopyPose(_pose), CopyDesktop(_desktop));
    }

    private static EmbodiedPoseSnapshot CopyPose(EmbodiedPoseSnapshot value)
    {
        if (value == null) return null;
        return new EmbodiedPoseSnapshot { Version = value.Version, ActiveRequestId = value.ActiveRequestId,
            ActiveSource = value.ActiveSource, CorrelationId = value.CorrelationId, ActiveSkillId = value.ActiveSkillId,
            OccupiedResources = value.OccupiedResources, ActionStatus = value.ActionStatus, TerminalReason = value.TerminalReason,
            PendingParameterIds = value.PendingParameterIds == null ? null : (string[])value.PendingParameterIds.Clone() };
    }

    private static DesktopBodySnapshot CopyDesktop(DesktopBodySnapshot value)
    {
        if (value == null) return null;
        return new DesktopBodySnapshot { Version = value.Version, X = value.X, Y = value.Y, VelocityX = value.VelocityX,
            VelocityY = value.VelocityY, OnGround = value.OnGround, IsDragging = value.IsDragging,
            IsPaused = value.IsPaused, IsActionMovementLocked = value.IsActionMovementLocked,
            GroundTask = value.GroundTask, Mode = value.Mode };
    }
}

public enum ExecutionHealth { Unknown, Healthy, Degraded, Unhealthy }

public sealed class ExecutionMonitorSnapshot
{
    public DateTime UtcTimestamp { get; private set; }
    public ExecutionHealth Health { get; private set; }
    public bool RendererReady { get; private set; }
    public bool ResourcesHeld { get; private set; }
    public bool Progressing { get; private set; }
    public int ObservationCount { get; private set; }
    public int FaultCount { get; private set; }
    public string LastReason { get; private set; }

    internal ExecutionMonitorSnapshot(DateTime utcTimestamp, ExecutionHealth health, bool rendererReady,
        bool resourcesHeld, bool progressing, int observationCount, int faultCount, string lastReason)
    {
        UtcTimestamp = utcTimestamp; Health = health; RendererReady = rendererReady; ResourcesHeld = resourcesHeld;
        Progressing = progressing; ObservationCount = observationCount; FaultCount = faultCount; LastReason = lastReason;
    }
}

/// <summary>执行层只读健康观测；不执行恢复、不写磁盘。</summary>
public sealed class ExecutionMonitor
{
    private ExecutionMonitorSnapshot _snapshot = new ExecutionMonitorSnapshot(DateTime.MinValue, ExecutionHealth.Unknown, false, false, false, 0, 0, null);

    public ExecutionMonitorSnapshot Snapshot { get { return _snapshot; } }

    public void Observe(DateTime utcTimestamp, bool rendererReady, bool resourcesHeld, bool progressing, string reason)
    {
        if (utcTimestamp.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC timestamp required", nameof(utcTimestamp));
        var healthy = rendererReady && (!resourcesHeld || progressing);
        var health = healthy ? ExecutionHealth.Healthy : (rendererReady ? ExecutionHealth.Degraded : ExecutionHealth.Unhealthy);
        var fault = health == ExecutionHealth.Healthy ? _snapshot.FaultCount : _snapshot.FaultCount + 1;
        _snapshot = new ExecutionMonitorSnapshot(utcTimestamp, health, rendererReady, resourcesHeld, progressing,
            _snapshot.ObservationCount + 1, fault, reason);
    }
}
