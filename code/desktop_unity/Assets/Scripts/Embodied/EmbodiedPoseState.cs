using System;
using System.Collections.Generic;

// 具身姿势状态最小集：只跟踪已认证技能执行器写入的参数还原记录。
// 完整虚拟骨架 PoseState 属后续阶段；本类保证 SafeRecovery 可以把每个
// 具身写入恢复到执行前基线，且重复恢复幂等。
public sealed class EmbodiedPoseState
{
    private readonly Dictionary<string, float> _baselines = new Dictionary<string, float>();
    private readonly Dictionary<string, float> _lastWritten = new Dictionary<string, float>();

    public void RecordWrite(string parameterId, float value, float restoreValue)
    {
        if (string.IsNullOrWhiteSpace(parameterId)) throw new ArgumentException("parameter id required", nameof(parameterId));
        _baselines[parameterId] = restoreValue;
        _lastWritten[parameterId] = value;
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
        foreach (var pair in _lastWritten)
        {
            apply(pair.Key, _baselines[pair.Key]);
            restored.Add(pair.Key);
        }
        _lastWritten.Clear();
        return restored;
    }
}
