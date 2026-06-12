using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Physics;
using UnityEngine;

// ===================================================================
//  调大小 → 改下面三个数值即可（改完保存，Unity 自动重编译）
// ===================================================================
//  模型缩放:     LIVE2D_SCALE      (默认 200, 改小→变小)
//  垂直偏移:     LIVE2D_OFFSET_Y   (默认 250, 正=下移, 负=上移)
//  相机远近:     (由 Camera 控制，无需在此调整)
// ===================================================================

/// <summary>
/// Live2D 渲染器 — 用 Cubism SDK 渲染符玄 Live2D 模型
///
/// 使用方式：
/// 1. 在 Unity 中导入 Cubism SDK 后，模型文件会自动生成 Prefab
/// 2. 将生成的 Prefab 拖到 modelPrefab 槽
/// 3. 运行时自动实例化并跟随物理坐标
/// </summary>
[RequireComponent(typeof(DesktopPet))]
public class Live2DRenderer : MonoBehaviour, IPetRenderer
{
    // ===================================================================
    // ===== 🎛️ 调参区 — 改这里 =====
    // ===================================================================
    const float LIVE2D_SCALE       = 200f;    // 模型缩放（越大→模型越大）
    const float LIVE2D_OFFSET_Y    = 0f;    // 垂直偏移（正数=下移，负数=上移）
    // ===================================================================

    // ================ 动画参数 ================
    // -- 空闲待机 --
    const float BREATH_AMPLITUDE   = 0.4f;    // 呼吸幅度
    const float BODY_SWAY_X        = 10.0f;  // 身体左右摆
    const float BODY_SWAY_Y        = 0.5f;   // 身体上下晃
    const float BODY_SWAY_Z        = 0.4f;   // 身体旋转
    const float HEAD_X             = 0.6f;   // 头部左右
    const float HEAD_Y             = 0.4f;   // 头部上下
    const float EYE_X              = 3f;     // 眼珠左右
    const float EYE_Y              = 2f;     // 眼珠上下
    const float IDLE_TILT          = 8f;     // 歪头幅度
    const float IDLE_SMILE         = 0.6f;   // 微笑幅度
    const float IDLE_MOUTH         = 0.4f;   // 张嘴幅度
    const float IDLE_BROW          = 6f;     // 眉毛幅度（动作2）
    const float IDLE_BROW_Y        = 6f;     // 眉毛抬起幅度（动作3）

    // -- 走路（侧面视角）--
    // 模型转体侧面，腿/手臂摆动可见
    // bodyAngleY符号由方向决定（翻转后视觉一致）
    const float WALK_SIDE_ANGLE    = 18f;    // 身体Y轴转体幅度（方向自动匹配）
    const float WALK_SWAY_FREQ     = 5f;     // 步频
    const float WALK_BOUNCE_PX    = 4f;     // 上下颠簸(像素)
    const float WALK_BODY_LEAN    = 5f;     // 身体前倾
    const float WALK_HEAD_TILT    = 12f;    // 头微低看路（正数=低头）
    const float WALK_LEG_LIFT     = 4f;     // 抬腿幅度 (Param165)
    const float WALK_LEG_SWING    = 6f;     // 腿前后摆幅 (Param126/129 位移)
    const float WALK_LEG_BEND     = 6f;     // 腿弯曲幅度 (Param127/131 透视)
    const float WALK_ARM_SWING    = 4f;     // 手臂摆动 (Param94)
    const float WALK_BODY_SWING   = 2f;     // 身体Z轴横摆(驱动衣服飘动, ParamBodyAngleZ)
    const float WALK_SHOULDER     = 1.5f;     // 耸肩 (Param153)
    const float WALK_BREATH       = 3f;     // 呼吸恒定加深（给物理持续输入）

    // -- 下落 --
    const float FALL_BODY_ANGLE_X  = -3f;    // 下落身体前倾
    const float FALL_HEAD_ANGLE_X  = -5f;    // 下落头部角度

    // -- 点击 --
    const float CLICK_BODY_ANGLE_X = -5f;    // 点击身体角度
    const float CLICK_HEAD_ANGLE_X = 8f;     // 点击头部角度
    const float CLICK_EYE_OPEN     = 0.3f;   // 点击眯眼
    const float CLICK_LOCK_TIME    = 1.0f;   // 点击姿势锁定秒数
    // ==================================================
    [Header("模型 Prefab")]
    [Tooltip("Cubism SDK 导入后生成的模型 Prefab（不拖也行，代码自动按路径加载）")]
    public GameObject modelPrefab;

    [Header("显示设置（改顶部 LIVE2D_SCALE / LIVE2D_OFFSET_Y 宏）")]
    [Tooltip("模型缩放")]
    public float modelScale = LIVE2D_SCALE;

    [Tooltip("模型垂直偏移（像素）")]
    public float verticalOffset = LIVE2D_OFFSET_Y;

    // Cubism 组件
    private GameObject _modelRoot;
    private CubismModel _cubismModel;
    private CubismPhysicsController _physicsController;

    // DesktopPet 引用
    private DesktopPet _pet;

    // 姿势锁定
    private bool _poseLocked = false;
    private float _poseLockUntil = 0f;

    // 眨眼
    private float _blinkTime = 0f;
    private float _blinkInterval = 3f;
    private bool _isBlinking = false;
    private float _blinkPhase = 0f;

    // 呼吸
    private float _breathPhase = 0f;

    // 随机小动作（站立时触发）
    private float _idleActionTime = 0f;
    private float _idleActionInterval = 8f;
    private int _currentIdleAction = 0; // 0=无, 1=歪头, 2=微笑, 3=挑眉

    // 随机微动用噪声偏移
    private float _noiseTimeX = 0f;
    private float _noiseTimeY = 0f;

    // 是否已加载
    private bool _loaded = false;

    // 走路颠簸当前偏移量（像素）
    private float _walkBounceOffset = 0f;

    // 走路相位
    private float _walkPhase = 0f;

    private void Start()
    {
        Debug.Log("[Live2DRenderer] Start() 被调用了");
        _pet = GetComponent<DesktopPet>();
        Debug.Log($"[Live2DRenderer] DesktopPet={( _pet != null)}");

        // ★ 强制从宏读取（忽略场景中序列化的旧值，改宏立即生效）
        modelScale = LIVE2D_SCALE;
        verticalOffset = LIVE2D_OFFSET_Y;

        TryLoadModel();
    }

    private void TryLoadModel()
    {
        Debug.Log($"[Live2DRenderer] TryLoadModel() modelPrefab当前值={modelPrefab}");
        // ★ 无条件优先用 AssetDatabase 按路径加载（场景序列化引用可能损毁）
        #if UNITY_EDITOR
        string prefabPath = "Assets/Live2D/Models/Fuxuan/符玄.prefab";
        Debug.Log($"[Live2DRenderer] 尝试 AssetDatabase 加载: {prefabPath}");
        GameObject resolvedPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Debug.Log($"[Live2DRenderer] AssetDatabase 结果={(resolvedPrefab != null ? resolvedPrefab.name : "null")}");
        if (resolvedPrefab != null)
        {
            modelPrefab = resolvedPrefab;
        }
        #endif
        // 降级：Resources.Load（需要把 prefab 放 Resources 文件夹下）
        if (modelPrefab == null)
        {
            modelPrefab = Resources.Load<GameObject>("Live2D/Models/Fuxuan/符玄");
        }

        if (modelPrefab != null)
        {
            _modelRoot = Instantiate(modelPrefab, transform);
            
            // 设置 Layer（确保 Camera 能看到）
            SetLayerRecursively(_modelRoot, 0); // 0 = Default
            
            Debug.Log($"[Live2DRenderer] _modelRoot={_modelRoot.name}, activeSelf={_modelRoot.activeSelf}, activeInHierarchy={_modelRoot.activeInHierarchy}");
            Debug.Log($"[Live2DRenderer] _modelRoot.transform.childCount={_modelRoot.transform.childCount}");

            _cubismModel = _modelRoot.GetComponentInChildren<CubismModel>();
            _physicsController = _modelRoot.GetComponentInChildren<CubismPhysicsController>();

            // 列出所有子物体和组件
            Debug.Log($"[Live2DRenderer] 模型实例化后结构:");
            ListAllChildren(_modelRoot, 0);

            // 检查 CubismRenderController
            var renderController = _modelRoot.GetComponentInChildren<Live2D.Cubism.Rendering.CubismRenderController>();
            Debug.Log($"[Live2DRenderer] CubismRenderController={renderController != null}");
            if (renderController != null)
            {
                Debug.Log($"[Live2DRenderer] CubismRenderController.enabled={renderController.enabled}");
            }

            _loaded = true;
            Debug.Log($"[Live2DRenderer] 模型 Prefab 实例化完成, CubismModel={_cubismModel != null}, Physics={_physicsController != null}");
            if (_cubismModel != null)
                Debug.Log($"[Live2DRenderer] 参数数量: {_cubismModel.Parameters.Length}");
        }

        if (!_loaded)
        {
            Debug.LogError("[Live2DRenderer] 没有设置 modelPrefab，请在 Inspector 中拖拽 Cubism 导入的模型 Prefab");
            enabled = false;
        }
    }
    
    private System.Collections.IEnumerator CheckRendererStatus()
    {
        yield return new WaitForSeconds(0.1f); // 延迟检查
        if (_modelRoot != null)
        {
            var renderers = _modelRoot.GetComponentsInChildren<Renderer>();
            int enabledCount = 0;
            int disabledCount = 0;
            foreach (var renderer in renderers)
            {
                if (renderer.enabled)
                    enabledCount++;
                else
                    disabledCount++;
            }
            Debug.Log($"[Live2DRenderer] 延迟检查 - 总共: {renderers.Length}, 启用: {enabledCount}, 禁用: {disabledCount}");
            
            // 强制启用所有 Renderer
            if (disabledCount > 0)
            {
                int forceEnabled = 0;
                foreach (var renderer in renderers)
                {
                    if (!renderer.enabled)
                    {
                        renderer.enabled = true;
                        forceEnabled++;
                    }
                }
                Debug.Log($"[Live2DRenderer] 强制启用了 {forceEnabled} 个 Renderer");
            }
        }
    }

    private void Update()
    {
        if (!_loaded || _cubismModel == null) return;

        // 累积走路相位并计算垂直颠簸偏移
        if (_pet != null && _pet.onGround && _pet.petVx != 0)
        {
            _walkPhase += Time.deltaTime * WALK_SWAY_FREQ;
            // 限制范围防精度损失（≈1个完整周期）
            if (_walkPhase > Mathf.PI * 2f) _walkPhase -= Mathf.PI * 2f;
            // ★ 颠簸（1 - abs(sin) = 腿并拢时最高，迈步时最低）
            _walkBounceOffset = (1f - Mathf.Abs(Mathf.Sin(_walkPhase))) * WALK_BOUNCE_PX;
        }
        else
        {
            _walkBounceOffset = 0f;
            _walkPhase = 0f;
        }

        UpdateModelPosition();
    }

    private void LateUpdate()
    {
        if (!_loaded || _cubismModel == null) return;

        // 累积噪声时间
        _noiseTimeX += Time.deltaTime * 0.6f;
        _noiseTimeY += Time.deltaTime * 0.4f;
        _breathPhase += Time.deltaTime * 2.0f;

        UpdateBlink();

        // ★ 走路/空闲统一在 LateUpdate 中设置参数
        // 此时 _walkPhase 已在 Update() 中更新完毕，相位准确
        if (_pet != null && _pet.onGround && _pet.petVx != 0)
        {
            UpdateWalkAnimation();
        }
        else
        {
            UpdateIdleAnimation();
        }
    }

    /// <summary>
    /// 核心空闲动画 — 使用 Perlin 噪声实现自然微动
    /// 只影响呼吸和极轻微的身体晃动，没有明显的周期性上下
    /// </summary>
    private void UpdateIdleAnimation()
    {
        // === 呼吸（为物理提供驱动信号，使衣服手臂自然摆动）===
        float breath = (Mathf.PerlinNoise(_breathPhase, 0f) - 0.5f) * BREATH_AMPLITUDE;
        SetParameter("ParamBreath", breath);

        // === 身体晃动（为物理提供输入，幅度轻微不显眼）===
        float swayX = (Mathf.PerlinNoise(_noiseTimeX, 1f) - 0.5f) * BODY_SWAY_X;
        float swayY = (Mathf.PerlinNoise(_noiseTimeX, 2f) - 0.5f) * BODY_SWAY_Y;
        float swayZ = (Mathf.PerlinNoise(_noiseTimeX, 3f) - 0.5f) * BODY_SWAY_Z;
        SetParameter("ParamBodyAngleX", swayX);
        SetParameter("ParamBodyAngleY", swayY);
        SetParameter("ParamBodyAngleZ", swayZ);

        // === 头部微动 ===
        float headX = (Mathf.PerlinNoise(_noiseTimeX, 4f) - 0.5f) * HEAD_X;
        float headY = (Mathf.PerlinNoise(_noiseTimeX, 5f) - 0.5f) * HEAD_Y;
        SetParameter("ParamAngleX", headX);
        SetParameter("ParamAngleY", headY);

        // === 眼球（Perlin 噪声平滑变化，不突兀）===
        float eyeX = (Mathf.PerlinNoise(_noiseTimeY, 6f) - 0.5f) * EYE_X;
        float eyeY = (Mathf.PerlinNoise(_noiseTimeY, 7f) - 0.5f) * EYE_Y;
        SetParameter("ParamEyeBallX", eyeX);
        SetParameter("ParamEyeBallY", eyeY);

        // === 随机小动作（隔一段时间触发一次）===
        _idleActionTime += Time.deltaTime;
        if (_idleActionTime >= _idleActionInterval)
        {
            _idleActionTime = 0f;
            _currentIdleAction = Random.Range(1, 4);
            _idleActionInterval = Random.Range(8f, 18f);
        }

        if (_currentIdleAction > 0)
        {
            _idleActionTime = Mathf.Min(_idleActionTime, 1.5f);
            float t = Mathf.Sin(_idleActionTime / 1.5f * Mathf.PI);

            if (_currentIdleAction == 1)
            {
                // 歪头
                float tilt = t * IDLE_TILT;
                SetParameter("ParamAngleZ", tilt);
            }
            else if (_currentIdleAction == 2)
            {
                // 微笑眯眼
                SetParameter("ParamEyeLSmile", t * IDLE_SMILE);
                SetParameter("ParamEyeRSmile", t * IDLE_SMILE);
                SetParameter("ParamMouthForm", t * IDLE_MOUTH);
            }
            else if (_currentIdleAction == 3)
            {
                // 眉毛微动
                SetParameter("ParamBrowRY", t * IDLE_BROW_Y);
                SetParameter("ParamBrowLY", t * IDLE_BROW_Y);
            }

            if (_idleActionTime >= 1.5f)
            {
                // 重置
                _currentIdleAction = 0;
                _idleActionTime = 0f;
                SetParameter("ParamAngleZ", 0f);
                SetParameter("ParamEyeLSmile", 0f);
                SetParameter("ParamEyeRSmile", 0f);
                SetParameter("ParamMouthForm", 0f);
                SetParameter("ParamBrowRY", 0f);
                SetParameter("ParamBrowLY", 0f);
            }
        }
    }

    /// <summary>
    /// 将屏幕坐标转为世界坐标，定位模型
    /// </summary>
    private void UpdateModelPosition()
    {
        if (_modelRoot == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        // 屏幕坐标 (左上原点, Y向下) → Unity 世界坐标
        float worldX = _pet.petX + _pet.petWidth / 2f;
        float worldY = _pet.petY + _pet.petHeight / 2f + verticalOffset - _walkBounceOffset;

        Vector3 screenPos = new Vector3(worldX, Screen.height - worldY, 10f);
        Vector3 worldPos = cam.ScreenToWorldPoint(screenPos);

        // 调试日志
        if (Time.frameCount % 60 == 0) // 每秒打印一次
        {
            Debug.Log($"[Live2DRenderer] pet({_pet.petX},{_pet.petY}) size({_pet.petWidth},{_pet.petHeight}) → screenPos({screenPos.x},{screenPos.y}) → worldPos({worldPos.x},{worldPos.y}), scale={modelScale}");
        }

        _modelRoot.transform.position = worldPos;

        // 根据朝向翻转（直接设置scale，不基于当前值）
        bool faceRight = _pet.petVx >= 0;
        Vector3 scale = new Vector3(modelScale, modelScale, 1f);
        scale.x *= (faceRight ? 1 : -1);
        _modelRoot.transform.localScale = scale;
    }

    /// <summary>
    /// 自动眨眼
    /// </summary>
    private void UpdateBlink()
    {
        if (_isBlinking)
        {
            _blinkPhase += Time.deltaTime;
            float blinkValue = Mathf.Clamp01(Mathf.Abs(Mathf.Sin(_blinkPhase * 20f)));
            SetParameter("ParamEyeLOpen", blinkValue);
            SetParameter("ParamEyeROpen", blinkValue);

            if (_blinkPhase >= 0.15f)
            {
                _isBlinking = false;
                _blinkPhase = 0f;
                SetParameter("ParamEyeLOpen", 1f);
                SetParameter("ParamEyeROpen", 1f);
            }
        }
        else
        {
            _blinkTime += Time.deltaTime;
            if (_blinkTime >= _blinkInterval)
            {
                _blinkTime = 0f;
                _isBlinking = true;
                _blinkInterval = Random.Range(2f, 5f);
            }
        }
    }

    /// <summary>
    /// 走路动画 — LateUpdate 中调用，相位已同步
    /// 
    /// ★ 侧面走路（横版过关风格）
    ///   用 ParamBodyAngleY 转体，观众能清楚看到抬腿和摆臂
    ///   身体稳定（恒定转体+前倾），腿/臂交替运动产生走路信号
    /// </summary>
    private void UpdateWalkAnimation()
    {
        float phase = _walkPhase;

        // ★ 身体转体侧面 — 使用正弦微摆而不是恒定值
        //   微摆让身体有走路扭动感，同时驱动物理使衣服自然飘动
        float bodyYaw = WALK_SIDE_ANGLE + Mathf.Sin(phase) * 3f;
        SetParameter("ParamBodyAngleY", bodyYaw);

        // 身体前倾（走路自然前倾）
        SetParameter("ParamBodyAngleX", WALK_BODY_LEAN);

        // ★ 身体左右横摆（ParamBodyAngleZ）驱动衣服飘动
        //   走路时骨盆左右摆动，带动裙摆/袖子物理
        float bodySwing = Mathf.Sin(phase) * WALK_BODY_SWING;
        SetParameter("ParamBodyAngleZ", bodySwing);

        // ★ 头低着看路，不要仰起
        //   Cubism 正数=低头，加大幅度确保低头感
        SetParameter("ParamAngleX", WALK_HEAD_TILT);

        // ★ 脸转向侧面，和身体一致（侧脸）
        SetParameter("ParamAngleY", WALK_SIDE_ANGLE);

        // 呼吸加深（持续驱动物理，让衣服/头发飘动）
        SetParameter("ParamBreath", WALK_BREATH + Mathf.Sin(phase) * 0.5f);

        // ★ 左腿参数
        //   左腿向前(+)时，右手臂也向前(+) — 交叉对位
        float legPhase = Mathf.Sin(phase);
        float rightPhase = -legPhase; // 右腿与左腿反相

        // 抬腿
        SetParameter("Param165", legPhase * WALK_LEG_LIFT);
        SetParameter("Param164", rightPhase * WALK_LEG_LIFT);

        // 前后摆动 + 弯曲
        SetParameter("Param126", legPhase * WALK_LEG_SWING);
        SetParameter("Param127", Mathf.Abs(legPhase) * WALK_LEG_BEND);

        // 右腿
        SetParameter("Param129", rightPhase * WALK_LEG_SWING);
        SetParameter("Param131", Mathf.Abs(rightPhase) * WALK_LEG_BEND);

        // ★ 右手臂与左腿同步（交叉对位：左腿前→右手前）
        //   左臂被physics锁定无法直接控制，用右手体现交叉摆臂
        SetParameter("Param94", legPhase * WALK_ARM_SWING);

        // 肩膀配合脚步
        SetParameter("Param153", Mathf.Abs(legPhase) * WALK_SHOULDER);
    }

    private void SetParameter(string name, float value)
    {
        if (_cubismModel == null) return;
        var param = _cubismModel.Parameters.FindById(name);
        if (param != null) param.Value = value;
    }

    #region IPetRenderer

    public void ShowDragPose()
    {
        SetParameter("ParamBodyAngleX", 0f);
        SetParameter("ParamAngleX", 0f);
        _poseLocked = false;
    }

    public void ShowClickPose()
    {
        SetParameter("ParamEyeLOpen", CLICK_EYE_OPEN);
        SetParameter("ParamEyeROpen", CLICK_EYE_OPEN);
        SetParameter("ParamAngleX", CLICK_HEAD_ANGLE_X);
        SetParameter("ParamBodyAngleX", CLICK_BODY_ANGLE_X);
        _poseLocked = true;
        _poseLockUntil = Time.time + CLICK_LOCK_TIME;
    }

    public void ShowLandPose()
    {
        SetParameter("ParamBodyAngleX", 0f);
        _poseLocked = false;
    }

    public void ShowWalkPose()
    {
        if (_poseLocked && Time.time < _poseLockUntil) return;
        _poseLocked = false;
    }

    public void ShowStopPose(float lockSeconds)
    {
        if (lockSeconds > 0f)
        {
            _poseLocked = true;
            _poseLockUntil = Time.time + lockSeconds;
        }
    }

    public void OnPetUpdate(int petX, int petY, int petWidth, int petHeight,
                            int petVx, int petVy, bool onGround, bool isDragging, bool isPaused)
    {
        if (!_loaded || _cubismModel == null) return;

        if (isDragging) return;

        if (_poseLocked && Time.time < _poseLockUntil) return;
        _poseLocked = false;

        if (!onGround && petVy > 0)
        {
            // 下落 — 身体前倾
            SetParameter("ParamBodyAngleX", FALL_BODY_ANGLE_X);
            SetParameter("ParamAngleX", FALL_HEAD_ANGLE_X);
            SetParameter("ParamBreath", 0f);
        }
        else if (onGround)
        {
            // 走路参数统一在 LateUpdate 中设置（确保相位同步）
            // 这里什么都不做，避免 Update 执行顺序问题
        }
    }

    #endregion

    private void ListAllChildren(GameObject go, int depth)
    {
        string indent = new string(' ', depth * 2);
        Debug.Log($"{indent}{go.name} (active={go.activeInHierarchy})");

        foreach (var component in go.GetComponents<Component>())
        {
            if (component == null) continue;
            if (component is Transform) continue;
            Debug.Log($"{indent}  [{component.GetType().Name}]");
        }

        foreach (Transform child in go.transform)
        {
            ListAllChildren(child.gameObject, depth + 1);
        }
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
    /// 设置模型透明度（HybridRenderer 交叉淡入淡出用）
    /// </summary>
    public void SetAlpha(float alpha)
    {
        if (_modelRoot == null) return;
        var renderers = _modelRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            // Live2D Cubism shader 没有 _Color 属性，跳过
            if (!r.material.HasProperty("_Color")) continue;
            Color c = r.material.color;
            c.a = Mathf.Clamp01(alpha);
            r.material.color = c;
        }
    }
}
