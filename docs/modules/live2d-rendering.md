# Live2D 渲染管道 — 渲染、参数映射与硬编码迁移

> **文档作用**: 本模块文档描述桌宠「Live2D 渲染」子系统的**代码真相**——模型加载双保险、80+ 参数映射、Perlin 噪声微动、天气↔表情联动、以及 `Live2DRenderer.cs` 约 427 处 `SetParameter`/`SetParameterValue` 匹配的迁移清单（P0-P4 分级）。改渲染/表情/动作参数相关代码前必读。
> **基本架构**: `HybridRenderer` → `Live2DRenderer`（恒走 Live2D，3D 分支不可用）→ Cubism SDK 5-r.4。当前渲染器是固定 Fuxuan fixture 的适配实现，不是通用模型 provider：编辑器优先读取 `Assets/Live2D/Models/Fuxuan/符玄.prefab`，构建运行时从 `StreamingAssets/Live2D/Fuxuan/符玄.model3.json` 读取，`Resources.Load("Fuxuan")` 仅作回退。参数映射：`Live2DParameterMapper`（语义名 ↔ Cubism 参数 ID）双向映射，核心入口 `Live2DRenderer.SetParameterValue(string, float)`。渲染器现由 `Live2DRenderer.cs`（模型加载、动作与参数）+ `Live2DRenderer.OverlayRendering.cs`（置顶叠加相机、RT、OnGUI 和性能档位）组成同一 partial 类。执行顺序：DesktopPet.Update（0）→ CubismPhysicsController.LateUpdate（800）→ Live2DRenderer.LateUpdate（801，覆盖物理重置参数）。
> **开发历史迭代**: N38（2026-08-02）完成硬编码动作迁移清单（历史基线 379+ 处调用、15 方法、P0-P4 分级）；7 个 legacy 方法（~270 行）已删除由 JSON + IdleActionScheduler 替代，仅保留星辉（#4）与法阵（#7）硬编码；N40 空闲动作 9 种 JSON 驱动；2026-08-29 复核当前 `Live2DRenderer.cs` 仍有约 427 处参数写入匹配，普通参数数据化尚未完成。
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

### 2.1.1 Windows 透明桌面叠加（2026-09-13）

- `WindowOverlay` 让 Unity 主窗口的纯黑背景透出桌面，并保留拖动/点击输入；主相机剔除 Live2D 专用 Layer 31。
- 播放器使用“Layer 31 叠加相机 → 局部 RenderTexture → `NativeLive2DOverlay`”路径。后者以 `UpdateLayeredWindow(ULW_ALPHA)` 输出逐像素 Alpha 窗口，因此不再受 DWM 对 IMGUI RT 回贴和色键的兼容性影响。
- 编辑器仍由 `OnGUI` 回贴局部 RT，供 VisualActionTester 与截图工具使用；Player 不回贴该 RT，避免重复显示。
- 验证入口只在 `.test_mode` 下启用：`@@sim:model-snapshot` 保存叠加 RT，另须以真实桌面截图确认 Native 覆盖层可见且无矩形背景。

### 2.2 模型加载双保险（固定 Fuxuan fixture）

```
Editor: Assets/Live2D/Models/Fuxuan/符玄.prefab
  → AssetDatabase.LoadAssetAtPath<GameObject>
  → 失败时 Resources.Load("Fuxuan") 回退
Player: StreamingAssets/Live2D/Fuxuan/符玄.model3.json
```

这三层路径是当前项目的代码事实，不等于通用 Live2D 目录扫描、运行时换模或热插拔能力。任意新模型需要单独建立合法资源登记、适配器实现、隔离 Probe 证据和人工验收；现有 Fuxuan 参数映射只能作为本地适配案例。

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

### 2.4.1 独立参数探针窗口（2026-09-16）

- `Assets/Scripts/Live2DProbe/ProbeWindowController.cs` 是与 `DesktopPet`、`Live2DRenderer` 和桌宠场景隔离的采样运行时；它只加载目标 Cubism Prefab，冻结非渲染参数写入者，再逐个写入目标 `CubismParameter`。
- 构建入口为 `build.ps1 -ProbeWindow -OutputDir <隔离目录>`，实际 Player 仅包含自动生成的 `Assets/Live2DProbe/ProbeWindow.unity`、目标 Prefab 与探针控制器，输出 `Live2DProbe.exe`，不会替换或控制正式 `DesktopPet.exe`。
- 运行必须提供隔离的 `FU_XUAN_DATA` 和 `.test_mode`；可用 `FU_XUAN_PROBE_PARAMETER=ParamAngleX` 限定单参。每个参数固定采集三轮 `baseline/min/mid/max/reset`，在数据根写出 `capability-report-probe-window.json` 和 15 张 PNG。
- 本地验收已用新构建产物复跑 `ParamAngleX`：3 轮/15 帧，最小/最大平均像素差为 2.319/2.361，复位差为 0 且 `resetStable=true`；人工查看最小/最大帧确认是头部左右朝向变化。该事实只证明独立采样和复位链路可用，不认证其他参数的语义。
- 默认“冻结”模式会让 `ParamBodyAngleX/Y/Z` 为零差异；设置 `FU_XUAN_PROBE_WRITER_MODE=physics` 时，探针仅额外保留 `CubismPhysicsController`，每个采样点等待 8 帧并调用 SDK `Stabilization()`，仍不启用桌宠行为。该模式已三轮复验三轴均可见且复位稳定；云端视觉复核仍须取得对隔离模型帧外发的单独授权。

### 2.5 Perlin 噪声微动（8 通道）

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

### 2.7 硬编码参数迁移清单（P0-P4 分级，2026-08-29 复核约 427 处匹配；379+ 为 N38 历史基线）

| 等级 | 含义 | 示例 | 处理 |
|------|------|------|------|
| 🔴 **P0 - 安全网** | 每帧强制清零/保护，永远不应迁移 | LateUpdate 中 Param63-71=1f、Param132-71 眼睛保护、poseLock 清零 | **保留** |
| 🟡 **P1 - 简易迁移** | 纯线性淡入淡出，可直接 JSON | UpdateIdleTilt、UpdateIdleSmile、歪头/微笑/挑眉/爱心/困惑、SetSwordFinger/SetHandPose/SetHandLayer | 阶段 2 |
| 🟠 **P2 - 中等迁移** | 含多段/条件判断，需封装扩展 | UpdateStretch（20 参数梯形）、UpdateCry（4Hz 抽泣）、UpdateBlush（3Hz 脉冲） | 阶段 3 |
| 🔵 **P3 - 复杂迁移** | 含物理(Spring/Perlin)/阶段状态机 | UpdateMagicCircle（~80+ 次/帧，3 Act）、UpdateStarSpin（5 阶段） | 阶段 4（或永久保留） |
| ⚪ **P4 - 保留硬编码** | 性能敏感/引擎耦合/永不移 | UpdateWalkAnimation、UpdateBlink、眼球 Perlin+鼠标覆盖 | 保留 |

**已迁移**（N38）：7 个 legacy 方法（~270 行）已删除，由 JSON + IdleActionScheduler 替代；**硬编码保留**：UpdateStarSpin（动作4 星辉）+ UpdateMagicCircle（动作7 法阵）。

### 2.8 空闲动作驱动

9 种 JSON 配置（7 参数化 + 2 硬编码特效），含权重/冷却/天气时段调制：夜晚微笑×0.3 / 雨哭×1.8 / 雪微笑×1.5。7 种普通动作的目标键已统一为 `Live2DParameterMapper` 语义名，运行时通过缓存的 `CubismParameter` 写入；调度器使用 Newtonsoft.Json 解析字典目标，并复用逐帧目标缓冲。旧 `ParamXX` 键仍由渲染器保留兼容回退。特效涉及参数：星星（Param451/541/1071/1081）、紫环 9 个、黑幕 2、白圈 5、镜头 3、头发速度 16、饰品速度 3、衣服速度 6。

### 2.9 行走与动作互斥（2026-08-20）

空闲动作和行走属于同一套 Live2D 姿态写入通道，不能在同一帧叠加：

- 自动空闲动作只有在 `isWalking == false` 时才允许启动；`isWalking` 要求宠物落地、有水平速度、未暂停且没有强制/AI 控制锁。
- 行走恢复时，`LateUpdate` 会先中断自动动作并调用 `ResetIdleAction(true)`，再写入行走姿态，避免上一动作的头部、身体或手臂参数残留。
- 右键/测试触发的旧动作（包括 #4 星辉、#7 法阵）使用 `_actionLocked`，并暂停 `DesktopPet` 的物理移动；动作结束时只恢复由该动作引入的暂停状态。
- #4 `UpdateStarSpin()` 仍是五阶段硬编码动作；#7 `UpdateMagicCircle()` 仍是五阶段 Spring/Perlin 复杂动作。二者均不应被行走姿态覆盖。
- 2026-09-11 可见播放器复核后，行走的节奏与身体参数维持用户确认的原始值：24px/s、单次移动 2–4 秒、停留 6–15 秒、5rad/s 步频与 4px 重心起伏。只保留测试模式的强制走路入口用于验收；不虚构模型不存在的腿部参数，也不改动上述动作互斥与停止收敛链路。

测试模式下可用以下 inbox 命令复核旧动作和渲染快照（必须先创建隔离目录中的 `.test_mode`）：

| 命令 | 作用 |
|------|------|
| `@@idle:1` … `@@idle:9` | 触发对应旧空闲动作；4、7 走硬编码实现，其余走 JSON 调度器 |
| `@@shot:<name>` | 保存当前 Live2D 模型快照到 `{FU_XUAN_DATA}/action_captures/<name>.png` |
| `@@sim:idle-actions:off` | **仅测试模式**暂停并复位已启动的空闲动作，供单一动作时序取证 |
| `@@sim:model-measurement-snapshot` | **仅测试模式**保存未裁切的模型 RT；用于时序测量，不能替代给视觉模型看的裁切快照 |

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

- `符玄.physics3.json` 的物理步进频率为 60 FPS，桌宠后台 High/Normal/Low 档的主循环目标现在为 60/45/30 FPS；这三个目标会随性能监控档位调整，不代表每档都能保证实际帧率。
- `Live2DRenderer.LateUpdate()` 在 Cubism Physics（800）之后执行；普通路径按隔帧节奏调用 `ForceUpdateNow()`。该调用会强制 Cubism Core 重算当前参数对应的网格，不会重新派发 `CubismPhysicsController`；隔帧的目的是限制额外 Core 重算的主线程开销。法阵（#7）保留其专用最终提交路径。
- 该隔帧策略是对“走路正常、停止抖动”回归的保守修复；待团结引擎许可证恢复后，需要在可见播放器中重新确认衣服后摆的流畅度，再决定是否拆分网格刷新与物理刷新。
- 2026-08-24 修复硬编码动作的手部穿模：`SetHandLayer()` 的 Param95/98/100/108/116/117/119/120 统一采用 `Live2DMotionTemplates` 的图层权重，不再使用偏低的旧值，避免抬臂时手/袖被衣服网格压到后方。

2026-08-24 在可见真实播放器中重新抓取并检查 #4/#7 的中段与结束截图；隐藏窗口截图会得到黑色 RT，因此视觉回归必须使用可见播放器。完整构建与运行时冒烟通过。

### 2.13 叠加渲染职责拆分（2026-08-26）

- `Live2DRenderer.OverlayRendering.cs` 统一承载模型置顶的 Layer 排除、叠加相机、RenderTexture 重建、OnGUI 绘制、资源释放和性能档位回调；`Live2DRenderer.cs` 保留模型加载、参数、动作和交互逻辑。
- 本次是 partial 文件边界整理，不改变 Layer 31、透明 RT、相机同步或 `rtResolutionScale` 的运行时行为；快速构建、完整构建和隔离运行时冒烟均已通过。
- 后续若调整 RT 格式、透明合成或性能档位，只需优先检查该 partial 与 `PerformanceMonitor` 的契约，并补可见播放器回归；不要把渲染资源释放重新散落回模型动作逻辑。

### 2.14 角色局部 RT 与高刷新率（2026-08-29）

- 正式构建中，模型叠加相机根据所有 Live2D `Renderer.bounds` 合并后的屏幕包围盒取景，额外保留 64px 边距；局部 RT 最小为 320×480，尺寸按 32px 量化，避免动画轻微变化导致每帧重建纹理。
- 原生透明窗口仍保持主屏大小，`OnGUI` 只把局部 RT 绘制到角色对应的屏幕矩形；这样保留现有拖拽、点击穿透和面板坐标链路，同时减少 Live2D RT 的像素填充。
- 主相机为非正交投影时自动回退全屏 RT；局部取景只对当前正交相机路径生效。
- 性能档位目标帧率调整为 High/Normal/Low = 60/45/30。代码构建和隔离冒烟已通过，但实际 GPU 占用、真实帧率和视觉裁切仍需在可见播放器中测量，不能仅以冒烟通过宣称性能收益。

### 2.15 AI 调试输入与拖动挣扎修复（2026-08-29）

- `RightPanel.CheckTestInbox()` 将 `@@sim:`（兼容别名 `@@input:`）交给 `RuntimeInputSimulator`；命令只在 `.test_mode` 下生效，不调用 OS 鼠标 API。命令本身不直接发起 LLM 请求，但点击/拖动会复用真实事件回调，可能触发 AutoChat；测试模式阻止生产持久化与云端调用。
- `@@sim:status` 输出位置/尺寸/拖动候选/拖动状态/速度/暂停状态；`@@sim:walk:left|right|stop` 可在测试模式强制启动或停止地面任务，用于对照步态和位移；`@@sim:click:x,y`、`@@sim:click:center` 复用真实点击姿势和事件回调；`@@sim:drag:x1,y1->x2,y2[,steps]` 与 `@@sim:drag:offset:dx,dy[,steps]` 按帧推进拖动，默认 12 步；`@@sim:reset`/`@@sim:release` 中止并清理模拟输入。
- `@@sim:screenshot[:name]` 在测试模式下调用 Unity `ScreenCapture` 保存当前渲染帧到 `DataRoot/test_screenshots/`。拖拽方向回归必须至少执行右拖截图、左拖截图，并在角色已镜像后重复一次，不能只看参数符号或编译结果。
- `DragHandler` 在按下后保持透明层接收输入，避免鼠标离开动态宠物矩形后丢失 MouseDrag/MouseUp；位置改为浮点累积后取整，并限制在屏幕内。窗口失焦会中止拖动，避免卡在 `isDragging`。
- `Live2DRenderer` 的挣扎速度每帧只采样一次，即使 `OnPetUpdate` 和 `LateUpdate` 都覆盖姿态也不会把速度重复衰减；速度平滑和左臂幅度已修正为可见、可跟手的范围。
- `DragHandler` 通过 `IPetRenderer.OnDragPointer` 把屏幕指针位置与位移传给 Live2D；`Live2DRenderer.Update()` 在 CubismPhysics 前用目标速度 + 加速度限幅写入真实物理输入 `ParamAngleX/Y/Z`、`ParamBodyAngleX/Y/Z`，停手后输入逐步衰减。拖拽相对角色中心的位置还会提供抓取点偏置，物理输出再驱动头发、衣服和手臂，形成方向相关的滞后/回弹。
- 2026-08-29 追加拖拽响应降幅：降低鼠标位移到物理输入、衣物/头发直接输出和整体视觉滞后的增益；手臂、腿部和身体挣扎改为随拖拽强度渐进，避免轻微拖动时大幅甩动，同时保留快速拖动的挣扎反馈。Quick/完整构建通过，并用 Unity 截图闭环完成左右拖动及镜像后方向回归；GPU/帧率和裁切仍待专项测量。

测试模式下可用 PowerShell 写入隔离 `inbox.txt`：

```powershell
Set-Content "$env:TEMP\fuxuan_smoke_test\inbox.txt" '@@sim:status'
Set-Content "$env:TEMP\fuxuan_smoke_test\inbox.txt" '@@sim:drag:offset:120,20,12'
```

### 2.16 参数写入缓存与物理刷新观测（2026-08-29）

- `Live2DRenderer` 在固定 Fuxuan fixture 加载完成后建立 `参数 ID → CubismParameter` 缓存；运行时的 `SetParameter()`、调试偏移和参数范围查询均复用缓存，避免高频路径反复调用 `Parameters.FindById()`。当前没有通用模型注册表、任意 `.model3.json` 导入或运行时热插拔换模。
- `Live2DParameterMapper` 现在也在映射加载或换模型时建立 `参数 ID → CubismParameter` 缓存；语义化动作的 `Set()`/`Get()` 复用该缓存，不再每帧重复 `FindById()`。换模型仍通过 `RefreshRanges()` 重建缓存，保持缺失参数和范围校验行为不变。
- 所有 `ForceUpdateNow()` 入口统一经过 `ForceUpdateModelNow()`，保留原有调用条件和物理顺序，只增加本帧、上一帧、最近一秒和历史单帧峰值计数，供后续可见播放器性能观测使用。
- 本轮不改变参数值、拖拽方向、物理频率或局部 RT 裁切策略；参数缓存与统计完成代码级和运行时链路验证，实际 CPU/GPU 收益仍需专项 Profiling 对照。
- `build.ps1 -Quick`、完整构建和隔离 `runtime_smoke.cjs --verbose` 已通过；EditMode XML 仍沿用项目现有的新鲜度规则单独判断。

### 2.17 局部 RT 取景单次更新（2026-08-29）

- `UpdateOverlayFraming()` 会遍历模型 Renderer 包围盒、计算局部 RT 尺寸并同步叠加相机；普通帧现在只在 `LateUpdate()` 的最终姿态完成后执行一次，不再在 `Update()` 阶段重复计算。
- 拖拽和点击姿态锁定会提前从 `LateUpdate()` 返回，因此这两条路径在 `ForceUpdateModelNow()` 后显式补做一次取景更新，保证局部 RT 跟随最终模型姿态。
- `OnGUI()` 只保留首次无效矩形时的兜底更新和 RT 绘制，不把包围盒计算绑定到 IMGUI 绘制事件；本轮 Quick、完整构建和隔离运行时冒烟通过，实际 CPU/GPU 收益仍待可见播放器 Profiling。

### 2.18 走路停止时的物理收敛（2026-08-30）

- 走路刚停止的 `IDLE_BLEND_DURATION` 期间，`LateUpdate()` 不再额外调用普通路径的 `ForceUpdateModelNow()`；此时 `Update()` 已在 Cubism Physics（800）前把停止输入归零，避免在姿态交接期间增加无效的 Core 网格重算。
- 该保护只覆盖“停止收敛”窗口，正常行走、稳定空闲，以及星辉（#4）/法阵（#7）专用刷新路径不变；`Live2DRenderer` 仍保持 801 晚于 Cubism Physics 800。
- 本轮 Quick、完整构建和隔离运行时冒烟均通过；停走瞬间的头部稳定性仍需在可见播放器中人工确认，不能仅以自动化冒烟替代视觉验收。

### 2.19 动作参数单写入者与同帧刷新审查（2026-09-06）

- 参数写入按阶段划分：`Update()` 仅写供 Physics(800) 读取的拖拽/边缘反弹输入；`LateUpdate()` 在 Physics 后写最终视觉姿态。拖拽的这两段写入是输入与输出的分层，`OnPetUpdate()` 在拖拽时提前返回，且速度一帧只采样一次，不属于同一参数的并发覆盖。
- `ActionPresetPlayer.StopWithFade()` 现在直接停止原播放协程。旧实现会把协程句柄换成一个不写参数的“淡出”协程，导致旧关键帧协程继续运行，随后与新预设或 AI 动作在同一帧争写参数。
- `GenerateMotionTool` 在启动 `MotionGenerator` 前拒绝已有旧式动作、预设动作或 AI 参数动作的请求，并立即停止表情；`PlayAction()` 与 `ForceIdleAction()` 也会立即停止表情，`PlayExpression()` 在动作/AI 控制锁生效时拒绝请求。这样预设、旧式动作、AI 关键帧与表情不会并行控制同一模型参数。
- 普通动作的最终 `ForceUpdateModelNow()` 延后到本帧后处理参数全部写完后统一提交，消除了左臂物理拦截与基础路径在偶数帧重复强制 Cubism Core 更新的问题。法阵的 `MaterialPropertyBlock` 刷新从每帧两遍 Drawable 遍历收束为最终阶段的一遍。
- 动作结束后，头发/衣摆的小幅 Bounds 波动曾使局部 RT 在相邻的 32px 量化尺寸之间反复重建（实测 `320×480 ↔ 320×512`），透明窗口会短暂闪黑，表现为头发闪烁。局部取景现在对一个量化单位的尺寸差保留现有 RT；只有至少 64px 的真实扩张才重建，64px 裁切边距覆盖该波动。
- `ForceUpdateCountThisFrame`、`ForceUpdateCountLastFrame`、`ForceUpdateCountLastSecond` 和 `ForceUpdateMaxPerFrame` 保留为可见播放器 Profiling 的观测口。2026-09-06 已通过 `build.ps1 -Quick` 和隔离 EditMode 测试（failed=0）；仍需在可见播放器中连续触发预设切换、`@@idle:7` 与 AI 动作，结合 Profiler 验收帧时间和视觉连贯性。

### 2.20 外部动作输入单租约（2026-09-16）

- `Assets/Scripts/Embodied/Live2DInputCoordinator.cs` 为表情、旧预设动作与 AI 生成动作分配递增的 `Live2DInputLease`；当前采用安全优先的全局单租约，活动租约未释放时第二个外部输入必定拒绝。
- `Live2DRenderer.PlayExpression()`、`PlayAction()`、`GenerateMotionTool`、`MotionAgent` 的自主/组合/表情动作以及 `VisionMotionVerifier` 已接入该协调器。动作或生成动作开始前以零淡出收束表情；动作正常完成、超时以及 Renderer 销毁会释放租约。非当前 `requestId` 不能释放活动租约。
- 本轮没有迁移 `Live2DRenderer` 自身的行走、掉落、拖拽、物理与空闲逐帧基线写入；它们仍是未经外部租约仲裁的内部基线。其最终参数提交现已经 `ParameterCommitBridge`，但这不等同于全模型已经完成动作资源级仲裁或唯一语义动作所有者。
- 已新增 `Live2DInputCoordinatorTests` 覆盖互斥、错误释放、防重用与 `ReleaseAll`；2026-09-16 隔离 `build.ps1 -RunTests` 通过（failed=0）。实施边界与后续桥接要求见 `docs/guides/approved/live2d-input-coordination.md`。

- 为 `AC-INPUT-04` 增加了只在 `.test_mode` 下可用的语义测试命令：`@@sim:lease:generated:begin|release` 只取得/释放 `GeneratedMotion` 租约，`@@sim:legacy:stretch` 只请求既有 `PlayAction("stretch")`；它们不接受参数 ID 或数值。2026-09-16 的隔离运行时复核中，生成动作租约活动时旧动作记录 `Rejected LegacyAction/stretch`；旧动作租约活动时生成动作记录 `Rejected GeneratedMotion/runtime-input-conflict-test`；旧动作最终记录 `Released LegacyAction/stretch#2: action-completed`。临时实例随后经 `@@test:quit` 正常退出。该证据只验证全局单租约互斥与收束，不认证任何动作语义或自然度。
- `@@test:quit` 在测试模式下会先调用 `Live2DRenderer.PrepareForTestExit()`：它收束测试候选、表情和旧动作，再以 `ReleaseAll("test-exit-fallback")` 处理未知活动租约，最后才走既有托盘退出回调；非测试模式调用会被拒绝。2026-09-16 隔离复核中，执行中的 `LegacyAction/stretch#1` 在退出前记录 `Released ...: action-test-exit`，随后记录 `test-exit input cleanup completed`，临时进程正常退出。此项只提供测试退出的确定性收束证据，不替代 Windows 关机/注销等真实系统生命周期验收。

### 2.21 参数提交桥接（2026-09-16）

- `ParameterCommitBridge` 持有 `参数 ID → CubismParameter` 的解析边界；`Live2DRenderer.SetParameter()`、调试偏移以及 `Live2DParameterMapper.Set()` 的运行时写入均经由其 `Commit()` 赋值。Mapper 仍负责语义解析与范围钳制，桥接层不承载动作策略。
- 独立探针和 `Assets/Scripts/Editor/` 标定工具仍直接写入模型参数，这是隔离诊断协议的一部分，不会链接入桌宠 Player 运行时；它们不得作为产品动作入口使用。
- 2026-09-16 `build.ps1 -Quick` 通过。动作原语尚未开放：能力目录条目仍为 `not-certified`，下一步是骨架候选组合验证，而不是把参数名交给 LLM。

### 2.22 骨架组合本地验证（2026-09-16）

- 独立 `Live2DProbe.exe` 新增 `FU_XUAN_PROBE_COMBINATIONS=skeleton` 模式；它固定检测 7 组证据：躯干+头部三轴、左右手臂，以及躯干+单臂。每组保存成员单项、组合最小/最大、复位帧，重复三轮。
- 本次在隔离目录完成 204 帧：7 组的复位差均为 `0`、`resetStable=true`；组合可见差为 0.722–7.176。人工抽查显示 Z 组合有可见的侧向头发/朝向变化，右臂候选组有可见抬臂。
- 这些仅构成组合视觉复核候选；没有自然度、方向稳定性与独立视觉认证前，任何组不得成为 `Certified` 或 LLM 可调用动作。
- DeepSeek 组合复核（2026-09-16，约 12.5k tokens）对 7 组均返回“可见、自然、复位一致、高置信”。头/躯干三轴组显示稳定的左右倾斜、俯仰与侧向摆动；两组手臂及躯干+单臂组则均被识别为双臂变化，尚无法可靠归因到单侧或确认躯干贡献。因此它们保持 `Supporting` 候选而非 `Certified`；下一轮必须用已有单成员帧完成归因复核。
- 成员归因复核（2026-09-16，约 21.6k tokens）已使用同一隔离证据包完成：`ParamAngleX` 为头部左右朝向候选，`ParamAngleY` 为头部俯仰候选，均为可见变化、复位一致、高置信；`ParamAngleZ` 的主影响是头发/配饰侧摆，头部仅为次级影响，不能提前命名为独立的头部横滚。DeepSeek 当时把 `Param31` 误判为双臂收拢/展开，后续全量帧差分、独立复验及交叉组合已将其更正为画面左侧单臂姿态候选；`Param34`、`Param36` 仍仅为双臂下垂/平举候选。`ParamBodyAngleX/Y/Z`、`Param32/33/37` 在冻结画面中无可见变化，其中 BodyAngle 三项仅保留为“待物理模式复验”的条件输入，不能判为无效或动作原语。
- 上述所有结果保持 `Supporting`：证据来自单一 DeepSeek 视觉裁判，且尚无资源归属与第二模型交叉验证。下一阶段是从全参数目录筛选单臂候选做隔离本地发现，先证明左右独立性，再决定是否申请第二轮云端复核；不得把这些参数 ID 暴露给 LLM 或写入 `fuxuan_map.json`。
- 单臂发现第一轮已在新的隔离目录重跑 `Param94`、`Param97`、`Param100`、`Param68`（各三轮、均复位稳定）。`Param94` 的峰值像素差为 `2.46979`，直接图像复核确认仅模型画面右侧手臂抬起，保留为单臂视觉复核候选；`Param97` 仅 `0.361097`，`Param100`/`Param68` 分别为 `0.039680`/`0.001096`，本轮均不具备足够的本地可见证据。该结果仍不等同于模型左右语义、资源归属或正式动作认证。
- 对 `Param94` 的 GLM 第二裁判复核已在用户授权后发起，但接口返回 `429`，未产生可用视觉结论。该失败必须保留为“第二模型暂不可用”，不得把 DeepSeek 结果重复计算为双模型通过，也不得据此降低认证门槛；待服务恢复后可复用相同帧哈希缓存协议重试。
- 经用户授权，GLM 不可用时由 Codex 以可见帧作替代审查：已逐帧核对 `Param94` 第一轮的 baseline/min/max/reset。max 仅使模型画面右侧的一只手臂由下垂抬起；min 与基线近似一致，reset 回到基线，未见头、躯干或另一侧手臂的连带变化。此人工裁判结论与本地像素指标和 DeepSeek 结论一致，允许其继续作为“单臂候选”参与后续组合发现；它不是 GLM 结果，不得将“画面右侧”擅自转换为模型语义左/右臂，也不足以单独授予 `Certified`。
- 全量截图的左右区域差分和逐帧复核纠正了此前 DeepSeek 对 `Param31` 的“双臂联动”误判：`Param31` 实际只影响画面左侧手臂的外展/收束姿态，三轮冻结复验峰值差为 `1.416524`、复位差为 `0`；`Param94` 只影响画面右侧手臂的抬起姿态。两者均保持 `Supporting`，且不使用模型语义左/右命名。
- 探针已新增通用 `FU_XUAN_PROBE_COMBINATION_IDS` 入口。双参数除了同向最小/最大外还采集 `min/max` 与 `max/min` 交叉角点，解决“两个参数有效方向相反”被遗漏的问题。`Param31 + Param94` 的交叉 `min/max` 三轮可见差为 `3.886314`、复位差为 `0`；直接帧审查未见互相覆盖或异常形变，证明两条单臂通道能并存，但它们不是镜像动作。
- `ParamBodyAngleX/Y/Z` 已在只保留 Cubism 物理控制器的隔离模式重跑，三者均三轮稳定并复位为零：X 峰值 `5.375540`、Y 峰值 `13.831004`、Z 峰值 `27.043394`。帧审查显示它们带动躯干/重心及头发、衣物等附属物，Z 的侧倾尤其大。三者归类为 `Conditional` 的物理躯干输入：后续只可经限幅、渐变和资源仲裁的内部技能使用，不得直接暴露给 LLM，也不得以冻结模式的“无变化”判定为无效。
- 2026-09-17 保守动态边界复验：物理模式下 `ParamBodyAngleX/Y` 在 `[-3,3]`、`ParamBodyAngleZ` 在 `[-0.75,0.75]` 各执行三次 12 步采样，峰值差为 `2.733860` / `1.753670` / `8.092014`，复位差均为 `0`。这是稳定离散采样而非生产逐帧插值认证；三者仍为 `Conditional`，不得注册技能或开放给 LLM。详见 [L3 躯干保守动态与动作边界验证](../truth/l3-torso-conservative-dynamic-boundary.md)。
- 同日对 Z 轴保守序列的全帧双模型评审曾发生分歧（DeepSeek 10、GLM 92）；从同一隔离帧裁出躯干区域后，DeepSeek/GLM 复核一致为 85/92，均认可连续小幅侧倾、稳定复位且不低于步行基线。该结果解决了评审尺度分歧，但只补齐候选视觉/语义/自然度证据；候选继续保持 `Conditional`，不可自动认证或接入运行时。详见 [L3 躯干 Z 轴候选双模型评审](../truth/l3-torso-z-candidate-review.md)。
- 保守幅度首轮已完成三轮隔离验证且全部复位稳定：`Param31` 以 `[-0.5, 0.5]`（峰值 `0.780912`）形成轻度画面左侧手臂姿态；`Param94` 以 `[-15, 30]`（峰值 `2.414272`）时，中间值 `7.5` 为自然的轻度画面右侧手臂外展，最大 `30` 接近平举，只能作为显式手势上限。物理躯干 X/Y 暂取 `[-3, 3]`（峰值 `2.767720`/`7.969861`）；Z 即使 `[-2, 2]` 仍过大，进一步收至 `[-0.75, 0.75]` 后为可见但轻微的侧倾（峰值 `8.092014`）。这些是测试通过的内部默认候选范围，不是正式 `fuxuan_map.json` 映射、LLM 接口或最终动作认证；实际运行仍须经渐变、资源仲裁和组合自然度测试。
- 保守组合首轮：自定义组合探针以 `FU_XUAN_PROBE_COMBINATION_RANGE_SCALE=0.25` 对 `ParamAngleX + Param94` 采样，三轮交叉角点可见差为 `3.261844`、复位差为 `0`。直接帧审查显示转头与轻度画面右侧手臂外展可同时出现，无穿模或层级覆盖；该组合可进入语义动作原语设计，但仍是 `Supporting` 证据，未开放给 LLM。
- 同一保守缩放下，`ParamBodyAngleX + Param31`（物理模式）三轮组合差为 `2.502688`/`2.471063`、复位差为 `0`，躯干物理跟随未覆盖画面左侧手臂姿态；`ParamBodyAngleX + ParamAngleY` 的交叉角点差为 `3.633059`、复位差为 `0`，帧审查确认躯干重心变化期间头部俯仰仍独立可控。两组均无穿模或异常层级，形成头—躯干—单臂首轮组合证据；仍需后续做动态渐变、抢占和长时间稳定性测试。
- 连续可感知性首轮：`Param94` 从 `0 → 15 → 0` 的 12 步扫动峰值差为 `2.085081`、复位差为 `0`，可作为显式手势候选；`Param31` 必须扫向负向 `0 → -0.5 → 0`，修复目标方向后峰值仅 `0.780912`、复位差为 `0`。因此 `Param31` 只保留为细微姿态层，不满足用户可明显感知的显式动作门槛；不得把连续可达误写成显式动作可用。
- 保守动态复验补充了 `ParamAngleX`：在独立探针中以 `[-15,15]` 的 12 步 `0 → 15 → 0` 扫动，峰值差 `1.721964`、复位差 `0`，相邻 RGB 均值/峰值 `0.3472/0.3520`。同批 `Param94` 的相邻 RGB 均值/峰值为 `0.7585/0.9114`。两者均说明离线插值不是只在端点跳变；它们仍不构成运行时自然度、组合认证或 LLM 接口。
- 独立探针现支持 `FU_XUAN_PROBE_COMBINATION_SWEEP_IDS`：对两个或更多参数在保守 `FU_XUAN_PROBE_COMBINATION_RANGE_SCALE` 内同步扫动、三轮重复并输出 `custom-combination-sweep-report.json`。`ParamAngleX + Param94` 在 `0.25` 缩放、12 步下得到组合峰值差 `3.238710`、最大复位差 `0`，全序列相邻 RGB 均值/峰值 `0.9336/1.1142`；首轮基线/峰值/复位帧审查未见明显穿模或层级反转。该能力只用于离线组合证据，结果仍为 `Supporting`，不开放 LLM 或正式映射。
- `scripts/test/run_probe_combination_sweep.cjs` 可选传入 `physics`，让独立探针只保留 Cubism 物理写入者；`ParamBodyAngleX + Param94` 在该模式、`0.1` 缩放、12 步下三轮复位差为 `0`，峰值差 `2.934924`，未见物理跟随覆盖手臂。躯干本身变化较轻微，仍仅是 `Conditional/Supporting` 组合证据，不构成单独躯干技能或自然度认证。

### 2.23 走路可感知性标定（2026-09-16）

- 用户确认现有硬编码走路的可感知幅度可作为首版动作质量的参照。参照对象是**内部姿态的时序变化**，不是桌宠根窗口横向位移；静态手势无需、也不应以屏幕移动距离达标。
- `scripts/test/walk_baseline_drive.cjs <独立构建 DesktopPet.exe>` 会创建临时 `FU_XUAN_DATA`/`.test_mode`，等待落地后暂停空闲动作、额外等待 2.5 秒使既有特效和 RT 尺寸收敛，再强制向右走并保存 16 张相位帧和 1 张停止恢复帧。它只启动隔离实例，不能用于生产数据目录或正在使用的正式 exe。
- `CaptureModelSnapshot(cropToModelArea: false)` 和 `@@sim:model-measurement-snapshot` 为该测量保留完整模型 RT；默认裁切快照行为不变，仍服务视觉审查。`scripts/live2d-probe/analyze_perceptibility_sequence.cjs <png目录>` 以同批最大 RT 画布、模型轮廓水平中心对齐后计算差异，剔除根横移但保留走路弹跳。
- 已在隔离临时构建复录纯走路周期：17 帧均为 `320×480`，空闲动作只发生在隔离命令前，走路期间未再启动。前 16 个行走相位的相邻 RGB 平均差为 `41.8427`、峰值 `97.6475`；相对首相位的峰值 RGB 差为 `109.7859`，轮廓变化峰值为 `0.3345`。可见帧复核确认头身左右摆动、衣饰/发饰跟随与步态变化。
- 这些数值仅是同一模型、同一 RT 路径、同一采样节奏下的**相对基线**，不是跨模型绝对评分，也尚不构成“动作已认证”。后续显式动作应以同一测量链路比较：先满足无干扰、连续变化、复位与自然度硬门槛，再按动作类型决定其应接近走路基准的全身/局部比例；微姿态层不适用该阈值。

### 2.24 Param94 开发者候选手势（2026-09-16）

- `@@sim:gesture:param94` 仅在 `.test_mode` 下可触发固定的 `0 → 15 → 0`、2.4 秒正弦缓入缓出序列。它只证明画面右侧单臂可上抬再放下；不赋予招呼、示意、挥手等语义，也不是正式用户技能、高层语义接口或 LLM 参数入口。
- 该候选必须通过 `Live2DInputCoordinator` 获取 `CandidateTest` 单租约，且只允许稳定静止状态启动；走路或其淡入/收束期间一律拒绝。隔离运行日志已验证：正常静止启动会记录 `Accepted CandidateTest/param94-gesture#1`，正常完成会记录 `Released ... candidate-test-completed`；另一次在已落地、`velocity=(1,0)`、`task=MoveRightTime` 的真实行走中触发，门禁记录 `walking=True` 与 `[CandidateTest] rejected-static-gate`，候选未启动。动作期间保留移动锁，完成时清零参数、释放租约和移动锁。
- 恢复验收已在独立隔离实例完成：在显式停止自动边缘移动并确认 `velocity=(0,0)` 后，候选被接受并正常释放；其后强制普通走路可再次达到 `velocity=(1,0)`。因此“正常完成后不遗留候选租约或移动锁”已获运行时证据。中途取消、渲染器异常与进程退出期间的恢复仍须单列测试，不得由本结论外推。
- 中途取消验收已完成：测试专用 `@@sim:gesture:param94:cancel` 在候选序列中段停止协程，记录 `candidate-test-cancelled` 释放和 `[CandidateTest] cleanup`，随后普通走路再次达到 `velocity=(1,0)`。测试退出验收也已完成：`@@test:quit` 在进入原有桌宠退出链前显式清理仍在运行的测试候选，并记录 `candidate-test-before-test-exit` 释放；这只为隔离测试提供可审计证据，不改变托盘退出或 Windows 关机/注销的既有验收结论。
- 它仍为 `Supporting` 候选：只允许用于开发者时序、抢占和恢复实验；不得注册为 `CertifiedSkillRegistry` 项、不得加入自主动作、不得改写正式参数映射。

## 三、开发历史迭代

| 版本 | 日期 | 变更 |
|------|------|------|
| N31-N37 | — | Perlin 噪声待机、自动眨眼+鼠标跟随、FPS 自适应、手部图层前置、调试偏移通道、模型加载双保险、参数范围自动打印、天气联动、KNOW_PATTERNS 单源化（KnownParameterPatterns.cs） |
| N38 | 2026-08-02 | 硬编码参数迁移清单（阶段一：梳理 379+ 处调用、15 方法、P0-P4 分级）；7 个 legacy 动作方法删除（~270 行）→ JSON + IdleActionScheduler；保留星辉/法阵硬编码 |
| N40 | 2026-08-08 | 空闲动作 9 种 JSON 配置驱动确认；迁移路线图阶段 2-5 待执行 |
| 2026-08-26 | 2026-08-26 | 将 Live2D 叠加渲染与性能档位入口拆至 `Live2DRenderer.OverlayRendering.cs`；行为不变，完整构建与隔离冒烟通过 |
| 2026-08-29 | 2026-08-29 | 叠加层改为模型包围盒局部 RT + 局部正交相机 + 局部 IMGUI 绘制；原生透明窗口保持全屏以兼容现有交互；目标帧率调整为 High/Normal/Low = 60/45/30，Quick/完整构建/隔离冒烟通过 |
| 2026-08-29 | 2026-08-29 | 增加 `@@sim`/`@@input` 测试输入链路；修复透明层导致的拖动中途丢输入、拖动越界和失焦残留；修复挣扎速度重复采样与明显迟滞；Quick/完整构建/隔离冒烟通过 |
| 2026-08-29 | 2026-08-29 | 对照官方 CubismTargetPoint 接入拖拽目标速度/加速度限幅；通过 `IPetRenderer.OnDragPointer` 将位移和抓取点送入 physics3 输入链路，移除 `OnPetUpdate` 中物理前的重复姿态覆盖；Quick/完整构建/隔离冒烟通过，真实观感仍需可见播放器确认 |
| 2026-08-29 | 2026-08-29 | 降低拖拽物理/衣物/头发/视觉滞后的响应增益，并让手臂、腿部、身体挣扎随拖拽强度渐进；使用 `@@sim:screenshot` 完成左右拖动与镜像后方向回归 |
| 2026-08-29 | 2026-08-29 | 缓存 Live2D 参数引用，并统一 `ForceUpdateNow()` 统计入口；不改变视觉参数和物理条件，Quick/完整构建/隔离冒烟通过，性能收益待专项测量 |
| 2026-08-29 | 2026-08-29 | 局部 RT 取景改为物理完成后的每帧单次更新；拖拽/点击锁定提前返回路径补齐取景同步，避免普通帧重复遍历 Renderer 包围盒 |
| 2026-08-29 | 2026-08-29 | `Live2DParameterMapper` 增加语义参数对象缓存，减少普通动作 `Set/Get` 的重复查找；Quick/完整构建/隔离冒烟通过，普通动作模板迁移仍继续 |
| 2026-08-29 | 2026-08-29 | 普通空闲动作配置改用语义参数名；修复 `JsonUtility` 无法读取 `Dictionary` 导致目标为空；复用目标/冷却缓冲并修复重复尾段的 `star_spin.json`；Quick/完整构建、隔离冒烟和 `@@idle:1` 截图闭环通过 |
| 2026-08-30 | 2026-08-30 | 走路停止过渡期间跳过普通路径的额外 `ForceUpdateNow()`，避免物理重复推进造成停下瞬间头部剧烈抖动；Quick/完整构建/隔离冒烟通过，真实观感待可见播放器确认 |
| 2026-09-06 | 2026-09-06 | 审查并收束预设、旧式动作、AI 关键帧和表情的参数写入权；修复预设淡出遗留协程、普通动作同帧重复 Cubism Core 更新、法阵重复 PropertyBlock 遍历，以及动作结束时局部 RT 在相邻量化尺寸间反复重建导致的头发闪烁；Quick、隔离 EditMode 与 #4/#7 临时播放器复测通过 |

## 四、编写注意事项

1. **LateUpdate 顺序铁则**：任何在 LateUpdate 里写参数的新逻辑必须在 Order 801 之后（或并入 Live2DRenderer.LateUpdate），否则被 Cubism Physics 覆盖
2. **P0 安全网永不迁移**：Param132-71 眼睛保护、poseLock 清零、_clickSavedParams 摸头锁定——这些是基础守卫不是动画，迁移=事故
3. **3D 分支勿修**：`HybridRenderer` 3D 分支 TODO、`Model3DRenderer` 注释与实现矛盾（注释称绿幕抠像、实际纯黑背景）——3D 未落地，改它浪费工时
4. **默认空闲表情是 "surprise"**，不是文档所称 "curious"——写默认表情逻辑时以代码为准
5. **迁移动作时**：保持「动作时冻结行走」（`_pet.Pause/Resume()`）；迁移后跑动作回归（play_action + 肉眼验证）
6. **测试模式**：涉及表情/动作的自动化测试须开 `.test_mode`，且 `set_expression`/`play_action` 属 operation 意图白名单
7. **参数语义映射**：新参数先查 `Live2DParameterMapper` 与 `KnownParameterPatterns.cs`（KNOW_PATTERNS 单源），勿重复硬编码参数 ID
8. **单写入者铁则**：每个可见参数在一个帧阶段只能有一个动作来源。新预设/协程必须先终止旧协程；AI 关键帧、旧式动作和表情要受同一控制锁约束；网格强制更新应在该帧全部参数写完后最多提交一次。
## 2026-09-13 Windows 桌面逐像素透明层

Windows D3D11 Player 的 Unity 主窗口不再承担 Live2D 像素合成：DWM 玻璃层仅保留
透明输入与窗口生命周期，`NativeLive2DOverlay` 将局部 `ModelOverlayRT` 以
`UpdateLayeredWindow(ULW_ALPHA)` 输出为独立、置顶、逐像素 Alpha 的 Win32 窗口。
这避免了色键方案在本机表现为整块洋红背景，以及 DWM 直接合成时吞掉模型的两类故障。

- 模型窗口在 `WM_NCHITTEST` 返回 `HTTRANSPARENT`，拖动和点击仍交给下方 Unity
  `DragHandler`；不要添加 `WS_EX_TRANSPARENT`，它会把模型延后绘制到 Unity 主窗之后。
- 覆盖层依据 `_overlayDrawRect` 定位，并将首帧可能产生的负 Bounds 限制到可见屏幕内。
- 像素数据由局部 RT 以 45 FPS 读回，转换为预乘 Alpha 的 BGRA，再交给 Win32；RT 快照
  入口仍可用于视觉验收。
> **2026-09-14 稳定发布基线**：正式透明展示固定为同步 RGBA32 → 预乘 BGRA 的 Win32 分层窗口路径，45 FPS 输出。`NativeLive2DOverlay` 只在模型尺寸变化时重建像素缓冲，不使用 `AsyncGPUReadback`；首次可见与失败诊断会记录矩形、帧率、最后成功提交和失败原因。

> **2026-09-17 拖动焦点重建修复**：原生模型覆盖窗在局部 RT 尺寸变化时重建，曾使 Unity 主窗短暂失焦并让 `DragHandler` 直接丢弃正在进行的拖动。根因修复在 `UpdateOverlayFraming()`：`DesktopPet.isDragging` 为真时固定当前 RT 尺寸，鼠标松开后再按最新 Bounds 重建原生覆盖窗；不再依赖不稳定的前台窗口归属判断。Quick、EditMode（219 通过、1 项既有忽略、0 失败）和临时播放器完整构建已通过，人工复测确认连续拖动及松手后的再次抓取均正常。
