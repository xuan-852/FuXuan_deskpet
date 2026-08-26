using UnityEngine;

/// <summary>
/// Live2DRenderer 的模型置顶叠加渲染与性能档位适配。
/// 保持渲染行为与主类一致，仅将相机、RenderTexture 和 OnGUI 生命周期隔离。
/// </summary>
public partial class Live2DRenderer
{
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

        // 根据性能档位创建 RT（High=100%, Normal=75%, Low=50%）
        float scale = GetRTScale();
        _overlayScreenW = Mathf.Max(1, (int)(Screen.width * scale));
        _overlayScreenH = Mathf.Max(1, (int)(Screen.height * scale));
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

        _overlayReady = true;
        Debug.Log($"[Live2DRenderer] 叠加相机就绪 RT=({_overlayScreenW}x{_overlayScreenH})");
    }

    /// <summary>每帧同步叠加相机位置</summary>
    private void SyncOverlayCamera()
    {
        if (_overlayCamera == null) return;
        Camera mainCam = Camera.main;
        if (mainCam == null) return;
        _overlayCamera.transform.position = mainCam.transform.position;
        _overlayCamera.transform.rotation = mainCam.transform.rotation;
        _overlayCamera.orthographicSize = mainCam.orthographicSize;
        _overlayCamera.fieldOfView = mainCam.fieldOfView;
    }

    private void OnGUI()
    {
        if (!_overlayReady || _overlayRT == null || _pet == null) return;

        // 窗口大小或性能档位变了就重建 RT
        float scale = GetRTScale();
        int targetW = Mathf.Max(1, (int)(Screen.width * scale));
        int targetH = Mathf.Max(1, (int)(Screen.height * scale));
        if (targetW != _overlayScreenW || targetH != _overlayScreenH)
        {
            _overlayScreenW = targetW;
            _overlayScreenH = targetH;
            if (_overlayRT != null) _overlayRT.Release();
            _overlayRT = new RenderTexture(_overlayScreenW, _overlayScreenH, 24, RenderTextureFormat.ARGB32);
            _overlayRT.name = "ModelOverlayRT";
            _overlayRT.useMipMap = false;
            _overlayRT.Create();
            _overlayCamera.targetTexture = _overlayRT;
            Debug.Log($"[Live2DRenderer] RT 分辨率变更: {_overlayScreenW}x{_overlayScreenH} (scale={scale})");
        }

        SyncOverlayCamera();

        // GUI.depth = 1 → 在底层 GUI 元素之上绘制
        GUI.depth = 1;

        // ★ 全屏绘制 RT — 叠加相机只渲染了 Layer 31（模型），背景透明 (0,0,0,0)
        //   用 alphaBlend=true 让透明区域露出下方的输入栏等 GUI 元素
        //   不再裁剪到 pet 范围（模型实际尺寸远大于 170px，裁剪导致头/脚消失）
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _overlayRT, ScaleMode.StretchToFill, true);
    }

    private void OnDestroy()
    {
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
