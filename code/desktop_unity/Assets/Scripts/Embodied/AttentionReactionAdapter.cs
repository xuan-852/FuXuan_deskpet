using UnityEngine;

/// <summary>
/// Bridges direct desktop-pet interaction into the local attention/life state.
/// It does not write Live2D parameters or create actions.
/// </summary>
[DisallowMultipleComponent]
public sealed class AttentionReactionAdapter : MonoBehaviour
{
    private DragHandler _dragHandler;
    private Live2DRenderer _renderer;
    private bool _bound;

    private void Update()
    {
        TryBind();
    }

    private void TryBind()
    {
        if (_bound) return;
        if (_dragHandler == null) _dragHandler = GetComponent<DragHandler>();
        if (_renderer == null) _renderer = GetComponent<Live2DRenderer>();
        if (_dragHandler == null || _renderer == null) return;

        _dragHandler.OnInteraction += HandleInteraction;
        _bound = true;
    }

    private void HandleInteraction(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        int separator = value.IndexOf(':');
        if (separator <= 0 || separator >= value.Length - 1) return;

        string phase = value.Substring(0, separator);
        string correlationId = value.Substring(separator + 1);
        MotionAgent.Instance?.NotifyInteraction();
        _renderer.PublishDirectInteraction(phase, correlationId);
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void OnDestroy()
    {
        Unbind();
    }

    private void Unbind()
    {
        if (!_bound || _dragHandler == null) return;
        _dragHandler.OnInteraction -= HandleInteraction;
        _bound = false;
    }
}
