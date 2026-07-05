# Live2DRenderer.cs 硬编码参数迁移清单

> 阶段一/六：梳理所有 `SetParameter()` 硬编码调用，标记为"可迁移到 JSON 动作预设"。
> 统计：全文件约 **320+ 次 SetParameter 调用**，分布在 15 个方法中。

---

## 优先级分级

| 等级 | 含义 | 示例 |
|------|------|------|
| 🔴 **P0 - 安全网** | 每帧强制清零/保护，永远不应迁移 | LateUpdate 中 Param63-71=1f |
| 🟡 **P1 - 简易迁移** | 纯线性淡入淡出，可直接 JSON | UpdateIdleTilt, UpdateIdleSmile |
| 🟠 **P2 - 中等迁移** | 含多段/条件判断，需封装扩展 | UpdateStretch, UpdateCry |
| 🔵 **P3 - 复杂迁移** | 含物理(Spring/FB)/阶段状态机 | UpdateMagicCircle, UpdateStarSpin |
| ⚪ **P4 - 保留硬编码** | 性能敏感/引擎耦合/永不移 | UpdateWalkAnimation, UpdateBlink |

---

## 详细清单

### 1. LateUpdate() — 安全网 & 条件守卫

**位置**: line 783~840+
**SetParameter 调用**: 28 次
**性质**: 全局守卫 + 动作锁覆盖 + 走路/空闲调度

| 调用 | 行 | 值 | 用途 | 优先级 |
|------|----|----|------|--------|
| ParamBodyAngleX/Y/Z | 770-772 | 0f | poseLock 安全清零 | 🔴 P0 |
| ParamAngleX/Y/Z | 773-775 | 0f | poseLock 安全清零 | 🔴 P0 |
| ParamBreath | 776 | 0f | poseLock 安全清零 | 🔴 P0 |
| Param34/36/37 | 777-779 | 0f | 左臂 poseLock 清零 | 🔴 P0 |
| _clickSavedParams 循环 | 801 | 动态 | 摸头锁定参数重设 | 🔴 P0 |
| Param132-71 (×9) | 818-826 | 0f/1f | **每帧眼睛保护** | 🔴 P0 |

**评估**: 全部 P0。这是基础守卫，不是"动画"。

---

### 2. UpdateIdleAnimation() — 空闲微动 + 表情基调

**位置**: line 1389~1500
**SetParameter 调用**: ~30 次

| 段落 | 参数 | 逻辑 | 优先级 |
|------|------|------|--------|
| 呼吸 | ParamBreath | Perlin 噪声驱动 | 🟡 P1 — 可封装为 "perlin_wave" |
| 身体晃动 | ParamBodyAngleX/Y/Z | Perlin 噪声驱动 | 🟡 P1 — 同上 |
| 头部微动 | ParamAngleX/Y | Perlin 噪声驱动 | 🟡 P1 — 同上 |
| 眼球 | ParamEyeBallX/Y | Perlin 噪声 + 鼠标覆盖 | ⚪ P4 — 鼠标跟随，保留 |
| 夜间垂眼 | ParamEyeLOpen/ROpen | 条件+lerp | 🟠 P2 — 含状态判断 |
| 阴雨委屈 | ParamBrowRY/LY/MouthForm | 天气条件 | 🟠 P2 — 含天气条件 |
| 晴微笑 | ParamMouthForm | 天气条件 | 🟠 P2 — 含天气条件 |
| 雪好奇 | ParamMouthOpenY/EyeLOpen/ROpen | 天气条件 | 🟠 P2 — 含天气条件 |
| 眼睛保护 (×9) | Param132~71 | 每帧清零 | 🔴 **P0** — 安全网 |
| **空闲动作调度** | 无 SetParameter | 加权随机选动作 | **不涉及参数** |

**评估**: 呼吸+身体晃动可抽象为 `idle_micro_motion` JSON，天气表情可抽象为 `weather_mood.json`。眼球鼠标跟随保留。

---

### 3. UpdateIdleTilt() — 动作1: 歪头

**位置**: line 1511~1518
**SetParameter**: 1 次
```csharp
SetParameter("ParamAngleZ", t * IDLE_TILT);  // t = sin(π * time/duration)
```

| 优先级 | 理由 |
|--------|------|
| 🟡 **P1** | 纯线性 sin 弧，2秒循环，可直接 JSON |

---

### 4. UpdateIdleSmile() — 动作2: 微笑

**位置**: line 1520~1529
**SetParameter**: 3 次
```csharp
SetParameter("ParamEyeLSmile", t * IDLE_SMILE);   // 眯眼
SetParameter("ParamEyeRSmile", t * IDLE_SMILE);    // 眯眼
SetParameter("ParamMouthForm", t * IDLE_MOUTH);    // 微笑
```

| 优先级 | 理由 |
|--------|------|
| 🟡 **P1** | 纯线性，2秒循环 |

---

### 5. UpdateIdleBrow() — 动作3: 挑眉

**位置**: line 1531~1539
**SetParameter**: 2 次
```csharp
SetParameter("ParamBrowRY", t * IDLE_BROW_Y);
SetParameter("ParamBrowLY", t * IDLE_BROW_Y);
```

| 优先级 | 理由 |
|--------|------|
| 🟡 **P1** | 纯线性，2秒循环 |

> 注：三者的 IDLE_* 常量数值已在代码中（IDLE_TILT=5, SMILE=0.3, MOUTH=0.15, BROW_Y=0.2），可在生成 JSON 时嵌入。

---

### 6. UpdateStarSpin() — 动作4: 星辉 ✨

**位置**: line 1545~1794
**SetParameter**: ~30 次/帧
**阶段数**: 5 阶段 (P1~P5)
**硬编码常量**: SPIN_DURATION=6s

**参数类别**:
- 星星特效: star_visibility/size/outer_scale/outer_appear/plate
- 表情: eyeL/R open/smile, mouth/form, brow, eyeBX/BY
- 手臂: Param31/32/33/94/97 (右臂全套)
- 身体/头: ParamBodyAngleZ(2f固定), ParamAngleZ(head_scale)
- 剑指: SetSwordFinger(), SetHandPose(), SetHandLayer()
- 手层: Param92/93

| 优先级 | 理由 |
|--------|------|
| 🔵 **P3** | 5 阶段状态机 + 每阶段独立 sin 摆动 + 常量复杂。但各阶段逻辑明确，可拆为 5 段 JSON |

---

### 7. UpdateStretch() — 动作5: 伸懒腰

**位置**: line 1795~1848
**SetParameter**: 20 次
**阶段**: 1 段 (梯形：快起→保持→快落)

```csharp
float rise = Mathf.Clamp01(t * 3f);   // 梯形上升
float hold = Mathf.Clamp01((1 - t) * 3f);  // 梯形下降
float phase = Mathf.Min(rise, hold);   // 梯形
```

**参数**:
- 右臂全套(Param31/32/33/94/97/95/117/98/100/116/120/108/119/93/118) × phase
- 左臂(Param34/36/37) × 0f
- 身体(ParamBodyAngleX/Z) × phase
- 头(ParamAngleX) × phase
- 表情(EyeLOpen/ROpen/MouthForm/Breath) × phase

| 优先级 | 理由 |
|--------|------|
| 🟠 **P2** | 梯形曲线简单，但涉及 20 个参数。可封装为 `trapezoid` 曲线 JSON |

---

### 8. UpdateCry() — 动作8: 委屈 😢

**位置**: line 1849~1890
**SetParameter**: 11 次
**阶段**: 1 段 (sin 弧)
**特殊**: 含 sin 抖动（抽泣）

| 优先级 | 理由 |
|--------|------|
| 🟠 **P2** | 单一 sin 弧 + 4Hz 抖动。JSON 需要扩展 "tremble" 曲线支持 |

---

### 9. UpdateBlush() — 动作10: 害羞 😊🖤

**位置**: line 1890~1929
**SetParameter**: 12 次
**特殊**: 3Hz 脉冲黑脸 + sin 弧叠加

| 优先级 | 理由 |
|--------|------|
| 🟠 **P2** | 含脉冲合成（基础 sin + 3Hz 闪烁），需要 `pulse` 曲线扩展 |

---

### 10. UpdateConfuse() — 动作11: 困惑 🤔

**位置**: line 1930~1969
**SetParameter**: 9 次
**阶段**: 1 段 (sin 弧)

| 优先级 | 理由 |
|--------|------|
| 🟡 **P1** | 纯 sin 弧，9 参数，直线 JSON |

---

### 11. UpdateMagicCircle() — 动作7: 法阵 ✨🔮

**位置**: line 1971~2318
**SetParameter**: ~80+ 次/帧
**阶段**: 3 Act (P1~P3)
**特殊**: 含物理(Spring-Damper/Perlin) + 16 个常量 + 4 个子函数

**参数类别**:
- 身体弹簧: ParamBodyAngleX (含 _magicSpringPosX 弹簧偏移)
- 头弹簧: ParamAngleX (含 _magicSpringPosH 弹簧偏移)
- 星星: Param451/541/1071/1081
- 紫环(9个): outer/mid/inner rot/vis/size
- 黑幕(2): Param121/137
- 白圈(5): Param136/133/134/135 + white glow
- 镜头(3): Param155/156/157
- 头发速度驱动(16个): Param5/7/9/11/14/17/19/21/23/35/41/43/45/55/62
- 饰品速度(3): Param91/74/89
- 衣服速度(6): Param82/87/84/49/51/57/60
- 眼睛保护(9): Param132~71
- 子函数: SetHandPose(), SetSwordFinger(), SetHandLayer(), ForceRefreshModelAfterFade()
- 子函数参数: 额外 23 次 Param 调用

| 优先级 | 理由 |
|--------|------|
| 🔵 **P3** | 含 Spring-Damper 物理 + Perlin 噪声 + camVel 头发驱动。**建议永久保留硬编码**，这是最复杂的特效段 |

---

### 12. SetSwordFinger() — 剑指手势

**位置**: line 2319~2335
**SetParameter**: 11 次

```csharp
SetParameter("Param102..107/111..115", h * factor);
```

| 优先级 | 理由 |
|--------|------|
| 🟡 **P1** | 线性映射 h→param，可直接 JSON。但**仅被 P3 的方法调用** |

---

### 13. SetHandPose() — 手部姿势

**位置**: line 2336~2355
**SetParameter**: 13 次

| 优先级 | 理由 |
|--------|------|
| 🟡 **P1** | 线性映射 |

---

### 14. SetHandLayer() — 手部图层

**位置**: line 2356~2375
**SetParameter**: 8 次

| 优先级 | 理由 |
|--------|------|
| 🟡 **P1** | 线性映射 |

---

### 15. ResetIdleAction() — 动作清理

**位置**: line 2418~2580
**SetParameter**: ~60 次
**用途**: 动作结束后清理所有被改过的参数

| 优先级 | 理由 |
|--------|------|
| 🔴 **P0** — **安全网** | 这是状态重置守卫，不是"动画"。用法阵所有参数清零，逐个恢复默认。即使动作迁移到 JSON，仍需保留清零逻辑 |

---

### 16. UpdateWalkAnimation() — 走路摆动

**位置**: line 2817~2885
**SetParameter**: 18 次

**逻辑**: sin 相位驱动 + 交叉对位 + blendWeight 消退

| 优先级 | 理由 |
|--------|------|
| ⚪ **P4** — 保留硬编码 | 每帧调用的实时走路动画，JSON 无法表达 sin 相位连续流式驱动 |

---

### 17. ApplyWalkBodyPose() — 走路体态

**位置**: line 2859~2894
**SetParameter**: 8 次

| 优先级 | 理由 |
|--------|------|
| ⚪ **P4** — 保留硬编码 | 同上，实时流式 |

---

### 18. UpdateWalkExpression() — 走路困倦表情

**位置**: line 2897~2965
**SetParameter**: 6 次

| 优先级 | 理由 |
|--------|------|
| ⚪ **P4** — 保留硬编码 | 随机触发 + 分段曲线 + 时间随机化 |

---

### 19. UpdateBlink() — 眨眼

**位置**: line 2777~2816
**SetParameter**: ~4 次

| 优先级 | 理由 |
|--------|------|
| ⚪ **P4** — 保留硬编码 | 性能敏感 + 随机间隔 + 实时 sin |

---

## 迁移优先级汇总

| 迁移批次 | 方法 | 参数数 | 估算工作量 |
|----------|------|--------|-----------|
| 🥇 **第一批** P1 | UpdateIdleTilt/Smile/Brow | 6 | 0.5天 — 纯 sin，可直接 JSON |
| 🥇 **第一批** P1 | UpdateConfuse | 9 | 0.5天 |
| 🥇 **第一批** P1 | SetSwordFinger/HandPose/HandLayer (重构为 JSON 预设) | 32 | 0.5天 |
| 🥈 **第二批** P2 | UpdateStretch | 20 | 0.5天 — 梯形曲线扩展 |
| 🥈 **第二批** P2 | UpdateCry | 11 | 1天 — 抖动扩展 |
| 🥈 **第二批** P2 | UpdateBlush | 12 | 1天 — pulse 扩展 |
| 🥈 **第二批** P2 | UpdateIdleAnimation (微动+天气) | ~15 | 1天 — 条件判断抽象 |
| 🥉 **第三批** P3 | UpdateStarSpin | ~30 | 2天 — 5 阶段状态机 |
| 🥉 **第三批** P3 | UpdateMagicCircle (含子函数) | ~80+ | **3~5天** — 含物理 |
| 🔴 **永不移** P0/P4 | LateUpdate安全网/ResetIdleAction/UpdateBlink/Walk | ~50 | — |

## 迁移策略建议

1. **ActionPresetPlayer 扩展曲线类型**：
   - `sin` (已有) — 用于 P1
   - `trapezoid` — 用于 UpdateStretch
   - `perlin_wave` — 用于空闲微动
   - `tremble` (高频叠加) — 用于委屈抽泣
   - `pulse` (类心跳) — 用于害羞黑脸闪烁

2. **分两阶段**:
   - Phase A：迁移 P1 方法（~1.5 天工作量）
   - Phase B：曲线扩展后再迁 P2（~2.5 天）

3. **UpdateMagicCircle 建议永不迁移**:
   - Spring-Damper 物理在 JSON 中无法表达
   - camVel 头发驱动需要每帧微分
   - 80+ 参数/帧导致 JSON 臃肿
   - 4 个子函数交叉调用难以 JSON 化
