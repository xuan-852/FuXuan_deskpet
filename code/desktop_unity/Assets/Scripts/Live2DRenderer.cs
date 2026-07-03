using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Physics;
using Live2D.Cubism.Framework.Raycasting;
using Live2D.Cubism.Rendering;
using Live2D.Cubism.Framework.Json;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
[DefaultExecutionOrder(801)]   // CubismPhysicsController=800，我们跑在物理之后
public class Live2DRenderer : MonoBehaviour, IPetRenderer
{
    // ===================================================================
    // ===== 🎛️ 调参区 — 改这里 =====
    // ===================================================================
    const float LIVE2D_SCALE       = 56.2f; // 模型缩放（越大→模型越大）
    const float LIVE2D_OFFSET_Y    = 0f;      // 垂直偏移（正数=下移，负数=上移）
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

    // 动作11: 困惑 🤔（歪头+皱眉+眯眼，只在 AI 困惑时触发）
    const float CONFUSE_DURATION    = 3f;
    const float CONFUSE_TILT        = 15f;    // 歪头幅度
    const float CONFUSE_BROW        = -3f;    // 皱眉（负=压低）
    const float CONFUSE_EYE_SQUINT  = 0.15f;  // 眯眼幅度
    const float CONFUSE_MOUTH       = 0.2f;   // 微微张嘴
    const float CONFUSE_HEAD_SIDE   = -5f;    // 头侧偏
    const float CONFUSE_BODY_SIDE   = 3f;     // 身体微侧

    // 动作9: 法阵显现 ✨（起势→剑指朝天结印→指尖凝光→扩散至全屏→消散）
    const float CIRCLE_DURATION       = 8.0f;
    // -- 法阵斜飞物理飘动 --
    const float CIRCLE_FLOAT_AMPLITUDE_BODY = 4f;    // 身体X轴漂移幅度 ±4°
    const float CIRCLE_FLOAT_AMPLITUDE_HEAD = 3f;    // 头部X轴漂移幅度 ±3°
    const float CIRCLE_FLOAT_SPEED         = 0.4f;   // Perlin 噪声速度
    const float CIRCLE_SPRING_STIFF        = 8f;     // 弹簧刚度（越大回正越快）
    const float CIRCLE_SPRING_DAMP_BOUNCE  = 3f;     // 弹性惯性模式阻尼（欠阻尼=有回弹）
    const float CIRCLE_SPRING_DAMP_SOFT    = 9f;     // 柔软跟随模式阻尼（临界阻尼=无过冲）

    // ===================================================================
    // ⭐9 法阵姿态参数 — 改这里调角度和渐入速度
    // ===================================================================
    // ★ 身体/头部角度给 Live2D 物理输入，带动头发/衣服自然摆动。
    const float CIRCLE_BODY_ANGLE_X    = -8f;     // 身体后仰角度（负=后仰）
    const float CIRCLE_HEAD_ANGLE_X    = -10f;    // 头部低垂角度（负=低头）
    const float CIRCLE_ANGLE_RAMP_DUR  = 1.0f;    // 角度渐入时长(秒)，越大头发/衣服越平缓
    // ★ 弹簧速度 → 头发/辫子/饰品/衣服（像拖拽回复一样，身体/头部飘动时自然甩动）
    const float CIRCLE_HAIR_SMOOTH      = 0.35f;  // SmoothDamp 平滑时间（越大越缓，防速度尖峰突跳）
    const float CIRCLE_HEAD_HAIR_FROM_VEL = 0.25f; // 头部头发（刘海/头发物理2/鬓发/后短发）— 10%原幅
    const float CIRCLE_HEAD_HAIR_MAX      = 0.6f;  // 头部头发最大摆动
    const float CIRCLE_BRAID_FROM_VEL   = 1.0f;   // 辫子（后发a/b/c/d）— 40%原幅
    const float CIRCLE_BRAID_MAX         = 2.4f;   // 辫子最大摆动
    const float CIRCLE_ORNAMENT_FROM_VEL = 1.0f;   // 饰品（发簪/头饰）— 40%原幅
    const float CIRCLE_ORNAMENT_MAX      = 2.4f;   // 饰品最大摆动
    const float CIRCLE_HEAD_ORNAMENT_FROM_VEL = 0.6f; // Param169 头饰 40%原幅
    const float CIRCLE_HEAD_ORNAMENT_MAX      = 1.6f;
    const float CIRCLE_CLOTH_FROM_VEL   = 2.0f;    // camX 速度→衣服倍数（原始不变）
    const float CIRCLE_CLOTH_MAX         = 8.0f;    // 原始不变
    // ===================================================================

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
    const float WALK_ARM_BIG      = 2f;     // 手臂大范围参数 (Param94, 范围[-30,60])
    const float WALK_ARM_SMALL    = 0.4f;   // 手臂小范围参数 (Param31~37, 范围[-1,1])
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

    // -- 点击区域（按 hitNormY 区分：0=头顶，1=脚底）--
    // 头部区 (0~0.35): 摸头 — 眯眼歪头
    // 身体区 (0.35~0.65): 戳身体 — 睁大眼张嘴惊讶
    const float POKE_EYE_OPEN      = 1.3f;   // 戳身体眼睛睁大
    const float POKE_MOUTH_OPEN    = 0.8f;   // 戳身体张嘴惊讶
    const float POKE_MOUTH_FORM    = 0.5f;   // 戳嘴巴型
    const float POKE_BROW_RAISE    = 10f;    // 戳眉毛抬起
    // 腿部区 (0.65~1.0): 碰腿 — 害羞开心（复用 BLUSH 参数）
    const float LEG_HIT_ANGLE_Z    = -6f;    // 碰腿歪头
    const float LEG_HIT_SMILE      = 0.6f;   // 碰腿微笑
    const float LEG_HIT_EYE_CLOSE  = 0.3f;   // 碰腿眯眼

    // -- 屏幕边缘碰撞反弹 --
    const float WALL_HIT_DURATION    = 0.5f;  // 反弹动画持续秒数

    // ===================================================================
    // ⭐4 拖拽挣扎参数 — 改这里调手/脚/头的幅度和频率
    // ===================================================================
    // -- 双臂 --
    const float DRAG_ARM_FREQ         = 4.5f;  // 摆臂频率（越大越急促）
    const float DRAG_RIGHT_AMP        = 3f;   // 右臂摆动幅度 (Param94 主驱动)
    const float DRAG_LEFT_AMP         = 0.1f;   // 左臂摆动幅度
    const float DRAG_JITTER1_FREQ     = 2f;  // 抖动1 频率
    const float DRAG_JITTER1_AMP      = 0.2f; // 抖动1 幅度（占幅度比例）
    const float DRAG_JITTER2_FREQ     = 1f; // 抖动2 频率
    const float DRAG_JITTER2_AMP      = 0.4f; // 抖动2 幅度
    // 右臂关节目录系数（乘以 rightBase，越大该关节动得越明显）
    const float DRAG_RPARAM94         = 1f;  // 右上臂旋转
    const float DRAG_RPARAM97         = 0.2f;  // 基础上臂旋转
    const float DRAG_RPARAM31         = 0.25f;  // 前臂
    const float DRAG_RPARAM32         = 0.1f;  // R2
    const float DRAG_RPARAM33         = 0.2f;  // 上臂
    const float DRAG_RPARAM93         = 0f;  // 手形切换（设为 0=常开，1=随幅度变化）
    const float DRAG_RPARAM118        = 0.6f;  // 右手伸出
    // 右臂透视图层系数（0=隐藏，1=全浮出）
    const float DRAG_LAYER95          = 0.8f;
    const float DRAG_LAYER117         = 0.5f;
    const float DRAG_LAYER98          = 0.6f;
    const float DRAG_LAYER100         = 0.6f;
    const float DRAG_LAYER116         = 0.4f;
    const float DRAG_LAYER120         = 0.8f;
    const float DRAG_LAYER108         = 0.8f;
    const float DRAG_LAYER119         = 0.8f;
    // 左臂关节目录系数（乘以 leftBase，负号自行在方法中用）
    const float DRAG_LPARAM34         = 0.1f;  // 左臂L1
    const float DRAG_LPARAM36         = 0.1f;  // 左臂L2
    const float DRAG_LPARAM37         = 0.1f;  // 左臂L3

    // -- 双腿 --
    const float DRAG_LEG_FREQ         = 5.0f;  // 踏步频率
    const float DRAG_LEG_SWING        = 12f;   // 腿前后摆幅 (Param126/129)
    const float DRAG_LEG_BEND         = 6f;    // 腿弯曲幅度 (Param127/131)
    const float DRAG_LEG_LIFT         = 8f;    // 抬腿幅度 (Param165/164)

    // -- 身体/头部（鼠标速度驱动 → 物理自然推导头发/法盘/裙子）--
    const float DRAG_TURN_ANGLE       = 10f;   // 拖拽转身角度 (ParamBodyAngleY, +朝右转)
    const float DRAG_TURN_SMOOTH      = 0.1f;  // 转身平滑速度（越大反应越快）
    const float DRAG_BODY_SWAY        = 5f;    // 身体左右扭动幅度 (ParamBodyAngleX)
    const float DRAG_BODY_FREQ        = 2.0f;  // 身体扭动频率
    // -- 速度→输入参数 + 直接驱动裙子/法盘（全部同方向，参考走路物理方向）--
    const float DRAG_VEL_LERP       = 0.01f;  // 速度平滑（越小越滑）
    const float DRAG_VEL_MAX        = 3f;      // 原始速度上限（防瞬冲，越大响应越快）
    const float DRAG_BODY_Z_SCALE   = 3f;     // 速度→ParamBodyAngleZ（给物理）
    const float DRAG_BODY_Z_MAX     = 12f;
    const float DRAG_DIRECT_SCALE   = 4f;     // 速度→直接驱动 Param82/87/84/49/51/57/60
    const float DRAG_DIRECT_MAX     = 35f;    // 直接驱动最大值
    const float DRAG_HEAD_X_SCALE   = 1.8f;   // 速度→ParamAngleX
    const float DRAG_HEAD_X_MAX     = 22f;
    const float DRAG_HEAD_Z_SCALE   = 1.2f;   // 速度→ParamAngleZ
    const float DRAG_HEAD_Z_MAX     = 16f;
    // -- 头发直接驱动参数（物理 Delay 太高，直接接管）--
    const float DRAG_HAIR_SCALE     = 0.8f;   // 速度→头发（相对于 d 的比例）
    const float DRAG_HAIR_MAX       = 20f;    // 头发驱动最大值
    const float DRAG_HAIR169_SCALE  = 0.6f;   // Param169 饰品头饰驱动
    const float DRAG_HAIR169_MAX    = 15f;
    const float DRAG_HEAD_SHAKE       = 5f;    // 头部左右摆动幅度 (ParamAngleX)
    const float DRAG_HEAD_SHAKE_FREQ  = 3.5f;  // 头部摆动频率
    const float DRAG_HEAD_TILT        = -2f;   // 头部后仰基准 (ParamAngleY，正=抬头)
    const float DRAG_HEAD_BOB         = 1f;    // 头部上下抖动幅度
    const float DRAG_HEAD_BOB_FREQ    = 2.0f;  // 头部上下抖动频率

    // -- 表情 --
    const float DRAG_EYE_OPEN         = 1.1f;  // 眼睛睁开幅度（1=正常，>1=睁大）
    const float DRAG_MOUTH_AMP        = 0.5f;  // 嘴巴张开基值
    const float DRAG_MOUTH_PULSE      = 0.3f;  // 嘴巴随呼吸波动幅度
    const float DRAG_MOUTH_FREQ       = 5.0f;  // 嘴巴波动频率
    const float DRAG_MOUTH_PHASE      = 1.0f;  // 嘴波动相位偏移
    const float DRAG_BROW             = 1.2f;  // 眉毛抬起幅度（>1=高抬）
    // ===================================================================
    const float WALL_HIT_EYE_OPEN    = 1.3f;  // 瞪眼幅度
    const float WALL_HIT_MOUTH_OPEN  = 0.5f;  // 张嘴幅度
    const float WALL_HIT_BODY_LEAN   = 8f;    // 身体后仰幅度

    // -- 鼠标跟随眼睛 --
    const float EYE_FOLLOW_DISTANCE  = 150f;  // 鼠标在此距离内触发眼睛跟随（像素）
    const float EYE_FOLLOW_MAX_X     = 10f;   // 眼珠最大水平偏移
    const float EYE_FOLLOW_MAX_Y     = 8f;    // 眼珠最大垂直偏移
    // ==================================================
    [Header("模型 Prefab")]
    [Tooltip("Cubism SDK 导入后生成的模型 Prefab（不拖也行，代码自动按路径加载）")]
    public GameObject modelPrefab;

    [Header("显示设置（改顶部 LIVE2D_SCALE / LIVE2D_OFFSET_Y 宏）")]
    [Tooltip("模型缩放")]
    public float modelScale = 56.2f;

    [Tooltip("模型垂直偏移（像素）")]
    public float verticalOffset = LIVE2D_OFFSET_Y;

    // Cubism 组件
    private GameObject _modelRoot;
    private CubismModel _cubismModel;
    private CubismPhysicsController _physicsController;
    private CubismParameterStore _paramStore;
    private CubismPhysicsSubRig _leftArmSubRig;

    // DesktopPet 引用
    private DesktopPet _pet;
    private DragHandler _dragHandler;
    private ChatBubble _chatBubble;
    private TimeWeatherController _timeController;
    private IdleChatGenerator _idleGen;

    // ===== 模型置顶（RenderTexture 叠层） =====
    private const int OVERLAY_LAYER = 31;            // 专用层
    private Camera _overlayCamera;
    private RenderTexture _overlayRT;
    private bool _overlayReady = false;
    private int _overlayScreenW = 0;
    private int _overlayScreenH = 0;

    // ===== 射线触摸检测 =====
    private CubismRaycaster _cubismRaycaster;
    private readonly CubismRaycastHit[] _raycastResults = new CubismRaycastHit[32];

    /// <summary>身体部位枚举</summary>
    public enum BodyPart
    {
        Head,       // 头
        Body,       // 身体/躯干
        Arm,        // 手臂
        Leg,        // 腿/脚
        Dress,      // 裙子/服饰
        Other       // 其他（头发/饰品等）
    }

    // 姿势锁定
    private bool _poseLocked = false;
    private float _poseLockUntil = 0f;
    // 点击姿势保存的参数（物理 order 800 会覆盖，LateUpdate 重新设）
    private readonly Dictionary<string, float> _clickSavedParams = new Dictionary<string, float>();

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
    private int _currentIdleAction = 0; // 0=无, 1=歪头, 2=微笑, 3=挑眉, 4=星辉, 5=伸懒腰, 6=委屈, 7=法阵, 8=害羞, 9=困惑
    // 各动作权重（对应动作 1-9），值越大出现概率越高
    private readonly int[] _idleActionWeights = new int[] { 5, 5, 3, 4, 3, 4, 2, 3, 0 };
    // 复合动作相位（用于多参数协同插值）
    private float _complexActionPhase = 0f;
    // 动作结束后的冷却时间（防动作无限重播）
    private float _idleActionCooldown = 0f;

    // 法阵斜飞物理飘动 — Spring-Damper 状态
    private float _magicFloatPhase = 0f;       // Perlin 噪声相位
    private float _magicSpringPosX = 0f;        // 弹簧位置（身体ParamBodyAngleX偏移）
    private float _magicSpringVelX = 0f;        // 弹簧速度
    private float _magicSpringPosH = 0f;        // 弹簧位置（头部ParamAngleX偏移）
    private float _magicSpringVelH = 0f;        // 弹簧速度
    private float _magicDamping;               // 当前阻尼系数（随机选弹性/柔软）
    private float _magicModeTimer = 0f;         // 模式切换计时
    private float _magicModeDuration = 0f;      // 当前模式持续时长
    private bool _magicModeBouncy = true;       // true=弹性惯性, false=柔软跟随
    private float _magicPrevCamX = 0f;            // 上一帧 camX（速度计算用）
    private float _magicHairSmooth = 0f;          // SmoothDamp 平滑后的速度值
    private float _magicHairSmoothVel = 0f;       // SmoothDamp 速度引用
    // ForceUpdateNow 后 Physics 会覆盖头发参数，存下计算值用于 ForceUpdate 后重新应用
    private float _magicHeadHairV = 0f;
    private float _magicBraidV = 0f;
    private float _magicOrnamentV = 0f;
    private float _magicHair169V = 0f;
    private float _magicClothV = 0f;
    private MaterialPropertyBlock _magicMpb;        // 缓存 MPB 用于每帧 ForceRefresh 防眼白

    // 随机微动用噪声偏移
    private float _noiseTimeX = 0f;
    private float _noiseTimeY = 0f;

    // 强制动作锁定（右键菜单触发，播放期间不被走路覆盖）
    private bool _actionLocked = false;
    public event System.Action OnForcedActionFinished;

    // ===== Live2DParameterMapper + 新动作系统 =====
    private Live2DParameterMapper _mapper;
    public Live2DActionController ActionController { get; private set; }
    // =====

    // ===== 调试偏移系统（DebugWindow 实时调参） =====
    /// <summary>是否启用调试偏移</summary>
    public bool debugOffsetEnabled = false;
    /// <summary>调试偏移表：参数名 → 偏移量（在动画值上叠加，每帧重新应用不累积）</summary>
    public Dictionary<string, float> debugOffsets = new Dictionary<string, float>();

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

    // 走路时随机触发表情的计时
    private float _walkExpressionTimer = 0f;
    private float _walkExpressionCooldown = 0f;

    // 屏幕边缘碰撞反弹
    private float _wallHitTime = 0f;

    // 鼠标眼睛跟随目标（null=使用默认 Perlin 噪声）
    private float? _eyeTargetX = null;
    private float? _eyeTargetY = null;

    // ===== 物理左臂(Param34/36/37)输出权重管理 =====
    // 用于在空闲动作期间暂存"手臂L"子刚体权重，物理弹簧内部动量不停，
    // 但输出权重归零后 Param34/36/37 不会收到物理驱动。
    private float[] _savedLeftArmWeights;
    private bool _leftArmPhysicsSaved = false;
    private float _leftArmRestoreTimer = -1f;       // 缓恢复计时器 (-1=未激活)
    private const float LEFT_ARM_RESTORE_DURATION = 0.4f; // 缓恢复时长（防回弹）

    // 拖拽平滑转身
    private float _dragSmoothBodyY = 0f;
    // 拖拽速度追踪（帧间 petX 增量，平滑后驱动物理和输出）
    private float _dragSmoothBodyZ = 0f;   // 身体/裙子/法盘输入
    private float _dragSmoothHeadX = 0f;   // 头左右输入
    private float _dragSmoothHeadZ = 0f;   // 头旋转输入
    private int _lastDragPetX = 0;
    private bool _dragInited = false;
    // 平滑眼睛跟随（防突变）
    private float _eyeSmoothX = 0f;
    private float _eyeSmoothY = 0f;
    private bool _eyeSmoothActive = false;

    private void Start()
    {
        Debug.Log("[Live2DRenderer] Start() 被调用了");
        _pet = GetComponent<DesktopPet>();
        _dragHandler = GetComponent<DragHandler>();
        _chatBubble = GetComponent<ChatBubble>();
        if (_chatBubble == null) _chatBubble = FindObjectOfType<ChatBubble>();
        _timeController = GetComponent<TimeWeatherController>();
        if (_timeController == null) _timeController = FindObjectOfType<TimeWeatherController>();
        Debug.Log($"[Live2DRenderer] DesktopPet={(_pet != null)}, DragHandler={(_dragHandler != null)}, ChatBubble={(_chatBubble != null)}, TimeWeatherController={(_timeController != null)}");

        // ★ 强制从宏读取（忽略场景中序列化的旧值，改宏立即生效）
        modelScale = 56.2f;
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

        // ★ 运行时降级：从 StreamingAssets 用 CubismModel3Json 加载
        if (modelPrefab == null && _modelRoot == null)
        {
            TryLoadFromStreamingAssets();
        }

        if (modelPrefab != null)
        {
            _modelRoot = Instantiate(modelPrefab, transform);
        }

        if (_modelRoot != null)
        {
            
#if UNITY_EDITOR
            // 编辑器调试：使用场景预设的相机参数
            SetLayerRecursively(_modelRoot, 0);
            _overlayReady = false;
            
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.cullingMask = -1; // Everything，防止之前 ExcludeOverlay 改坏了
            }
#else
            SetLayerRecursively(_modelRoot, OVERLAY_LAYER);
            ExcludeOverlayLayerFromMainCamera();
#endif
            
            Debug.Log($"[Live2DRenderer] _modelRoot={_modelRoot.name}, activeSelf={_modelRoot.activeSelf}, activeInHierarchy={_modelRoot.activeInHierarchy}");
            Debug.Log($"[Live2DRenderer] _modelRoot.transform.childCount={_modelRoot.transform.childCount}");

            _cubismModel = _modelRoot.GetComponentInChildren<CubismModel>();
            _physicsController = _modelRoot.GetComponentInChildren<CubismPhysicsController>();
            _paramStore = _modelRoot.GetComponentInChildren<CubismParameterStore>();

            // ★ 初始化 CubismRaycaster（射线触摸检测）
            _cubismRaycaster = _modelRoot.GetComponentInChildren<CubismRaycaster>();
            if (_cubismRaycaster == null)
            {
                _cubismRaycaster = _modelRoot.AddComponent<CubismRaycaster>();
                Debug.Log("[Live2DRenderer] ✓ 自动添加 CubismRaycaster");
            }

            // ★ 为所有 Drawable 添加 CubismRaycastable 组件，实现精确模型碰撞
            int raycastableCount = 0;
            foreach (var drawable in _cubismModel.Drawables)
            {
                if (drawable.GetComponent<CubismRaycastable>() == null)
                {
                    drawable.gameObject.AddComponent<CubismRaycastable>();
                    raycastableCount++;
                }
            }
            // 刷新 raycaster 缓存（Refresh 是 private 方法，通过反射调用）
            var refreshMethod = typeof(CubismRaycaster).GetMethod("Refresh",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (refreshMethod != null)
                refreshMethod.Invoke(_cubismRaycaster, null);
            Debug.Log($"[Live2DRenderer] ✓ 为 {raycastableCount} 个 Drawable 添加了 CubismRaycastable");

            // ★ 计算模型 Drawable 的 Y 轴范围，用于触摸部位分类
            CacheDrawableClassificationBounds();

            // ★ 通过反射获取"手臂L"物理子刚体引用（Rig 是私有属性）
            if (_physicsController != null)
            {
                var rigField = typeof(CubismPhysicsController).GetField("_rig",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (rigField != null)
                {
                    var rig = rigField.GetValue(_physicsController) as CubismPhysicsRig;
                    if (rig != null)
                    {
                        _leftArmSubRig = rig.GetSubRig("手臂L");
                        if (_leftArmSubRig != null)
                            Debug.Log("[Live2DRenderer] ✓ 找到左臂物理子刚体 '手臂L'");
                        else
                            Debug.LogWarning("[Live2DRenderer] ⚠ 未找到 '手臂L' 物理子刚体");
                    }
                }
            }

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

            // 编辑器下不创建叠加相机（主相机直接可见）
#if !UNITY_EDITOR
            SetupOverlayRendering();
#endif

            // ★ 初始化参数映射器 + 新动作系统
            InitActionSystem();
            
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
    
    /// <summary>
    /// ★ 运行时从 StreamingAssets 用 CubismModel3Json API 加载模型
    /// </summary>
    private void TryLoadFromStreamingAssets()
    {
        string modelJsonPath = Path.Combine(Application.streamingAssetsPath, "Live2D", "Fuxuan", "符玄.model3.json");
        Debug.Log($"[Live2DRenderer] 尝试 StreamingAssets 加载: {modelJsonPath}");

        if (!File.Exists(modelJsonPath))
        {
            Debug.LogError($"[Live2DRenderer] StreamingAssets 模型文件不存在: {modelJsonPath}");
            return;
        }

        // 使用自定义加载委托读取 StreamingAssets 中的文件
        CubismModel3Json.LoadAssetAtPathHandler loadHandler = (System.Type assetType, string assetPath) =>
        {
            if (!File.Exists(assetPath))
            {
                Debug.LogWarning($"[Live2DRenderer] 引用的文件不存在: {assetPath}, assetType={assetType}");
                return null;
            }

            if (assetType == typeof(string))
            {
                return File.ReadAllText(assetPath);
            }
            else if (assetType == typeof(byte[]))
            {
                return File.ReadAllBytes(assetPath);
            }
            else if (assetType == typeof(Texture2D))
            {
                byte[] imageData = File.ReadAllBytes(assetPath);
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(imageData))
                    return tex;
                Debug.LogWarning($"[Live2DRenderer] 纹理加载失败: {assetPath}");
                return null;
            }

            Debug.LogWarning($"[Live2DRenderer] 未处理的资源类型: {assetType}, path={assetPath}");
            return null;
        };

        CubismModel3Json modelJson = CubismModel3Json.LoadAtPath(modelJsonPath, loadHandler);
        if (modelJson == null)
        {
            Debug.LogError("[Live2DRenderer] CubismModel3Json.LoadAtPath 返回 null");
            return;
        }

        CubismModel model = modelJson.ToModel(
            CubismBuiltinPickers.MaterialPicker,
            CubismBuiltinPickers.TexturePicker,
            shouldImportAsOriginalWorkflow: true
        );

        if (model == null)
        {
            Debug.LogError("[Live2DRenderer] modelJson.ToModel 返回 null");
            return;
        }

        _modelRoot = model.gameObject;
        _modelRoot.transform.SetParent(transform, false);
        _modelRoot.name = "符玄 (StreamingAssets)";

        Debug.Log($"[Live2DRenderer] ✓ StreamingAssets 加载成功: {_modelRoot.name}");
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

        // ★ 拖拽中不累积走路相位、不走体态逻辑（由 UpdateDragStruggle 接管身体参数给物理）
        if (_pet != null && _pet.isDragging)
        {
            _walkPhase = 0f;
            _walkBounceOffset = 0f;
            UpdateModelPosition();
            return;
        }

        // 累积走路相位并计算垂直颠簸偏移
        // ★ 暂停时也清零，防止睡眠唤醒后宠物在原地上下颠簸
        bool isPhysActive = (_pet != null && !_pet.isPaused);
        if (isPhysActive && _pet != null && _pet.onGround && _pet.petVx != 0)
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

        // ★ 体态提前给物理用：Physics 在 CubismUpdateController.LateUpdate(0)
        //   中读取 ParamBodyAngleX/Y/Z 来驱动衣服。我们在 Update() 中先设好
        //   走路的转体/前倾/低头，确保物理拿到正确的体态输入。
        bool isWalking = (_pet != null && _pet.onGround && _pet.petVx != 0 && !_pet.isPaused && !_actionLocked);
        if (isWalking)
        {
            float bodyWeight = 1f;
            if (_walkFadeInRemaining > 0f)
            {
                float raw = 1f - Mathf.Clamp01(_walkFadeInRemaining / WALK_FADE_IN_DURATION);
                bodyWeight = raw * raw;
                _walkFadeInRemaining -= Time.deltaTime;
            }
            ApplyWalkBodyPose(bodyWeight);
        }
        else if (_wasWalkingLastFrame || _walkBlendRemaining > 0f)
        {
            // 过渡帧：_walkBlendRemaining 要到 LateUpdate 才设，但物理在 LateUpdate(0) 就要读体态了
            // 用 _wasWalkingLastFrame 兜住「刚停的第一帧」不设体态的空窗期
            float blendWeight = Mathf.Clamp01(_walkBlendRemaining / IDLE_BLEND_DURATION);
            float eased = blendWeight * blendWeight;
            ApplyWalkBodyPose(eased);
        }
        else
        {
            // ★ 空闲时：给物理系统中性体态 + 头角度信号，防止左臂物理残留偏量
            //    Physics(800) 读取 ParamBodyAngleX/Y/Z（身体）、
            //    ParamAngleX/Y/Z（头部）、ParamBreath（呼吸）来决定
            //    衣服/头发/手臂(Param34/36/37)的物理摆动。
            //    如果不设干净，物理读到走路残留值，左臂就歪到裙子背后。
            //
            // ★ 也设 Param34/36/37=0 打断 RestoreParameters 循环：
            //    CubismParameterStore(order 150) 在 LateUpdate 阶段保存参数值。
            //    如果不在这里设 0，order 150 会保存上一帧物理输出残留的非零值 →
            //    ForceUpdateNow→RestoreParameters 恢复非零，导致弹簧动量永不归零。
            SetParameter("ParamBodyAngleX", 0f);
            SetParameter("ParamBodyAngleY", 0f);
            SetParameter("ParamBodyAngleZ", 0f);
            SetParameter("ParamAngleX", 0f);
            SetParameter("ParamAngleY", 0f);
            SetParameter("ParamAngleZ", 0f);
            SetParameter("ParamBreath", 0f);
            SetParameter("Param34", 0f);
            SetParameter("Param36", 0f);
            SetParameter("Param37", 0f);
        }
    }

    private void LateUpdate()
    {
        if (!_loaded || _cubismModel == null) return;

        // ★ 拖拽中 → 物理(order 800)已跑完，挣扎参数被物理覆盖了，在这里重新设一遍
        if (_pet != null && _pet.isDragging)
        {
            // 平滑转身（_dragSmoothBodyY 在 UpdateDragStruggle 中更新）
            UpdateDragStruggle();
            if (_cubismModel != null) _cubismModel.ForceUpdateNow();
            return;
        }

        // ★ 点击/摸头锁定中 → 重新设置被物理覆盖的参数
        // ★ 但如果已有强制动作（如法阵），跳过点击锁定，让动作驱动参数
        if (_poseLocked && Time.time < _poseLockUntil && !_actionLocked)
        {
            foreach (var kv in _clickSavedParams)
                SetParameter(kv.Key, kv.Value);
            if (_cubismModel != null) _cubismModel.ForceUpdateNow();
            return;
        }

        // ★ 更新新动作系统（表情淡入淡出）
        ActionController?.Update(Time.deltaTime);

        // 累积噪声时间
        _noiseTimeX += Time.deltaTime * 0.6f;
        _noiseTimeY += Time.deltaTime * 0.4f;
        _breathPhase += Time.deltaTime * 2.0f;

        UpdateBlink();

        // ★ 每帧强制清零眼睛白覆盖层（防 Cubism 物理/表情预设覆盖）
        //    Param63-71 是眼睛高光/光点层，设 1f 保留高光使眼睛有神
        SetParameter("Param132", 0f);
        SetParameter("Param63", 1f);
        SetParameter("Param64", 1f);
        SetParameter("Param65", 1f);
        SetParameter("Param67", 1f);
        SetParameter("Param68", 1f);
        SetParameter("Param69", 1f);
        SetParameter("Param70", 1f);
        SetParameter("Param71", 1f);

        // ★ 走路/空闲统一在 LateUpdate 中设置参数
        // 此时 _walkPhase 已在 Update() 中更新完毕，相位准确
        bool isWalking = (_pet != null && _pet.onGround && _pet.petVx != 0 && !_pet.isPaused && !_actionLocked);

        if (isWalking)
        {
            if (!_wasWalkingLastFrame && !_actionLocked)
            {
                // 空闲→走路：清表情残留 + 开始体态淡入
                ResetIdleAction();
                _walkBlendRemaining = 0f;
                _walkFadeInRemaining = WALK_FADE_IN_DURATION;
                // 走路表情冷却：走一会儿才随机触发
                _walkExpressionTimer = 0f;
                _walkExpressionCooldown = Random.Range(3f, 8f);
            }
            // 走路淡入权重（手脚幅度从0渐增至1）
            float walkAnimWeight = 1f;
            if (_walkFadeInRemaining > 0f)
            {
                float raw = 1f - Mathf.Clamp01(_walkFadeInRemaining / WALK_FADE_IN_DURATION);
                walkAnimWeight = raw * raw; // ease-in
            }
            UpdateWalkAnimation(walkAnimWeight);

            // ★ 走路时随机触发傲娇闭眼表情
            UpdateWalkExpression();
        }
        else
        {
            if (_wasWalkingLastFrame)
            {
                // 走路→空闲：开始混合消退
                _walkBlendRemaining = IDLE_BLEND_DURATION;
                _walkFadeInRemaining = 0f; // 重置淡入（下次空闲→走路重新开始）

                // 走路表情残留清理：恢复面部默认值
                _walkExpressionTimer = 0f;
                _walkExpressionCooldown = 0f;
                SetParameter("ParamEyeLOpen", 1f);
                SetParameter("ParamEyeROpen", 1f);
                SetParameter("ParamEyeLSmile", 0f);
                SetParameter("ParamEyeRSmile", 0f);
                SetParameter("ParamMouthForm", 0f);
                SetParameter("ParamAngleZ", 0f);
                SetParameter("ParamAngleY", 0f);
            }

            if (_walkBlendRemaining > 0f)
            {
                // ⭐3 无缝过渡：混合期同时播走路（渐消）和空闲动画
                float blendWeight = Mathf.Clamp01(_walkBlendRemaining / IDLE_BLEND_DURATION);
                // 先播空闲动画（覆盖基础呼吸/Perlin/眼睛）
                UpdateIdleAnimation();
                // 再叠加消退中的走路动画参数（手臂/腿逐渐停止）
                UpdateWalkAnimation(blendWeight);
                _walkBlendRemaining -= Time.deltaTime;
            }
            else
            {
                UpdateIdleAnimation();
            }
        }

        _wasWalkingLastFrame = isWalking;

        // ★ 屏幕边缘碰撞反弹动画：覆盖在现有参数之上
        if (_wallHitTime > 0f)
        {
            float t = _wallHitTime / WALL_HIT_DURATION; // 1→0
            float progress = Mathf.Clamp01(t);

            // 瞪眼（出场的头部+身体角度混合）
            float eyeOpen = Mathf.Lerp(WALL_HIT_EYE_OPEN, 1f, progress);
            SetParameter("ParamEyeLOpen", eyeOpen);
            SetParameter("ParamEyeROpen", eyeOpen);

            // 张嘴（渐消）
            float mouthOpen = Mathf.Lerp(WALL_HIT_MOUTH_OPEN, 0f, progress * progress);
            SetParameter("ParamMouthOpenY", mouthOpen);

            // 身体后仰（迅速弹回）
            float bodyLean = Mathf.Lerp(WALL_HIT_BODY_LEAN, 0f, progress * 2f);
            SetParameter("ParamBodyAngleX", bodyLean);

            // 头部微缩（受惊）
            float headBack = Mathf.Lerp(3f, 0f, progress);
            SetParameter("ParamAngleY", headBack);

            _wallHitTime -= Time.deltaTime;
        }

        // ★ 鼠标眼睛跟随覆盖：在所有动画参数之后、ForceUpdateNow 之前设置
        //    但强制动作时禁用（如星辉/AI动作需控制眼珠方向）
        bool eyeOverridden = (_eyeTargetX.HasValue || _eyeTargetY.HasValue) && !_actionLocked;
        if (eyeOverridden)
        {
            // 平滑追踪目标值（用 lerp 防止眼球突变）
            float rawTargetX = (_eyeTargetX ?? 0f) * EYE_FOLLOW_MAX_X;
            float rawTargetY = (_eyeTargetY ?? 0f) * EYE_FOLLOW_MAX_Y;

            _eyeSmoothX = Mathf.Lerp(_eyeSmoothX, rawTargetX, 0.08f);
            _eyeSmoothY = Mathf.Lerp(_eyeSmoothY, rawTargetY, 0.08f);
            _eyeSmoothActive = true;

            SetParameter("ParamEyeBallX", _eyeSmoothX);
            SetParameter("ParamEyeBallY", _eyeSmoothY);
        }
        else if (_eyeSmoothActive && !_actionLocked)
        {
            // 缓慢退回中心（让眼球自然回归，不跳）
            _eyeSmoothX = Mathf.Lerp(_eyeSmoothX, 0f, 0.04f);
            _eyeSmoothY = Mathf.Lerp(_eyeSmoothY, 0f, 0.04f);
            SetParameter("ParamEyeBallX", _eyeSmoothX);
            SetParameter("ParamEyeBallY", _eyeSmoothY);
            if (Mathf.Abs(_eyeSmoothX) < 0.1f && Mathf.Abs(_eyeSmoothY) < 0.1f)
                _eyeSmoothActive = false;
        }

        // ★ 强制网格更新：Cubism 的网格在 Update() 阶段已用 C++ 核心算完，
        //    Physics(800) 覆盖了衣服参数，我们(801)覆盖了手臂参数，
        //    但网格仍是旧参数结果，需强制刷新用最新参数重新算一遍。
        // ★ 每帧都 ForceUpdateNow：因为眼睛保护参数（Param132等）每帧都设了值，
        //   如果不强制更新，C++ 核心保留旧值（如法阵动作残留的非零 Param132），
        //   导致眼睛持续发白。无条件执行确保参数始终被渲染引擎采纳。
        bool hasActiveAction = (_currentIdleAction > 0 && _idleActionTime > 0f);
        bool debugOffsetActive = (debugOffsetEnabled && !_actionLocked && !hasActiveAction && debugOffsets != null && debugOffsets.Count > 0);
        // ★ 重要：空闲动作（如星辉/伸懒腰）也要 ForceUpdate，否则物理系统覆盖了我们的值
        _cubismModel.ForceUpdateNow();

        // ============================================================
        // ★ 左臂(Param34/36/37) 物理拦截：双重 ForceUpdate + 权重归零
        //
        // 问题链：
        //   1. CubismParameterStore(order 150) 保存当前参数值
        //   2. Physics(order 800) 把 Param34/36/37 写为非零（弹簧动量）
        //   3. 我们(order 801) 设回 0 并 ForceUpdateNow
        //   4. ForceUpdateNow→Update()→RestoreParameters() 恢复 step 1 保存的非零值
        //   5. 最终网格用的是 step 4 的非零值 → 左臂依然扭曲！
        //
        // 解决：
        //   a) 二次 ForceUpdate：第一次后强设 0→SaveParameters→第二次 ForceUpdateNow，
        //      第二次 RestoreParameters 恢复的是刚保存的 0。
        //   b) 同时归零"手臂L"物理输出权重：让下一帧物理评估时左臂输出 = 0，
        //      打断弹簧动量在帧间的传递。
        // ============================================================
        if (hasActiveAction || _actionLocked)
        {
            // === 0) 首次进入动作时保存左臂物理权重 ===
            // ★ 如果在缓恢复过程中被新动作打断：重置恢复计时器
            if (_leftArmRestoreTimer >= 0f) _leftArmRestoreTimer = -1f;

            if (!_leftArmPhysicsSaved)
            {
                SaveLeftArmWeights();
            }

            // === 1) 归零"手臂L"输出权重（影响下一帧 physics evaluate） ===
            ZeroLeftArmWeights();

            // === 2) 第二次强设左臂 + 保存到参数存储 + 二次 ForceUpdate ===
            SetParameter("Param34", 0f);
            SetParameter("Param36", 0f);
            SetParameter("Param37", 0f);

            if (_paramStore != null)
            {
                _paramStore.SaveParameters();
            }

            _cubismModel.ForceUpdateNow();
        }
        else
        {
            // === 缓恢复左臂物理权重（动作结束后，防弹簧回弹） ===
            // ★ 法阵等长时间动作中，左臂物理子刚体的弹簧内部已积累大量动量。
            //   若瞬间恢复输出权重，弹簧动量一次性释放 → Param34/36/37 突跳 → 视觉回弹。
            //   解决：在 LEFT_ARM_RESTORE_DURATION 秒内线性淡入输出权重。
            if (_leftArmPhysicsSaved)
            {
                if (_leftArmRestoreTimer < 0f)
                {
                    // 首次进入恢复：启动缓恢复计时器
                    _leftArmRestoreTimer = 0f;
                }

                _leftArmRestoreTimer += Time.deltaTime;
                float t = Mathf.Clamp01(_leftArmRestoreTimer / LEFT_ARM_RESTORE_DURATION);
                float weight = EaseOutQuad(t); // 缓出：先快后慢，进一步平滑过渡

                // 应用插值后的权重
                if (_savedLeftArmWeights != null && _leftArmSubRig != null && _leftArmSubRig.Output != null)
                {
                    int n = Mathf.Min(_savedLeftArmWeights.Length, _leftArmSubRig.Output.Length);
                    for (int i = 0; i < n; i++)
                    {
                        _leftArmSubRig.Output[i].Weight = Mathf.Lerp(0f, _savedLeftArmWeights[i], weight);
                    }
                }

                // 恢复完成 → 清理
                if (t >= 1f)
                {
                    _savedLeftArmWeights = null;
                    _leftArmPhysicsSaved = false;
                    _leftArmRestoreTimer = -1f;
                }
            }
        }

        // ★ 调试偏移通道：动画完成后，在动画值上叠加偏移量
        // 因动画每帧重新设值，偏移不会累积（动画值 + 偏移 = 最终值）
        // 任一空闲动作运行时暂停偏移，避免破坏动画手部/手指姿态
        if (debugOffsetActive)
        {
            foreach (var kv in debugOffsets)
            {
                var p = _cubismModel.Parameters.FindById(kv.Key);
                if (p != null)
                {
                    float animVal = p.Value;                      // 动画已设置的值
                    p.Value = Mathf.Clamp(animVal + kv.Value,     // 叠加偏移
                        p.MinimumValue, p.MaximumValue);
                }
            }

            // ★ 自动设置手部图层/透视参数，让手浮到衣服前面
            SetParameter("Param95", 1f);
            SetParameter("Param117", 0.8f);
            SetParameter("Param98", 0.8f);
            SetParameter("Param100", 0.8f);
            SetParameter("Param116", 0.6f);
            SetParameter("Param120", 1f);
            SetParameter("Param108", 1f);
            SetParameter("Param119", 1f);

            // ★ 调试偏移的第二次 ForceUpdate (无法合并，因为偏移值在第一次 ForceUpdate 后才读取到的 animVal)
            _cubismModel.ForceUpdateNow();
        }

        // ★ ForceUpdateNow 会触发 Cubism 更新管线，Physics 重新覆盖头发/衣服参数。
        //    最后一次 ForceUpdate 后重新应用 + SaveParameters，确保最终值是我们设定的。
        // ★ 无条件 _currentIdleAction==7（不依赖 _actionLocked），
        //   自然触发（_actionLocked=false）时同样需要 ForceRefreshModelAfterFade 保护。
        if (_currentIdleAction == 7)
        {
            // ★ 头部头发：10%原幅
            SetParameter("Param5", _magicHeadHairV);  SetParameter("Param7", _magicHeadHairV);
            SetParameter("Param9", _magicHeadHairV);  SetParameter("Param11", _magicHeadHairV);
            SetParameter("Param14", _magicHeadHairV); SetParameter("Param17", _magicHeadHairV);
            SetParameter("Param19", _magicHeadHairV); SetParameter("Param21", _magicHeadHairV);
            SetParameter("Param23", _magicHeadHairV); SetParameter("Param35", _magicHeadHairV);
            SetParameter("Param41", _magicHeadHairV);
            // ★ 后发/辫子：40%原幅
            SetParameter("Param43", _magicBraidV);    SetParameter("Param45", _magicBraidV);
            SetParameter("Param55", _magicBraidV);    SetParameter("Param62", _magicBraidV);
            // ★ 发饰品：40%原幅
            SetParameter("Param91", _magicOrnamentV); SetParameter("Param74", _magicOrnamentV);
            SetParameter("Param89", _magicOrnamentV);
            // ★ Param169 头饰
            SetParameter("Param169", _magicHair169V);
            SetParameter("Param82", _magicClothV);  SetParameter("Param87", _magicClothV);
            SetParameter("Param84", _magicClothV * 0.6f);
            SetParameter("Param49", _magicClothV);  SetParameter("Param51", _magicClothV);
            SetParameter("Param57", _magicClothV);  SetParameter("Param60", _magicClothV);
            // 先保存参数再 ForceUpdate，让 RestoreParameters 恢复我们的值而非 Physics 覆盖值
            if (_paramStore != null) _paramStore.SaveParameters();
            _cubismModel.ForceUpdateNow();

            // ★ ForceUpdateNow 触发了 Cubism 物理管线，Physics 重新计算了
            //   ParamBodyAngleX/ParamAngleX，覆盖了 UpdateMagicCircle() 中设置的
            //   Perlin+Spring 飘动偏移。此处重新应用身体角度。
            //   从 UpdateMagicCircle() 计算出的 _magicSpringPosX/_magicSpringPosH
            //   在弹簧物理中已更新，直接使用。
            float angleFade = Mathf.Clamp01(_complexActionPhase / CIRCLE_ANGLE_RAMP_DUR);
            float fadeMag = Mathf.Clamp01(_complexActionPhase / CIRCLE_DURATION);
            if (fadeMag >= 1f)
            {
                // Act 3 消散段：用 fade 淡出（与 UpdateMagicCircle 逻辑一致）
                float hAct3 = (fadeMag - 0.65f) / (1f - 0.65f);
                float act3Fade = 1f - EaseOutQuad(hAct3);
                float fadeBody = act3Fade * act3Fade;
                SetParameter("ParamBodyAngleX", fadeBody * angleFade * (CIRCLE_BODY_ANGLE_X + _magicSpringPosX));
                SetParameter("ParamAngleX", fadeBody * angleFade * (CIRCLE_HEAD_ANGLE_X + _magicSpringPosH));
            }
            else
            {
                SetParameter("ParamBodyAngleX", angleFade * (CIRCLE_BODY_ANGLE_X + _magicSpringPosX));
                SetParameter("ParamAngleX", angleFade * (CIRCLE_HEAD_ANGLE_X + _magicSpringPosH));
            }
            SetParameter("ParamBodyAngleZ", 0);

            // ★ ForceUpdateNow 后 CubismRenderController 可能重置了 GPU 的 MultiplyColor，
            //   需要重新强制设置为白色，防止眼睛变暗。
            ForceRefreshModelAfterFade();
        }

        // ============================================================
        // ★ ArtMesh 渲染层调试日志 — 仅在法阵激活时每 300 帧记录非默认颜色
        //    ScreenColor.a 默认 = 1.0，用 a>0.01 判断会误匹配全部 ArtMesh
        //    改为仅当 RGB 有颜色值时才记录（非纯透明 ScreenColor）
        // ============================================================
        if (Time.frameCount % 300 == 0 && _actionLocked && _cubismModel?.Drawables != null)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int count = 0;
            foreach (var drawable in _cubismModel.Drawables)
            {
                var multColor = drawable.MultiplyColor;
                var screenColor = drawable.ScreenColor;
                // 记录任何 MultiplyColor 非白色，或 ScreenColor 有色（r/g/b > 0.01）的 ArtMesh
                // 注意：ScreenColor.a 默认为 1.0（全透明不叠加颜色），不能用作判断条件
                if (multColor.r < 0.99f || multColor.g < 0.99f || multColor.b < 0.99f || multColor.a < 0.99f ||
                    screenColor.r > 0.01f || screenColor.g > 0.01f || screenColor.b > 0.01f)
                {
                    sb.Append($" [{drawable.name}](id={drawable.Id}) mul=({multColor.r:F2},{multColor.g:F2},{multColor.b:F2},{multColor.a:F2})" +
                        $" scr=({screenColor.r:F2},{screenColor.g:F2},{screenColor.b:F2},{screenColor.a:F2})");
                    count++;
                }
            }
            if (count > 0)
                Debug.Log($"[EYE_MESH] found={count}:{sb.ToString()}");
        }

        // ============================================================
        // ★ 待机气泡：>30秒无交互时头顶冒泡
        // ============================================================
        float idleDuration = (_dragHandler != null)
            ? Time.time - _dragHandler.lastInteractionTime : 0f;
        if (_dragHandler != null && _chatBubble != null)
        {
            UpdateIdleBubble(idleDuration);
        }
    }

    /// <summary>
    /// 待机气泡逻辑：长时间无交互时偶尔冒泡
    /// </summary>
    private float _idleBubbleTimer = 0f;
    private bool _idleBubbleShown = false;
    private static readonly string[] IDLE_BUBBLES = new string[]
    {
        "嗯…好无聊~",
        "好安静呀…",
        "有人吗~~",
        "想去散步…",
        "想喝奶茶…",
        "ZZZ…",
        "你在干嘛呢？",
        "好闲哦…",
        "今天天气怎么样~",
        "要不要一起玩？",
        "嗯…本座方才占了一卦，卦象说…今日运势不错。",
        "这世间的因果线，本座一眼便能看穿。你嘛…倒是有趣。",
        "太卜司今日无事，正好清静片刻。",
        "青雀又溜号了罢…也罢，本座今日心情好，不计较。",
        "大衍穷观阵的推演结果…嗯，不出所料。",
        "你盯着本座作甚？法眼无遗，可不会看漏哦。",
        "唔…第三眼有些发酸，待本座歇一歇。",
        "罗浮的风，总带着几分丹鼎司的草木香。",
        "凡人的命运如棋局，而本座…自然是最高的弈者。",
        "你最近的气运…嗯，不好不坏，平平无奇。",
        "今日份的星轨推演已完成，一切尽在掌握。",
        "想听本座替你卜一卦么？…也是，缘法未至。",
        "本座虽然自信，但从未说过自己不会出错——当然，出错的概率极小。",
        "卜算之道，在于知微见著。你刚才眨眼三次，有烦恼罢？",
        "景元将军今日又推脱公务了罢？哼，意料之中。",
        "穷观阵内三千世界，本座看得太多，偶尔也想看看凡间的景色。",
        "你身上有种…说不清道不明的因果缠绕——想听解释么？算了，说来话长。",
        "丹鼎司的医术确实了得，但论窥探天机，还是本座更胜一筹。",
        "太卜司的案牍堆积如山…唔，本座什么都没说。",
        "你来的正好，帮本座看看这份星图——对，就是那里，是否有偏差？",
        "法眼所见，万象皆有其轨迹。比如你，命中带…带什么来着？忘了。",
        "将军总说本座急功近利，呵，他不急是他无能。",
        "好想找个清净的地方躺一天…但太卜司离了本座可不行。",
        "你说，一个人明知命运却还要往前走，是勇敢还是愚蠢？",
        "天地如逆旅，你我皆是行人。…只不过本座走得比你快些。",
        "本座的第三眼能看见各种可能性的分支，但你面前这条…很有趣。",
        "星槎海的风声，透过穷观阵也能听见呢。",
        "工造司今天又炸了一回…习惯了。",
        "云骑军今日演练，阵势还行——本座若是指挥，必定更好。",
        "地衡司的案卷堆得比太卜司还高，本座平衡了。",
        "偶尔偷得浮生半日闲，也不错。",
        "你心里藏着事，瞒不过本座的法眼。想说便说，不说便罢。",
        "卜者不自卜，但本座偏偏可以——当然，结果通常不怎么愉快。",
        "星宿之间的微妙偏移，凡人是看不见的。嗯，你们看不见也好。",
        "本座的占卜之术，可是师从玉阙太卜竟天——虽然他预言本座会…算了不说这个。",
        "你最近运势一般，但也不必担忧，本座替你留意着。",
        "冥冥之中自有定数，但定数之外尚有变数——这变数，就是本座。",
        "你在想什么？不必开口，本座已经算到了。…才怪，猜的。",
        "太卜司的茶不错，你要不要来一杯？…没有？那本座自己享用了。",
        "每次用穷观阵推演星轨，都感觉自己在触碰宇宙的脉搏。",
        "你认识青雀么？那家伙要是能有你一半勤快就好了。"
    };

    private void UpdateIdleBubble(float idleDuration)
    {
        // 无交互超过 30 秒，进入待机气泡状态
        if (idleDuration >= 30f && !_pet.isPaused && !_pet.isDragging)
        {
            if (!_idleBubbleShown)
            {
                _idleBubbleTimer = 0f;
                _idleBubbleShown = true;
            }
            else
            {
                _idleBubbleTimer += Time.deltaTime;
                // 每 15~30 秒随机冒泡一次
                if (_idleBubbleTimer >= Random.Range(15f, 30f))
                {
                    if (!_chatBubble.IsShowing)
                    {
                        string msg = PickIdleBubbleMessage();
                        _chatBubble.ShowMessage(msg, 4f, ChatBubble.MsgPriority.Low);
                    }
                    _idleBubbleTimer = 0f;
                }
            }
        }
        else
        {
            _idleBubbleShown = false;
            _idleBubbleTimer = 0f;
        }
    }

    /// <summary>
    /// 选取待机气泡消息（50% 概率随机池，50% 概率天气/时间特化）
    /// </summary>
    private string PickIdleBubbleMessage()
    {
        bool useTimeWeather = (_timeController != null) && (Random.value < 0.5f);
        if (useTimeWeather)
        {
            // 时间特化
            if (_timeController.isSleepyTime)
                return SLEEPY_BUBBLES[Random.Range(0, SLEEPY_BUBBLES.Length)];
            else if (_timeController.isNight)
                return NIGHT_BUBBLES[Random.Range(0, NIGHT_BUBBLES.Length)];
            else if (_timeController.hour >= 5 && _timeController.hour < 8)
                return MORNING_BUBBLES[Random.Range(0, MORNING_BUBBLES.Length)];

            // 天气特化 — 优先用 AI 生成的语录
            if (_timeController.weatherFetched && _timeController.aiWeatherReady)
            {
                string aiMsg = _timeController.PickAiWeatherLine();
                if (aiMsg != null) return aiMsg;
                // 回退：AI 还没准备好则走硬编码
            }

            // 天气特化（硬编码回退）
            if (_timeController.weatherFetched)
            {
                var wt = _timeController.weather;
                string tempLabel = _timeController.temperatureC < 5f ? "好冷" :
                                   _timeController.temperatureC > 30f ? "好热" : null;
                if (wt == TimeWeatherController.WeatherType.Rain ||
                    wt == TimeWeatherController.WeatherType.Drizzle)
                {
                    string[] rainMsgs = tempLabel != null
                        ? new string[] { $"下雨了{tempLabel}…", "听雨声发呆~", "淅淅沥沥…" }
                        : new string[] { "下雨了呢~", "听雨声发呆~", "淅淅沥沥…" };
                    return rainMsgs[Random.Range(0, rainMsgs.Length)];
                }
                if (wt == TimeWeatherController.WeatherType.Thunder)
                    return THUNDER_BUBBLES[Random.Range(0, THUNDER_BUBBLES.Length)];
                if (wt == TimeWeatherController.WeatherType.Snow)
                    return SNOW_BUBBLES[Random.Range(0, SNOW_BUBBLES.Length)];
                if (wt == TimeWeatherController.WeatherType.Clear)
                    return SUNNY_BUBBLES[Random.Range(0, SUNNY_BUBBLES.Length)];
                if (tempLabel != null)
                    return $"今天{tempLabel}呀…";
            }
        }
        return IDLE_BUBBLES[Random.Range(0, IDLE_BUBBLES.Length)];
    }

    private static readonly string[] SLEEPY_BUBBLES = new string[]
    {
        "好晚了…该睡了~",
        "哈欠~~困了…",
        "眼睛快睁不开了…",
        "想躺床上…",
        "再玩一会就睡…Zzz",
        "子时已过，按理说该歇息了…但本座偏不。",
        "星象显示今夜宜早睡——但本座何时听过星象的建议？",
        "夜观天象…唔，眼皮有点撑不住了。",
        "这个时辰还在活动的人，不是夜游神就是心虚睡不着——你是哪种？",
        "穷观阵的推演在深夜格外清晰…可惜本座也格外困。",
    };

    private static readonly string[] NIGHT_BUBBLES = new string[]
    {
        "晚上好呀~",
        "月色真美…",
        "一个人在晚上有点寂寞呢~",
        "还在熬夜吗？",
        "星辰的轨迹在夜晚最为清晰，就像人心在独处时最为坦诚。",
        "夜晚的罗浮，星槎如萤火般划过天际…好看罢？",
        "你还不睡，是在等什么，还是在躲什么？",
        "月华如水，倒是卜算的好时辰。要不要本座替你起一卦？",
        "深夜的安静让本座想起玉阙…那里的星空更密，也更冷。",
        "穷观阵在夜间的推演准确率会高一些——毕竟天机更愿意在暗处现身。",
    };

    private static readonly string[] MORNING_BUBBLES = new string[]
    {
        "早安~",
        "早呀！又是新的一天~",
        "哈欠…早上好…",
        "今天也要加油哦！",
        "一日之计在于晨——虽然本座更想赖床。",
        "卯时了，该醒了。本座的第三眼已经看清了今日的气运…尚可。",
        "清晨的灵气最足，适合推演今日的卦象。",
        "噢？起得挺早嘛，本座对你刮目相看了。",
        "早起的鸟儿有虫吃——但本座不是鸟，本座是太卜。",
        "晨光穿透穷观阵的水镜，折射出七彩的光…一天的好兆头。",
    };

    private static readonly string[] THUNDER_BUBBLES = new string[]
    {
        "打雷了好可怕！",
        "轰隆隆…好吓人…",
        "雷雨天气要注意安全~",
        "雷霆之怒，天地变色…但本座的法眼比雷更亮。",
        "工造司的炸炉声跟这比起来，简直是小巫见大巫。",
        "雷声太大，扰乱了穷观阵的感应…罢了，今日歇业。",
        "惊雷乍响，倒让本座想起当年玉阙的一场雷暴——那才叫真正的天威。",
    };

    private static readonly string[] SNOW_BUBBLES = new string[]
    {
        "下雪了！好漂亮~",
        "雪白白的好美~",
        "想堆雪人…",
        "瑞雪兆丰年——这是罗浮罕见的吉兆。",
        "雪掩万物，连太卜司的案牍都显得干净了几分。",
        "落雪无声，天地间仿佛只剩下穷观阵的低吟。",
        "雪中的罗浮别有一番风韵——只可惜云骑军巡逻怕是要抱怨了。",
        "冰晶凝结在法眼之上，看到的星轨也带上了霜花。",
    };

    private static readonly string[] SUNNY_BUBBLES = new string[]
    {
        "今天天气真好~",
        "太阳暖洋洋的~",
        "好天气让人心情好~",
        "想出去晒太阳~",
        "晴空万里，正适合出游——但本座要坐镇太卜司。",
        "阳光明媚，星轨也格外清晰…一切都在本座的预料之中。",
        "这样的天气，青雀肯定又找借口偷溜出去了罢。",
        "天朗气清，惠风和畅…不如来卜一卦？本座今日心情好，不收卦资。",
        "阳光透过穷观阵的琉璃瓦，在地面投出七彩光斑——本座的办公室景致不错罢？",
        "好天气配上好心情，你今日的气运也比平时旺了几分。",
    };

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

        // === 眼球 — 鼠标跟随覆盖由 LateUpdate 末尾统一处理 ===
        // 默认 Perlin 噪声自然微动（如无鼠标目标）
        float eyeX = (Mathf.PerlinNoise(_noiseTimeY, 6f) - 0.5f) * EYE_X;
        float eyeY = (Mathf.PerlinNoise(_noiseTimeY, 7f) - 0.5f) * EYE_Y;
        SetParameter("ParamEyeBallX", eyeX);
        SetParameter("ParamEyeBallY", eyeY);

        // === 昼夜/天气基调表情 ===
        if (_timeController != null)
        {
            float nightDroop = 0f;
            if (_timeController.isSleepyTime) nightDroop = 0.15f;       // 22~5点：眼皮微垂
            else if (_timeController.isNight) nightDroop = 0.07f;       // 18~22点：轻微
            if (nightDroop > 0f && !_isBlinking)
            {
                SetParameter("ParamEyeLOpen", Mathf.Lerp(1f, 0.7f, nightDroop));
                SetParameter("ParamEyeROpen", Mathf.Lerp(1f, 0.7f, nightDroop));
            }

            // 天气基调
            if (_timeController.weatherFetched)
            {
                var wt = _timeController.weather;
                if (wt == TimeWeatherController.WeatherType.Rain ||
                    wt == TimeWeatherController.WeatherType.Drizzle ||
                    wt == TimeWeatherController.WeatherType.Thunder ||
                    wt == TimeWeatherController.WeatherType.Overcast)
                {
                    // 阴雨 → 轻度委屈：眉毛微抬 + 嘴巴微嘟
                    SetParameter("ParamBrowRY", Mathf.Lerp(0f, 4f, 0.3f));
                    SetParameter("ParamBrowLY", Mathf.Lerp(0f, 4f, 0.3f));
                    SetParameter("ParamMouthForm", Mathf.Lerp(0f, 0.2f, 0.3f));
                }
                else if (wt == TimeWeatherController.WeatherType.Clear ||
                         wt == TimeWeatherController.WeatherType.Cloudy)
                {
                    // 晴/多云 → 自然微笑
                    SetParameter("ParamMouthForm", Mathf.Lerp(0f, 0.2f, 0.2f));
                }
                else if (wt == TimeWeatherController.WeatherType.Snow)
                {
                    // 下雪 → 微微张嘴（好奇）
                    SetParameter("ParamMouthOpenY", Mathf.Lerp(0f, 0.4f, 0.2f));
                    SetParameter("ParamEyeLOpen", Mathf.Lerp(1f, 1.2f, 0.15f));
                    SetParameter("ParamEyeROpen", Mathf.Lerp(1f, 1.2f, 0.15f));
                }
            }
        }

        // === 每帧强制清零眼睛白覆盖层（防 Cubism 物理/默认值覆盖）===
        SetParameter("Param132", 0f); // 白色覆盖层（使眼变白）
        // Param63-71 是眼睛高光/光点层，设 1f 保留高光使眼睛有神
        SetParameter("Param63", 1f);  // 高光R1
        SetParameter("Param64", 1f);  // 高光R2
        SetParameter("Param65", 1f);  // 光点R1
        SetParameter("Param67", 1f);  // 高光L1
        SetParameter("Param68", 1f);  // 高光L2
        SetParameter("Param69", 1f);  // 光点R2
        SetParameter("Param70", 1f);  // 光点L1
        SetParameter("Param71", 1f);  // 光点L2

        // === 空闲动作：加权随机选取（权重越高越容易出现）===
        // 动作: 1=歪头, 2=微笑, 3=挑眉, 4=星辉, 5=伸懒腰, 6=委屈, 7=法阵, 8=害羞

        // ★ 暂停时（菜单打开），不播新动作
        bool isPaused = (_pet != null && _pet.isPaused);

        // 动作冷却衰减
        if (_idleActionCooldown > 0f) _idleActionCooldown -= Time.deltaTime;

        // 当前动作结束后，加权随机选取下一个（走路/动作锁定时不触发，需冷却）
        if (_currentIdleAction == 0 && !isPaused && !_actionLocked && _idleActionCooldown <= 0f)
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
                case 6: UpdateCry(); break;
                case 7: UpdateMagicCircle(); break;
                case 8: UpdateBlush(); break;
                case 9: UpdateConfuse(); break;
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
        if (_idleActionTime >= duration) ResetIdleAction(true);
    }

    /// <summary>动作2: 微笑 — 眯眼微笑</summary>
    private void UpdateIdleSmile()
    {
        float duration = 2f;
        float t = Mathf.Sin(_idleActionTime / duration * Mathf.PI);
        SetParameter("ParamEyeLSmile", t * IDLE_SMILE);
        SetParameter("ParamEyeRSmile", t * IDLE_SMILE);
        SetParameter("ParamMouthForm", t * IDLE_MOUTH);
        if (_idleActionTime >= duration) ResetIdleAction(true);
    }

    /// <summary>动作3: 挑眉 — 眉毛微动</summary>
    private void UpdateIdleBrow()
    {
        float duration = 2f;
        float t = Mathf.Sin(_idleActionTime / duration * Mathf.PI);
        SetParameter("ParamBrowRY", t * IDLE_BROW_Y);
        SetParameter("ParamBrowLY", t * IDLE_BROW_Y);
        if (_idleActionTime >= duration) ResetIdleAction(true);
    }

    /// <summary>
    /// 动作4: 星辉环绕 ✨
    /// 五阶段硬编码：星现→伸手→触摸→满盈→归位
    /// 与 ActionPresetPlayer 无关，纯 idle switch 驱动
    /// </summary>
    private void UpdateStarSpin()
    {
        float p = _complexActionPhase; // 秒
        float duration = SPIN_DURATION; // 6f
        float t = Mathf.Clamp01(p / duration);

        if (t >= 1f) { ResetIdleAction(true); return; }

        // === 阶段边界（秒） ===
        const float P1 = 0.8f;   // Phase1: 星现·抬头
        const float P2 = 2.5f;   // Phase2: 凝视·伸手
        const float P3 = 3.5f;   // Phase3: 触摸星辰
        const float P4 = 4.5f;   // Phase4: 星辉满盈
        // Phase5: 4.5→5.5s 星隐·归位

        // 阶段内归一化进度 0→1
        float p1 = Mathf.Clamp01(p / P1);
        float p2 = Mathf.Clamp01((p - P1) / (P2 - P1));
        float p3 = Mathf.Clamp01((p - P2) / (P3 - P2));
        float p4 = Mathf.Clamp01((p - P3) / (P4 - P3));
        float p5 = Mathf.Clamp01((p - P4) / (duration - P4));

        float easeP1 = EaseOutQuad(p1);
        float easeP2 = EaseOutQuad(p2);
        float easeP3 = EaseOutQuad(p3);
        float easeP4 = EaseOutQuad(p4);
        float easeP5 = EaseOutQuad(p5);

        // ★ 腰不动 — 全程固定
        SetParameter("ParamBodyAngleZ", 2f);

        // ===== 星星参数 =====
        float starVis, starSize, outerScale, outerAppear, plate;

        if (p < P1)
        {
            starVis = easeP1;           // 0→1
            starSize = easeP1 * 0.5f;   // 0→0.5
            outerScale = 0f;
            outerAppear = 0f;
            plate = 0f;
        }
        else if (p < P2)
        {
            starVis = 1f;
            starSize = 0.5f + easeP2 * 0.7f;  // 0.5→1.2
            outerScale = easeP2 * 0.8f;        // 0→0.8
            outerAppear = 0f;
            plate = 0f;
        }
        else if (p < P3)
        {
            starVis = 1f;
            starSize = 1.2f;
            outerScale = 0.8f + easeP3 * 0.7f; // 0.8→1.5
            outerAppear = easeP3;               // 0→1
            plate = 0f;
        }
        else if (p < P4)
        {
            starVis = 1f;
            starSize = 1.2f;
            outerScale = 1.5f * (1f - easeP4); // 1.5→0
            outerAppear = 1f - easeP4;          // 1→0
            plate = easeP4 * 0.7f;              // 0→0.7
        }
        else
        {
            // Phase5: 所有星星归零
            float f = 1f - easeP5; // 1→0
            starVis = f;
            starSize = f * 1.2f;
            outerScale = 0f;
            outerAppear = 0f;
            plate = f * 0.7f;
        }

        _mapper.Set("star_visibility",         starVis);
        _mapper.Set("star_size",               starSize);
        _mapper.Set("star_outer_scale",        outerScale);
        _mapper.Set("star_outer_appear",       outerAppear);
        _mapper.Set("star_plate_transparency", plate);

        // ===== 表情 =====
        float eyeSmile, mouthForm, browUp, eyeLOpen, eyeROpen;
        float eyeBX, eyeBY;

        if (p < P1)
        {
            // 惊喜：睁大眼，眼珠向上，眉上扬，嘴微张
            eyeLOpen = 1f;
            eyeROpen = 1f;
            eyeBX = 0f;
            eyeBY = -easeP1 * 0.5f;
            eyeSmile = 0f;
            mouthForm = easeP1 * 0.2f;
            browUp = easeP1;
        }
        else if (p < P2)
        {
            // 笑纹渐现，眼珠看右上
            eyeLOpen = 1f;
            eyeROpen = 1f;
            eyeBX = easeP2 * 0.3f;
            eyeBY = -0.5f + easeP2 * 0.1f;  // -0.5→-0.4
            eyeSmile = easeP2 * 0.3f;
            mouthForm = 0.2f + easeP2 * 0.1f; // 0.2→0.3
            browUp = 1f;
        }
        else if (p < P3)
        {
            // 笑意加深，眼珠随星星摆动（sin）
            float sway = Mathf.Sin(p * 3f) * 0.15f;
            eyeLOpen = 1f;
            eyeROpen = 1f;
            eyeBX = 0.3f + sway;              // 0.3±0.15
            eyeBY = -0.4f;
            eyeSmile = 0.3f + easeP3 * 0.3f;  // 0.3→0.6
            mouthForm = 0.3f + easeP3 * 0.2f; // 0.3→0.5
            browUp = 1f - easeP3 * 0.3f;      // 1→0.7
        }
        else if (p < P4)
        {
            // 微笑最甜，眼睛回中
            eyeLOpen = 1f;
            eyeROpen = 1f;
            eyeBX = 0.3f * (1f - easeP4);
            eyeBY = -0.4f + easeP4 * 0.2f;    // -0.4→-0.2
            eyeSmile = 0.6f + easeP4 * 0.2f;  // 0.6→0.8
            mouthForm = 0.5f + easeP4 * 0.1f; // 0.5→0.6
            browUp = 0.7f * (1f - easeP4);    // 0.7→0
        }
        else
        {
            // 归零
            eyeLOpen = 1f;
            eyeROpen = 1f;
            eyeBX = 0f;
            eyeBY = 0f;
            eyeSmile = 0.8f * (1f - easeP5);
            mouthForm = 0.6f * (1f - easeP5);
            browUp = 0f;
        }

        SetParameter("ParamEyeLOpen", eyeLOpen);
        SetParameter("ParamEyeROpen", eyeROpen);
        SetParameter("ParamEyeBallX", eyeBX);
        SetParameter("ParamEyeBallY", eyeBY);
        SetParameter("ParamEyeLSmile", eyeSmile);
        SetParameter("ParamEyeRSmile", eyeSmile);
        SetParameter("ParamMouthForm", mouthForm);
        SetParameter("ParamBrowRY", browUp * 3f);
        SetParameter("ParamBrowLY", browUp * 3f);

        // ===== 头部 =====
        float headX, headY, headZ;

        if (p < P1)
        {
            headX = -easeP1 * 8f;     // 抬头
            headY = -easeP1 * 3f;     // 微左偏
            headZ = 0f;
        }
        else if (p < P2)
        {
            headX = -8f;
            headY = -(3f - easeP2 * 2f); // -3→-1（头转向右）
            headZ = easeP2 * 3f;         // 歪头欣赏
        }
        else if (p < P3)
        {
            // 微微摆动
            float sway = Mathf.Sin(p * 2.5f) * 1.5f;
            headX = -8f;
            headY = -1f + sway;
            headZ = 3f;
        }
        else if (p < P4)
        {
            headX = -8f;
            headY = -1f * (1f - easeP4);
            headZ = 3f * (1f - easeP4);
        }
        else
        {
            headX = -8f * (1f - easeP5);
            headY = 0f;
            headZ = 0f;
        }

        SetParameter("ParamAngleX", headX);
        SetParameter("ParamAngleY", headY);
        SetParameter("ParamAngleZ", headZ);

        // ===== 身体 =====
        float bodyX;

        if (p < P1)
            bodyX = -easeP1 * 3f;
        else if (p < P4)
            bodyX = -6f;              // 后仰加深
        else
            bodyX = -6f * (1f - easeP5);

        SetParameter("ParamBodyAngleX", bodyX);

        // ===== 双手手臂 =====
        float armRaise;

        if (p < P1)
        {
            armRaise = easeP1;           // Phase 1: 0→1 手臂抬起
        }
        else if (p < P3)
        {
            armRaise = 1f;               // Phase 2-3: 保持抬起
        }
        else if (p < P4)
        {
            armRaise = 1f - easeP4;      // Phase 4: 1→0 慢慢放下
        }
        else
        {
            armRaise = 0f;               // Phase 5: 保持放下（防弹回）
        }

        // 右手臂（用 _mapper.Set 映射表支持换模型）
        _mapper.Set("arm_right_upper",         armRaise * 8f);
        _mapper.Set("arm_right_mid",           armRaise * 4f);
        _mapper.Set("arm_right_lower",         armRaise * 6f);
        _mapper.Set("arm_right_rotation",      armRaise * 10f);
        _mapper.Set("arm_right_base_rotation", armRaise * 4f);
        _mapper.Set("arm_right_reach",        -armRaise * 0.2f);
        _mapper.Set("arm_right_wrist_z",      -armRaise * 8f);

        // ★ 左臂无独立的旋转/位置参数，但物理系统会驱动 Param34/36/37，
        //    必须显式钳回默认值（0），否则物理残留导致手臂向右裙子背后扭曲
        SetParameter("Param34", 0f);
        SetParameter("Param36", 0f);
        SetParameter("Param37", 0f);

        // 手部图层（随 armRaise 平滑消退，不会弹起）
        float handLayer = Mathf.Min(1f, armRaise * 1.2f);
        SetHandLayer(handLayer);
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
        SetParameter("Param31", phase * 4f);   // 右臂R1（前臂）
        SetParameter("Param32", phase * 3f);   // 右臂R2
        SetParameter("Param33", phase * 5f);   // 右臂R1（上臂）
        SetParameter("Param94", phase * 10f);  // 右手上臂旋转
        SetParameter("Param97", phase * 3f);   // 右手 基础上臂旋转
        SetParameter("Param95", phase * 0.8f); // 右手 基础 上壁透视
        SetParameter("Param117", phase * 0.5f);// 右手 基础 上壁透视2
        SetParameter("Param98", phase * 0.6f); // 右手 基础下壁透视
        SetParameter("Param100", phase * 0.6f);// 右手 基础手 透视
        SetParameter("Param116", phase * 0.4f);// 透视2
        SetParameter("Param120", phase * 0.8f);// 手 向前透视效果
        SetParameter("Param108", phase * 0.8f);// 右手 基础 图层顺序
        SetParameter("Param119", phase * 0.8f);// 伸手 图层调整
        SetParameter("Param93", phase);         // 右手 基础 切换
        SetParameter("Param118", phase * 0.6f); // 右手伸出参数
        // 左臂配合（自然放下）
        SetParameter("Param34", phase * 0f);   // 左臂L1
        SetParameter("Param36", phase * 0f);   // 左臂L2
        SetParameter("Param37", phase * 0f);   // 左臂L3

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
        if (t >= 1f) ResetIdleAction(true);
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

        if (t >= 1f) ResetIdleAction(true);
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

        if (t >= 1f) ResetIdleAction(true);
    }

    /// <summary>
    /// 动作11: 困惑 🤔
    /// 歪头+皱眉+眯眼+嘴巴微张 — 像小狗听不懂时歪头那样
    /// 权重为 0，不自发触发，仅当 AI 回复困惑内容时由 AutoChat 强制调用
    /// </summary>
    private void UpdateConfuse()
    {
        float p = _complexActionPhase;
        float duration = CONFUSE_DURATION;

        float t = Mathf.Clamp01(p / duration);
        float eased = Mathf.Sin(t * Mathf.PI);

        // 歪头（经典困惑姿势）
        SetParameter("ParamAngleZ", eased * CONFUSE_TILT);

        // 头微微偏
        SetParameter("ParamAngleX", eased * CONFUSE_HEAD_SIDE);

        // 皱眉（压低眉毛）
        SetParameter("ParamBrowRY", eased * CONFUSE_BROW);
        SetParameter("ParamBrowLY", eased * CONFUSE_BROW);

        // 微微眯眼（困惑地打量）
        SetParameter("ParamEyeLOpen", 1f - eased * CONFUSE_EYE_SQUINT);
        SetParameter("ParamEyeROpen", 1f - eased * CONFUSE_EYE_SQUINT);

        // 嘴巴微张
        SetParameter("ParamMouthForm", eased * CONFUSE_MOUTH);

        // 身体微侧
        SetParameter("ParamBodyAngleZ", eased * CONFUSE_BODY_SIDE);

        if (t >= 1f) ResetIdleAction(true);
    }

    /// <summary>
    /// 法阵显现 ✨
    /// 五阶段：举过头顶→剑指成型→指尖凝光→扩散至全屏→消散
    /// 视觉特效包括：黑幕淡入、镜头推近、紫环旋转、白圈扩散、星星闪烁、眼镜发光
    /// </summary>
    // Ease 辅助函数
    private float EaseOutQuad(float x) { return 1f - (1f - x) * (1f - x); }
    private float EaseInQuad(float x) { return x * x; }
    private float EaseInCubic(float x) { return x * x * x; }

    private void UpdateMagicCircle()
    {
        float p = _complexActionPhase;
        float duration = CIRCLE_DURATION;
        float t = Mathf.Clamp01(p / duration);

        // ★ 每帧强制重置 GPU MultiplyColor，防 CubismRenderController（order 10000）
        //   在上一帧写回的非白色 SharedPropertyBlock 覆盖 ArtMesh 眼色。
        ForceRefreshModelAfterFade();

        // ===== 身体姿态（全阶段 + 物理飘动） =====
        // 弹簧初始化
        if (p == 0f)
        {
            _magicSpringPosX = 0f; _magicSpringVelX = 0f;
            _magicSpringPosH = 0f; _magicSpringVelH = 0f;
            _magicFloatPhase = 0f;
            _magicModeTimer = 0f;
            _magicPrevCamX = 0f;
            _magicHairSmooth = 0f;
            _magicHairSmoothVel = 0f;
            _magicModeDuration = Random.Range(2f, 5f);
            _magicModeBouncy = Random.value > 0.5f;
            _magicDamping = _magicModeBouncy ? CIRCLE_SPRING_DAMP_BOUNCE : CIRCLE_SPRING_DAMP_SOFT;
        }

        // 更新 Perlin 噪声目标 + Spring-Damper 物理
        float mdt = Time.deltaTime;
        _magicFloatPhase += mdt * CIRCLE_FLOAT_SPEED;
        _magicModeTimer += mdt;
        if (_magicModeTimer >= _magicModeDuration)
        {
            _magicModeTimer = 0f;
            _magicModeDuration = Random.Range(2f, 5f);
            _magicModeBouncy = Random.value > 0.5f;
            _magicDamping = _magicModeBouncy ? CIRCLE_SPRING_DAMP_BOUNCE : CIRCLE_SPRING_DAMP_SOFT;
        }

        // ★ 弹簧力也随 angleFade 渐入，防止在隐藏期积累动量后突然弹出
        float angleFade = Mathf.Clamp01(p / CIRCLE_ANGLE_RAMP_DUR);
        float springFade = angleFade * angleFade; // 二次曲线：弹簧更慢建立
        float targetX = (Mathf.PerlinNoise(_magicFloatPhase, 0f) - 0.5f) * CIRCLE_FLOAT_AMPLITUDE_BODY;
        float targetH = (Mathf.PerlinNoise(_magicFloatPhase, 1f) - 0.5f) * CIRCLE_FLOAT_AMPLITUDE_HEAD;
        float forceX = springFade * (CIRCLE_SPRING_STIFF * (targetX - _magicSpringPosX) - _magicDamping * _magicSpringVelX);
        _magicSpringVelX += forceX * mdt;
        _magicSpringPosX += _magicSpringVelX * mdt;
        float forceH = springFade * (CIRCLE_SPRING_STIFF * (targetH - _magicSpringPosH) - _magicDamping * _magicSpringVelH);
        _magicSpringVelH += forceH * mdt;
        _magicSpringPosH += _magicSpringVelH * mdt;

        // 角度渐入（续：angleFade 在上面已计算）

        SetParameter("ParamBodyAngleX", angleFade * (CIRCLE_BODY_ANGLE_X + _magicSpringPosX));
        SetParameter("ParamAngleX", angleFade * (CIRCLE_HEAD_ANGLE_X + _magicSpringPosH));
        SetParameter("ParamBodyAngleZ", 0);

        // ===== 视觉特效 — 三幕结构 =====
        // Act 1 (t=0→0.40): 斜上升 — 手举过头顶→剑指成型，镜头推近到最大值
        // Act 2 (t=0.40→0.65): 悬停亮阵 — 镜头固定最远，全屏爆发，法阵峰值
        // Act 3 (t=0.65→1.0): 回正消散 — 镜头回正，一切渐消
        const float P1 = 0.20f;  // Act1: 举过头顶
        const float P2 = 0.40f;  // Act1: 剑指成型到达最高点
        const float P3 = 0.65f;  // Act2: 悬停结束 → Act3 开始
        // P4 = 1.00f 消散完毕

        // 星星
        float starVis = 0f, starSize = 0f, outerScale = 0f, outerAppear = 0f;

        // 紫环
        float ringOuterVis = 0f, ringMidVis = 0f, ringInnerVis = 0f;
        float ringOuterRot = 0f, ringMidRot = 0f, ringInnerRot = 0f;
        float ringOuterSize = 0f, ringMidSize = 0f, ringInnerSize = 0f;

        // 黑幕/白圈
        float darkScreen = 0f, darkAppear = 0f;
        float whiteCircle = 0f, whiteCircleSize = 0f;
        float whiteX = 0f, whiteY = 0f;

        // 镜头
        float camX = 0f, camY = 0f, charScale = 1f;

        // 眼镜发光
        float eyeOpen = 1f; // 睁眼幅度（>1=睁大，瞳孔露出更多→更亮）

        // ==== Act 1: 斜上升 (t=0→0.40) ====
        // ---- P1: 举过头顶（手势淡入）----
        if (t < P1)
        {
            float h = EaseInCubic(t / P1);

            SetHandPose(h);
            SetSwordFinger(h);
            SetHandLayer(h);
            SetParameter("Param92", h);

            // 星星淡入
            starVis = h;
            starSize = h * 0.3f;

            // 黑幕淡入
            darkScreen = h * 0.08f;
            darkAppear = h * 0.05f;

            // 镜头微推
            charScale = 1f + h * 0.08f;
            camX = h * 2f;
            camY = h * 1f;
        }
        // ---- P2: 剑指成型→举到最高 ----
        else if (t < P2)
        {
            float h = (t - P1) / (P2 - P1);
            float eased = EaseOutQuad(h);

            SetHandPose(1f);
            SetSwordFinger(1f);
            SetHandLayer(1f);
            SetParameter("Param92", 1f);

            // 星星全亮
            starVis = 1f;
            starSize = 0.3f + eased * 0.5f;
            outerScale = eased * 0.6f;
            outerAppear = eased * 0.5f;

            // 紫环显现
            ringOuterVis = eased;
            ringMidVis = eased * 0.7f;
            ringInnerVis = eased * 0.4f;
            ringOuterRot = eased * 30f;
            ringMidRot = eased * 45f;
            ringInnerRot = eased * 60f;
            ringOuterSize = 1f + eased * 0.5f;
            ringMidSize = 1f + eased * 0.3f;
            ringInnerSize = 1f + eased * 0.2f;

            // 黑幕加深
            darkScreen = 0.08f + eased * 0.12f;
            darkAppear = 0.05f + eased * 0.08f;

            // 镜头推近到最大
            charScale = 1.08f + eased * 0.27f;
            camX = 2f + eased * 6f;
            camY = 1f + eased * 4f;
        }
        // ==== Act 2: 悬停亮阵 (t=0.40→0.65) ====
        else if (t < P3)
        {
            float h = (t - P2) / (P3 - P2); // 0→1
            float eased = EaseOutQuad(h);

            // 手势保持在最高位
            SetHandPose(1f);
            SetSwordFinger(1f);
            SetHandLayer(1f);
            SetParameter("Param92", 1f);

            // 镜头固定在最远位置（不变）
            charScale = 1.35f;
            camX = 8f;
            camY = 5f;

            // 星星：前1/3淡出星星→七星盘，后2/3最大
            if (h < 0.33f)
            {
                float s = h / 0.33f;
                starVis = 1f - s * 0.5f;
                starSize = 0.8f - s * 0.03f;
                outerScale = 1f * (1f - s);
                outerAppear = 1f * (1f - s);
            }
            else
            {
                starVis = 0.5f;
                starSize = 0.77f;
                outerScale = 0f;
                outerAppear = 0f;
            }

            // 紫环：加速旋转到峰值转速（全屏爆发）
            ringOuterVis = 1f;
            ringMidVis = Mathf.Lerp(0.7f, 1f, eased);
            ringInnerVis = Mathf.Lerp(0.4f, 0.6f, eased);
            ringOuterRot = 30f + eased * 120f;
            ringMidRot = 45f + eased * 180f;
            ringInnerRot = 60f + eased * 240f;
            ringOuterSize = 1.5f + eased * 2.3f; // 扩散
            ringMidSize = 1.3f + eased * 2.0f;
            ringInnerSize = 1.2f + eased * 1.5f;

            // 黑幕维持最深
            darkScreen = 0.25f;
            darkAppear = 0.13f;

            // 白圈全屏展开
            whiteCircle = 0.08f + eased * 0.17f;
            whiteCircleSize = 0.05f + eased * 0.30f;
            whiteX = eased * 0.5f;
            whiteY = eased * 0.4f;
        }
        // ==== Act 3: 回正消散 (t=0.65→1.0) ====
        else
        {
            float h = (t - P3) / (1f - P3); // 0→1
            float fade = 1f - EaseOutQuad(h); // 1→0

            // 手脚渐收
            float handFade = 1f - h;
            SetHandPose(handFade);
            SetSwordFinger(handFade);
            SetHandLayer(handFade);
            SetParameter("Param92", handFade);

            // 身体角度消退
            float fadeBody = fade * fade;
            SetParameter("ParamBodyAngleX", fadeBody * angleFade * (CIRCLE_BODY_ANGLE_X + _magicSpringPosX));
            SetParameter("ParamAngleX", fadeBody * angleFade * (CIRCLE_HEAD_ANGLE_X + _magicSpringPosH));

            // 星星渐消
            starVis = fade * 0.5f;
            starSize = fade * 0.77f;
            outerScale = 0f;
            outerAppear = 0f;

            // 紫环渐消
            ringOuterVis = 1f - h;
            ringMidVis = (1f - h * 0.7f);
            ringInnerVis = (1f - h * 0.5f);
            ringOuterSize = 3.8f + h * 2f;
            ringMidSize = 3.3f + h * 1.5f;
            ringInnerSize = 2.7f + h * 1f;
            ringOuterRot = 150f + h * 60f;
            ringMidRot = 225f + h * 90f;
            ringInnerRot = 300f + h * 120f;

            // 黑幕渐消
            darkScreen = fade * 0.25f;
            darkAppear = fade * 0.13f;

            // 白圈渐消
            whiteCircle = fade * 0.25f;
            whiteCircleSize = 0.35f + h * 0.3f;
            whiteX = fade * 0.5f;
            whiteY = fade * 0.4f;

            // 镜头回正（h=0时camX=8,fade=1; h=1时camX=0,fade=0）
            charScale = 0.8775f + h * 0.1225f;
            camX = 8f * fade;
            camY = 5f * fade;

            if (t >= 1f)
            {
                Debug.LogWarning($"[Live2DRenderer] ⚠ MagicCircle 结束: p={p:F4}, t={t:F4}, complexActionPhase={_complexActionPhase:F4}, dur={duration}\n"
                    + new System.Diagnostics.StackTrace().ToString());
                ResetIdleAction(true);
            }
        }

        // ===== 应用视觉特效参数 =====
        // 星星
        SetParameter("Param451", starVis); // star_visibility
        SetParameter("Param541", starSize); // star_size
        SetParameter("Param1071", outerScale); // star_outer_scale
        SetParameter("Param1081", outerAppear); // star_outer_appear

        // 紫环
        SetParameter("Param421", ringOuterRot); // 外紫环 转
        SetParameter("Param431", ringOuterVis); // 外紫环显隐
        SetParameter("Param441", ringOuterSize); // 外紫环大小
        SetParameter("Param901", ringMidRot); // 中紫环 转
        SetParameter("Param911", ringMidVis); // 中紫环显隐
        SetParameter("Param961", ringMidSize); // 中紫环大小
        SetParameter("Param881", ringInnerRot); // 内紫环 转
        SetParameter("Param741", ringInnerVis); // 内紫环显隐
        SetParameter("Param731", ringInnerSize); // 内紫环大小

        // 黑幕
        SetParameter("Param121", darkScreen); // 黑幕切换
        SetParameter("Param137", darkAppear); // 黑幕 透明显现

        // 白圈
        SetParameter("Param136", whiteCircle); // 白圈不透明度
        SetParameter("Param133", whiteCircleSize); // 白圈 大小
        SetParameter("Param134", whiteX); // 白圈 位移x
        SetParameter("Param135", whiteY); // 白圈 位移Y

        // 镜头
        SetParameter("Param155", camX); // 镜头 X
        SetParameter("Param156", camY); // 镜头Y
        SetParameter("Param157", charScale); // 人物缩小放大

        // ===== camX 速度驱动头发/衣服/配饰（反向拖拽）=====
        // 取 camX 每帧变化量 → 瞬时速度。移动时才有反向拖拽，
        // 稳定时（即使在远端）速度=0 → 头发归零，始终保持中间态。
        float dt = Time.deltaTime;
        float camVel = (dt > 0f) ? (camX - _magicPrevCamX) / dt : 0f;
        _magicPrevCamX = camX;
        _magicHairSmooth = Mathf.SmoothDamp(_magicHairSmooth, camVel, ref _magicHairSmoothVel, CIRCLE_HAIR_SMOOTH);
        float smoothVel = _magicHairSmooth; // 平滑后的速度，正=向右移
        float headHairV = Mathf.Clamp(-smoothVel * CIRCLE_HEAD_HAIR_FROM_VEL, -CIRCLE_HEAD_HAIR_MAX, CIRCLE_HEAD_HAIR_MAX);
        float braidV    = Mathf.Clamp(-smoothVel * CIRCLE_BRAID_FROM_VEL, -CIRCLE_BRAID_MAX, CIRCLE_BRAID_MAX);
        float ornamentV = Mathf.Clamp(-smoothVel * CIRCLE_ORNAMENT_FROM_VEL, -CIRCLE_ORNAMENT_MAX, CIRCLE_ORNAMENT_MAX);
        float hair169V  = Mathf.Clamp(-smoothVel * CIRCLE_HEAD_ORNAMENT_FROM_VEL, -CIRCLE_HEAD_ORNAMENT_MAX, CIRCLE_HEAD_ORNAMENT_MAX);
        // ★ 头部头发（刘海/头发物理2/鬓发/后短发）：10%原幅
        SetParameter("Param5", headHairV);  SetParameter("Param7", headHairV);
        SetParameter("Param9", headHairV);  SetParameter("Param11", headHairV);
        SetParameter("Param14", headHairV); SetParameter("Param17", headHairV);
        SetParameter("Param19", headHairV); SetParameter("Param21", headHairV);
        SetParameter("Param23", headHairV); SetParameter("Param35", headHairV);
        SetParameter("Param41", headHairV);
        // ★ 后发/辫子（后发a/b/c/d）：40%原幅
        SetParameter("Param43", braidV);    SetParameter("Param45", braidV);
        SetParameter("Param55", braidV);    SetParameter("Param62", braidV);
        // ★ 发饰品（发簪/发饰）：40%原幅
        SetParameter("Param91", ornamentV); SetParameter("Param74", ornamentV);
        SetParameter("Param89", ornamentV);
        // ★ Param169 头饰：40%原幅
        SetParameter("Param169", hair169V);
        float clothV = Mathf.Clamp(-smoothVel * CIRCLE_CLOTH_FROM_VEL, -CIRCLE_CLOTH_MAX, CIRCLE_CLOTH_MAX);
        SetParameter("Param82", clothV);  SetParameter("Param87", clothV);
        SetParameter("Param84", clothV * 0.6f);
        SetParameter("Param49", clothV);  SetParameter("Param51", clothV);
        SetParameter("Param57", clothV);  SetParameter("Param60", clothV);
        // ★ 保存计算值供 ForceUpdateNow 后重新应用（Physics 会覆盖头发参数）
        _magicHeadHairV = headHairV;
        _magicBraidV = braidV;
        _magicOrnamentV = ornamentV;
        _magicHair169V = hair169V;
        _magicClothV = clothV;

        // 眼睛——正常睁眼，不泛白
        // ★ Param132="白色覆盖层（使眼变白）"，非零即显白，法阵全程保持 0
        SetParameter("Param132", 0f);
        // Param63-71 是眼睛高光/光点层，设 1f 保留高光使眼睛有神
        SetParameter("Param63", 1f);
        SetParameter("Param64", 1f);
        SetParameter("Param65", 1f);
        SetParameter("Param67", 1f);
        SetParameter("Param68", 1f);
        SetParameter("Param69", 1f);
        SetParameter("Param70", 1f);
        SetParameter("Param71", 1f);
        // ★ 适度睁大瞳孔（露出瞳孔颜色），自然亮不泛白
        SetParameter("ParamEyeLOpen", eyeOpen);
        SetParameter("ParamEyeROpen", eyeOpen);
    }

    /// <summary>设置剑指手指参数（h=0~1 控制强度，用于淡入淡出，不设Param92防10指重叠）</summary>
    private void SetSwordFinger(float h)
    {
        SetParameter("Param102", 0f);
        SetParameter("Param103", 0f);
        SetParameter("Param105", 0f);
        SetParameter("Param106", 0f);
        SetParameter("Param107", 0f);
        SetParameter("Param111", h * 0.2f);
        SetParameter("Param112", h * 1f);
        SetParameter("Param113", h * 1f);
        SetParameter("Param114", h * 0.2f);
        SetParameter("Param115", h * 0.2f);
        SetParameter("Param110", h * -0.5f);
    }

    /// <summary>设置剑指单手姿势（右手剑指，h=0~1 控制强度，用于淡入淡出）
    /// ★ 左手不抬起（保持自然下垂），符玄法阵是右手单手指天</summary>
    private void SetHandPose(float h)
    {
        // 右手（改为顺时针）
        SetParameter("Param94", h * -4.84f);
        SetParameter("Param97", h * -27.42f);
        SetParameter("Param93", h * 1f); // 右手切换→剑指模式
        SetParameter("Param118", h * -0.32f);
        SetParameter("Param99", h * -18.71f);
        SetParameter("Param38", h * 0f);
        SetParameter("Param39", h * 0f);
        SetParameter("Param31", h * -8f);
        SetParameter("Param32", h * -6f);  // 右臂R2
        SetParameter("Param33", h * -10f); // 右臂R1（上臂）
        // ★ 左手保持0（不下垂也不抬起），维持自然体态
        SetParameter("Param34", 0f);
        SetParameter("Param36", 0f);
        SetParameter("Param37", 0f);
    }

    /// <summary>设置手部透视/图层（不最高，让手自然融入身体）</summary>
    private void SetHandLayer(float layer)
    {
        SetParameter("Param95", layer * 0.6f);
        SetParameter("Param117", layer * 0.5f);
        SetParameter("Param98", layer * 0.5f);
        SetParameter("Param100", layer * 0.5f);
        SetParameter("Param116", layer * 0.4f);
        SetParameter("Param120", layer * 0.6f);
        SetParameter("Param108", layer * 0.6f);
        SetParameter("Param119", layer * 0.6f);
    }

    // ================================================================
    // ★ 左臂(Param34/36/37) 物理权重管理
    //    "手臂L"子刚体内部有弹簧动量（Particles[].Velocity 等），
    //    即使我们在 C# 设 Param34=0，物理下一帧评估时仍会产出非零输出。
    //    通过暂存并归零输出权重，让物理输出乘以 0 后不写入参数值。
    // ================================================================

    /// <summary>保存"手臂L"当前输出权重到备份数组，然后设所有权重=0</summary>
    private void SaveLeftArmWeights()
    {
        if (_leftArmSubRig == null || _leftArmSubRig.Output == null) return;

        int n = _leftArmSubRig.Output.Length;
        _savedLeftArmWeights = new float[n];
        for (int i = 0; i < n; i++)
        {
            _savedLeftArmWeights[i] = _leftArmSubRig.Output[i].Weight;
            _leftArmSubRig.Output[i].Weight = 0f;
        }
        _leftArmPhysicsSaved = true;
    }

    /// <summary>恢复"手臂L"输出权重为备份值</summary>
    private void RestoreLeftArmWeights()
    {
        if (_leftArmSubRig == null || _leftArmSubRig.Output == null) return;
        if (_savedLeftArmWeights == null) { _leftArmPhysicsSaved = false; return; }

        int n = Mathf.Min(_savedLeftArmWeights.Length, _leftArmSubRig.Output.Length);
        for (int i = 0; i < n; i++)
        {
            _leftArmSubRig.Output[i].Weight = _savedLeftArmWeights[i];
        }
        _savedLeftArmWeights = null;
        _leftArmPhysicsSaved = false;
    }

    /// <summary>归零"手臂L"输出权重（每帧在动作持续期间执行）</summary>
    private void ZeroLeftArmWeights()
    {
        if (_leftArmSubRig == null || _leftArmSubRig.Output == null) return;

        for (int i = 0; i < _leftArmSubRig.Output.Length; i++)
        {
            _leftArmSubRig.Output[i].Weight = 0f;
        }
    }

    /// <summary>重置空闲动作，清理参数</summary>
    /// <param name="force">true=强制重置（动作自然完成时用，跳过 _actionLocked 守卫）</param>
    private void ResetIdleAction(bool force = false)
    {
        // 🛡️ 防御性守卫：强制动作锁定期间禁止重置
        //     防止某个尚未确定的代码路径在强制动作（如法阵#7）播放期间
        //     意外调用 ResetIdleAction()，导致动作提前终止。
        // ✅ force=true 时跳过守卫（动作自然结束时的合法清理）
        if (_actionLocked && !force)
        {
            Debug.LogWarning("[Live2DRenderer] 🛡️ ResetIdleAction 被阻断: _actionLocked=" + _actionLocked
                + ", _currentIdleAction=" + _currentIdleAction
                + "\n" + new System.Diagnostics.StackTrace().ToString());
            return;
        }

        // ★ 恢复 OverrideFlag，让 CubismRenderController 重新控制 MultiplyColor
        //    ForceRefreshModelAfterFade() 设了 OverrideFlag=true 防止法阵期间覆盖，
        //    动作结束后必须恢复，否则表情/预设再也无法改变 ArtMesh 颜色 → 眼白发白。
        TryRestoreOverrideFlag();

        // ★ 保存当前动作ID（在清零前），用于特殊冷却判断
        int prevAction = _currentIdleAction;

        bool wasLocked = _actionLocked;
        _actionLocked = false;

        _currentIdleAction = 0;
        _idleActionTime = 0f;
        _complexActionPhase = 0f;
        // ★ 法阵（动作7）播完后长冷却，防立即重播
        _idleActionCooldown = (prevAction == 7) ? 60f : 1.5f;

        // ★ 新系统：停止表情淡出（避免残留）
        StopExpression(0.15f);

        // 清理可能被改过的参数（表情/特殊参数）
        SetParameter("ParamAngleZ", 0f);
        SetParameter("ParamEyeLSmile", 0f);
        SetParameter("ParamEyeRSmile", 0f);
        SetParameter("ParamMouthForm", 0f);
        SetParameter("ParamBrowRY", 0f);
        SetParameter("ParamBrowLY", 0f);
        SetParameter("ParamEyeLOpen", 1f);
        SetParameter("ParamEyeROpen", 1f);
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
        SetParameter("Param31", 0f);  // 右手臂R1（前臂）
        SetParameter("Param32", 0f);  // 右手臂R2
        SetParameter("Param33", 0f);  // 右手臂R1（上臂）
        SetParameter("Param93", 0f);  // 右手基础切换
        SetParameter("Param34", 0f);  // 左手手臂L1
        SetParameter("Param36", 0f);  // 左手手臂L2
        SetParameter("Param37", 0f);  // 左手手臂L3
        SetParameter("Param401", 0f); // 外蒙版
        SetParameter("Param104", 0f); // 生气
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
        // 正常五指清零（防Param92切回普通模式时有残留值）
        SetParameter("Param102", 0f);
        SetParameter("Param103", 0f);
        SetParameter("Param105", 0f);
        SetParameter("Param106", 0f);
        SetParameter("Param107", 0f);
        // 法阵视觉特效参数清零
        SetParameter("Param155", 0f); // 镜头X
        SetParameter("Param156", 0f); // 镜头Y
        SetParameter("Param157", 0f); // 人物缩小放大
        // 星星特效参数
        SetParameter("Param451", 0f); // star_visibility
        SetParameter("Param541", 0f); // star_size
        SetParameter("Param1071", 0f); // star_outer_scale
        SetParameter("Param1081", 0f); // star_outer_appear
        SetParameter("Param154", 0f); // star_plate_transparency
        // 紫环特效参数
        SetParameter("Param421", 0f); // 外紫环旋转
        SetParameter("Param431", 0f); // 外紫环显隐
        SetParameter("Param441", 0f); // 外紫环大小
        SetParameter("Param901", 0f); // 中紫环旋转
        SetParameter("Param911", 0f); // 中紫环显隐
        SetParameter("Param961", 0f); // 中紫环大小
        SetParameter("Param881", 0f); // 内紫环旋转
        SetParameter("Param741", 0f); // 内紫环显隐
        SetParameter("Param731", 0f); // 内紫环大小
        // 黑幕/白圈/发光
        SetParameter("Param121", 0f); // 黑幕切换
        SetParameter("Param137", 0f); // 黑幕透明度
        SetParameter("Param136", 0f); // 白圈不透明度
        SetParameter("Param133", 0f); // 白圈大小
        SetParameter("Param134", 0f); // 白圈位移x
        SetParameter("Param135", 0f); // 白圈位移y
        SetParameter("Param132", 0f); // 眼镜发光（白色覆盖层）
        // Param63-71 是眼睛高光/光点层，设 1f 保留高光使眼睛有神
        SetParameter("Param63", 1f); // 高光R1
        SetParameter("Param64", 1f); // 高光R2
        SetParameter("Param67", 1f); // 高光L1
        SetParameter("Param68", 1f); // 高光L2
        SetParameter("Param65", 1f); // 光点R1
        SetParameter("Param69", 1f); // 光点R2
        SetParameter("Param70", 1f); // 光点L1
        SetParameter("Param71", 1f); // 光点L2

        // ★ 法阵结束时恢复 Cubism OverrideFlag，让表情等能正常修改 MultiplyColor
        if (_cubismModel != null)
        {
            var renderController = _cubismModel.GetComponent<CubismRenderController>();
            if (renderController?.Renderers != null)
            {
                foreach (var renderer in renderController.Renderers)
                {
                    renderer.OverrideFlagForDrawableMultiplyColors = false;
                    renderer.OverrideFlagForDrawableScreenColors = false;
                }
            }
            _cubismModel.ForceUpdateNow();
        }

        if (wasLocked)
        {
            Debug.LogWarning("[Live2DRenderer] 强制动作完成，触发回调\n"
                + new System.Diagnostics.StackTrace().ToString());
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

        // ★ 法阵（actionId=7）：初始化弹簧飘动状态，确保 Perlin+Spring 在第一帧生效
        if (actionId == 7)
        {
            _magicSpringPosX = 0f; _magicSpringVelX = 0f;
            _magicSpringPosH = 0f; _magicSpringVelH = 0f;
            _magicFloatPhase = Random.Range(0f, 100f); // ✓ 随机种子，确保 Perlin 产生变化
            _magicModeTimer = 0f;
            _magicPrevCamX = 0f;
            _magicHairSmooth = 0f;
            _magicHairSmoothVel = 0f;
            _magicModeDuration = Random.Range(2f, 5f);
            _magicModeBouncy = Random.value > 0.5f;
            _magicDamping = _magicModeBouncy ? CIRCLE_SPRING_DAMP_BOUNCE : CIRCLE_SPRING_DAMP_SOFT;
        }

        // ★ 强制动作时，清理可能冲突的调试偏移，防止偏移覆盖动画
        if (debugOffsetEnabled && debugOffsets != null && debugOffsets.Count > 0)
        {
            // 手部/手臂相关参数列表（与法阵/伸懒腰等动作冲突的）
            string[] handParams = new string[]
            {
                "Param33","Param31","Param32","Param94","Param97",
                "Param93","Param118","Param99","Param92",
                "Param95","Param117","Param98","Param100","Param116","Param120",
                "Param108","Param119","Param34","Param36","Param37",
                "Param110","Param111","Param112","Param113","Param114","Param115"
            };
            foreach (var name in handParams)
            {
                if (debugOffsets.ContainsKey(name))
                    debugOffsets.Remove(name);
            }
            if (debugOffsets.Count == 0)
                debugOffsetEnabled = false;
        }

        Debug.Log($"[Live2DRenderer] ▶ 强制动作 #{actionId}（锁定，不被走路覆盖）");
    }

    /// <summary>
    /// 强制播放指定动作（新系统 — 按名称，支持表情和复合动作）
    /// 名称格式: "exp:happy" = 表情, "act:stretch" = 复合动作, "idle:5" = 旧式动作
    /// </summary>
    public void ForceAction(string actionSpec, System.Action onComplete = null)
    {
        if (!_loaded || _cubismModel == null || ActionController == null)
        {
            onComplete?.Invoke();
            return;
        }

        if (actionSpec.StartsWith("exp:"))
        {
            // 表情
            string expName = actionSpec.Substring(4);
            ActionController.PlayExpression(expName);
            onComplete?.Invoke(); // 表情立即返回（淡入中）
        }
        else if (actionSpec.StartsWith("act:"))
        {
            // 复合动作 — 异步播放
            string actName = actionSpec.Substring(4);
            PlayAction(actName, onComplete);
        }
        else if (actionSpec.StartsWith("idle:"))
        {
            // 旧式动作向后兼容
            string numStr = actionSpec.Substring(5);
            if (int.TryParse(numStr, out int id))
                ForceIdleAction(id);
            onComplete?.Invoke();
        }
        else
        {
            // 裸名：先查动作，再查表情
            if (ActionController.Actions != null &&
                System.Linq.Enumerable.Contains(ActionController.Actions.AvailableActions, actionSpec))
            {
                PlayAction(actionSpec, onComplete);
            }
            else if (ActionController.Expressions != null &&
                     System.Linq.Enumerable.Contains(ActionController.Expressions.AvailableExpressions, actionSpec))
            {
                ActionController.PlayExpression(actionSpec);
                onComplete?.Invoke();
            }
            else
            {
                Debug.LogWarning($"[Live2DRenderer] 未知动作/表情: {actionSpec}");
                onComplete?.Invoke();
            }
        }
    }

    /// <summary>
    /// 按权重随机选取一个空闲动作（1-11），受时间/天气调节
    /// </summary>
    private int PickWeightedIdleAction()
    {
        // 从基值复制，然后根据昼夜/天气调节
        int[] w = new int[_idleActionWeights.Length];
        for (int i = 0; i < w.Length; i++) w[i] = _idleActionWeights[i];

        // ★ 夜间/犯困时段 → 活跃动作减少，犯困/委屈增加
        bool isNight = (_timeController != null && _timeController.isNight);
        bool isSleepy = (_timeController != null && _timeController.isSleepyTime);
        if (isNight)
        {
            w[3] = Mathf.Max(1, w[3] - 1);  // 动作4 星辉
            w[5] = w[5] + 1;                // 动作6 委屈/困
        }
        if (isSleepy)
        {
            w[5] = w[5] + 2;                // 动作6 更想睡
            w[0] = w[0] + 1;                // 动作1 歪头（没精神歪着）
        }

        // ★ 天气调节
        if (_timeController != null && _timeController.weatherFetched)
        {
            var wt = _timeController.weather;
            if (wt == TimeWeatherController.WeatherType.Rain ||
                wt == TimeWeatherController.WeatherType.Drizzle ||
                wt == TimeWeatherController.WeatherType.Thunder)
            {
                w[5] = w[5] + 1;            // 动作6 委屈（下雨天不开心）
            }
            else if (wt == TimeWeatherController.WeatherType.Clear ||
                     wt == TimeWeatherController.WeatherType.Cloudy)
            {
                w[3] = w[3] + 1;            // 动作4 星辉
            }
            else if (wt == TimeWeatherController.WeatherType.Snow)
            {
                w[2] = w[2] + 1;            // 动作3 挑眉（好奇看雪）
                w[5] = w[5] + 1;            // 动作6 委屈（冷）
            }
        }

        int totalWeight = 0;
        for (int i = 0; i < w.Length; i++)
            totalWeight += w[i];

        int roll = Random.Range(0, totalWeight);
        int cumulative = 0;
        for (int i = 0; i < w.Length; i++)
        {
            cumulative += w[i];
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

        // 调试日志（仅在编辑器中，每秒一次）
#if UNITY_EDITOR
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"[Live2DRenderer] pet({_pet.petX},{_pet.petY}) size({_pet.petWidth},{_pet.petHeight}) → screenPos({screenPos.x},{screenPos.y}) → worldPos({worldPos.x},{worldPos.y}), scale={modelScale}");
        }
#endif

        _modelRoot.transform.position = worldPos;

        // 根据朝向翻转（直接设置scale，不基于当前值）
        bool faceRight = _pet.petVx >= 0;
        Vector3 scale = new Vector3(modelScale, modelScale, 1f);
        // 拖拽中不翻转 scale.x（用 ParamBodyAngleY 平滑转身，避免鼠标微晃时模型 180° 弹跳）
        if (!_pet.isDragging)
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
    /// <summary>
    /// 走路手臂/腿摆动动画
    /// </summary>
    /// <param name="blendWeight">混合权重：1=全幅度走路，0=不设任何值（默认=1）</param>
    private void UpdateWalkAnimation(float blendWeight = 1f)
    {
        if (blendWeight <= 0f) return;
        float phase = _walkPhase;

        // ★ 腿/臂动态摆动 — 停止后渐消不需要这些
        // ★ 左腿参数
        //   左腿向前(+)时，右手臂也向前(+) — 交叉对位
        float legPhase = Mathf.Sin(phase);
        float rightPhase = -legPhase; // 右腿与左腿反相

        // 抬腿（已乘 blendWeight 消退）
        SetParameter("Param165", legPhase * WALK_LEG_LIFT * blendWeight);
        SetParameter("Param164", rightPhase * WALK_LEG_LIFT * blendWeight);

        // 前后摆动 + 弯曲
        SetParameter("Param126", legPhase * WALK_LEG_SWING * blendWeight);
        SetParameter("Param127", Mathf.Abs(legPhase) * WALK_LEG_BEND * blendWeight);

        // 右腿
        SetParameter("Param129", rightPhase * WALK_LEG_SWING * blendWeight);
        SetParameter("Param131", Mathf.Abs(rightPhase) * WALK_LEG_BEND * blendWeight);

        // ★ 右手臂与左腿同步（交叉对位：左腿前→右手前）
        //   左臂与右腿同步（右腿前→左手前），与右臂反相
        SetParameter("Param94", legPhase * WALK_ARM_BIG * blendWeight);          // 右臂 上臂旋转 (大范围)
        SetParameter("Param31", legPhase * WALK_ARM_SMALL * 0.7f * blendWeight); // 右臂R1
        SetParameter("Param32", legPhase * WALK_ARM_SMALL * 0.4f * blendWeight); // 右臂R2
        SetParameter("Param33", legPhase * WALK_ARM_SMALL * 0.4f * blendWeight); // 右臂R1上臂
        float leftArm = rightPhase * WALK_ARM_SMALL * blendWeight;
        SetParameter("Param34", leftArm * 0.7f);  // 左臂L1
        SetParameter("Param36", leftArm * 0.4f);  // 左臂L2
        SetParameter("Param37", leftArm * 0.4f);  // 左臂L3

        // 肩膀配合脚步
        SetParameter("Param153", Mathf.Abs(legPhase) * WALK_SHOULDER * blendWeight);
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

    /// <summary>
    /// 走路时随机触发困倦表情——先眯眼笑纹→再慢慢垂眼→微张嘴（打哈欠感）
    /// 夜间（18~6）触发更频繁，犯困时段（22~5）最频繁
    /// </summary>
    private void UpdateWalkExpression()
    {
        // 冷却中
        if (_walkExpressionCooldown > 0f)
        {
            _walkExpressionCooldown -= Time.deltaTime;
            return;
        }

        _walkExpressionTimer += Time.deltaTime;

        // 夜间触发更多：白天 2.5~4 秒 → 夜晚 3~5 秒 → 犯困 4~7 秒
        bool sleepyTime = _timeController != null && _timeController.isSleepyTime;
        bool nightTime = _timeController != null && _timeController.isNight;
        float duration;
        if (sleepyTime)
            duration = 4f + Random.value * 3f;
        else if (nightTime)
            duration = 3f + Random.value * 2f;
        else
            duration = 2.5f + Random.value * 1.5f;

        float t = Mathf.Clamp01(_walkExpressionTimer / duration);

        // ★ 分段曲线：0~30% 先眯眼（眼皮还睁着），30~100% 从睁眼慢慢垂到全闭
        float smilePhase = Mathf.Clamp01(t / 0.3f);
        float closePhase = Mathf.Clamp01((t - 0.3f) / 0.7f);
        float smileVal = smilePhase * smilePhase * (3f - 2f * smilePhase); // smoothstep
        float closeVal = closePhase * closePhase * closePhase;             // easeInCubic 更缓慢闭眼

        // 闭眼（先留缝再慢慢全垂）
        SetParameter("ParamEyeLOpen", Mathf.Lerp(1f, 0f, closeVal));
        SetParameter("ParamEyeROpen", Mathf.Lerp(1f, 0f, closeVal));
        // 眯眼笑纹（先出后消，配合垂眼一起消退）
        float smileDecay = smileVal * (1f - t);
        SetParameter("ParamEyeLSmile", smileDecay * 0.5f);
        SetParameter("ParamEyeRSmile", smileDecay * 0.5f);
        // 嘴微张（打哈欠感，配合闭眼节奏）
        SetParameter("ParamMouthOpenY", closeVal * 0.3f);
        // ★ 头继续下垂：走路体态已低头 8°（ApplyWalkBodyPose），困了再下垂 4° → 共 12°
        //   用 closeVal 同步节奏，不会出现一开始跳变
        SetParameter("ParamAngleY", 8f + closeVal * 4f);

        if (t >= 1f)
        {
            // 表情结束：恢复面部默认值
            SetParameter("ParamEyeLOpen", 1f);
            SetParameter("ParamEyeROpen", 1f);
            SetParameter("ParamEyeLSmile", 0f);
            SetParameter("ParamEyeRSmile", 0f);
            SetParameter("ParamMouthOpenY", 0f);
            // ★ ParamAngleY 不重置——ApplyWalkBodyPose 在 Update() 中每帧重设走路体态

            // 冷却：白天 5~13 秒 → 夜晚 3~8 秒 → 犯困 2~5 秒
            _walkExpressionTimer = 0f;
            if (sleepyTime)
                _walkExpressionCooldown = 2f + Random.value * 3f;
            else if (nightTime)
                _walkExpressionCooldown = 3f + Random.value * 5f;
            else
                _walkExpressionCooldown = 5f + Random.value * 8f;
        }
    }

    // ================================================================
    //  新动作系统（Live2DParameterMapper + ExpressionManager + ActionPresetPlayer）
    // ================================================================

    /// <summary>初始化参数映射器和动作控制器</summary>
    private void InitActionSystem()
    {
        if (_cubismModel == null) return;

        _mapper = new Live2DParameterMapper(_cubismModel);

        // 从 Resources 加载映射文件
        TextAsset mapAsset = Resources.Load<TextAsset>("Live2D/ParamMaps/fuxuan_map");
        if (mapAsset != null)
        {
            _mapper.LoadMappingFromJson(mapAsset.text);
            Debug.Log($"[Live2DRenderer] 参数映射器已加载: {_mapper.ModelName}, {_mapper.SemanticToId.Count} 条目");
        }
        else
        {
            Debug.LogWarning("[Live2DRenderer] Resources 中未找到映射文件，尝试从文件系统加载");
            string mapPath = System.IO.Path.Combine(Application.dataPath,
                "Scripts/Live2DFramework/ParamMaps/fuxuan_map.json");
            if (System.IO.File.Exists(mapPath))
            {
                _mapper.LoadMappingFromFile(mapPath);
            }
        }

        // 创建动作控制器
        ActionController = new Live2DActionController(_mapper, this);

        // 加载表情和动作预设
        ActionController.LoadAllPresets();

        Debug.Log($"[Live2DRenderer] 动作控制器已就绪，表情={ActionController.Expressions.AvailableExpressions.Count}个，动作={ActionController.Actions.AvailableActions.Count}个");
    }

    // ================================================================
    //  新动作系统公开 API — 供 AI / 右键菜单 / 外部调用
    // ================================================================

    /// <summary>播放表情（带淡入淡出）</summary>
    public void PlayExpression(string name, float fadeTime = -1f)
    {
        if (ActionController == null) return;
        ActionController.PlayExpression(name, fadeTime);
    }

    /// <summary>停止表情</summary>
    public void StopExpression(float fadeTime = -1f)
    {
        ActionController?.StopExpression(fadeTime);

        // ★ 修复：表情停止后眼睛保持暗色的问题
        //   根因：表情/动作修改了 ArtMesh MultiplyColor → GPU 被写入暗色。
        //       停止后 ExpressionManager 淡出参数，但 Cubism 的 IsBlendColorDirty
        //       不重新变 true → GPU MaterialPropertyBlock 缓存保持暗色。
        //   ❌ 之前调 ApplyMultiplyColor() 但它在淡出还没完成时读取的仍是暗色值。
        //   ✅ 延迟等淡出完成 → 用本地 MaterialPropertyBlock 直接盖 GPU 为白色。
        CancelInvoke(nameof(ForceRefreshModelAfterFade));
        float delay = fadeTime >= 0f ? fadeTime + 0.1f : 0.4f;
        Invoke(nameof(ForceRefreshModelAfterFade), delay);
    }

    /// <summary>
    /// 延迟刷新 GPU MaterialPropertyBlock 为白色（等表情淡出完成后调用）。
    /// 用本地 MPB 实例，不依赖 Cubism 的 ApplyMultiplyColor（因它读原生数据）。
    /// </summary>
    private void ForceRefreshModelAfterFade()
    {
        if (_cubismModel == null) return;

        var renderController = _cubismModel.GetComponent<CubismRenderController>();
        if (renderController?.Renderers == null) return;

        // 缓存 MPB，避免每帧 new 产生 GC
        if (_magicMpb == null)
            _magicMpb = new MaterialPropertyBlock();

        // 用本地 MPB 覆盖所有 Renderer，不受 Cubism SharedPropertyBlock 影响。
        // 同时设置 OverrideFlag + LastMultiplyColor，阻止 CubismRenderController
        // 在 OnLateUpdate (order 10000) 中用非白色的模型数据写回 GPU。
        foreach (var renderer in renderController.Renderers)
        {
            renderer.OverrideFlagForDrawableMultiplyColors = true;
            renderer.OverrideFlagForDrawableScreenColors = true;
            renderer.MultiplyColor = Color.white;
            renderer.LastMultiplyColor = Color.white;
            renderer.LastIsUseUserMultiplyColor = true;
            renderer.ScreenColor = Color.clear;
            renderer.LastScreenColor = Color.clear;
            renderer.LastIsUseUserScreenColors = true;
            renderer.MeshRenderer.GetPropertyBlock(_magicMpb);
            _magicMpb.SetColor("cubism_MultiplyColor", Color.white);
            _magicMpb.SetColor("cubism_ScreenColor", Color.clear);
            renderer.MeshRenderer.SetPropertyBlock(_magicMpb);
        }
    }

    /// <summary>
    /// 恢复 OverrideFlag，让 CubismRenderController 重新控制 MultiplyColor/ScreenColor。
    /// 法阵/动作期间 ForceRefreshModelAfterFade() 设了这些 flag，
    /// 结束后必须恢复，否则表情系统再也无法改变 ArtMesh 颜色 → 眼睛发白/颜色固化。
    /// </summary>
    private void TryRestoreOverrideFlag()
    {
        if (_cubismModel == null) return;
        var renderController = _cubismModel.GetComponent<CubismRenderController>();
        if (renderController?.Renderers == null) return;
        foreach (var renderer in renderController.Renderers)
        {
            renderer.OverrideFlagForDrawableMultiplyColors = false;
            renderer.OverrideFlagForDrawableScreenColors = false;
        }
    }

    /// <summary>播放复合动作</summary>
    public void PlayAction(string name, System.Action onComplete = null)
    {
        if (ActionController == null)
        {
            onComplete?.Invoke();
            return;
        }

        // ★ 清空闲动作残留状态，防止旧式 idle 在新动作结束后继续播放已过时的动作
        ResetIdleAction();

        // ★ 暂停宠物物理（停走 + 冻结状态机），避免"边走边做动作"
        if (_pet != null)
            _pet.Pause(0f);

        _actionLocked = true; // 锁定不被走路覆盖

        // ★ 安全超时：万一协程挂了/被 StopCoroutine 杀掉了导致 onComplete 永远不触发，
        //   这个 Invoke 会在最大动作时长后强行释放锁，防止宠物永久卡死。
        //   协程正常完成时会 CancelInvoke 取消这个超时。
        float maxDuration = GetMaxActionDuration(name);
        CancelInvoke(nameof(ReleaseActionLock));
        Invoke(nameof(ReleaseActionLock), maxDuration);

        ActionController.PlayAction(name, () =>
        {
            // 安全超时已触发则不再重复释放
            if (!_actionLocked) return;
            CancelInvoke(nameof(ReleaseActionLock));
            _actionLocked = false;
            // ★ 动作完毕重置所有动作参数（含 Param132 等眼睛白色覆盖层），
            //   防止 ActionPreset 残留的值导致眼睛发白
            ResetIdleAction();
            // ★ 强制刷新网格，让 Cubism 用模型自身数据重算 ArtMesh 颜色
            //   不要调 ForceRefreshModelAfterFade() — 它会把 OverrideFlag=true 固化所有 Drawable 为白色，
            //   导致表情/预设再也无法控制 MultiplyColor，眼睛发白。
            if (_cubismModel != null) _cubismModel.ForceUpdateNow();
            // ★ 恢复宠物物理
            if (_pet != null) _pet.Resume();
            OnForcedActionFinished?.Invoke();
            onComplete?.Invoke();
        });
    }

    /// <summary>根据动作名估算最大时长（秒），超时后强行释放锁</summary>
    private float GetMaxActionDuration(string name)
    {
        // 已知动作的合理最大时长（含 globalFadeIn + 所有 phase + globalFadeOut + 余量）
        return name switch
        {
            "star_spin" => 8f,    // 5.5s + 余量
            "stretch"   => 6f,    // 4.5s + 余量
            "cry"       => 5f,
            "blush"     => 5f,
            "confuse"   => 5f,
            "magic_circle" => 10f,
            _ => 6f // 默认 6 秒
        };
    }

    /// <summary>强制释放动作锁（Invoke 回调），恢复宠物运动</summary>
    private void ReleaseActionLock()
    {
        if (!_actionLocked) return;
        Debug.LogWarning($"[Live2DRenderer] ⏰ 动作超时强制释放锁 (ActionLocked={_actionLocked})");
        _actionLocked = false;
        // ★ 超时强制清理动作参数（含 Param132 眼睛白色覆盖层）
        ResetIdleAction();
        if (_cubismModel != null) _cubismModel.ForceUpdateNow();
        // ★ 注意：不要调 ForceRefreshModelAfterFade()，它会把 OverrideFlag=true 固化，
        //   导致表情系统再也无法控制 ArtMesh 颜色 → 眼睛发白。
        if (_pet != null && _pet.isPaused)
            _pet.Resume();
        // 通知调用方（ContextMenu 恢复宠物状态）
        OnForcedActionFinished?.Invoke();
    }

    /// <summary>获取可用的表情列表（逗号分隔字符串）</summary>
    public string GetAvailableExpressions()
    {
        if (ActionController?.Expressions == null) return "无";
        return string.Join(", ", ActionController.Expressions.AvailableExpressions);
    }

    /// <summary>获取可用的动作列表（逗号分隔字符串）</summary>
    public string GetAvailableActions()
    {
        if (ActionController?.Actions == null) return "无";
        return string.Join(", ", ActionController.Actions.AvailableActions);
    }

    // ================================================================
    // ===== 射线触摸检测系统 =====
    // ================================================================

    // Drawable 名称关键词 → 身体部位映射表
    private static readonly (string keyword, BodyPart part)[] DrawableBodyPartKeywords = new (string, BodyPart)[]
    {
        ("頭",      BodyPart.Head),
        ("顔",      BodyPart.Head),
        ("脸",      BodyPart.Head),
        ("头",      BodyPart.Head),
        ("颈",      BodyPart.Head),
        ("neck",    BodyPart.Head),
        ("head",    BodyPart.Head),
        ("face",    BodyPart.Head),
        ("体",      BodyPart.Body),
        ("Body",    BodyPart.Body),
        ("body",    BodyPart.Body),
        ("胸",      BodyPart.Body),
        ("胸",      BodyPart.Body),
        ("腰",      BodyPart.Body),
        ("脚",      BodyPart.Leg),
        ("足",      BodyPart.Leg),
        ("腿",      BodyPart.Leg),
        ("leg",     BodyPart.Leg),
        ("foot",    BodyPart.Leg),
        ("腕",      BodyPart.Arm),
        ("手",      BodyPart.Arm),
        ("arm",     BodyPart.Arm),
        ("finger",  BodyPart.Arm),
        ("袖",      BodyPart.Arm),
        ("裙",      BodyPart.Dress),
        ("skirt",   BodyPart.Dress),
        ("饰",      BodyPart.Dress),
        ("衣",      BodyPart.Dress),
        ("dress",   BodyPart.Dress),
        ("发",      BodyPart.Other),
        ("hair",    BodyPart.Other),
        ("髪",      BodyPart.Other),
    };

    // Drawable 名称缓存 → 身体部位
    private Dictionary<string, BodyPart> _drawableBodyParts = new Dictionary<string, BodyPart>();
    // 是否已缓存分类
    private bool _drawableClassificationCached = false;
    // 各部位 Drawable 列表
    private Dictionary<BodyPart, List<string>> _bodyPartDrawables = new Dictionary<BodyPart, List<string>>();
    // 模型整体 Y 边界（屏幕空间）
    private float _modelBoundsTop = 0f;
    private float _modelBoundsBottom = 1f;

    /// <summary>计算 Drawable 名称到身体部位的映射，缓存 Y 范围</summary>
    private void CacheDrawableClassificationBounds()
    {
        if (_cubismModel == null) return;

        _drawableBodyParts.Clear();
        _bodyPartDrawables.Clear();
        foreach (BodyPart part in System.Enum.GetValues(typeof(BodyPart)))
            _bodyPartDrawables[part] = new List<string>();

        float minY = float.MaxValue;
        float maxY = float.MinValue;

        foreach (var drawable in _cubismModel.Drawables)
        {
            string name = drawable.name ?? drawable.Id;
            BodyPart part = ClassifyDrawableByName(name);
            _drawableBodyParts[name] = part;
            _bodyPartDrawables[part].Add(name);

            // 计算模型世界坐标 Y 范围
            var renderer = drawable.GetComponent<CubismRenderer>();
            if (renderer != null)
            {
                var bounds = renderer.Mesh.bounds;
                Vector3 worldMin = renderer.transform.TransformPoint(bounds.min);
                Vector3 worldMax = renderer.transform.TransformPoint(bounds.max);
                if (worldMin.y < minY) minY = worldMin.y;
                if (worldMax.y > maxY) maxY = worldMax.y;
            }
        }

        // 存为屏幕空间参考（后续用世界->屏幕转换）
        _modelBoundsTop = maxY;
        _modelBoundsBottom = minY;

        _drawableClassificationCached = true;
        Debug.Log($"[Live2DRenderer] Drawable 分类完成: 头={_bodyPartDrawables[BodyPart.Head].Count}, " +
                  $"身体={_bodyPartDrawables[BodyPart.Body].Count}, 手臂={_bodyPartDrawables[BodyPart.Arm].Count}, " +
                  $"腿={_bodyPartDrawables[BodyPart.Leg].Count}, 裙子={_bodyPartDrawables[BodyPart.Dress].Count}, " +
                  $"其他={_bodyPartDrawables[BodyPart.Other].Count}");
    }

    /// <summary>根据 Drawable 名称分类身体部位</summary>
    private BodyPart ClassifyDrawableByName(string name)
    {
        string lower = name.ToLowerInvariant();
        foreach (var (keyword, part) in DrawableBodyPartKeywords)
        {
            if (lower.Contains(keyword.ToLowerInvariant()))
                return part;
        }
        return BodyPart.Other;
    }

    /// <summary>
    /// 发射射线检测点击的模型 Drawable，返回最优先命中部位
    /// </summary>
    private BodyPart RaycastToBodyPart(Vector2 screenPos)
    {
        if (_cubismRaycaster == null || _cubismModel == null)
            return BodyPart.Other;

        Camera cam = Camera.main;
        if (cam == null) return BodyPart.Other;

        // 屏幕坐标 → 射线
        Ray ray = cam.ScreenPointToRay(screenPos);
        int hitCount = _cubismRaycaster.Raycast(ray, _raycastResults, 100f);

        if (hitCount == 0)
            return BodyPart.Other;

        // 命中优先级：头 > 身体 > 手臂 > 腿 > 裙子 > 其他
        BodyPart[] priority = new BodyPart[]
        {
            BodyPart.Head, BodyPart.Body, BodyPart.Arm,
            BodyPart.Leg, BodyPart.Dress, BodyPart.Other
        };

        // 收集所有命中的部位（按距离排序自动由 raycaster 保证）
        for (int i = 0; i < hitCount && i < _raycastResults.Length; i++)
        {
            string drawableName = _raycastResults[i].Drawable?.name ?? _raycastResults[i].Drawable?.Id;
            if (string.IsNullOrEmpty(drawableName)) continue;

            BodyPart part;
            if (_drawableBodyParts.TryGetValue(drawableName, out part))
            {
                // 按优先级返回第一个匹配的
                for (int p = 0; p < priority.Length; p++)
                {
                    if (part == priority[p])
                        return part;
                }
            }
        }

        return BodyPart.Other;
    }

    /// <summary>包围盒层级退化的身体部位估算（当射线检测失败时用 hitNormY 回退）</summary>
    private BodyPart EstimateBodyPartByHeight(float hitNormY)
    {
        if (hitNormY <= 0.30f) return BodyPart.Head;     // 头顶区
        if (hitNormY <= 0.55f) return BodyPart.Body;     // 身体区
        if (hitNormY <= 0.70f) return BodyPart.Arm;      // 手臂区（身体两侧）
        if (hitNormY <= 0.85f) return BodyPart.Dress;     // 裙子区
        return BodyPart.Leg;                               // 腿脚区
    }

    #region IPetRenderer

    public void ShowDragPose()
    {
        SetParameter("ParamBodyAngleX", 0f);
        SetParameter("ParamAngleX", 0f);
        _poseLocked = false;
        // 记录当前帧位置，重置速度追踪（新拖拽从零开始）
        _lastDragPetX = _pet != null ? _pet.petX : 0;
        _dragSmoothBodyZ = 0f;
        _dragSmoothHeadX = 0f;
        _dragSmoothHeadZ = 0f;
        _dragInited = true;
    }

    public void ShowClickPose(Vector2 screenPos)
    {
        _clickSavedParams.Clear();

        // ★ 先用 CubismRaycaster 精确检测命中的身体部位
        BodyPart hitPart = RaycastToBodyPart(screenPos);

        // ★ 射线检测失败的回退方案：用垂直位置估算
        if (hitPart == BodyPart.Other)
        {
            Camera cam = Camera.main;
            if (cam != null && _pet != null)
            {
                float hitNormY = Mathf.Clamp01((screenPos.y - _pet.petY) / _pet.petHeight);
                hitPart = EstimateBodyPartByHeight(hitNormY);
            }
        }

        switch (hitPart)
        {
            case BodyPart.Head:
                // === 摸头：眯眼歪头，开心 ===
                _clickSavedParams["ParamEyeLOpen"] = CLICK_EYE_OPEN;
                _clickSavedParams["ParamEyeROpen"] = CLICK_EYE_OPEN;
                _clickSavedParams["ParamAngleX"] = CLICK_HEAD_ANGLE_X;
                _clickSavedParams["ParamBodyAngleX"] = CLICK_BODY_ANGLE_X;
                _clickSavedParams["ParamEyeLSmile"] = 0.3f;
                _clickSavedParams["ParamEyeRSmile"] = 0.3f;
                break;

            case BodyPart.Body:
                // === 戳身体：睁大眼张嘴惊讶 ===
                _clickSavedParams["ParamEyeLOpen"] = POKE_EYE_OPEN;
                _clickSavedParams["ParamEyeROpen"] = POKE_EYE_OPEN;
                _clickSavedParams["ParamMouthOpenY"] = POKE_MOUTH_OPEN;
                _clickSavedParams["ParamMouthForm"] = POKE_MOUTH_FORM;
                _clickSavedParams["ParamBrowRY"] = POKE_BROW_RAISE;
                _clickSavedParams["ParamBrowLY"] = POKE_BROW_RAISE;
                _clickSavedParams["ParamAngleX"] = CLICK_HEAD_ANGLE_X * 0.5f;
                break;

            case BodyPart.Arm:
                // === 碰手臂：微微害羞 + 收手 ===
                _clickSavedParams["ParamAngleX"] = CLICK_HEAD_ANGLE_X * 0.3f;
                _clickSavedParams["ParamAngleZ"] = LEG_HIT_ANGLE_Z * 0.5f;
                _clickSavedParams["ParamEyeLSmile"] = LEG_HIT_SMILE * 0.6f;
                _clickSavedParams["ParamEyeRSmile"] = LEG_HIT_SMILE * 0.6f;
                _clickSavedParams["ParamEyeLOpen"] = 0.7f;
                _clickSavedParams["ParamEyeROpen"] = 0.7f;
                _clickSavedParams["Param94"] = -2f;
                _clickSavedParams["Param97"] = -5f;
                break;

            case BodyPart.Leg:
                // === 碰腿：害羞开心 ===
                _clickSavedParams["ParamEyeLOpen"] = LEG_HIT_EYE_CLOSE;
                _clickSavedParams["ParamEyeROpen"] = LEG_HIT_EYE_CLOSE;
                _clickSavedParams["ParamEyeLSmile"] = LEG_HIT_SMILE;
                _clickSavedParams["ParamEyeRSmile"] = LEG_HIT_SMILE;
                _clickSavedParams["ParamAngleZ"] = LEG_HIT_ANGLE_Z;
                _clickSavedParams["ParamAngleX"] = CLICK_HEAD_ANGLE_X * 0.3f;
                _clickSavedParams["ParamBodyAngleX"] = CLICK_BODY_ANGLE_X * 0.5f;
                _clickSavedParams["ParamBreath"] = 1.5f;
                break;

            case BodyPart.Dress:
                // === 碰裙子：害羞 + 转头 ===
                _clickSavedParams["ParamAngleZ"] = LEG_HIT_ANGLE_Z * 0.8f;
                _clickSavedParams["ParamAngleX"] = CLICK_HEAD_ANGLE_X;
                _clickSavedParams["ParamBodyAngleX"] = CLICK_BODY_ANGLE_X * 0.5f;
                _clickSavedParams["ParamEyeLSmile"] = LEG_HIT_SMILE;
                _clickSavedParams["ParamEyeRSmile"] = LEG_HIT_SMILE;
                _clickSavedParams["ParamEyeLOpen"] = LEG_HIT_EYE_CLOSE;
                _clickSavedParams["ParamEyeROpen"] = LEG_HIT_EYE_CLOSE;
                _clickSavedParams["ParamBrowRY"] = 3f;
                _clickSavedParams["ParamBrowLY"] = 3f;
                break;

            default:
                // === 其他（头发/饰品）：简单回应 ===
                _clickSavedParams["ParamAngleX"] = CLICK_HEAD_ANGLE_X * 0.5f;
                _clickSavedParams["ParamBodyAngleX"] = CLICK_BODY_ANGLE_X * 0.3f;
                break;
        }

        Debug.Log($"[Live2DRenderer] 点击部位: {hitPart}");

        foreach (var kv in _clickSavedParams)
            SetParameter(kv.Key, kv.Value);

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

    public void ShowWallHitPose(int direction)
    {
        // 开启反弹动画计时器
        _wallHitTime = WALL_HIT_DURATION;

        // 瞪眼（瞬时覆盖，后续由 LateUpdate 衰减）
        SetParameter("ParamEyeLOpen", WALL_HIT_EYE_OPEN);
        SetParameter("ParamEyeROpen", WALL_HIT_EYE_OPEN);
        SetParameter("ParamMouthOpenY", WALL_HIT_MOUTH_OPEN);

        // 身体往反方向倾斜（受惊后仰）
        float bodyLean = (direction > 0) ? -WALL_HIT_BODY_LEAN : WALL_HIT_BODY_LEAN;
        SetParameter("ParamBodyAngleX", bodyLean);

        Debug.Log($"[Live2DRenderer] 墙碰! direction={direction}");
    }

    public void SetEyeTarget(float? targetX, float? targetY)
    {
        _eyeTargetX = targetX;
        _eyeTargetY = targetY;
    }

    public void OnPetUpdate(int petX, int petY, int petWidth, int petHeight,
                            int petVx, int petVy, bool onGround, bool isDragging, bool isPaused)
    {
        if (!_loaded || _cubismModel == null) return;

        if (isDragging)
        {
            // ⭐4 拖拽挣扎（LateUpdate 中也会调用以覆盖物理）
            UpdateDragStruggle();
            if (_cubismModel != null) _cubismModel.ForceUpdateNow();
            return;
        }

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

    /// <summary>
    /// ⭐4 拖拽挣扎动画 — 手脚交替划水 + 身体扭动 + 慌张表情
    /// 从 OnPetUpdate 抽出，供 LateUpdate 在物理之后重新覆盖
    /// </summary>
    private void UpdateDragStruggle()
    {
        float t = Time.time;

        // 初始化拖拽速度追踪（防第一帧跳变）
        if (!_dragInited)
        {
            _lastDragPetX = _pet != null ? _pet.petX : 0;
            _dragSmoothBodyZ = 0f;
            _dragSmoothHeadX = 0f;
            _dragSmoothHeadZ = 0f;
            _dragInited = true;
        }

        // === 双臂交替挣扎（模型坐标系：右臂正=向前，左臂负=向前）===
        float phase = t * DRAG_ARM_FREQ;
        float swing = Mathf.Sin(phase);
        float jitter = Mathf.Sin(t * DRAG_JITTER1_FREQ) * DRAG_JITTER1_AMP
                     + Mathf.Sin(t * DRAG_JITTER2_FREQ) * DRAG_JITTER2_AMP;
        float rightBase = swing * DRAG_RIGHT_AMP * (1f + jitter);
        // 左臂同相位（模型坐标系下右正=向前，左负=向前，同相位=真正交替）
        float leftBase = swing * DRAG_LEFT_AMP;

        // 右臂关节
        float rMag = Mathf.Clamp01((rightBase + DRAG_RIGHT_AMP) / (DRAG_RIGHT_AMP * 2f));
        SetParameter("Param94", rightBase * DRAG_RPARAM94);
        SetParameter("Param97", rightBase * DRAG_RPARAM97);
        SetParameter("Param31", rightBase * DRAG_RPARAM31);
        SetParameter("Param32", rightBase * DRAG_RPARAM32);
        SetParameter("Param33", rightBase * DRAG_RPARAM33);
        SetParameter("Param93", rMag * DRAG_RPARAM93);
        SetParameter("Param118", rMag * DRAG_RPARAM118);
        // 右手透视图层跟随幅度
        SetParameter("Param95", rMag * DRAG_LAYER95);
        SetParameter("Param117", rMag * DRAG_LAYER117);
        SetParameter("Param98", rMag * DRAG_LAYER98);
        SetParameter("Param100", rMag * DRAG_LAYER100);
        SetParameter("Param116", rMag * DRAG_LAYER116);
        SetParameter("Param120", rMag * DRAG_LAYER120);
        SetParameter("Param108", rMag * DRAG_LAYER108);
        SetParameter("Param119", rMag * DRAG_LAYER119);

        // 左臂
        SetParameter("Param34", leftBase * DRAG_LPARAM34);
        SetParameter("Param36", leftBase * DRAG_LPARAM36);
        SetParameter("Param37", leftBase * DRAG_LPARAM37);

        // 双腿交替
        float legPhase = t * DRAG_LEG_FREQ;
        float legSwing = Mathf.Sin(legPhase);
        float rightLeg = -legSwing;
        SetParameter("Param126", legSwing * DRAG_LEG_SWING);
        SetParameter("Param127", Mathf.Abs(legSwing) * DRAG_LEG_BEND);
        SetParameter("Param129", rightLeg * DRAG_LEG_SWING);
        SetParameter("Param131", Mathf.Abs(rightLeg) * DRAG_LEG_BEND);
        SetParameter("Param165", legSwing * DRAG_LEG_LIFT);
        SetParameter("Param164", rightLeg * DRAG_LEG_LIFT);

        // 身体晃动（带平滑转身：鼠标方向决定 ParamBodyAngleY，不做 scale.x 硬翻转）
        float targetBodyY = _pet != null ? (_pet.petVx > 0 ? DRAG_TURN_ANGLE : -DRAG_TURN_ANGLE) : 0f;
        _dragSmoothBodyY = Mathf.Lerp(_dragSmoothBodyY, targetBodyY, DRAG_TURN_SMOOTH);
        SetParameter("ParamBodyAngleY", _dragSmoothBodyY);

        // ★ 帧间速度 → 输入参数 + 直接驱动裙子/法盘（全部同方向）
        float rawVel = _pet != null ? (_pet.petX - _lastDragPetX) : 0f;
        _lastDragPetX = _pet != null ? _pet.petX : 0;
        rawVel = Mathf.Clamp(rawVel, -DRAG_VEL_MAX, DRAG_VEL_MAX); // ← 限幅防瞬冲

        // 平滑滤波
        _dragSmoothBodyZ = Mathf.Lerp(_dragSmoothBodyZ, rawVel, DRAG_VEL_LERP);
        _dragSmoothHeadX = Mathf.Lerp(_dragSmoothHeadX, rawVel, DRAG_VEL_LERP);
        _dragSmoothHeadZ = Mathf.Lerp(_dragSmoothHeadZ, rawVel, DRAG_VEL_LERP);

        float v = _dragSmoothBodyZ;

        // ---- 输入参数（给物理系统，聊胜于无）----
        float bodyZ = Mathf.Clamp(-v * DRAG_BODY_Z_SCALE, -DRAG_BODY_Z_MAX, DRAG_BODY_Z_MAX);
        SetParameter("ParamBodyAngleZ", bodyZ);
        SetParameter("ParamBodyAngleX", Mathf.Sin(t * DRAG_BODY_FREQ) * DRAG_BODY_SWAY);

        // ---- 直接驱动裙子/法盘/帘子（与鼠标方向相反）----
        float d = Mathf.Clamp(-v * DRAG_DIRECT_SCALE, -DRAG_DIRECT_MAX, DRAG_DIRECT_MAX);
        SetParameter("Param82", d);
        SetParameter("Param87", d);
        SetParameter("Param84", d * 0.6f);
        SetParameter("Param49", d);
        SetParameter("Param51", d);
        SetParameter("Param57", d);
        SetParameter("Param60", d);

        // ---- 直接驱动头发（物理 Delay 太高，同方向驱动）----
        float h = Mathf.Clamp(-v * DRAG_HAIR_SCALE, -DRAG_HAIR_MAX, DRAG_HAIR_MAX);
        // 刘海
        SetParameter("Param5", h);
        SetParameter("Param7", h);
        SetParameter("Param9", h);
        // 头发物理2
        SetParameter("Param11", h);
        SetParameter("Param14", h);
        SetParameter("Param17", h);
        // 后发B
        SetParameter("Param19", h);
        SetParameter("Param21", h);
        // 鬓发
        SetParameter("Param23", h);
        SetParameter("Param35", h);
        SetParameter("Param41", h);
        // 后发
        SetParameter("Param43", h);
        SetParameter("Param45", h);
        SetParameter("Param55", h);
        SetParameter("Param62", h);
        // 饰品
        SetParameter("Param91", h);
        SetParameter("Param74", h);
        SetParameter("Param89", h);
        // 头饰（Param169 单独小幅度）
        float h169 = Mathf.Clamp(-v * DRAG_HAIR169_SCALE, -DRAG_HAIR169_MAX, DRAG_HAIR169_MAX);
        SetParameter("Param169", h169);

        float headX = Mathf.Clamp(-v * DRAG_HEAD_X_SCALE, -DRAG_HEAD_X_MAX, DRAG_HEAD_X_MAX);
        float headXShake = headX + Mathf.Sin(t * DRAG_HEAD_SHAKE_FREQ) * DRAG_HEAD_SHAKE;
        SetParameter("ParamAngleX", headXShake);
        float headZ = Mathf.Clamp(v * DRAG_HEAD_Z_SCALE, -DRAG_HEAD_Z_MAX, DRAG_HEAD_Z_MAX);
        SetParameter("ParamAngleZ", headZ);
        SetParameter("ParamAngleY", DRAG_HEAD_TILT + Mathf.Sin(t * DRAG_HEAD_BOB_FREQ) * DRAG_HEAD_BOB);

        // 表情
        SetParameter("ParamEyeLOpen", DRAG_EYE_OPEN);
        SetParameter("ParamEyeROpen", DRAG_EYE_OPEN);
        SetParameter("ParamMouthOpenY", DRAG_MOUTH_AMP + Mathf.Sin(t * DRAG_MOUTH_FREQ + DRAG_MOUTH_PHASE) * DRAG_MOUTH_PULSE);
        SetParameter("ParamBrowL", DRAG_BROW);
        SetParameter("ParamBrowR", DRAG_BROW);
    }

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

    // ============================================================
    //  模型置顶 — 叠加渲染
    // ============================================================

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

    void OnGUI()
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

        // GUI.depth = 1 → 在 BottomInputBar(depth=0) 之上绘制
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

    // ===== 公开接口（供 ContextMenu 调试用） =====

    /// <summary>当前播放的随机动作 ID（0=无）</summary>
    public int CurrentActionId => _currentIdleAction;

    /// <summary>是否被强制动作锁定</summary>
    public bool IsActionLocked => _actionLocked;

    /// <summary>设置参数值（公开版）</summary>
    public void SetParameterValue(string name, float value)
    {
        SetParameter(name, value);
    }

    /// <summary>获取参数当前值，失败返回 0</summary>
    public float GetParameterValue(string name)
    {
        if (_cubismModel == null) return 0f;
        var param = _cubismModel.Parameters.FindById(name);
        return param != null ? param.Value : 0f;
    }

    /// <summary>获取参数最小值，失败返回 0</summary>
    public float GetParameterMin(string name)
    {
        if (_cubismModel == null) return 0f;
        var param = _cubismModel.Parameters.FindById(name);
        return param != null ? param.MinimumValue : 0f;
    }

    /// <summary>获取参数最大值，失败返回 0</summary>
    public float GetParameterMax(string name)
    {
        if (_cubismModel == null) return 0f;
        var param = _cubismModel.Parameters.FindById(name);
        return param != null ? param.MaximumValue : 0f;
    }

    /// <summary>获取所有参数名称列表</summary>
    public string[] GetAllParameterNames()
    {
        if (_cubismModel == null) return System.Array.Empty<string>();
        var names = new string[_cubismModel.Parameters.Length];
        for (int i = 0; i < names.Length; i++)
            names[i] = _cubismModel.Parameters[i].Id;
        return names;
    }

#if UNITY_EDITOR
    // ===== 表情快捷测试 =====
    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 表情/happy")]
    private static void TestPlayHappy(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayExpression("happy"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 表情/surprise")]
    private static void TestPlaySurprise(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayExpression("surprise"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 表情/sleepy")]
    private static void TestPlaySleepy(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayExpression("sleepy"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 表情/angry")]
    private static void TestPlayAngry(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayExpression("angry"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 表情/sad")]
    private static void TestPlaySad(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayExpression("sad"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 表情/blush")]
    private static void TestPlayBlush(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayExpression("blush"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 表情/love")]
    private static void TestPlayLove(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayExpression("love"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 表情/confused")]
    private static void TestPlayConfused(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayExpression("confused"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 表情/neutral")]
    private static void TestPlayNeutral(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayExpression("neutral"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 表情/tear")]
    private static void TestPlayTear(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayExpression("tear"); }

    // ===== 动作快捷测试 =====
    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 动作/stretch")]
    private static void TestPlayStretch(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayAction("stretch"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 动作/star_spin")]
    private static void TestPlayStarSpin(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayAction("star_spin"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 动作/tilt")]
    private static void TestPlayTilt(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayAction("tilt"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 动作/smile")]
    private static void TestPlaySmile(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayAction("smile"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 动作/magic_circle")]
    private static void TestPlayMagicCircle(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayAction("magic_circle"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 动作/cry")]
    private static void TestPlayCry(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayAction("cry"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 动作/confuse")]
    private static void TestPlayConfuse(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayAction("confuse"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 动作/brow")]
    private static void TestPlayBrow(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayAction("brow"); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/▶ 动作/blush")]
    private static void TestPlayActionBlush(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.PlayAction("blush"); }

    // ===== 工具按钮 =====
    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/⏹ 停止所有")]
    private static void TestStopAll(UnityEditor.MenuCommand cmd) { var r = (Live2DRenderer)cmd.context; r.StopExpression(0.3f); }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/ℹ 列出可用表情")]
    private static void TestListExpr(UnityEditor.MenuCommand cmd)
    {
        var r = (Live2DRenderer)cmd.context;
        Debug.Log($"[RenderCtx] 可用表情: {r.GetAvailableExpressions()}");
    }

    [UnityEditor.MenuItem("CONTEXT/Live2DRenderer/ℹ 列出可用动作")]
    private static void TestListAct(UnityEditor.MenuCommand cmd)
    {
        var r = (Live2DRenderer)cmd.context;
        Debug.Log($"[RenderCtx] 可用动作: {r.GetAvailableActions()}");
    }
#endif
}
