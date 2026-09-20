using System;
using System.Collections.Generic;

public enum LifeEventType
{
    UserSpoke,
    ConversationCompleted,
    UserReturned,
    UserInactive,
    DirectInteraction,
    ActionStarted,
    ActionCompleted,
    ActionInterrupted,
    ActionRecoveryFailed,
    BodyObserved,
    EmotionObserved
}

public enum LifePresence
{
    Unknown,
    Present,
    Away
}

public enum LifeActivity
{
    Unknown,
    Idle,
    Working,
    Interacting
}

public enum LifeActionStatus
{
    None,
    Active,
    Completed,
    Interrupted,
    RecoveryFailed
}

public sealed class LifeEvent
{
    public string EventId { get; private set; }
    public LifeEventType Type { get; private set; }
    public DateTime UtcTimestamp { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public string Source { get; private set; }
    public string CorrelationId { get; private set; }
    public int Importance { get; private set; }
    public string Summary { get; private set; }

    public LifeEvent(string eventId, LifeEventType type, DateTime utcTimestamp,
        DateTime expiresAtUtc, string source, string correlationId,
        int importance, string summary)
    {
        ValidateToken(eventId, nameof(eventId), 96);
        ValidateToken(source, nameof(source), 96);
        ValidateOptional(correlationId, nameof(correlationId), 96);
        ValidateOptional(summary, nameof(summary), 160);
        if (utcTimestamp.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC timestamp required", nameof(utcTimestamp));
        if (expiresAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC expiry required", nameof(expiresAtUtc));
        if (expiresAtUtc < utcTimestamp)
            throw new ArgumentException("expiry must not precede timestamp", nameof(expiresAtUtc));
        if (importance < 0 || importance > 100)
            throw new ArgumentOutOfRangeException(nameof(importance));

        EventId = eventId;
        Type = type;
        UtcTimestamp = utcTimestamp;
        ExpiresAtUtc = expiresAtUtc;
        Source = source;
        CorrelationId = correlationId;
        Importance = importance;
        Summary = summary;
    }

    public bool IsExpired(DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC timestamp required", nameof(nowUtc));
        return nowUtc > ExpiresAtUtc;
    }

    private static void ValidateToken(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("short token required", name);
        ValidateOptional(value, name, maxLength);
    }

    private static void ValidateOptional(string value, string name, int maxLength)
    {
        if (value == null) return;
        if (value.Length > maxLength || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
            throw new ArgumentException("bounded single-line summary required", name);
    }
}

public sealed class LifeSignalMetadata
{
    public string Source { get; internal set; }
    public DateTime UpdatedAtUtc { get; internal set; }
    public DateTime ExpiresAtUtc { get; internal set; }
    public float Confidence { get; internal set; }
    public string Reason { get; internal set; }

    internal LifeSignalMetadata Copy()
    {
        return new LifeSignalMetadata { Source = Source, UpdatedAtUtc = UpdatedAtUtc,
            ExpiresAtUtc = ExpiresAtUtc, Confidence = Confidence, Reason = Reason };
    }
}

public sealed class LifeStateSnapshot
{
    public long Version { get; internal set; }
    public DateTime UtcTimestamp { get; internal set; }
    public LifePresence Presence { get; internal set; }
    public LifeActivity Activity { get; internal set; }
    public string AttentionTarget { get; internal set; }
    public float Arousal { get; internal set; }
    public float Valence { get; internal set; }
    public float Energy { get; internal set; }
    public float Warmth { get; internal set; }
    public LifeActionStatus ActionStatus { get; internal set; }
    public string CurrentAction { get; internal set; }
    public string LastInterruption { get; internal set; }
    public string LastActionResult { get; internal set; }
    public int RecentEventCount { get; internal set; }
    public LifeSignalMetadata PresenceMetadata { get; internal set; }
    public LifeSignalMetadata ActivityMetadata { get; internal set; }
    public LifeSignalMetadata AttentionMetadata { get; internal set; }
    public LifeSignalMetadata EmotionMetadata { get; internal set; }
    public LifeSignalMetadata ActionMetadata { get; internal set; }

    internal LifeStateSnapshot Copy()
    {
        return new LifeStateSnapshot { Version = Version, UtcTimestamp = UtcTimestamp,
            Presence = Presence, Activity = Activity, AttentionTarget = AttentionTarget,
            Arousal = Arousal, Valence = Valence, Energy = Energy, Warmth = Warmth,
            ActionStatus = ActionStatus, CurrentAction = CurrentAction,
            LastInterruption = LastInterruption, LastActionResult = LastActionResult,
            RecentEventCount = RecentEventCount,
            PresenceMetadata = PresenceMetadata == null ? null : PresenceMetadata.Copy(),
            ActivityMetadata = ActivityMetadata == null ? null : ActivityMetadata.Copy(),
            AttentionMetadata = AttentionMetadata == null ? null : AttentionMetadata.Copy(),
            EmotionMetadata = EmotionMetadata == null ? null : EmotionMetadata.Copy(),
            ActionMetadata = ActionMetadata == null ? null : ActionMetadata.Copy() };
    }
}

public sealed class LifeStateStore
{
    private readonly int _capacity;
    private readonly List<LifeEvent> _events = new List<LifeEvent>();
    private readonly HashSet<string> _eventIds = new HashSet<string>();
    private readonly HashSet<string> _correlationIds = new HashSet<string>();
    private LifeStateSnapshot _state;

    public LifeStateStore(int capacity = 64)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _state = NewInitial(DateTime.UtcNow);
    }

    public int Capacity { get { return _capacity; } }
    public int EventCount { get { return _events.Count; } }
    public LifeStateSnapshot Snapshot { get { return _state.Copy(); } }

    public bool Append(LifeEvent lifeEvent, DateTime nowUtc)
    {
        if (lifeEvent == null) throw new ArgumentNullException(nameof(lifeEvent));
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC timestamp required", nameof(nowUtc));
        Expire(nowUtc);
        if (lifeEvent.IsExpired(nowUtc) || _eventIds.Contains(lifeEvent.EventId)) return false;
        string phaseKey = CorrelationPhaseKey(lifeEvent);
        if (phaseKey != null && _correlationIds.Contains(phaseKey)) return false;

        _eventIds.Add(lifeEvent.EventId);
        if (phaseKey != null) _correlationIds.Add(phaseKey);
        _events.Add(lifeEvent);
        if (_events.Count > _capacity)
        {
            var removed = _events[0];
            _events.RemoveAt(0);
            _eventIds.Remove(removed.EventId);
            string removedPhaseKey = CorrelationPhaseKey(removed);
            if (removedPhaseKey != null) _correlationIds.Remove(removedPhaseKey);
        }
        Reduce(lifeEvent, nowUtc);
        return true;
    }

    public void Expire(DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC timestamp required", nameof(nowUtc));
        bool changed = false;
        if (_state.PresenceMetadata != null && nowUtc > _state.PresenceMetadata.ExpiresAtUtc)
        { _state.Presence = LifePresence.Unknown; _state.PresenceMetadata = null; changed = true; }
        if (_state.ActivityMetadata != null && nowUtc > _state.ActivityMetadata.ExpiresAtUtc)
        { _state.Activity = LifeActivity.Unknown; _state.ActivityMetadata = null; changed = true; }
        if (_state.AttentionMetadata != null && nowUtc > _state.AttentionMetadata.ExpiresAtUtc)
        { _state.AttentionTarget = null; _state.AttentionMetadata = null; changed = true; }
        if (_state.EmotionMetadata != null && nowUtc > _state.EmotionMetadata.ExpiresAtUtc)
        { _state.Arousal = 0f; _state.Valence = 0f; _state.Warmth = 0f; _state.Energy = 0.5f; _state.EmotionMetadata = null; changed = true; }
        if (_state.ActionMetadata != null && nowUtc > _state.ActionMetadata.ExpiresAtUtc)
        { _state.ActionStatus = LifeActionStatus.None; _state.CurrentAction = null; _state.ActionMetadata = null; changed = true; }
        if (changed) Touch(nowUtc);
    }

    private void Reduce(LifeEvent value, DateTime nowUtc)
    {
        LifeSignalMetadata metadata = Metadata(value);
        switch (value.Type)
        {
            case LifeEventType.UserSpoke:
                SetPresence(LifePresence.Present, metadata);
                SetActivity(LifeActivity.Interacting, metadata);
                break;
            case LifeEventType.ConversationCompleted:
                SetPresence(LifePresence.Present, metadata);
                SetActivity(LifeActivity.Interacting, metadata);
                break;
            case LifeEventType.UserReturned:
                SetPresence(LifePresence.Present, metadata);
                SetActivity(LifeActivity.Idle, metadata);
                break;
            case LifeEventType.UserInactive:
                SetPresence(LifePresence.Away, metadata);
                SetActivity(LifeActivity.Idle, metadata);
                break;
            case LifeEventType.DirectInteraction:
                SetPresence(LifePresence.Present, metadata);
                SetActivity(LifeActivity.Interacting, metadata);
                _state.AttentionTarget = "pet";
                _state.AttentionMetadata = metadata.Copy();
                break;
            case LifeEventType.ActionStarted:
                _state.ActionStatus = LifeActionStatus.Active;
                _state.CurrentAction = value.Summary;
                _state.ActionMetadata = metadata;
                break;
            case LifeEventType.ActionCompleted:
                _state.ActionStatus = LifeActionStatus.Completed;
                _state.CurrentAction = null;
                _state.LastActionResult = value.Summary;
                _state.ActionMetadata = metadata;
                break;
            case LifeEventType.ActionInterrupted:
                _state.ActionStatus = LifeActionStatus.Interrupted;
                _state.CurrentAction = null;
                _state.LastInterruption = value.Summary;
                _state.ActionMetadata = metadata;
                break;
            case LifeEventType.ActionRecoveryFailed:
                _state.ActionStatus = LifeActionStatus.RecoveryFailed;
                _state.CurrentAction = null;
                _state.LastInterruption = value.Summary;
                _state.ActionMetadata = metadata;
                break;
            case LifeEventType.EmotionObserved:
                ParseEmotion(value.Summary);
                _state.EmotionMetadata = metadata;
                break;
            case LifeEventType.BodyObserved:
                break;
        }
        Touch(nowUtc);
    }

    private void ParseEmotion(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return;
        string[] parts = summary.Split(',');
        float parsed;
        if (parts.Length > 0 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)) _state.Valence = Clamp(parsed, -1f, 1f);
        if (parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)) _state.Arousal = Clamp(parsed, 0f, 1f);
        if (parts.Length > 2 && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)) _state.Energy = Clamp(parsed, 0f, 1f);
        if (parts.Length > 3 && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)) _state.Warmth = Clamp(parsed, -1f, 1f);
    }

    private static string CorrelationPhaseKey(LifeEvent value)
    {
        if (string.IsNullOrEmpty(value.CorrelationId)) return null;
        return value.CorrelationId + "|" + value.Type.ToString();
    }

    private static float Clamp(float value, float min, float max) { return value < min ? min : value > max ? max : value; }
    private static LifeSignalMetadata Metadata(LifeEvent value)
    {
        return new LifeSignalMetadata { Source = value.Source, UpdatedAtUtc = value.UtcTimestamp,
            ExpiresAtUtc = value.ExpiresAtUtc, Confidence = value.Importance / 100f,
            Reason = value.Summary ?? value.Type.ToString() };
    }
    private void SetPresence(LifePresence value, LifeSignalMetadata metadata) { _state.Presence = value; _state.PresenceMetadata = metadata; }
    private void SetActivity(LifeActivity value, LifeSignalMetadata metadata) { _state.Activity = value; _state.ActivityMetadata = metadata; }
    private void Touch(DateTime nowUtc) { _state.Version++; _state.UtcTimestamp = nowUtc; _state.RecentEventCount = _events.Count; }
    private static LifeStateSnapshot NewInitial(DateTime nowUtc)
    {
        return new LifeStateSnapshot { Version = 0, UtcTimestamp = nowUtc, Presence = LifePresence.Unknown,
            Activity = LifeActivity.Unknown, ActionStatus = LifeActionStatus.None, Energy = 0.5f, Warmth = 0f };
    }
}
