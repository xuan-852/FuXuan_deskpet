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
    const float LIVE2D_OFFSET_Y    = 100f;    // 垂直偏移（正数=下移，负数=上移）
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

    // -- 丰富静止动作 --

    // 动作1: 星辉环绕（紫环旋转 + 星星闪烁）
    const float SPIN_DURATION      = 6f;
    const float SPIN_RING_OUTER    = 30f;    // 外紫环旋转幅度
    const float SPIN_RING_MID      = 45f;    // 中紫环旋转幅度
    const float SPIN_RING_INNER    = 60f;    // 内紫环旋转幅度

    // 动作2: 伸懒腰（抬右手 + 后仰 + 眯眼张嘴）
    const float STRETCH_DURATION   = 4.5f;
    const float STRETCH_R_ARM      = 25f;    // 右手伸出
    const float STRETCH_BODY_BACK  = -5f;    // 身体后仰
    const float STRETCH_MOUTH_OPEN = 0.6f;   // 张嘴
    const float STRETCH_EYE_CLOSE  = 0.4f;   // 眯眼

    // 动作3: 爱心眨眼（爱心眼 + 歪头微笑）
    const float HEART_DURATION     = 3f;
    const float HEART_EYE          = 1f;     // 爱心眼
    const float HEART_TILT         = 12f;    // 歪头
    const float HEART_SMILE        = 0.8f;   // 微笑

    // 动作7: 数钱钱 💰
    const float MONEY_DURATION     = 3.5f;
    const float MONEY_SMILE        = 0.6f;   // 微笑幅度
    const float MONEY_TILT         = 10f;    // 歪头幅度
    const float MONEY_SWAY         = 4f;     // 身体摇摆幅度

    // 动作8: 委屈 😢
    const float CRY_DURATION       = 3.5f;
    const float CRY_HEAD_DOWN      = 6f;     // 低头幅度
    const float CRY_MOUTH_TREM     = 0.3f;   // 嘴巴微颤
    const float CRY_BROW_UP        = 5f;     // 眉毛抬起（委屈八字眉）

    // 动作10: 害羞黑脸 😊🖤
    const float BLUSH_DURATION     = 3.5f;
    const float BLUSH_DARK         = 1f;     // 黑脸程度
    const float BLUSH_LOOK_AWAY    = -8f;    // 眼神躲闪
    const float BLUSH_SMILE        = 0.5f;   // 害羞微笑

    // 动作9: 法阵显现 ✨（起势→剑指朝天结印→指尖凝光→扩散至全屏→消散）
    const float CIRCLE_DURATION       = 8.0f;
    // -- 走路（侧面视角）--
    // 模型转体侧面，腿/手臂摆动可见
    // bodyAngleY符号由方向决定（翻转后视觉一致）
    const float WALK_SIDE_ANGLE    = 18f;    // 身体Y轴转体幅度（方向自动匹配）
    const float WALK_SWAY_FREQ     = 5f;     // 步频
    const float WALK_BOUNCE_PX    = 4f;     // 上下颠簸(像素)
    const float WALK_BODY_LEAN    = 5f;     // 身体前倾
    const float WALK_HEAD_TILT    = 8f;     // 头微低看路（ParamAngleY 正数=低头）
    const float WALK_LEG_LIFT     = 4f;     // 抬腿幅度 (Param165)
    const float WALK_LEG_SWING    = 6f;     // 腿前后摆幅 (Param126/129 位移)
    const float WALK_LEG_BEND     = 6f;     // 腿弯曲幅度 (Param127/131 透视)
    const float WALK_ARM_SWING    = 2f;     // 手臂摆动 (Param34/36/37=左臂, Param94=右臂)
    const float WALK_BODY_SWING   = 2f;     // 身体Z轴横摆(驱动衣服飘动, ParamBodyAngleZ)
    const float WALK_SHOULDER     = 1.5f;   // 耸肩 (Param153)
    const float WALK_BREATH       = 3f;     // 呼吸恒定加深（给物理持续输入）
    const float IDLE_BLEND_DURATION = 0.4f;  // 走路→空闲混合消退时长
    const float WALK_FADE_IN_DURATION = 0.3f; // 空闲→走路体态淡入时长

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
    private int _currentIdleAction = 0; // 0=无, 1=歪头, 2=微笑, 3=挑眉, 4=星辉, 5=伸懒腰, 6=爱心眼, 7=数钱, 8=委屈, 9=法阵, 10=害羞
    // 各动作权重（对应动作 1-10），值越大出现概率越高
    private readonly int[] _idleActionWeights = new int[] { 5, 5, 3, 2, 2, 3, 2, 3, 1, 3 };
    // 复合动作相位（用于多参数协同插值）
    private float _complexActionPhase = 0f;

    // 随机微动用噪声偏移
    private float _noiseTimeX = 0f;
    private float _noiseTimeY = 0f;

    // 强制动作锁定（右键菜单触发，播放期间不被走路覆盖）
    private bool _actionLocked = false;
    public event System.Action OnForcedActionFinished;

    // 是否已加载
    private bool _loaded = false;

    // 走路颠簸当前偏移量（像素）
    private float _walkBounceOffset = 0f;

    // 走路相位
    private float _walkPhase = 0f;

    // 上一帧是否在走路（用于检测走路↔空闲切换）
    private bool _wasWalkingLastFrame = false;

    // 走路→空闲混合消退计时
    private float _walkBlendRemaining = 0f;

    // 空闲→走路体态淡入计时（动作结束后切走路不生硬）
    private float _walkFadeInRemaining = 0f;

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
            {
                Debug.Log($"[Live2DRenderer] 参数数量: {_cubismModel.Parameters.Length}");
                // ★ 打印所有参数名 + 范围，找真实的手部参数
                string allParams = "";
                foreach (var p in _cubismModel.Parameters)
                    allParams += p.Id + ", ";
                Debug.Log("[Live2DRenderer] 所有参数: " + allParams);

                // ★ 打印重点参数的范围（单位类型 + 最小/最大/默认值）
                string[] keyParams = new string[] {
                    "Param93", "Param94", "Param92", "Param118", 
                    "Param33", "Param31", "Param32", "Param97",
                    "Param95", "Param117", "Param98", "Param100", 
                    "Param116", "Param120", "Param108", "Param119",
                    "Param34", "Param36", "Param37",
                    "Param110", "Param111", "Param99",
                    "Param112", "Param113", "Param114", "Param115"
                };
                string rangeInfo = "[Live2DRenderer] 关键参数范围: ";
                foreach (var pid in keyParams)
                {
                    var p = _cubismModel.Parameters.FindById(pid);
                    if (p != null)
                        rangeInfo += $"{pid}(min={p.MinimumValue:F1},max={p.MaximumValue:F1},def={p.DefaultValue:F1}) ";
                }
                Debug.Log(rangeInfo);
            }
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
        bool isWalking = (_pet != null && _pet.onGround && _pet.petVx != 0 && !_pet.isPaused && !_actionLocked);

        if (isWalking)
        {
            if (!_wasWalkingLastFrame)
            {
                // 空闲→走路：清表情残留 + 开始体态淡入
                ResetIdleAction();
                _walkBlendRemaining = 0f;
                _walkFadeInRemaining = WALK_FADE_IN_DURATION;
            }
            UpdateWalkAnimation();
        }
        else
        {
            if (_wasWalkingLastFrame)
            {
                // 走路→空闲：开始混合消退
                _walkBlendRemaining = IDLE_BLEND_DURATION;
                _walkFadeInRemaining = 0f; // 重置淡入（下次空闲→走路重新开始）
            }

            if (_walkBlendRemaining > 0f)
            {
                // 混合期：先播空闲（设噪声/眼部/呼吸），再叠走路渐消（覆盖体态参数）
                UpdateIdleAnimation();
                float blendWeight = Mathf.Clamp01(_walkBlendRemaining / IDLE_BLEND_DURATION);
                float eased = blendWeight * blendWeight; // 二次缓出
                ApplyWalkBodyPose(eased);
                _walkBlendRemaining -= Time.deltaTime;
            }
            else
            {
                UpdateIdleAnimation();
            }
        }

        _wasWalkingLastFrame = isWalking;
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

        // === 空闲动作：加权随机选取（权重越高越容易出现）===
        // 动作: 1=歪头, 2=微笑, 3=挑眉, 4=星辉, 5=伸懒腰, 6=爱心眼, 7=数钱, 8=委屈, 9=法阵, 10=害羞

        // ★ 暂停时（菜单打开），不播新动作
        bool isPaused = (_pet != null && _pet.isPaused);

        // 当前动作结束后，加权随机选取下一个
        if (_currentIdleAction == 0 && !isPaused)
        {
            _currentIdleAction = PickWeightedIdleAction();
            _idleActionTime = 0f;
            _complexActionPhase = 0f;
            Debug.Log($"[Live2DRenderer] ▶ 动作 #{_currentIdleAction}");
        }

        if (_currentIdleAction > 0)
        {
            _idleActionTime += Time.deltaTime;
            _complexActionPhase += Time.deltaTime;

            switch (_currentIdleAction)
            {
                case 1: UpdateIdleTilt(); break;
                case 2: UpdateIdleSmile(); break;
                case 3: UpdateIdleBrow(); break;
                case 4: UpdateStarSpin(); break;
                case 5: UpdateStretch(); break;
                case 6: UpdateHeartEyes(); break;
                case 7: UpdateMoney(); break;
                case 8: UpdateCry(); break;
                case 9: UpdateMagicCircle(); break;
                case 10: UpdateBlush(); break;
            }
        }
    }

    #region 空闲随机动作

    /// <summary>动作1: 歪头 — 往一侧歪头卖萌</summary>
    private void UpdateIdleTilt()
    {
        float duration = 2f;
        float t = Mathf.Sin(_idleActionTime / duration * Mathf.PI);
        SetParameter("ParamAngleZ", t * IDLE_TILT);
        if (_idleActionTime >= duration) ResetIdleAction();
    }

    /// <summary>动作2: 微笑 — 眯眼微笑</summary>
    private void UpdateIdleSmile()
    {
        float duration = 2f;
        float t = Mathf.Sin(_idleActionTime / duration * Mathf.PI);
        SetParameter("ParamEyeLSmile", t * IDLE_SMILE);
        SetParameter("ParamEyeRSmile", t * IDLE_SMILE);
        SetParameter("ParamMouthForm", t * IDLE_MOUTH);
        if (_idleActionTime >= duration) ResetIdleAction();
    }

    /// <summary>动作3: 挑眉 — 眉毛微动</summary>
    private void UpdateIdleBrow()
    {
        float duration = 2f;
        float t = Mathf.Sin(_idleActionTime / duration * Mathf.PI);
        SetParameter("ParamBrowRY", t * IDLE_BROW_Y);
        SetParameter("ParamBrowLY", t * IDLE_BROW_Y);
        if (_idleActionTime >= duration) ResetIdleAction();
    }

    /// <summary>
    /// 动作4: 星辉环绕 ✨
    /// 紫环旋转 + 星星闪烁 — 像法阵一样黑幕+发光
    /// </summary>
    private void UpdateStarSpin()
    {
        float p = _complexActionPhase;
        float duration = SPIN_DURATION;

        // 总进度 0→1, 缓入缓出
        float t = Mathf.Clamp01(p / duration);
        float eased = Mathf.Sin(t * Mathf.PI); // 0→1→0 平滑

        // ★ 黑幕背景（像法阵一样，让星辉发光更明显）
        float darkIn = Mathf.Clamp01((p - 0f) / (duration * 0.15f));       // 快开
        float darkOut = Mathf.Clamp01((duration - p) / (duration * 0.2f));  // 渐消
        float dark = darkIn * darkOut;
        SetParameter("Param121", Mathf.Clamp01(dark * 1.2f)); // 黑幕切换（略超1确保完全开启）
        SetParameter("Param137", dark * 0.8f);                // 黑幕显示
        SetParameter("Param132", dark * 0.8f);                // 眼镜发光

        // ★ 环显隐 + 大小（必须设显隐环才会出现）
        float ringVis = Mathf.Clamp01(eased * 1.3f); // 略超1让环更亮
        SetParameter("Param431", ringVis);       // 外紫环显隐
        SetParameter("Param911", ringVis);       // 中紫环显隐
        SetParameter("Param741", ringVis);       // 内紫环显隐
        SetParameter("Param441", eased * 1f);  // 外紫环大小
        SetParameter("Param961", eased * 1f);  // 中紫环大小
        SetParameter("Param731", eased * 1f);  // 内紫环大小

        // 三环旋转（内中外，不同速度）
        SetParameter("Param421", Mathf.Sin(p * 2f) * eased * SPIN_RING_OUTER);    // 外紫环 转 (ParamGroup20)
        SetParameter("Param422", Mathf.Sin(p * 2f) * eased * SPIN_RING_OUTER);    // 外紫环 转 (ParamGroup21)
        SetParameter("Param901", Mathf.Sin(p * 2.7f) * eased * SPIN_RING_MID);   // 中紫环 转 (ParamGroup20)
        SetParameter("Param902", Mathf.Sin(p * 2.7f) * eased * SPIN_RING_MID);   // 中紫环 转 (ParamGroup21)
        SetParameter("Param881", Mathf.Sin(p * 3.5f) * eased * SPIN_RING_INNER); // 内紫环 转 (ParamGroup20)
        SetParameter("Param882", Mathf.Sin(p * 3.5f) * eased * SPIN_RING_INNER); // 内紫环 转 (ParamGroup21)

        // 蒙版加强（让环更亮更明显）
        float maskBoost = Mathf.Clamp01(eased * 1.2f);
        SetParameter("Param401", maskBoost * 0.8f); // 外蒙版
        SetParameter("Param411", maskBoost * 0.7f); // 中蒙版
        SetParameter("Param891", maskBoost * 0.7f); // 内蒙版

        // 星星显隐 + 大小（呼吸闪烁）
        float starPulse = (Mathf.Sin(p * 3f) + 1f) * 0.5f * eased;
        SetParameter("Param451", starPulse * 1.2f);   // 星显隐（加强）
        SetParameter("Param541", starPulse * 0.8f); // 星大小

        // 外围星
        float outerStar = (Mathf.Sin(p * 2.3f) + 1f) * 0.5f * eased;
        SetParameter("Param1071", outerStar * 0.6f); // 外围星变大
        SetParameter("Param1081", outerStar * 0.8f); // 外星出现

        if (t >= 1f) ResetIdleAction();
    }

    /// <summary>
    /// 动作5: 伸懒腰 🥱
    /// 右手举高 + 身体后仰 + 眯眼张嘴
    /// </summary>
    private void UpdateStretch()
    {
        float p = _complexActionPhase;
        float duration = STRETCH_DURATION;

        float t = Mathf.Clamp01(p / duration);

        // 0→1 (起) 然后保持到快结束
        float rise = Mathf.Clamp01(t * 3f);
        float hold = Mathf.Clamp01((1f - t) * 3f);
        float phase = Mathf.Min(rise, hold); // 梯形：快速起→保持→快速落

        // ★ 右臂全套抬升参数
        SetParameter("Param33", phase * 5f);   // 右手臂R1（上臂）0→5
        SetParameter("Param31", phase * 4f);   // 右手臂R1（前臂）0→4
        SetParameter("Param32", phase * 3f);   // 右手臂R2 0→3
        SetParameter("Param94", phase * 10f);  // 右手上臂旋转 0→10
        SetParameter("Param97", phase * 3f);   // 右手 基础上臂旋转 0→3
        SetParameter("Param95", phase * 0.8f); // 右手 基础 上壁透视 0→0.8
        SetParameter("Param117", phase * 0.5f);// 右手 基础 上壁透视2 0→0.5
        SetParameter("Param98", phase * 0.6f); // 右手 基础下壁透视 0→0.6
        SetParameter("Param100", phase * 0.6f);// 右手 基础手 透视 0→0.6
        SetParameter("Param116", phase * 0.4f);// 透视2 0→0.4
        SetParameter("Param120", phase * 0.8f);// 手 向前透视效果 0→0.8
        SetParameter("Param108", phase * 0.8f);// 右手 基础 图层顺序 0→0.8
        SetParameter("Param119", phase * 0.8f);// 伸手 图层调整 0→0.8
        SetParameter("Param93", phase);         // 右手 基础 切换 0→1
        SetParameter("Param118", phase * 0.6f); // 右手伸出参数 0→0.6
        // 左臂配合
        SetParameter("Param34", -phase * 3f);   // 左臂L1
        SetParameter("Param36", -phase * 2f);   // 左臂L2
        SetParameter("Param37", -phase * 1.5f); // 左臂L3

        // 身体后仰
        SetParameter("ParamBodyAngleX", phase * STRETCH_BODY_BACK);
        SetParameter("ParamBodyAngleZ", phase * 3f); // 身体微侧

        // 头略微后仰
        SetParameter("ParamAngleX", phase * (-8f));

        // 眯眼 + 张嘴（打哈欠表情）
        SetParameter("ParamEyeLOpen", 1f - phase * STRETCH_EYE_CLOSE);
        SetParameter("ParamEyeROpen", 1f - phase * STRETCH_EYE_CLOSE);
        SetParameter("ParamMouthForm", phase * STRETCH_MOUTH_OPEN);
        SetParameter("ParamBreath", phase * 0.5f); // 深吸一口气

        // 结束恢复
        if (t >= 1f) ResetIdleAction();
    }

    /// <summary>
    /// 动作6: 爱心眨眼 💕
    /// 爱心眼出现 + 歪头 + 微笑
    /// </summary>
    private void UpdateHeartEyes()
    {
        float p = _complexActionPhase;
        float duration = HEART_DURATION;

        float t = Mathf.Clamp01(p / duration);
        float eased = Mathf.Sin(t * Mathf.PI);

        // 爱心眼闪烁
        float heartPulse = (Mathf.Sin(p * 6f) + 1f) * 0.5f * eased;
        SetParameter("Param109", heartPulse);     // 爱心眼

        // 歪头 + 微笑
        float tiltPulse = Mathf.Sin(p * 2f) * eased;
        SetParameter("ParamAngleZ", tiltPulse * HEART_TILT);
        SetParameter("ParamEyeLSmile", eased * HEART_SMILE);
        SetParameter("ParamEyeRSmile", eased * HEART_SMILE);

        // 身体微倾
        SetParameter("ParamBodyAngleZ", tiltPulse * 4f);

        if (t >= 1f) ResetIdleAction();
    }

    /// <summary>
    /// 动作7: 数钱钱 💰
    /// 双眼放光 + 美滋滋微笑 + 身体微摇
    /// </summary>
    private void UpdateMoney()
    {
        float p = _complexActionPhase;
        float duration = MONEY_DURATION;

        float t = Mathf.Clamp01(p / duration);
        float eased = Mathf.Sin(t * Mathf.PI);

        // 数钱表情（Param122 = 钱）
        SetParameter("Param122", eased);

        // ★ 双眼放光（像法阵那样的眼镜发光效果）
        float glow = Mathf.Clamp01(eased * 1.1f);
        SetParameter("Param121", glow * 0.6f);  // 黑幕切换（半透明黑幕衬托发光）
        SetParameter("Param137", glow * 0.4f);  // 黑幕显示
        SetParameter("Param132", glow * 0.7f);  // 眼镜发光 ✨

        // 微笑（美滋滋）
        SetParameter("ParamEyeLSmile", eased * MONEY_SMILE);
        SetParameter("ParamEyeRSmile", eased * MONEY_SMILE);

        // 歪头卖萌
        SetParameter("ParamAngleZ", Mathf.Sin(p * 1.5f) * eased * MONEY_TILT);

        // 身体美滋滋地摇晃
        float sway = Mathf.Sin(p * 2f) * eased * MONEY_SWAY;
        SetParameter("ParamBodyAngleZ", sway);
        SetParameter("ParamBodyAngleX", sway * 0.3f);

        // 眼睛微微眯起（满足感）
        SetParameter("ParamEyeLOpen", 1f - eased * 0.15f);
        SetParameter("ParamEyeROpen", 1f - eased * 0.15f);

        if (t >= 1f) ResetIdleAction();
    }

    /// <summary>
    /// 动作8: 委屈 😢
    /// 泪眼汪汪 + 低头 + 八字眉 + 嘴巴微颤
    /// </summary>
    private void UpdateCry()
    {
        float p = _complexActionPhase;
        float duration = CRY_DURATION;

        float t = Mathf.Clamp01(p / duration);
        float eased = Mathf.Sin(t * Mathf.PI);

        // 泪眼表情（Param130 = 泪眼）
        float tear = Mathf.Sin(p * 4f) * eased;
        float tearFade = Mathf.Clamp01(tear);
        SetParameter("Param130", tearFade);

        // 低头（委屈低头）
        SetParameter("ParamAngleY", eased * CRY_HEAD_DOWN);

        // 嘴巴微颤（委屈时嘴巴会有点抖）
        float mouthTrem = Mathf.Sin(p * 6f) * eased * CRY_MOUTH_TREM;
        SetParameter("ParamMouthForm", Mathf.Abs(mouthTrem));

        // 八字眉（眉头抬起 = 委屈状）
        float browUp = eased * CRY_BROW_UP;
        SetParameter("ParamBrowRY", browUp);   // 右眉抬起
        SetParameter("ParamBrowLY", browUp);   // 左眉抬起

        // 眼睛微微睁大（泪眼汪汪）
        SetParameter("ParamEyeLOpen", 1f + eased * 0.15f);
        SetParameter("ParamEyeROpen", 1f + eased * 0.15f);

        // 身体微微抽动（抽泣感）
        float sob = Mathf.Sin(p * 3.5f) * eased * 1.5f;
        SetParameter("ParamBodyAngleX", sob);
        SetParameter("ParamBodyAngleZ", sob * 0.5f);

        if (t >= 1f) ResetIdleAction();
    }

    /// <summary>
    /// 动作10: 害羞黑脸 😊🖤
    /// 脸黑（Param101）+ 眼神躲闪 + 低头害羞微笑
    /// </summary>
    private void UpdateBlush()
    {
        float p = _complexActionPhase;
        float duration = BLUSH_DURATION;

        float t = Mathf.Clamp01(p / duration);
        float eased = Mathf.Sin(t * Mathf.PI);

        // 黑脸表情（Param101 = 黑脸）
        float darkPulse = (Mathf.Sin(p * 3f) + 1f) * 0.5f * eased;
        SetParameter("Param101", darkPulse * BLUSH_DARK);

        // 害羞低头 + 眼神躲闪
        SetParameter("ParamAngleY", eased * 4f);            // 微低头
        SetParameter("ParamAngleZ", Mathf.Sin(p * 1.5f) * eased * BLUSH_LOOK_AWAY); // 头扭开

        // 害羞微笑
        SetParameter("ParamEyeLSmile", eased * BLUSH_SMILE);
        SetParameter("ParamEyeRSmile", eased * BLUSH_SMILE);

        // 眼睛微微眯起
        SetParameter("ParamEyeLOpen", 1f - eased * 0.2f);
        SetParameter("ParamEyeROpen", 1f - eased * 0.2f);

        // 身体微侧（害羞扭捏）
        float sway = Mathf.Sin(p * 2.5f) * eased * 3f;
        SetParameter("ParamBodyAngleX", sway);
        SetParameter("ParamBodyAngleZ", sway * 0.5f);

        // 嘴巴微张（欲言又止）
        SetParameter("ParamMouthForm", eased * 0.3f);

        if (t >= 1f) ResetIdleAction();
    }

    /// <summary>
    /// 动作9: 法阵显现 ✨
    /// 五阶段：举过头顶→剑指成型→指尖凝光→白光扩散→消散
    /// </summary>
    private void UpdateMagicCircle()
    {
        float p = _complexActionPhase;
        float duration = CIRCLE_DURATION;
        float t = Mathf.Clamp01(p / duration);

        // ─── Phase 1: 举过头顶 (0→1.6s, 0%~20%) ───
        if (t < 0.20f)
        {
            float phase1 = t / 0.20f;

            // ★ 右臂抬起为剑指姿势：上臂右偏下、下臂左偏上
            SetParameter("Param33", phase1 * 12f);  // 右手臂R1（上臂）0→12
            SetParameter("Param31", phase1 * 6f);   // 右手臂R1（前臂）0→6
            SetParameter("Param32", phase1 * 8f);   // 右手臂R2 0→8
            SetParameter("Param94", phase1 * -6f);  // 右手上臂旋转 0→-6
            SetParameter("Param97", 0f);             // 右手 基础上臂旋转 0
            // ★ 透视校正——让手臂在衣服前
            SetParameter("Param95", phase1 * 1f);    // 右手 基础 上壁透视 0→1
            SetParameter("Param117", phase1 * 0.8f); // 右手 基础 上壁透视2 0→0.8
            SetParameter("Param98", phase1 * 0.8f);  // 右手 基础下壁透视 0→0.8
            SetParameter("Param100", phase1 * 0.8f); // 右手 基础手 透视 0→0.8
            SetParameter("Param116", phase1 * 0.6f); // 透视2 0→0.6
            SetParameter("Param120", phase1 * 1f);   // 手 向前透视效果 0→1
            // ★ 图层顺序——手放到衣服前面！
            SetParameter("Param108", phase1 * 1f);   // 右手 基础 图层顺序 0→1
            SetParameter("Param119", phase1 * 1f);   // 伸手 图层调整 0→1
            // 左臂配合
            SetParameter("Param34", -phase1 * 6f);   // 左臂L1 0→-6
            SetParameter("Param36", -phase1 * 4.5f); // 左臂L2 0→-4.5
            SetParameter("Param37", -phase1 * 3f); // 左臂L3 0→-3
            
            // ★ 右手基础开关 + 伸出
            SetParameter("Param93", phase1);          // 右手 基础 切换 0→1
            SetParameter("Param118", phase1 * 0.8f);  // 右手伸出参数 0→0.8
            SetParameter("Param92", 0f);              // 切换→基础模式（非手指）
            
            // 身体后仰 + 抬头
            SetParameter("ParamBodyAngleX", phase1 * -8f);  // 身体前后倾 0→-8
            SetParameter("ParamAngleX", phase1 * -10f);     // 头部上下 0→-10
            SetParameter("ParamBodyAngleZ", 0);            // 身体侧转 0
            
            // 法阵特效
            SetParameter("Param132", phase1 * 0.8f);   // 法眼发光 0→0.8
            SetParameter("Param154", phase1 * 0.8f);   // 七星盘透明度 0→0.8
            SetParameter("Param133", 0);             // 白圈大小 0
            SetParameter("Param136", 0);             // 白圈透明度 0
            SetParameter("Param134", 0);             // 白圈偏移X 0
            SetParameter("Param135", 0);             // 白圈偏移Y 0
        }
        // ─── Phase 2: 剑指成型 (1.6→3.6s, 20%~45%) ───
        else if (t < 0.45f)
        {
            float phase2 = (t - 0.20f) / 0.25f;

            // ★ 右臂保持剑指姿势：上臂右偏下、下臂左偏上
            SetParameter("Param33", 12f);     // 右手臂R1（上臂）保持
            SetParameter("Param31", 6f);      // 右手臂R1（前臂）保持
            SetParameter("Param32", 8f);      // 右手臂R2 保持
            SetParameter("Param94", -6f);     // 右手上臂旋转 -6
            SetParameter("Param97", 0f);      // 右手 基础上臂旋转 0
            // ★ 透视校正保持
            SetParameter("Param95", 1f);      // 右手 基础 上壁透视
            SetParameter("Param117", 0.8f);   // 右手 基础 上壁透视2
            SetParameter("Param98", 0.8f);    // 右手 基础下壁透视
            SetParameter("Param100", 0.8f);   // 右手 基础手 透视
            SetParameter("Param116", 0.6f);   // 透视2
            SetParameter("Param120", 1f);     // 手 向前透视效果
            // ★ 图层顺序——手放到衣服前面！
            SetParameter("Param108", 1f);     // 右手 基础 图层顺序
            SetParameter("Param119", 1f);     // 伸手 图层调整
            // 左臂保持
            SetParameter("Param34", -6f);     // 左臂L1 保持
            SetParameter("Param36", -4.5f);   // 左臂L2 保持
            SetParameter("Param37", -3f);   // 左臂L3 保持
            
            // ★ 右手保持开启 + 伸出
            SetParameter("Param93", 1f);          // 右手 基础 切换 ON
            SetParameter("Param118", 0.8f);        // 右手伸出
            
            // ★ 剑指手势：食指(2)+中指(3)伸出，拇指(1)/无名指(4)/小指(5)收起
            SetParameter("Param92", phase2);       // 右手切换→手指模式 0→1
            SetParameter("Param111", 0.2f);        // 右手 指 手指1(拇指) 收
            SetParameter("Param112", 1f);           // 右手 指 手指2(食指) 伸
            SetParameter("Param113", 1f);           // 右手 指 手指3(中指) 伸
            SetParameter("Param114", 0.2f);        // 右手 指 手指4(无名指) 收
            SetParameter("Param115", 0.2f);        // 右手 指 手指5(小指) 收
            
            // ★ 手腕微调（调整让手指指天）
            SetParameter("Param110", -0.5f);        // 右手 指 Z 微旋（反向调整）
            SetParameter("Param99", 0.3f);         // 右手 基础手腕Z 微调（反向调整）
            
            // 身体保持姿态
            SetParameter("ParamBodyAngleX", -8f);  // 身体前后倾 -8
            SetParameter("ParamAngleX", -10f);     // 头部上下 -10
            SetParameter("ParamBodyAngleZ", 0);     // 身体侧转 0
            
            // 法阵特效（保持 + 脉动）
            SetParameter("Param132", 0.8f);   // 法眼发光 0.8
            float platePulse = (Mathf.Sin(p * 3.5f) * 0.5f + 0.5f) * 0.8f;
            SetParameter("Param154", platePulse);   // 七星盘脉动
            SetParameter("Param133", 0);         // 白圈大小 0
            SetParameter("Param136", 0);         // 白圈透明度 0
            SetParameter("Param134", 0);         // 白圈偏移X 0
            SetParameter("Param135", 0);         // 白圈偏移Y 0
        }
        // ─── Phase 3: 指尖凝光 (3.0→4.0s, 38%~50%) ───
        else if (t < 0.50f)
        {
            float phase3 = (t - 0.38f) / 0.12f;

            // ★ 右臂保持剑指姿势：上臂右偏下、下臂左偏上
            SetParameter("Param33", 12f);     // 右手臂R1（上臂）保持
            SetParameter("Param31", 6f);      // 右手臂R1（前臂）保持
            SetParameter("Param32", 8f);      // 右手臂R2 保持
            SetParameter("Param94", -6f);     // 右手上臂旋转 -6
            SetParameter("Param97", 0f);      // 右手 基础上臂旋转 0
            // ★ 透视校正保持
            SetParameter("Param95", 1f);      // 右手 基础 上壁透视
            SetParameter("Param117", 0.8f);   // 右手 基础 上壁透视2
            SetParameter("Param98", 0.8f);    // 右手 基础下壁透视
            SetParameter("Param100", 0.8f);   // 右手 基础手 透视
            SetParameter("Param116", 0.6f);   // 透视2
            SetParameter("Param120", 1f);     // 手 向前透视效果
            // ★ 图层顺序——手放到衣服前面！
            SetParameter("Param108", 1f);     // 右手 基础 图层顺序
            SetParameter("Param119", 1f);     // 伸手 图层调整
            // 左臂保持
            SetParameter("Param34", -6f);     // 左臂L1 保持
            SetParameter("Param36", -4.5f);   // 左臂L2 保持
            SetParameter("Param37", -3f);   // 左臂L3 保持
            
            // ★ 右手剑指保持
            SetParameter("Param93", 1f);          // 右手 ON
            SetParameter("Param118", 0.8f);        // 右手伸出
            SetParameter("Param92", 1f);           // 右手→手指模式
            SetParameter("Param111", 0.2f);        // 拇指 收
            SetParameter("Param112", 1f);           // 食指 伸
            SetParameter("Param113", 1f);           // 中指 伸
            SetParameter("Param114", 0.2f);        // 无名指 收
            SetParameter("Param115", 0.2f);        // 小指 收
            SetParameter("Param110", -0.5f);        // 指Z（调整让手指指天）
            
            // 身体保持
            SetParameter("ParamBodyAngleX", -8f);
            SetParameter("ParamAngleX", -10f);
            SetParameter("ParamBodyAngleZ", 0);
            
            // 白点凝聚
            SetParameter("Param133", phase3 * 5f);       // 白圈大小 0→5
            SetParameter("Param136", phase3);           // 白圈透明度 0→1
            SetParameter("Param134", 0);              // 白圈偏移X 0
            SetParameter("Param135", 0);              // 白圈偏移Y 0
            
            // 七星盘隐退
            SetParameter("Param132", 0.8f - phase3 * 0.56f); // 法眼 0.8→0.24
            SetParameter("Param154", 0.8f - phase3 * 0.56f); // 七星盘 0.8→0.24
        }
        // ─── Phase 4: 白光扩散 (3.8→6.0s, 48%~75%) ───
        else if (t < 0.75f)
        {
            float phase4 = (t - 0.48f) / 0.27f;

            // ★ 手臂被能量震回
            float rightFade = 1f - phase4 * 0.5f; // 1→0.5
            SetParameter("Param33", 12f * rightFade);      // 右手臂R1（上臂）12→6
            SetParameter("Param31", 6f * rightFade);      // 右手臂R1（前臂）6→3
            SetParameter("Param32", 8f * rightFade);      // 右手臂R2 8→4
            SetParameter("Param94", -6f * rightFade);     // 右手上臂旋转 -6→-3
            SetParameter("Param97", 0f);                  // 右手 基础上臂旋转 0
            SetParameter("Param95", 1f * rightFade);     // 右手 基础 上壁透视 1→0.5
            SetParameter("Param117", 0.8f * rightFade);  // 右手 基础 上壁透视2 0.8→0.4
            SetParameter("Param98", 0.8f * rightFade);   // 右手 基础下壁透视 0.8→0.4
            SetParameter("Param100", 0.8f * rightFade);  // 右手 基础手 透视 0.8→0.4
            SetParameter("Param116", 0.6f * rightFade);  // 透视2 0.6→0.3
            SetParameter("Param120", 1f * rightFade);    // 手 向前透视效果 1→0.5
            SetParameter("Param108", 1f * rightFade);    // 右手 基础 图层顺序 1→0.5
            SetParameter("Param119", 1f * rightFade);    // 伸手 图层调整 1→0.5
            float leftFade = -6f + phase4 * 2.5f;
            SetParameter("Param34", leftFade);     // 左臂L1 -6→-3.5
            SetParameter("Param36", -4.5f + phase4 * 2f);  // 左臂L2 -4.5→-2.5
            SetParameter("Param37", -3f + phase4 * 1.5f);    // 左臂L3 -3→-1.5
            
            // ★ 剑指收回 + 手渐收
            float handOut = 1f - phase4 * 0.5f;  // 1→0.5
            SetParameter("Param93", handOut);     // 右手切换 渐关
            SetParameter("Param118", 0.8f - phase4 * 0.4f); // 右手伸出 0.8→0.4
            SetParameter("Param92", 1f - phase4); // 手指模式 1→0
            SetParameter("Param111", 0.2f);
            SetParameter("Param112", 1f - phase4 * 0.8f);  // 食指 1→0.2
            SetParameter("Param113", 1f - phase4 * 0.8f);  // 中指 1→0.2
            SetParameter("Param114", 0.2f);
            SetParameter("Param115", 0.2f);
            SetParameter("Param110", -0.5f + phase4 * 0.5f); // 指Z回正（从-0.5→0）
            
            // 身体继续保持
            SetParameter("ParamBodyAngleX", -8f);
            SetParameter("ParamAngleX", -10f);
            SetParameter("ParamAngleZ", 0);
            
            // 白圈扩散到全屏
            SetParameter("Param133", 5f + phase4 * 55f);     // 白圈大小 5→60
            SetParameter("Param136", 1f - phase4);               // 白圈透明度 1→0
            SetParameter("Param134", 0);
            SetParameter("Param135", 0);
            
            // 人物缩小 + 镜头上移
            SetParameter("Param157", -phase4 * 0.15f);         // 人物缩放 0→-0.15
            SetParameter("Param156", -phase4 * 3f);             // 镜头Y偏移 0→-3
            
            // 法眼隐去
            SetParameter("Param132", 0.24f - phase4 * 0.24f); // 法眼 0.24→0
            SetParameter("Param154", 0.24f - phase4 * 0.24f); // 七星盘 0.24→0
        }
        // ─── Phase 5: 消散 (6.0→8.0s, 75%~100%) ───
        else
        {
            float phase5 = (t - 0.75f) / 0.25f;
            float fade = 1f - phase5;

            // ★ 双手缓缓放下
            SetParameter("Param33", fade * 12f);    // 右手臂R1（上臂）12→0
            SetParameter("Param31", fade * 6f);     // 右手臂R1（前臂）6→0
            SetParameter("Param32", fade * 8f);     // 右手臂R2 8→0
            SetParameter("Param94", fade * -6f);    // 右手上臂旋转 -6→0
            SetParameter("Param97", 0f);            // 右手 基础上臂旋转 0
            SetParameter("Param95", fade * 1f);     // 右手 基础 上壁透视 1→0
            SetParameter("Param117", fade * 0.8f);  // 右手 基础 上壁透视2 0.8→0
            SetParameter("Param98", fade * 0.8f);   // 右手 基础下壁透视 0.8→0
            SetParameter("Param100", fade * 0.8f);  // 右手 基础手 透视 0.8→0
            SetParameter("Param116", fade * 0.6f);  // 透视2 0.6→0
            SetParameter("Param120", fade * 1f);    // 手 向前透视效果 1→0
            SetParameter("Param108", fade * 1f);    // 右手 基础 图层顺序 1→0
            SetParameter("Param119", fade * 1f);    // 伸手 图层调整 1→0
            SetParameter("Param34", fade * -6f);    // 左臂L1 -6→0
            SetParameter("Param36", fade * -4.5f);  // 左臂L2 -4.5→0
            SetParameter("Param37", fade * -3f);  // 左臂L3 -3→0
            
            // ★ 右手完全收回
            SetParameter("Param93", fade * 0.5f);   // 右手切换 渐灭
            SetParameter("Param118", fade * 0.4f);  // 右手伸出 渐收
            SetParameter("Param92", 0f);             // 手指模式 OFF
            SetParameter("Param111", 0f);
            SetParameter("Param112", 0f);
            SetParameter("Param113", 0f);
            SetParameter("Param114", 0f);
            SetParameter("Param115", 0f);
            SetParameter("Param110", 0f);            // 指Z回正
            SetParameter("Param99", 0f);             // 手腕Z回正
            
            // 身体回正
            SetParameter("ParamBodyAngleX", fade * -8f);    // -8→0
            SetParameter("ParamAngleX", fade * -10f);      // -10→0
            SetParameter("ParamBodyAngleZ", 0);
            
            // 白光消退
            SetParameter("Param133", fade * 60f * 0.6f); // 60→36
            SetParameter("Param136", fade * 0.6f);       // 0→0.6
            
            // 人物恢复正常
            SetParameter("Param157", fade * -0.15f);     // -0.15→0
            SetParameter("Param156", fade * -3f);          // -3→0
            SetParameter("Param134", 0);
            SetParameter("Param135", 0);
            
            // 法眼余光消散
            SetParameter("Param132", 0);
            SetParameter("Param154", 0);
        }

        if (t >= 1f) ResetIdleAction();
    }

    /// <summary>重置空闲动作，清理参数</summary>
    private void ResetIdleAction()
    {
        bool wasLocked = _actionLocked;
        _actionLocked = false;

        _currentIdleAction = 0;
        _idleActionTime = 0f;
        _complexActionPhase = 0f;

        // 清理可能被改过的参数（表情/特殊参数）
        SetParameter("ParamAngleZ", 0f);
        SetParameter("ParamEyeLSmile", 0f);
        SetParameter("ParamEyeRSmile", 0f);
        SetParameter("ParamMouthForm", 0f);
        SetParameter("ParamBrowRY", 0f);
        SetParameter("ParamBrowLY", 0f);
        SetParameter("ParamEyeLOpen", 1f);
        SetParameter("ParamEyeROpen", 1f);
        SetParameter("Param109", 0f); // 爱心眼
        SetParameter("Param451", 0f); // 星显隐
        SetParameter("Param541", 0f); // 星大小
        SetParameter("Param1071", 0f); // 外围星变大
        SetParameter("Param1081", 0f); // 外星出现
        SetParameter("Param411", 0f); // 中蒙版(星辉)
        SetParameter("Param891", 0f); // 内蒙版(星辉)
        SetParameter("Param431", 0f); // 外紫环显隐
        SetParameter("Param911", 0f); // 中紫环显隐
        SetParameter("Param741", 0f); // 内紫环显隐
        SetParameter("Param441", 0f); // 外紫环大小
        SetParameter("Param961", 0f); // 中紫环大小
        SetParameter("Param731", 0f); // 内紫环大小
        SetParameter("Param421", 0f); // 外紫环转(Group20)
        SetParameter("Param422", 0f); // 外紫环转(Group21)
        SetParameter("Param901", 0f); // 中紫环转(Group20)
        SetParameter("Param902", 0f); // 中紫环转(Group21)
        SetParameter("Param881", 0f); // 内紫环转(Group20)
        SetParameter("Param882", 0f); // 内紫环转(Group21)
        SetParameter("Param94", 0f);  // 右手上臂旋转
        SetParameter("Param97", 0f);  // 右手 基础上臂旋转
        SetParameter("Param95", 0f);  // 右手 基础 上壁透视
        SetParameter("Param117", 0f); // 右手 基础 上壁透视2
        SetParameter("Param98", 0f);  // 右手 基础下壁透视
        SetParameter("Param100", 0f); // 右手 基础手 透视
        SetParameter("Param116", 0f); // 透视2
        SetParameter("Param120", 0f); // 手 向前透视效果
        SetParameter("Param108", 0f); // 右手 基础 图层顺序
        SetParameter("Param119", 0f); // 伸手 图层调整
        SetParameter("Param33", 0f);  // 右手臂R1（上臂）
        SetParameter("Param31", 0f);  // 右手臂R1（前臂）
        SetParameter("Param32", 0f);  // 右手臂R2
        SetParameter("Param93", 0f);  // 右手基础切换
        SetParameter("Param34", 0f);  // 左手手臂L1
        SetParameter("Param36", 0f);  // 左手手臂L2
        SetParameter("Param37", 0f);  // 左手手臂L3
        SetParameter("Param401", 0f); // 外蒙版
        SetParameter("Param104", 0f); // 生气
        SetParameter("Param122", 0f); // 钱（数钱）
        SetParameter("Param130", 0f); // 泪眼（委屈）
        SetParameter("Param101", 0f); // 黑脸（害羞）
        SetParameter("Param92", 0f);  // 右手切换→手指模式 OFF
        SetParameter("Param118", 0f); // 右手伸出参数
        SetParameter("Param99", 0f);  // 手腕Z
        SetParameter("Param110", 0f); // 手指Z旋转
        SetParameter("Param111", 0f); // 手指1(拇指)
        SetParameter("Param112", 0f); // 手指2(食指)
        SetParameter("Param113", 0f); // 手指3(中指)
        SetParameter("Param114", 0f); // 手指4(无名指)
        SetParameter("Param115", 0f); // 手指5(小指)
        // 法阵参数
        SetParameter("Param121", 0f); // 黑幕切换
        SetParameter("Param137", 0f); // 黑幕透明显现
        SetParameter("Param154", 0f); // 七星盘透明
        SetParameter("Param133", 0f); // 白圈大小
        SetParameter("Param136", 0f); // 白圈不透明度
        SetParameter("Param134", 0f); // 白圈位移X
        SetParameter("Param135", 0f); // 白圈位移Y
        SetParameter("Param132", 0f); // 眼镜发光
        SetParameter("Param155", 0f); // 镜头X
        SetParameter("Param156", 0f); // 镜头Y
        SetParameter("Param157", 0f); // 人物缩小放大

        if (wasLocked)
        {
            Debug.Log("[Live2DRenderer] 强制动作完成，触发回调");
            OnForcedActionFinished?.Invoke();
        }
    }

    #endregion

    /// <summary>
    /// 强制播放指定空闲动作（被右键菜单调用）
    /// </summary>
    public void ForceIdleAction(int actionId)
    {
        if (!_loaded || _cubismModel == null) return;
        _actionLocked = true;
        _currentIdleAction = actionId;
        _idleActionTime = 0f;
        _complexActionPhase = 0f;
        Debug.Log($"[Live2DRenderer] ▶ 强制动作 #{actionId}（锁定，不被走路覆盖）");
    }

    /// <summary>
    /// 按权重随机选取一个空闲动作（1-10）
    /// </summary>
    private int PickWeightedIdleAction()
    {
        int totalWeight = 0;
        for (int i = 0; i < _idleActionWeights.Length; i++)
            totalWeight += _idleActionWeights[i];

        int roll = Random.Range(0, totalWeight);
        int cumulative = 0;
        for (int i = 0; i < _idleActionWeights.Length; i++)
        {
            cumulative += _idleActionWeights[i];
            if (roll < cumulative)
                return i + 1; // 动作编号从 1 开始
        }
        return 1; // fallback
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

        // ★ 空闲→走路淡入：体态（转体/前倾/低头）渐入，腿臂立刻动
        float bodyWeight = 1f;
        if (_walkFadeInRemaining > 0f)
        {
            float raw = 1f - Mathf.Clamp01(_walkFadeInRemaining / WALK_FADE_IN_DURATION);
            bodyWeight = raw * raw; // 二次缓入
            _walkFadeInRemaining -= Time.deltaTime;
        }

        // 走路的体态（转体 + 前倾 + 低头 + 呼吸）
        ApplyWalkBodyPose(bodyWeight);

        // ★ 腿/臂动态摆动 — 停止后渐消不需要这些
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
        //   左臂与右腿同步（右腿前→左手前），与右臂反相
        SetParameter("Param94", legPhase * WALK_ARM_SWING);       // 右臂 (Param94=右手上臂旋转)
        float leftArm = rightPhase * WALK_ARM_SWING;
        SetParameter("Param34", leftArm);    // 左臂L1
        SetParameter("Param36", leftArm * 0.7f);  // 左臂L2
        SetParameter("Param37", leftArm * 0.5f);  // 左臂L3

        // 肩膀配合脚步
        SetParameter("Param153", Mathf.Abs(legPhase) * WALK_SHOULDER);
    }

    /// <summary>
    /// 走路的体态姿势（转体 + 前倾 + 低头 + 呼吸加深）
    /// 用 weight 控制消退：1=全走路态，0=完全消失
    /// </summary>
    private void ApplyWalkBodyPose(float weight)
    {
        if (weight <= 0f) return;

        float phase = _walkPhase;

        // 身体转体侧面
        float bodyYaw = (WALK_SIDE_ANGLE + Mathf.Sin(phase) * 3f) * weight;
        SetParameter("ParamBodyAngleY", bodyYaw);

        // 身体前倾
        SetParameter("ParamBodyAngleX", WALK_BODY_LEAN * weight);

        // 身体左右横摆（驱动衣服物理）
        float bodySwing = Mathf.Sin(phase) * WALK_BODY_SWING * weight;
        SetParameter("ParamBodyAngleZ", bodySwing);

        // 脸转向侧面（与身体方向一致）
        SetParameter("ParamAngleX", WALK_SIDE_ANGLE * weight);

        // 头微低看路
        SetParameter("ParamAngleY", WALK_HEAD_TILT * weight);

        // 呼吸加深
        SetParameter("ParamBreath", (WALK_BREATH + Mathf.Sin(phase) * 0.5f) * weight);
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
