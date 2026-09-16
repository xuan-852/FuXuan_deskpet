using System;
using Live2D.Cubism.Core;

/// <summary>
/// The only runtime sink for values emitted by the renderer baseline and the
/// semantic mapper.  It owns no motion policy: the input coordinator decides
/// who may execute, while this bridge keeps the Cubism write boundary separate
/// from every producer.
/// </summary>
public sealed class ParameterCommitBridge
{
    private readonly Func<string, CubismParameter> _resolve;

    public ParameterCommitBridge(Func<string, CubismParameter> resolve)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
    }

    public bool Commit(string parameterId, float value)
    {
        if (string.IsNullOrEmpty(parameterId)) return false;
        CubismParameter parameter = _resolve(parameterId);
        if (parameter == null) return false;
        parameter.Value = value;
        return true;
    }
}
