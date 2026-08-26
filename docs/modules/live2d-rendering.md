# Live2D 渲染管道 — 渲染、参数映射与硬编码迁移

> **文档作用**: 本模块文档描述桌宠「Live2D 渲染」子系统的**代码真相**——模型加载双保险、80+ 参数映射、Perlin 噪声微动、天气↔表情联动、以及 `Live2DRenderer.cs` 379+ 处硬编码 `SetParameter` 调用的迁移清单（P0-P4 分级）。改渲染/表情/动作参数相关代码前必读。
> **基本架构**: `HybridRenderer` → `Live2DRenderer`（恒走 Live2D，3D 分支不可用）→ Cubism SDK 5-r.4。模型加载：AssetDatabase → Resources.Load("Fuxuan") 降级。参数映射：`Live2DParameterMapper`（语义名 ↔ Cubism 参数 ID）双向映射，核心入口 `Live2DRenderer.SetParameterValue(string, float)`。渲染器现由 `Live2DRenderer.cs`（模型加载、动作与参数）+ `Live2DRenderer.OverlayRendering.cs`（置顶叠加相机、RT、OnGUI 和性能档位）组成同一 partial 类。执行顺序：DesktopPet.Update（0）→ CubismPhysicsController.LateUpdate（800）→ Live2DRenderer.LateUpdate（801，覆盖物理重置参数）。
> **开发历史迭代**: N38（2026-08-02）完成硬编码动作迁移清单（379+ 处调用、15 方法、P0-P4 分级）；7 个 legacy 方法（~270 行）已删除由 JSON + IdleActionScheduler 替代，仅保留星辉（#4）与法阵（#7）硬编码；N40 空闲动作 9 种 JSON 驱动。迁移路线图：阶段 1 梳理完成 → 阶段 2 P1 迁移（1 天）→ 阶段 3 P2（2 天）→ 阶段 4 P3（3 天）→ 阶段 5 P0/P4 视需求。
> **编写注意事项**: ①`LateUpdate`（801）必须晚于 Cubism Physics（800）执行，否则物理覆盖关键参数；②P0 安全网（Param132-71 眼睛保护等）每帧强制清零，**永远不应迁移**；③3D 分支恒不可用（HybridRenderer TODO / Model3DRenderer 注释与实现矛盾），勿修 3D；④默认空闲表情是 "surprise" 非 "curious"；⑤迁移 P1-P3 动作时保持「动作时冻结行走」（_pet.Pause/Resume）。

---

## 一、文档作用

- **服务对象**: 开发者 + AI 编码代理。任何涉及 Live2D 参数写入、表情/动作播放、空闲微动、模型加载、天气联动的改动。
- **回答的问题**:
  - Live2D 模型怎么加载？参数怎么映射？
  - 每帧参数写入顺序是什么？为什么 LateUpdate 801 > 800？
  - 哪些硬编码参数可以迁到 JSON？哪些永远不能动？
  - 天气/表情/空闲动作怎么联动？
- **关联文档**: `code-truth-architecture.md`（物理与渲染层）｜`modules/action-agent.md`（动作执行端）｜`modules/chat-ui.md`（像素模式并行渲染）。旧版硬编码迁移清单已并入本模块，不再单独维护。

## 二、基本架构

### 2.1 渲染链路

```
HybridRenderer → Live2DRenderer (恒走 Live2D)
  → Cubism SDK 5-r.4 (CubismPhysicsController 物理模拟)
  → SetParameterValue(name, value) ← Live2DParameterMapper 双向映射 (语义名 ↔ 参数 ID)
```

### 2.2 模型加载双保险

```
AssetDatabase.LoadAssetAtPath<GameObject> (Editor)
  → 成功 → 实例化
  → 失败 → Resources.Load("Fuxuan") 降级 (Build)
```

### 2.3 执行顺序（关键）

| Order | 组件 | 职责 |
|-------|------|------|
| 0 | `DesktopPet.Update()` | 物理更新、状态转换、行走相位 |
| 800 | `CubismPhysicsController.LateUpdate()` | 衣服/头发物理模拟 |
| 801 | `Live2DRenderer.LateUpdate()` | 覆盖物理重置参数 + 空闲动画 + 交互反馈 |

> 801 > 800 确保所有参数在 Cubism Physics 运算后写入，避免物理覆盖关键参数。

### 2.4 参数映射（80+ 参数）

| 部位 | 参数 | 数量 |
|------|------|------|
| 头部 | ParamAngleX/Y/Z | 3 |
| 身体 | ParamBodyAngleX/Y/Z | 3 |
| 眼睛 | ParamEyeLOpen/ROpen, Ball, Smile | 6 |
| 眉毛 | ParamBrowRY/LY, RX/LX | 4 |
| 嘴 | ParamMouthForm, OpenY | 2 |
| 手臂 | Param31-37, 92-120 | 36+ |
| 呼吸 | ParamBreath | 1 |

### 2.5 Perlin 噪声微动（7 通道）

| 参数 | 通道 | 描述 |
|------|------|------|
| ParamBreath | (t, 0) | 呼吸 |
| ParamBodyAngleX/Y/Z | (t, 1-3) | 身体三轴晃动 |
| ParamAngleX/Y | (t, 4-5) | 头部微动 |
| ParamEyeBallX/Y | (t+offset, 6-7) | 眼球微动 |

### 2.6 天气↔表情联动

| 天气 | 表情 | 参数变化 |
|------|------|---------|
| ☀️ 晴 | 微笑 | MouthForm +0.2 |
| 🌧 雨 | 委屈 | BrowRY/LY +4, MouthForm 微嘟 |
| ⛈ 雷暴 | 警惕 | 同上 + EyeLOpen 微睁 |
| ❄️ 雪 | 好奇 | MouthOpenY +0.4, EyeLOpen +0.2 |
| 🌙 夜晚 | 困倦 | EyeLOpen 垂 +0.07 |

### 2.7 硬编码参数迁移清单（P0-P4 分级，2026-08-02 实测 379+ 处）

| 等级 | 含义 | 示例 | 处理 |
|------|------|------|------|
| 🔴 **P0 - 安全网** | 每帧强制清零/保护，永远不应迁移 | LateUpdate 中 Param63-71=1f、Param132-71 眼睛保护、poseLock 清零 | **保留** |
| 🟡 **P1 - 简易迁移** | 纯线性淡入淡出，可直接 JSON | UpdateIdleTilt、UpdateIdleSmile、歪头/微笑/挑眉/爱心/困惑、SetSwordFinger/SetHandPose/SetHandLayer | 阶段 2 |
| 🟠 **P2 - 中等迁移** | 含多段/条件判断，需封装扩展 | UpdateStretch（20 参数梯形）、UpdateCry（4Hz 抽泣）、UpdateBlush（3Hz 脉冲） | 阶段 3 |
| 🔵 **P3 - 复杂迁移** | 含物理(Spring/Perlin)/阶段状态机 | UpdateMagicCircle（~80+ 次/帧，3 Act）、UpdateStarSpin（5 阶段） | 阶段 4（或永久保留） |
| ⚪ **P4 - 保留硬编码** | 性能敏感/引擎耦合/永不移 | UpdateWalkAnimation、UpdateBlink、眼球 Perlin+鼠标覆盖 | 保留 |

**已迁移**（N38）：7 个 legacy 方法（~270 行）已删除，由 JSON + IdleActionScheduler 替代；**硬编码保留**：UpdateStarSpin（动作4 星辉）+ UpdateMagicCircle（动作7 法阵）。

### 2.8 空闲动作驱动

9 种 JSON 配置（7 参数化 + 2 硬编码特效），含权重/冷却/天气时段调制：夜晚微笑×0.3 / 雨哭×1.8 / 雪微笑×1.5。特效涉及参数：星星（Param451/541/1071/1081）、紫环 9 个、黑幕 2、白圈 5、镜头 3、头发速度 16、饰品速度 3、衣服速度 6。

### 2.9 行走与动作互斥（2026-08-20）

空闲动作和行走属于同一套 Live2D 姿态写入通道，不能在同一帧叠加：

- 自动空闲动作只有在 `isWalking == false` 时才允许启动；`isWalking` 要求宠物落地、有水平速度、未暂停且没有强制/AI 控制锁。
- 行走恢复时，`LateUpdate` 会先中断自动动作并调用 `ResetIdleAction(true)`，再写入行走姿态，避免上一动作的头部、身体或手臂参数残留。
- 右键/测试触发的旧动作（包括 #4 星辉、#7 法阵）使用 `_actionLocked`，并暂停 `DesktopPet` 的物理移动；动作结束时只恢复由该动作引入的暂停状态。
- #4 `UpdateStarSpin()` 仍是五阶段硬编码动作；#7 `UpdateMagicCircle()` 仍是五阶段 Spring/Perlin 复杂动作。二者均不应被行走姿态覆盖。

测试模式下可用以下 inbox 命令复核旧动作和渲染快照（必须先创建隔离目录中的 `.test_mode`）：

| 命令 | 作用 |
|------|------|
| `@@idle:1` … `@@idle:9` | 触发对应旧空闲动作；4、7 走硬编码实现，其余走 JSON 调度器 |
| `@@shot:<name>` | 保存当前 Live2D 模型快照到 `{FU_XUAN_DATA}/action_captures/<name>.png` |

### 2.10 停止过渡与物理输入稳定（2026-08-26）

- `Live2DRenderer.Update()` 在停止且无边缘反弹时，于 `CubismPhysics(order 800)` 前把身体、头部、呼吸和左臂物理输入固定为 0；`LateUpdate()` 只负责最终渲染姿态，避免物理输入在两个生命周期阶段之间来回跳变。
- 走路停止后的 `IDLE_BLEND_DURATION` 淡出期间禁止启动新的自动空闲动作，等走路手脚参数完全淡出后再开始空闲动作，避免走路参数与 JSON 动作同帧抢写。
- 屏幕左右边缘反弹的身体/头部姿态由 `Update()` 在物理步进前统一写入；`LateUpdate()` 只保留眼睛和嘴的视觉覆盖，避免贴边时衣服物理在走路体态与反弹体态之间交替取值。
- 这些保护共同覆盖“移动后停止”和“贴边反弹”两条衣服物理路径；停止时不再使用 LateUpdate 之后的 SmoothDamp 二次写入。

### 2.12 唤醒窗口恢复（2026-08-26）

- 连续异常启动状态只作为 DWM 恢复时序的保护信号，不再直接跳过透明层重建。
- `WindowOverlay.OnApplicationPause(false)` 与 `OnResumeFromSleep()` 在安全模式下仍进入 `RebuildAfterDelay()`；这样睡眠或显卡驱动恢复后，即使分层窗口暂时变成 alpha=0，也会重新应用 DWM、置顶和显示样式。
- `DesktopPet` 在稳定运行 60 秒后清除连续异常计数和安全模式标记；`.test_mode` 完全跳过生产 `PlayerPrefs` 看门狗状态。

### 2.11 物理网格刷新与帧率（2026-08-26）

- `符玄.physics3.json` 的物理步进频率为 60 FPS，桌宠后台 High 档的主循环目标为 30 FPS；这两个频率不同不代表要跳过渲染帧。
- `Live2DRenderer.LateUpdate()` 在 Cubism Physics（800）之后执行；普通路径按隔帧节奏调用 `ForceUpdateNow()`，避免额外刷新再次推进物理弹簧。法阵（#7）和星辰（#4）仍保留各自的专用刷新路径，避免重复全量更新。
- 该隔帧策略是对“走路正常、停止抖动”回归的保守修复；待团结引擎许可证恢复后，需要在可见播放器中重新确认衣服后摆的流畅度，再决定是否拆分网格刷新与物理刷新。
- 2026-08-24 修复硬编码动作的手部穿模：`SetHandLayer()` 的 Param95/98/100/108/116/117/119/120 统一采用 `Live2DMotionTemplates` 的图层权重，不再使用偏低的旧值，避免抬臂时手/袖被衣服网格压到后方。

2026-08-24 在可见真实播放器中重新抓取并检查 #4/#7 的中段与结束截图；隐藏窗口截图会得到黑色 RT，因此视觉回归必须使用可见播放器。完整构建与运行时冒烟通过。

### 2.13 叠加渲染职责拆分（2026-08-26）

- `Live2DRenderer.OverlayRendering.cs` 统一承载模型置顶的 Layer 排除、叠加相机、RenderTexture 重建、OnGUI 绘制、资源释放和性能档位回调；`Live2DRenderer.cs` 保留模型加载、参数、动作和交互逻辑。
- 本次是 partial 文件边界整理，不改变 Layer 31、透明 RT、相机同步或 `rtResolutionScale` 的运行时行为；快速构建、完整构建和隔离运行时冒烟均已通过。
- 后续若调整 RT 格式、透明合成或性能档位，只需优先检查该 partial 与 `PerformanceMonitor` 的契约，并补可见播放器回归；不要把渲染资源释放重新散落回模型动作逻辑。

## 三、开发历史迭代

| 版本 | 日期 | 变更 |
|------|------|------|
| N31-N37 | — | Perlin 噪声待机、自动眨眼+鼠标跟随、FPS 自适应、手部图层前置、调试偏移通道、模型加载双保险、参数范围自动打印、天气联动、KNOW_PATTERNS 单源化（KnownParameterPatterns.cs） |
| N38 | 2026-08-02 | 硬编码参数迁移清单（阶段一：梳理 379+ 处调用、15 方法、P0-P4 分级）；7 个 legacy 动作方法删除（~270 行）→ JSON + IdleActionScheduler；保留星辉/法阵硬编码 |
| N40 | 2026-08-08 | 空闲动作 9 种 JSON 配置驱动确认；迁移路线图阶段 2-5 待执行 |
| 2026-08-26 | 2026-08-26 | 将 Live2D 叠加渲染与性能档位入口拆至 `Live2DRenderer.OverlayRendering.cs`；行为不变，完整构建与隔离冒烟通过 |

## 四、编写注意事项

1. **LateUpdate 顺序铁则**：任何在 LateUpdate 里写参数的新逻辑必须在 Order 801 之后（或并入 Live2DRenderer.LateUpdate），否则被 Cubism Physics 覆盖
2. **P0 安全网永不迁移**：Param132-71 眼睛保护、poseLock 清零、_clickSavedParams 摸头锁定——这些是基础守卫不是动画，迁移=事故
3. **3D 分支勿修**：`HybridRenderer` 3D 分支 TODO、`Model3DRenderer` 注释与实现矛盾（注释称绿幕抠像、实际纯黑背景）——3D 未落地，改它浪费工时
4. **默认空闲表情是 "surprise"**，不是文档所称 "curious"——写默认表情逻辑时以代码为准
5. **迁移动作时**：保持「动作时冻结行走」（`_pet.Pause/Resume()`）；迁移后跑动作回归（play_action + 肉眼验证）
6. **测试模式**：涉及表情/动作的自动化测试须开 `.test_mode`，且 `set_expression`/`play_action` 属 operation 意图白名单
7. **参数语义映射**：新参数先查 `Live2DParameterMapper` 与 `KnownParameterPatterns.cs`（KNOW_PATTERNS 单源），勿重复硬编码参数 ID
