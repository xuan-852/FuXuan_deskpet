using UnityEngine;

/// <summary>
/// Live2DRenderer 的模型置顶叠加渲染与性能档位适配。
/// 保持渲染行为与主类一致，仅将相机、RenderTexture 和 OnGUI 生命周期隔离。
/// </summary>
public partial class Live2DRenderer
{
    // 局部叠加渲染：原生透明窗口仍保持主屏大小，但 Live2D 只渲染模型附近的区域。
    // 这样可以保留现有拖拽/点击穿透/UI 坐标链路，同时避免全屏 RT 填充 GPU。
    private const float LOCAL_OVERLAY_PADDING_PX = 64f;
    private const float LOCAL_OVERLAY_MIN_WIDTH_PX = 320f;
    private const float LOCAL_OVERLAY_MIN_HEIGHT_PX = 480f;
    private const int LOCAL_OVERLAY_SIZE_QUANTUM = 32;
    private Renderer[] _overlayModelRenderers;
    private Rect _overlayDrawRect;
    private bool _overlayPerspectiveFallbackLogged;

    /// <summary>从主相机剔除叠加层</summary>
    private void ExcludeOverlayLayerFromMainCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        cam.cullingMask &= ~(1 << OVERLAY_LAYER);
        Debug.Log($"[Live2DRenderer] 主相机已剔除 Layer {OVERLAY_LAYER}");
    }

    /// <summary>获取当前 RT 分辨率缩放（从 PerformanceMonitor 读取）</summary>
    private float GetRTScale()
    {
        var pet = GetComponent<DesktopPet>();
        if (pet != null)
        {
            var pm = pet.GetPerformanceMonitor();
            if (pm != null) return pm.rtResolutionScale;
        }
        return 1f; // 默认全分辨率
    }

    /// <summary>设置叠加相机+RT</summary>
    private void SetupOverlayRendering()
    {
        if (_modelRoot == null) return;

        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        _overlayModelRenderers = _modelRoot.GetComponentsInChildren<Renderer>(true);

        // 先用局部 RT 建立资源，随后由 UpdateOverlayFraming() 按模型包围盒校准尺寸。
        // 不再按主屏尺寸分配 Live2D 专用 RT。
        _overlayScreenW = QuantizeOverlaySize(Mathf.CeilToInt(LOCAL_OVERLAY_MIN_WIDTH_PX * GetRTScale()));
        _overlayScreenH = QuantizeOverlaySize(Mathf.CeilToInt(LOCAL_OVERLAY_MIN_HEIGHT_PX * GetRTScale()));
        _overlayRT = new RenderTexture(_overlayScreenW, _overlayScreenH, 24, RenderTextureFormat.ARGB32);
        _overlayRT.name = "ModelOverlayRT";
        _overlayRT.useMipMap = false;
        _overlayRT.Create();

        // 创建叠加相机
        GameObject camGO = new GameObject("ModelOverlayCamera");
        camGO.transform.parent = transform;
        camGO.transform.position = mainCam.transform.position;
        camGO.transform.rotation = mainCam.transform.rotation;
        _overlayCamera = camGO.AddComponent<Camera>();
        _overlayCamera.CopyFrom(mainCam);
        _overlayCamera.targetTexture = _overlayRT;
        _overlayCamera.cullingMask = 1 << OVERLAY_LAYER;
        _overlayCamera.clearFlags = CameraClearFlags.SolidColor;
        _overlayCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _overlayCamera.depth = mainCam.depth + 1;
        _overlayCamera.allowHDR = false;
        _overlayCamera.allowMSAA = false;
        _overlayCamera.rect = new Rect(0f, 0f, 1f, 1f);

        _overlayReady = true;
        UpdateOverlayFraming();
#if !UNITY_EDITOR
        _nativeOverlay = gameObject.GetComponent<NativeLive2DOverlay>();
        if (_nativeOverlay == null) _nativeOverlay = gameObject.AddComponent<NativeLive2DOverlay>();
        _nativeOverlay.SetSource(_overlayRT, _overlayDrawRect);
#endif
        Debug.Log($"[Live2DRenderer] 局部叠加相机就绪 RT=({_overlayScreenW}x{_overlayScreenH}), drawRect={_overlayDrawRect}");
    }

    private static int QuantizeOverlaySize(int value)
    {
        int safe = Mathf.Max(LOCAL_OVERLAY_SIZE_QUANTUM, value);
        return ((safe + LOCAL_OVERLAY_SIZE_QUANTUM - 1) / LOCAL_OVERLAY_SIZE_QUANTUM) * LOCAL_OVERLAY_SIZE_QUANTUM;
    }

    private void RebuildOverlayTexture(int width, int height)
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);
        if (_overlayRT != null && _overlayRT.width == width && _overlayRT.height == height)
            return;

        if (_overlayRT != null)
        {
            _overlayRT.Release();
            Destroy(_overlayRT);
        }

        _overlayScreenW = width;
        _overlayScreenH = height;
        _overlayRT = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        _overlayRT.name = "ModelOverlayRT";
        _overlayRT.useMipMap = false;
        _overlayRT.Create();
        if (_overlayCamera != null)
            _overlayCamera.targetTexture = _overlayRT;

        Debug.Log($"[Live2DRenderer] 局部 RT 重建: {_overlayScreenW}x{_overlayScreenH}");
    }

    private bool TryGetModelScreenRect(Camera cam, out Rect rect)
    {
        rect = default;
        if (cam == null || _overlayModelRenderers == null || _overlayModelRenderers.Length == 0)
            return false;

        bool hasBounds = false;
        Bounds combined = default;
        foreach (var modelRenderer in _overlayModelRenderers)
        {
            if (modelRenderer == null || !modelRenderer.enabled) continue;
            if (!hasBounds)
            {
                combined = modelRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(modelRenderer.bounds);
            }
        }

        if (!hasBounds || combined.size.x <= 0f || combined.size.y <= 0f)
            return false;

        Vector3 min = combined.min;
        Vector3 max = combined.max;
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        // 用合并包围盒的 8 个角点投影，兼容模型翻转和相机轻微旋转。
        for (int ix = 0; ix <= 1; ix++)
        {
            for (int iy = 0; iy <= 1; iy++)
            {
                for (int iz = 0; iz <= 1; iz++)
                {
                    Vector3 world = new Vector3(ix == 0 ? min.x : max.x,
                        iy == 0 ? min.y : max.y,
                        iz == 0 ? min.z : max.z);
                    Vector3 screen = cam.WorldToScreenPoint(world);
                    if (screen.z <= 0f) continue;
                    minX = Mathf.Min(minX, screen.x);
                    maxX = Mathf.Max(maxX, screen.x);
                    minY = Mathf.Min(minY, screen.y);
                    maxY = Mathf.Max(maxY, screen.y);
                }
            }
        }

        if (float.IsInfinity(minX) || maxX <= minX || maxY <= minY)
            return false;

        // Unity 屏幕坐标左下为原点，IMGUI 坐标左上为原点。
        rect = Rect.MinMaxRect(minX, Screen.height - maxY, maxX, Screen.height - minY);
        return rect.width > 1f && rect.height > 1f;
    }

    private Rect GetFallbackOverlayRect()
    {
        float centerX = Screen.width * 0.5f;
        float centerY = Screen.height * 0.5f;
        if (_pet != null)
        {
            centerX = _pet.petX + _pet.petWidth * 0.5f;
            centerY = _pet.petY + _pet.petHeight * 0.5f;
        }

        return new Rect(centerX - LOCAL_OVERLAY_MIN_WIDTH_PX * 0.5f,
            centerY - LOCAL_OVERLAY_MIN_HEIGHT_PX * 0.5f,
            LOCAL_OVERLAY_MIN_WIDTH_PX,
            LOCAL_OVERLAY_MIN_HEIGHT_PX);
    }

    /// <summary>
    /// 根据模型实际屏幕包围盒调整局部 RT 与叠加相机。
    /// 仅正交相机启用局部裁剪；其他投影自动回退全屏，避免投影错位。
    /// </summary>
    private void UpdateOverlayFraming()
    {
        if (!_overlayReady || _overlayCamera == null || _overlayRT == null) return;
        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        if (!mainCam.orthographic)
        {
            if (!_overlayPerspectiveFallbackLogged)
            {
                Debug.LogWarning("[Live2DRenderer] 主相机不是正交相机，局部叠加渲染回退为全屏 RT");
                _overlayPerspectiveFallbackLogged = true;
            }

            _overlayDrawRect = new Rect(0f, 0f, Screen.width, Screen.height);
            RebuildOverlayTexture(QuantizeOverlaySize(Mathf.CeilToInt(Screen.width * GetRTScale())),
                QuantizeOverlaySize(Mathf.CeilToInt(Screen.height * GetRTScale())));
            _overlayCamera.transform.position = mainCam.transform.position;
            _overlayCamera.transform.rotation = mainCam.transform.rotation;
            _overlayCamera.orthographicSize = mainCam.orthographicSize;
            _overlayCamera.fieldOfView = mainCam.fieldOfView;
            _overlayCamera.aspect = (float)_overlayScreenW / _overlayScreenH;
#if !UNITY_EDITOR
            _nativeOverlay?.SetSource(_overlayRT, _overlayDrawRect);
#endif
            return;
        }

        Rect modelRect;
        if (!TryGetModelScreenRect(mainCam, out modelRect))
            modelRect = GetFallbackOverlayRect();

        float centerX = modelRect.center.x;
        float centerY = modelRect.center.y;
        float cropWidth = Mathf.Max(LOCAL_OVERLAY_MIN_WIDTH_PX, modelRect.width + LOCAL_OVERLAY_PADDING_PX * 2f);
        float cropHeight = Mathf.Max(LOCAL_OVERLAY_MIN_HEIGHT_PX, modelRect.height + LOCAL_OVERLAY_PADDING_PX * 2f);

        float scale = GetRTScale();
        int targetWidth = QuantizeOverlaySize(Mathf.CeilToInt(cropWidth * scale));
        int targetHeight = QuantizeOverlaySize(Mathf.CeilToInt(cropHeight * scale));

        // 动作结束时头发/衣摆的 Bounds 会在相邻两帧跨过一个 32px 量化边界。
        // 若立即按 480↔512 反复销毁并创建 RT，透明窗口会闪黑，视觉上像头发
        // 在闪烁。保留一个量化单位的余量；只有模型实际扩张至少两个量化单位
        // 才重建，64px padding 足以覆盖这一小段包围盒波动。
        if (_overlayRT != null &&
            Mathf.Abs(targetWidth - _overlayScreenW) <= LOCAL_OVERLAY_SIZE_QUANTUM &&
            Mathf.Abs(targetHeight - _overlayScreenH) <= LOCAL_OVERLAY_SIZE_QUANTUM)
        {
            targetWidth = _overlayScreenW;
            targetHeight = _overlayScreenH;
        }

        // Recreating the native layered overlay transfers focus on some Windows
        // configurations. Keep its backing texture stable throughout a pointer
        // drag; the latest bounds are applied immediately after the mouse releases.
        if (_pet != null && _pet.isDragging && _overlayRT != null)
        {
            targetWidth = _overlayScreenW;
            targetHeight = _overlayScreenH;
        }

        cropWidth = targetWidth / Mathf.Max(0.01f, scale);
        cropHeight = targetHeight / Mathf.Max(0.01f, scale);

        // Cubism 的初始 Bounds 可能在首帧仍是 prefab 坐标（出现负数屏幕坐标）。
        // 覆盖窗口不能因此整个落到显示器外；保留完整尺寸并把左上角限制到当前屏幕。
        float drawX = Mathf.Clamp(centerX - cropWidth * 0.5f, 0f, Mathf.Max(0f, Screen.width - cropWidth));
        float drawY = Mathf.Clamp(centerY - cropHeight * 0.5f, 0f, Mathf.Max(0f, Screen.height - cropHeight));
        _overlayDrawRect = new Rect(drawX, drawY,
            cropWidth, cropHeight);
        RebuildOverlayTexture(targetWidth, targetHeight);

        Vector3 screenCenter = new Vector3(_overlayDrawRect.center.x,
            Screen.height - _overlayDrawRect.center.y, mainCam.nearClipPlane);
        Vector3 worldCenter = mainCam.ScreenToWorldPoint(screenCenter);
        _overlayCamera.transform.position = new Vector3(worldCenter.x, worldCenter.y, mainCam.transform.position.z);
        _overlayCamera.transform.rotation = mainCam.transform.rotation;
        _overlayCamera.orthographicSize = mainCam.orthographicSize * cropHeight / Mathf.Max(1f, Screen.height);
        _overlayCamera.aspect = (float)_overlayScreenW / Mathf.Max(1, _overlayScreenH);
        _overlayCamera.fieldOfView = mainCam.fieldOfView;
#if !UNITY_EDITOR
        _nativeOverlay?.SetSource(_overlayRT, _overlayDrawRect);
#endif
    }

    private void OnGUI()
    {
#if !UNITY_EDITOR
        // Windows Player 由 NativeLive2DOverlay 以逐像素 Alpha 合成，不能再把 RT
        // 回贴到 DWM 透明主窗口，否则 D3D11 下会再次出现整块背景或模型被吞掉。
        return;
#else
        if (!_overlayReady || _overlayRT == null || _pet == null) return;

        // Framing 在 LateUpdate 中更新；这里仅处理首次初始化或极端情况下的兜底。
        if (_overlayDrawRect.width <= 0f || _overlayDrawRect.height <= 0f)
            UpdateOverlayFraming();

        // GUI.depth = 1 → 在底层 GUI 元素之上绘制
        GUI.depth = 1;

        // 局部绘制 RT；原生窗口仍是全屏透明窗口，透明区域露出下方桌面/UI。
        GUI.DrawTexture(_overlayDrawRect, _overlayRT, ScaleMode.StretchToFill, true);
#endif
    }

    private void OnDestroy()
    {
        CancelTestParam94Gesture("candidate-test-renderer-destroyed");
        _inputCoordinator.ReleaseAll("renderer-destroyed");
        if (_nativeOverlay != null)
        {
            _nativeOverlay.DisposeOverlay();
            _nativeOverlay = null;
        }
        if (_overlayRT != null)
        {
            _overlayRT.Release();
            Destroy(_overlayRT);
            _overlayRT = null;
        }
        if (_overlayCamera != null)
        {
            Destroy(_overlayCamera.gameObject);
            _overlayCamera = null;
        }
        _overlayReady = false;
    }

    private void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null) return;
        go.layer = layer;
        foreach (Transform child in go.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    /// <summary>
    /// 性能档位变化时回调 — 由 DesktopPet.PerformanceMonitor 触发
    /// </summary>
    public void OnPerformanceTierChanged(PerformanceTier tier)
    {
        // 分辨率缩放由 OnGUI 中的重建逻辑自动处理（读取 PerformanceMonitor.rtResolutionScale）
        Debug.Log($"[Live2DRenderer] 性能档位：{tier}");
    }
}
