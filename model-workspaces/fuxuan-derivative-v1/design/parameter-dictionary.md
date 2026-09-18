# 参数设计表（Stage 1 草案）

> 这是语义与资源规划，不是 Cubism 已创建参数事实。Stage 3 必须以新工程实际参数、native min/default/max 和 Probe 结果为准。

| 语义通道 | 预期作用 | 首版 | 后续 |
|---|---|---:|---:|
| `head_angle_x/y/z` | 头部左右、俯仰、侧倾 | 是 | — |
| `body_angle_x/y/z` | 身体重心与躯干微动 | 是 | — |
| `breath` | 呼吸周期 | 是 | — |
| `eye_l_open` / `eye_r_open` | 左右眼开合 | 是 | — |
| `eye_ball_x/y` | 眼球跟随 | 是 | — |
| `brow_l/r_y` | 眉毛轻微上下 | 是 | 复杂表情扩展 |
| `mouth_form/open` | 嘴型与开合 | 是 | 口型扩展 |
| `hair_front/back_sway` | 前发/后发低幅跟随 | 是 | — |
| `ornament_sway` | 发饰/挂件低幅跟随 | 是 | — |
| `sleeve_sway_l/r` | 袖口低幅跟随 | 是 | 肩臂动作 |
| `skirt_sway_front/back` | 裙摆低幅跟随 | 是 | 走路 |
| `arm_l/r_pose` | 肩臂独立姿态通道 | 预留 | 后续认证 |
| `hand_l/r_pose` | 手部手势通道 | 预留 | 后续认证 |
| `effect_slots` | 特效资源槽 | 预留 | 后续认证 |

硬规则：新模型使用自己的参数 ID；语义表不能直接写入生产映射；无 Probe 的参数只停留在候选层；每帧可见参数只有一个写入者。
