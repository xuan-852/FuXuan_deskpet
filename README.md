# Desktop Pet — 符玄桌面宠物

![Unity](https://img.shields.io/badge/Unity-2022.3.62_LTS-000000?logo=unity)
![Live2D](https://img.shields.io/badge/Live2D-Cubism_5--r.4-FF6B9D)
![C#](https://img.shields.io/badge/C%23-512BD4?logo=csharp)
![DeepSeek](https://img.shields.io/badge/DeepSeek-Function_Calling-4F46E5)
![Platform](https://img.shields.io/badge/Platform-Windows_10%2F11-00A4EF?logo=windows)

将 **崩坏：星穹铁道 — 符玄** 作为 Live2D 桌面宠物，在 Windows 桌面上陪伴你。

利用 Unity 透明窗口 + Live2D Cubism SDK 渲染，结合物理模拟、交互反馈、昼夜/天气响应、AI 对话、微信小程序数据打通，营造生动的桌面伙伴体验。

---

## ✨ 功能一览

### 🎮 交互
- **拖拽移动** — 按住任意位置拖拽，角色会挣扎划水 + 衣服/头发物理摆动
- **分区点击反馈** — 点击头部/身体/腿部有不同反应（歪头戳脸/害羞捂胸/踢腿）
- **右键菜单** — 设置、动作、聊天、便签四标签面板
- **AI 对话** — 底部输入框 + DeepSeek Function Calling，可调用 28+ 工具
- **文件搜索** — 集成 Everything 实现毫秒级全盘文件搜索，AI 可直接「帮我找文件」
- **小程序数据互通** — AI 可查询微信小程序服务端的考试安排、课程表、成绩、学业概览
- **AI 操控角色** — 对话指令即可切换面部表情、播放动作动画、停止动作
- **点击穿透** — 鼠标在宠物上可交互，在宠物外直接穿透到桌面，无需拖拽"激活"
- **开机自启** — 系统托盘一键设置开机自启

### 🎭 动画
- **自然待机** — Perlin 噪声驱动的呼吸、身体微晃、眼球微动
- **11 种空闲动作** — 歪头卖萌、微笑眯眼、挑眉、星辉环绕、伸懒腰、爱心眨眼、数钱、委屈、法阵展开、害羞黑脸、困惑歪头
- **走路动画** — 横版走路 + 身体颠簸 + 无缝空闲过渡
- **平滑转身** — 方向切换时 scale.x 用 Lerp 渐变，避免 180° 瞬间翻转（TURN_SPEED=10, ≈0.15s）
- **走路犯困表情** — 走路时随机触发眼皮渐沉 + 低头，夜晚/深夜更频繁
- **眨眼** — 自动随机眨眼
- **鼠标跟随** — 眼球平滑追踪鼠标位置
- **FPS 自适应** — 性能低时自动降低动画频率

### 🌤 时间/天气响应
- **昼夜感知** — 读取系统时间，夜晚眼皮微垂、犯困动作增多
- **天气响应** — 通过 wttr.in API 获取当地天气：
  - ☀️ 晴/多云 → 自然微笑
  - 🌧 阴雨/雷雨 → 委屈表情 + 皱眉
  - ❄️ 下雪 → 好奇张嘴睁大眼
- **待机气泡** — 无交互后头顶冒泡，内容根据时间/天气变化（共 90+ 条符玄风格台词）
- **气泡古风装饰** — 紫色云纹角饰（左上+右下）+ 独立呼吸闪烁星点，全部代码生成无额外贴图

### 🤖 AI 聊天
- **DeepSeek API** — 集成 DeepSeek Chat + Function Calling，最多 5 轮工具调用循环
- **28+ 工具** — 打开网页、搜索、搜文件（Everything 毫秒级）、截图、调音量、记便签、查天气、查成绩、查课表、查考试、学业概览、切换表情、播放动作等
- **协程异步执行** — 5 个网络 IO/后台工具（学业查询 + 文件搜索）通过 Unity 协程异步执行，不阻塞主线程
- **Everything 文件搜索** — AI 可通过「帮我找文件」调用 `search_files` 工具，优先使用 Everything CLI（es.exe）实现全盘毫秒级搜索，未安装时自动回退递归搜索
- **小程序数据互通** — 连接微信课表小程序服务端，实时查询学业数据
- **AI 操控角色** — 直接说"换个开心表情""伸个懒腰"，AI 自动调用表情/动作工具
- **自动闲聊** — 无操作一段时间后角色主动搭话
- **句子队列** — 长回复逐句显示，打字机效果
- **优先级气泡** — AI 回复高优显示，不被闲话问候覆盖

### 📋 便签提醒
- **增删改查** — 本地 JSON 持久化，支持每日/工作日/每周重复
- **到期提醒** — 头顶气泡 + Windows Toast 通知
- **手机推送** — 通过 Server酱³ 推送到手机 App
- **AI 驱动** — 聊天时直接说"提醒我下午3点买菜"，AI 自动调用工具
- **服务端同步** — 小程序服务端统一维护提醒队列

### 🔎 文件搜索（Everything 集成）
- **毫秒级全盘搜索** — 集成 Everything CLI（es.exe），AI 说「帮我找文件」时毫秒级返回结果
- **自动检测** — 启动时自动搜索 Program Files、LocalAppData 及 PATH 中的 es.exe
- **智能回退** — 未安装 Everything 时自动切换到递归目录搜索
- **路径限定** — 支持指定搜索根目录，缩小搜索范围
- **最多 200 结果** — 防结果过多，必要时可缩小查询词

### 🏃 物理
- **CubismPhysics** — 衣服/头发/裙子/配饰自然物理摆动
- **直接驱动** — 拖拽时帧间速度实时输入物理系统，裙子/法盘/头发惯性跟随
- **头发驱动** — 20 个输出参数全部物理绕过，实现飘逸效果

### 🖥 技术特性
- **透明窗口** — Win32 API（DWM DwmExtendFrameIntoClientArea）实现 Unity 窗口穿透 + 镂空，无绿边
- **点击穿透** — 每帧动态管理 WS_EX_TRANSPARENT，宠物内交互、宠物外穿透
- **系统托盘** — Shell_NotifyIcon 最小化到通知区域，支持开机自启
- **底部输入栏** — 内置 AI 聊天输入框 + Windows 搜索风格
- **调试窗口** — 实时调参面板（FPS/CPU/内存监控）
- **编码优化** — 默认 GBK 编码兼容中文
- **性能监控** — FPS/CPU/内存实时监控，低帧率自动调节

---

## 📂 目录结构

```
Desktop_per_pro/
├── code/desktop unity/
│   ├── Assets/
│   │   ├── Scripts/              ← C# 脚本（完整）
│   │   │   ├── DesktopPet.cs         # 主控制器
│   │   │   ├── Live2DRenderer.cs     # Live2D 渲染 + 动画 + 物理驱动
│   │   │   ├── DragHandler.cs        # 拖拽/点击交互
│   │   │   ├── TimeWeatherController.cs  # 昼夜/天气
│   │   │   ├── ChatBubble.cs         # 头顶气泡（含优先级系统）
│   │   │   ├── ChatManager.cs        # AI 对话 + Function Calling + 协程工具调度
│   │   │   ├── AutoChat.cs           # 自动闲聊
│   │   │   ├── BottomInputBar.cs     # 底部输入栏
│   │   │   ├── ContextMenu.cs        # 右键菜单（设置/动作/聊天/便签）
│   │   │   ├── ReminderManager.cs    # 便签提醒 + Server酱³ 推送
│   │   │   ├── ServerPollService.cs  # 服务端推送轮询 + 数据查询
│   │   │   ├── PerformanceMonitor.cs # FPS/CPU/内存监控
│   │   │   ├── SystemTrayManager.cs  # 系统托盘图标
│   │   │   ├── DebugWindow.cs        # 调试调参面板
│   │   │   ├── WindowOverlay.cs      # 透明窗口（DWM DwmExtendFrameIntoClientArea）
│   │   │   ├── IPetRenderer.cs       # 渲染接口
│   │   │   ├── HybridRenderer.cs     # 混合渲染器
│   │   │   ├── Model3DRenderer.cs    # 3D 渲染器
│   │   │   ├── ToolCallInvoker.cs    # AI 工具调用分发（28+ 工具，含协程异步执行）
│   │   │   ├── PetConfig.cs          # 天机簿 — 配置持久化（JSON保存/加载）
│   │   │   ├── PetMemory.cs          # 忆境 — 长期记忆系统（记忆摘要+JSON持久）
│   │   ├── Animations/               # 3D 模型动画
│   │   ├── Editor/                   # 构建脚本
│   │   ├── Live2D/Models/Fuxuan/     ← 符玄 Live2D 模型
│   │   ├── Models/Fuxuan/            # 3D FBX 模型
│   │   ├── Prefabs/                  # 预制体
│   │   ├── Scenes/SampleScene.scene  # 主场景
│   │   ├── StreamingAssets/          # 流式资源
│   │   └── Resources/                # 运行时加载资源
│   ├── Packages/
│   │   └── manifest.json             # 依赖管理
│   └── ProjectSettings/              # Unity 项目设置
├── file/                             # 原始模型文件（gitignored）
├── project_brief/                    # 设计文档 (LaTeX + PDF)
└── README.md
```

## 📜 脚本架构

```
DesktopPet (主控制器, Update order=0)
├── DragHandler          ← 鼠标交互 / 点击穿透
├── ChatBubble           ← 头顶气泡（含优先级系统）
├── ChatManager          ← AI 对话 + Function Calling + 逐句队列 + 协程调度
│   └── 加载 Resources/SystemPrompt.txt + 注入 PetMemory 记忆
├── AutoChat             ← 自动闲聊 + 问候库
├── BottomInputBar       ← 底部输入栏
├── ContextMenu          ← 右键菜单
├── TimeWeatherController ← 时间/天气
├── ReminderManager      ← 便签提醒 + Server酱³ 推送
├── ServerPollService    ← 服务端轮询 + 小程序数据查询
├── PerformanceMonitor   ← FPS/CPU/内存监控
├── SystemTrayManager    ← 系统托盘图标
├── DebugWindow          ← 调试面板
├── PetConfig            ← 天机簿 — 配置持久化（JSON保存/加载，重启时 ApplyAll）
├── PetMemory            ← 忆境 — 长期记忆系统（自动记录关键交互，注入 system prompt）
├── WindowOverlay        ← 透明窗口（DWM 玻璃层）
└── Live2DRenderer (IPetRenderer, Update order=801)
    └── CubismPhysicsController (order=800) ← 物理
```

**执行顺序：**
1. `DesktopPet.Update(0)` → 状态更新（位置、速度、行走相位）
2. `CubismPhysicsController.Update(800)` → 衣服/头发物理模拟
3. `Live2DRenderer.LateUpdate(801)` → 覆盖被物理重置的参数 + 空闲动画 + 交互反馈

### ⚡ 协程异步工具架构

AI 工具调用中，网络 IO 型工具（5 个）通过 Unity 协程异步执行，不阻塞主线程：

```
ChatManager.SendMessage()
  └─ StartCoroutine(SendRequestCoroutine())
       └─ StartCoroutine(DoToolLoop())   ← 最多 5 轮 tool_call 循环
            ├─ StartCoroutine(PostRequest())  ← HTTP 请求
            ├─ 解析 tool_calls
            ├─ IsCoroutineTool()? ═╗
            │   YES ──→ StartCoroutine(ToolCallInvoker.ExecuteCoroutine())
            │   NO  ──→ Execute() 同步执行
            └─ 继续下一轮 / 结束

ToolCallInvoker (协程工具注册表)
├── RegisterCoroutineTools()
│   ├── query_exams       → RunAsyncTool(ServerPollService.QueryUpcomingExamsAsync)
│   ├── query_scores      → RunAsyncTool(ServerPollService.QueryScoresAsync)
│   ├── query_schedule    → RunAsyncTool(ServerPollService.QueryScheduleAsync)
│   ├── query_user_status → RunAsyncTool(ServerPollService.QueryUserStatusAsync)
│   └── search_files      → RunAsyncTool(Task.Run → SearchFilesTask)
│
├── RunAsyncTool(Func<Task<string>>)
│   └── 每帧 yield return null 检查 task.IsCompleted ← 非阻塞
│
├── GetCoroutineResult()  → 获取异步执行结果
└── IsCoroutineTool()     → 判断工具是否走协程
```

### 🧠 长期记忆 + 配置持久化架构

```
PetMemory (忆境, 单例, DontDestroyOnLoad)
├── AddMemory(summary, topic)  ← OnToolResult 自动调用
│   └── 同话题冷却 120s 防重复
├── GetFormattedMemories()      → 注入到 SystemPrompt
├── Save() / Load()             → pet_memory.json
└── ClearMemories()             → 右键菜单按钮

PetConfig (天机簿, 单例, DontDestroyOnLoad)
├── CollectAll()  ← 从 ChatManager/TimeWeatherController/DesktopPet 读取
├── Save()        → pet_config.json
├── Load()        ← 启动时自动加载
└── ApplyAll()    → 写入各组件覆盖 Inspector 默认值

ChatManager (SystemPrompt 注入流程)
├── Resources/SystemPrompt.txt … 静态 prompt 模板
├── {current_time} … 运行时替换为 DateTime.Now
└── PetMemory.Instance?.GetFormattedMemories() … 追加到末尾
```

**三个系统的协作流程：**
1. 启动 → PetConfig.Load() → PetConfig.ApplyAll() 恢复权重/API/天气
2. 启动 → ChatManager.Awake() 加载 SystemPrompt.txt
3. 对话 → BuildSystemPrompt() 替换 {current_time} + 注入记忆
4. 工具调用 → OnToolResult → PetMemory.AddMemory() 自动记录
5. 右键菜单「保存配置」→ PetConfig.CollectAll() + Save()
6. 右键菜单「清空忆境」→ PetMemory.ClearMemories()

**协程 vs 同步的分界策略：**
- **同步执行**（23+ 个工具）：`open_url`、`take_screenshot`、`set_volume` 等瞬时完成的系统操作
- **协程异步执行**（5 个工具）：`query_exams`、`query_scores`、`query_schedule`、`query_user_status`、`search_files` —— 涉及 HTTP 请求或后台线程文件搜索，可能耗时数百毫秒到数秒
- `RunAsyncTool` 将 `async Task` 包装为协程，每帧检测 `IsCompleted`，不阻塞 Unity 主线程

## 🔧 开发环境

| 工具 | 版本 |
|---|---|
| Unity | 2022.3.62 LTS |
| Live2D Cubism SDK | 5-r.4 |
| .NET / C# | .NET Framework 4.x / C# 9.0 |
| Windows | 10/11 |
| IDE | Visual Studio 2022 / VS Code |

## 🚀 快速开始

1. **克隆仓库**
   ```bash
   git clone https://github.com/xuan-852/Desktop_per_pro.git
   ```

2. **导入 Live2D Cubism SDK**
   - 从 [Live2D 官网](https://www.live2d.com/sdk/about/) 下载 Cubism SDK 5-r.4
   - 导入到 Unity 项目中

3. **放置模型**
   - 将 符玄 Live2D 模型文件放到 `Assets/Live2D/Models/Fuxuan/` 目录下
   - Cubism SDK 会自动生成 Prefab

4. **在 Unity 中打开场景** `Assets/Scenes/SampleScene.scene`
   - 检查 `DesktopPet` 对象的 Inspector 中 `Live2DRenderer.modelPrefab` 是否已引用

5. **运行** → 点击 Play

## 📦 依赖

- [Live2D Cubism SDK 5-r.4](https://www.live2d.com/sdk/about/)
- Newtonsoft.Json（Unity 包管理器安装）
- Unity UI (UGUI)
- [Everything CLI (es.exe)](https://www.voidtools.com/downloads/)（可选）— 用于毫秒级全盘文件搜索，下载 `ES-1.1.0.30.x64.zip` 解压到 `%LOCALAPPDATA%\Everything\es.exe` 即可

## 📄 许可证

本项目仅用于个人学习和技术研究，严禁商业用途。

- Live2D 模型版权归 © 米哈游（崩坏：星穹铁道）所有
- Cubism SDK 版权归 © Live2D Inc. 所有

## 📚 参考

- [Live2D Cubism SDK 文档](https://docs.live2d.com/)
- [Unity 透明窗口实现](https://github.com/XJINE/Unity_TransparentWindowManager)
- 原 GDI+ 桌宠项目：`D:\C\Desktop pet\`
- 流萤 Live2D 模型来源：[B站@是依七哒](https://space.bilibili.com/457683484) / [Scighost/Firefly](https://github.com/Scighost/Firefly)

## ⚠️ 已知问题

### 1. 窗口 Z 顺序问题

**现象：** 拖动任意普通窗口（如 Edge、文件资源管理器）时，窗口会出现在底部输入栏与任务栏之间的透明间隙中。效果上违背了 `[任务栏] → [输入框] → [窗口] → [宠物]` 的期望层级。

**原因：** Unity 窗口为全屏 TOPMOST（保证宠物永远在顶层），所有非 TOPMOST 的普通窗口都渲染在 Unity 窗口之下。任务管理器等自带 TOPMOST 属性的窗口不受影响。

**尝试过的方案：**
- `SPI_GETWORKAREA` 限制窗口到工作区 → 宠物被任务栏遮挡
- 白底背景延伸到屏幕底部 → Edge 等窗口被白底"切断"，视觉更差
- 半透明遮罩延伸到屏幕底部 → 仅缓解，未从根本上解决问题

**根本矛盾：** 全屏 TOPMOST 窗口无法同时满足"普通窗口在宠物之上"和"宠物在任务栏之上"。需引入更精细的窗口管理策略（如动态调整窗口大小/位置、多窗口分层渲染、或 Hook 窗口 Z 顺序事件）方可解决。

**状态：** ⏳ 待后续研究

### 2. 空闲动作不够自然

**现象：** 部分空闲动作（尤其是过渡衔接时）生硬不自然，整体流畅度仍有提升空间。

**原因：** 当前动作系统为纯参数插值（缓入缓出曲线），缺少对 Live2D 模型自身 BlendShape 混合的精细控制。动作切换时的参数冲突（如前一个动作的参数尚未完全归零，新动作已经开始叠加）导致视觉上的"跳变"感。

**涉及动作：** 歪头、微笑、挑眉、星辉、伸懒腰、爱心眼、数钱、委屈、法阵、害羞、困惑共 11 种。

### 3. AI 活动感知与性格优化（待改进）

**现象：** AI 对用户活动的感知粗粒度（仅 8 类：coding/gaming/studying/browsing/entertainment/communication/idle/other），
窗口标题被丢弃未传给 AI；AI 回复存在说教感和攻击性，"傲娇"特质被过度放大。

**根因与改进方向：**

| 维度 | 现状 | 改进目标 |
|------|------|---------|
| **活动感知粒度** | 8 个笼统分类，用 ActivityTracker.PollForeground() 做 GetForegroundWindow 快照 | 将窗口标题实时注入 SystemPrompt，让 DeepSeek 自行理解用户当前活动（如"VS Code 写 Python"、"Edge 看 B 站"） |
| **多窗口感知** | 仅追踪最顶层窗口，`EnumWindows` 未使用 | 枚举所有可见窗口，分析多任务上下文 |
| **浏览器深度** | 仅识别进程名（如 msedge.exe） | 集成浏览器插件或 Accessibility API 读取标签页 URL |
| **性格温婉约束** | SystemPrompt.txt 缺少"不要评判/说教"约束 | 加入温婉指令抑制 AI 的说教倾向 |
| **傲娇权重** | IdleChatGenerator 的 system prompt 中"傲娇"权重过高 | 平衡"傲"与"娇"的比例，语气更温柔 |
| **窗口标题利用** | Classify() 后丢弃标题 | 传标题给 AI 用于回复上下文 |

**参考开源方案：** [ActivityWatch](https://github.com/ActivityWatch/aw-watcher-window)（⭐ 14k+）是最大的开源时间追踪项目，但其 Windows 实现同样使用
`GetForegroundWindow` 单窗口追踪。改进方向可借鉴其生态中的浏览器扩展（aw-watcher-web）获取标签页 URL，
以及 aw-watcher-afk 组件检测空闲状态。

**状态：** ⏳ 待后续优化

**状态：** 🔧 持续调整中

### 3. 视觉特效已移除

**现象：** 动作 4（星辉环绕）和动作 9（法阵显现）的视觉特效（紫环旋转、黑幕、眼镜发光、星星闪烁、七星盘、白圈）已被移除，仅保留动作时长和空壳函数。动作 7（数钱）的"双眼放光"参数也可能受影响。

**原因：** 过渡到 DWM 黑色透明方案后，半透明特效像素与黑色背景不兼容。在 RGBA 渲染中，半透明特效像素与黑色 (0,0,0) 背景混合后会变暗或消失，无法正确显示。

**影响范围：**
- 动作 4（星辉）：紫环旋转、黑幕、眼镜发光、星星闪烁 → 全部移除
- 动作 9（法阵）：黑幕、白圈、七星盘、眼镜发光、镜头缩放 → 全部移除，仅保留五阶段手势变化
- 动作 7（数钱）：Param121/137/132 双眼放光 → 显示效果受限

**解决方向：** 需要实现独立的半透明叠加层渲染（不依赖 Unity 主窗口的黑色透明），或在 DWM 外部另建一个半透明覆盖窗口来渲染特效。

**状态：** ⏳ 待后续研究

### 4. 3D 渲染器未实现

**现象：** `Model3DRenderer.cs` 已挂载但处于 disabled 状态，3D 渲染通路仅有接口骨架，无法实际使用。

**背景：** 项目中设计了 `IPetRenderer` 接口和 `HybridRenderer` 混合渲染器，意图在 Live2D（精细表情）和 3D（走路/飞行动画）之间切换。当前仅 Live2D 渲染器活跃，所有走路/飞行状态也由 Live2D 模拟完成。

**影响：**
- `HybridRenderer` 的渲染模式 2（强制 3D）不可用
- 走路/飞行动画无法使用 3D 骨骼动画
- `Model3DRenderer` 的背景同样设为黑色以跟随 DWM 透明方案，但无实际渲染内容

**状态：** ⏳ 待后续实现

### 5. 困惑动作（11 号）权重为 0

**现象：** 困惑（Confused）动作在空闲动作循环中权重为 0，永远不会自发触发。

**原因：** 代码中 `_idleActionWeights` 数组的第 11 个元素值为 `0`。有趣的是，LaTeX 文档（`project_brief/report.tex`）的动作权重表中记录困惑权重为 `2`（占 6.5%），与代码实际值不符。困惑动作被设计为仅由外部强制调用（如 AI 对话中角色表达困惑时调用），而非自然空闲循环的一部分。

**影响：** 用户在普通使用中永远看不到歪头 + 皱眉 + 眯眼的困惑表情，除非通过 AI 聊天或调试面板强制触发。

**状态：** 🔧 可按需调整权重使其自然出现（同时需同步更新 LaTeX 文档）

### 6. Everything 未安装时文件搜索降级

**现象：** 若未安装 Everything（es.exe），`search_files` 工具将自动回退到递归目录搜索模式，速度较慢且仅能搜索桌面目录，全盘搜索不可用。

**解决：** 从 [voidtools.com](https://www.voidtools.com/downloads/) 下载 `ES-1.1.0.30.x64.zip`，将 `es.exe` 解压到 `%LOCALAPPDATA%\Everything\es.exe` 即可。

**状态：** 📦 可选依赖

### 7. 工具调用同步阻塞（待优化）

**现象：** 所有 AI 工具（`search_files`、`query_scores` 等）在当前线程同步执行，阻塞 Unity 主线程。

**影响：** Everything 模式下 `search_files` 为毫秒级，但递归搜索回退时可能阻塞主线程长达 10 秒。异步协程方案已规划但尚未实施。

**状态：** ⏳ 待后续优化
