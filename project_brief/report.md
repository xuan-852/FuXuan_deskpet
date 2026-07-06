# 符玄桌面宠物 — 技术报告

> 文档版本: N32d · 最后更新: 2026-07-07

---

## 摘要

本文档描述了 **符玄桌面宠物 (Fu Xuan Desktop Pet)** 项目的完整技术架构与实现细节。该项目基于 Unity（团结引擎 Tuanjie 2022.3.62t7）与 Live2D Cubism SDK 5-r.4，构建了一个运行于 Windows 桌面的 AI 驱动虚拟角色。系统深度集成 DeepSeek Chat API（含 Function Calling）、GLM-4V 多模态视觉模型、以及多层级感知与记忆系统，实现了从自然语言交互到具身动作表达的完整闭环。

---

## 目录

1. [项目概述](#1-项目概述)
2. [系统架构](#2-系统架构)
3. [渲染管道](#3-渲染管道)
4. [AI 对话系统](#4-ai-对话系统)
5. [具身动作系统](#5-具身动作系统)
6. [闭环具身智能验证](#6-闭环具身智能验证)
7. [交互系统](#7-交互系统)
8. [感知与记忆系统](#8-感知与记忆系统)
9. [桌面物理引擎](#9-桌面物理引擎)
10. [窗口与系统集成](#10-窗口与系统集成)
11. [性能优化](#11-性能优化)
12. [便签与提醒系统](#12-便签与提醒系统)
13. [构建与部署](#13-构建与部署)
14. [附录](#14-附录)

---

## 1. 项目概述

### 1.1 项目目标

开发一个运行于 Windows 桌面的、具有 AI 对话、物理交互、环境感知能力的 Live2D 虚拟角色桌面宠物。

### 1.2 技术栈

| 组件 | 技术选型 |
|------|---------|
| 游戏引擎 | Tuanjie 2022.3.62t7（团结引擎，Unity 2022.3 LTS 衍生版） |
| 2D 角色渲染 | Live2D Cubism SDK 5-r.4 |
| 3D 角色渲染 | Unity Animator + FBX（预留） |
| AI 对话 | DeepSeek Chat API + Function Calling |
| 视觉模型 | GLM-4V（智谱） |
| 天气数据 | wttr.in / QWeather API |
| 文件搜索 | Everything CLI (es.exe) / 递归回退 |
| 推送通知 | Server酱³ |
| 窗口系统 | Win32 API (DWM / Shell_NotifyIcon) |
| 构建脚本 | PowerShell |

### 1.3 核心指标

- 渲染帧率：20-60fps（自适应降档）
- AI 响应延迟：1-5s（取决于网络与工具调用轮次）
- 内存管理：800MB GC 触发 / 1200MB 强制 GC
- 工具调用：27+ 注册工具，最多 5 轮回环
- 空闲动作：9 种 JSON 配置驱动 + 2 种硬编码特效
- 长期记忆：核心事实 + Top-20 重要 + 近期琐事，JSON 持久化

---

## 2. 系统架构

### 2.1 分层架构

系统采用分层架构设计，从底层到上层依次为：

```
┌──────────────────────────────────────────────────────────┐
│                     UI 表示层                             │
│   ChatBubble  ContextMenu  BottomInputBar  DebugWindow   │
├──────────────────────────────────────────────────────────┤
│                    AI 核心层                              │
│   ChatManager  ToolCallInvoker  IdleChatGenerator        │
│   MotionTranslator  MotionGenerator  MotionVerifier      │
├──────────────────────────────────────────────────────────┤
│                    感知层                                 │
│   ActivityTracker  BrowserTabReader  PetMemory           │
│   TimeWeatherController  PerformanceMonitor              │
├──────────────────────────────────────────────────────────┤
│                    物理层                                 │
│   DesktopPet (重力/碰撞/地面检测)  DragHandler           │
├──────────────────────────────────────────────────────────┤
│                    渲染层                                 │
│   HybridRenderer → Live2DRenderer / Model3DRenderer     │
├──────────────────────────────────────────────────────────┤
│                    系统层                                 │
│   WindowOverlay (DWM)  SystemTrayManager  ServerPoll     │
└──────────────────────────────────────────────────────────┘
```

### 2.2 执行顺序

Unity 脚本执行顺序通过 `DefaultExecutionOrder` 属性严格控制：

1. **DesktopPet.Update()** (order=0) — 物理更新、状态转换、行走相位
2. **CubismPhysicsController.LateUpdate()** (order=800) — 衣服/头发/裙子物理模拟
3. **Live2DRenderer.LateUpdate()** (order=801) — 覆盖被物理重置的 Live2D 参数、空闲动画、交互反馈

> 顺序 801 确保所有参数在 Cubism Physics 运算后写入，避免物理覆盖关键参数（如手臂摆幅）。

### 2.3 依赖注入模式

组件间通过 Unity Inspector 序列化引用或单例模式连接：

```csharp
// DesktopPet 持有核心组件引用（Inspector 拖拽赋值）
public DragHandler dragHandler;
public Live2DRenderer renderer;
public ChatManager chatManager;
public WindowOverlay windowOverlay;

// 全局单例访问
ChatConfig.ApiKey  // 环境变量静态访问
PetMemory.Instance  // 忆境单例
PetConfig.Instance  // 天机簿单例
```

---

## 3. 渲染管道

### 3.1 Live2D Cubism 渲染

#### 3.1.1 模型加载双保险

`Live2DRenderer` 使用两级加载策略：

```
AssetDatabase.LoadAssetAtPath<GameObject>(path)
    ├── 成功 → 实例化 prefab
    └── 失败 → Resources.Load("Fuxuan") 降级
```

第一级加载 StreamingAssets 中的 Cubism 预制体（Editor 模式），第二级从 Resources 目录运行时加载（构建模式降级）。

#### 3.1.2 参数映射系统

`Live2DParameterMapper` 建立语义参数名 → Cubism 参数 ID 的双向映射：

```csharp
// 注册语义参数
mapper.RegisterParameter("ParamAngleX", "head_angle_x");
mapper.RegisterParameter("ParamBodyAngleX", "body_angle_x");
// 使用
mapper.SetParameterValue("head_angle_x", 15f);  // 头左倾15度
```

涵盖约 80+ 个 Live2D 参数，按身体部位分组：
- 头部：`ParamAngleX/Y/Z`（3轴旋转）
- 身体：`ParamBodyAngleX/Y/Z`（3轴倾角）
- 眼睛：`ParamEyeLOpen/ROpen`、`ParamEyeBallX/Y`、`ParamEyeLSmile/RSmile`
- 眉毛：`ParamBrowRY/LY`、`ParamBrowRX/LX`
- 嘴巴：`ParamMouthForm`、`ParamMouthOpenY`
- 手臂：`Param31-37`、`Param92-120`（左右臂多段）
- 呼吸：`ParamBreath`

#### 3.1.3 Perlin 噪声驱动空闲微动

空闲状态由 Perlin 噪声驱动 7 组微动参数，消除周期性感：

| 参数 | 通道 | 振幅 | 描述 |
|------|------|------|------|
| ParamBreath | (t, 0) | BREATH_AMPLITUDE | 呼吸起伏 |
| ParamBodyAngleX | (t, 1) | BODY_SWAY_X | 身体左右晃动 |
| ParamBodyAngleY | (t, 2) | BODY_SWAY_Y | 身体前后晃 |
| ParamBodyAngleZ | (t, 3) | BODY_SWAY_Z | 身体旋转晃 |
| ParamAngleX | (t, 4) | HEAD_X | 头部左右微动 |
| ParamAngleY | (t, 5) | HEAD_Y | 头部抬头/低头微动 |
| ParamEyeBallX | (t+offset, 6) | EYE_X | 眼球左右微动 |
| ParamEyeBallY | (t+offset, 7) | EYE_Y | 眼球上下微动 |

噪声相位每帧递增 `_noiseTimeX += Time.deltaTime * NOISE_SPEED`，多通道使用不同的 Perlin 种子值确保互不关联。

#### 3.1.4 表情管理

表情通过 `ExpressionManager` 实现淡入淡出插值：

```csharp
// 设置表情（0.3s 淡入）
SetExpression("开心", 0.3f);
// 恢复默认（0.5s 淡出）
ClearExpression(0.5f);
```

每个表情定义为一个参数-值字典，支持同时叠加多个表情（混合权重求和）。

### 3.2 天气表情联动

天气状态自动影响空闲表情基调：

| 天气 | 表情效果 |
|------|---------|
| ☀️ 晴/多云 | ParamMouthForm 微笑 +0.2 |
| 🌧 雨/阴 | ParamBrowRY/LY 眉抬 +4f，MouthForm 微嘟 +0.2 |
| ⛈ 雷暴 | 同上 + ParamEyeLOpen 微睁 |
| ❄️ 雪 | ParamMouthOpenY 微张 +0.4，EyeLOpen 微睁 +0.2 |
| 🌙 夜晚 (18-22点) | EyeLOpen 垂 +0.07 |
| 😴 深夜 (22-5点) | EyeLOpen 垂 +0.15 |

### 3.3 DWM 透明窗口

窗口透明使用 Windows DWM API 实现：

```csharp
// 1. 设置窗口样式
SetWindowLong(hwnd, GWL_EXSTYLE, WS_EX_LAYERED | WS_EX_TRANSPARENT);

// 2. 扩展玻璃框架（黑色区域=透明）
MARGINS margins = new MARGINS { cxLeftWidth = -1 };
DwmExtendFrameIntoClientArea(hwnd, ref margins);

// 3. 设置背景纯黑透明
Camera.backgroundColor = new Color(0, 0, 0, 0);
```

关键特性：
- **无绿边**：DWM 玻璃层与纯黑背景配合，消除色键抠图的半透明边缘伪影
- **分层渲染**：Live2D 渲染到 RenderTexture，通过 RawImage 显示在 Layer 31
- **点击穿透**：每帧根据鼠标位置动态切换 WS_EX_TRANSPARENT
- **DWM 崩溃保护**：连续 5 次重建失败则跳过 DwmExtendFrameIntoClientArea

### 3.4 3D 模型支持（预留）

`Model3DRenderer` 实现了 `IPetRenderer` 接口但默认未启用。`HybridRenderer` 目前硬编码使用 Live2D 渲染路径：

```csharp
// HybridRenderer.cs — 当前始终走 Live2D
protected override void OnEnable() {
    RequestSwitch(true, false);  // toLive2D=true, instant=false
}
```

3D 渲染器已配置 Orthographic 相机 + Directional Light + Animator 动画状态机（`FuXuanAnimatorController`），待后续启用。

---

## 4. AI 对话系统

### 4.1 DeepSeek Chat API 集成

#### 4.1.1 系统提示词注入

`ChatManager.BuildSystemPrompt()` 在每次对话前动态构建 System Prompt：

```csharp
private string BuildSystemPrompt() {
    string prompt = Resources.Load<TextAsset>("SystemPrompt").text;
    prompt = prompt.Replace("{current_time}", DateTime.Now.ToString("HH:mm"));
    
    // 注入活动观察
    if (ActivityTracker.Instance != null)
        prompt += "\n[活动观察] " + ActivityTracker.Instance.GetEnvironmentSummary();
    
    // 注入长期记忆
    if (PetMemory.Instance != null)
        prompt += "\n[忆境] " + PetMemory.Instance.GetFormattedMemories();
    
    // 注入 Live2D 参数知识
    prompt += ParameterKnowledgeProvider.GetParameterKnowledge();
    
    // 注入闭环验证能力描述
    prompt += GetClosedLoopCapabilityDescription();
    
    return prompt;
}
```

注入内容包括：
- 当前时间（替换 `{current_time}` 占位符）
- 法眼活动追踪摘要（前台窗口、分类、多窗口环境）
- 浏览器标签页列表（通过 UI Automation）
- 长期记忆格式化文本
- Live2D 参数语义知识（告诉 AI 每个参数的作用）
- 具身动作生成和闭环验证能力描述

#### 4.1.2 Function Calling 架构

25+ 个注册工具通过 JSON Schema 描述暴露给 DeepSeek：

```json
{
    "name": "open_url",
    "description": "打开指定 URL",
    "parameters": {
        "url": { "type": "string", "description": "要打开的 URL" }
    }
}
```

#### 4.1.3 多轮工具调用循环

`ChatManager` 实现了最多 5 轮的工具调用回环：

```
SendMessage()
    └── StartCoroutine(SendRequestCoroutine())
        ├── 1. 发送请求（含 system prompt + 历史）
        ├── 2. 解析 response
        ├── 3. 如果有 tool_calls:
        │   ├── 检查 round < 5
        │   ├── 遍历 tool_calls:
        │   │   ├── IsCoroutineTool()? → StartCoroutine(ExecuteCoroutine())
        │   │   └── 同步工具 → Execute()
        │   ├── 收集结果
        │   └── 回到 1（下一轮）
        └── 4. 无 tool_calls → 处理回复文本
```

#### 4.1.4 异步工具执行

5 个可能耗时的工具通过协程异步执行，不阻塞主线程：

| 工具 | 异步方式 | 典型耗时 |
|------|---------|---------|
| query_exams | Task.Run → HTTP GET | 200-800ms |
| query_scores | Task.Run → HTTP GET | 200-800ms |
| query_schedule | Task.Run → HTTP GET | 200-800ms |
| query_user_status | Task.Run → HTTP GET | 200-800ms |
| search_files | Task.Run → Everything CLI | <100ms 或 1-3s |

协程异步封装：

```csharp
private IEnumerator RunAsyncTool(Func<Task<string>> asyncFunc, Action<string> onResult) {
    var task = asyncFunc();
    while (!task.IsCompleted) {
        yield return null;  // 每帧检查，不阻塞
    }
    onResult(task.Result);
}
```

### 4.2 工具索引

| 工具 | 功能 | 类型 |
|------|------|------|
| get_time | 获取当前时间 | 同步 |
| open_url | 打开网页/文件 | 同步 |
| search_web | 联网搜索 | 同步 |
| get_weather | 查询天气 | 同步 |
| take_screenshot | 截屏 + GLM-4V 分析 | 同步 |
| set_volume / mute | 音量控制 | 同步 |
| manage_reminders | 便签 CRUD | 同步 |
| system_power | 关机/重启/睡眠 | 同步 |
| execute_command | 执行 CMD | 同步 |
| get_clipboard / set_clipboard | 剪贴板 | 同步 |
| get_memories / write_memory | 长期记忆操作 | 同步 |
| start_conversation | 发起聊天（含开场白推荐） | 同步 |
| messenger | 模拟回复 | 同步 |
| write_note | 写便签 | 同步 |
| send_notification | 发送 Windows 通知 | 同步 |
| get_pet_status | 获取宠物位置/状态 | 同步 |
| get_mouse_pos | 获取鼠标坐标 | 同步 |
| set_expression | 切换面部表情 | 同步 |
| play_action | 播放预设动作 | 同步 |
| stop_action | 停止当前动作 | 同步 |
| query_exams | 查询考试安排 | **协程异步** |
| query_scores | 查询成绩 | **协程异步** |
| query_schedule | 查询课表 | **协程异步** |
| query_user_status | 查询学业概览 | **协程异步** |
| search_files | 全盘文件搜索 | **协程异步** |
| inspect_motion_memory | 查看动作记忆 | 同步 |
| generate_motion | LLM 生成自定义动作 | **协程异步** |

### 4.3 句子队列与优先级气泡

#### 4.3.1 逐句显示

长回复被拆分为单个句子，以打字机风格逐句显示：

```csharp
// 句子分割
string[] sentences = replyText.Split(
    new[] { '。', '！', '？', '\n' },
    StringSplitOptions.RemoveEmptyEntries);

// 逐句推送至队列
foreach (var sentence in sentences) {
    _sentenceQueue.Enqueue(sentence.Trim() + "。");
}
```

#### 4.3.2 气泡优先级系统

`ChatBubble` 实现了三级优先级抢占机制：

| 优先级 | 用途 | 覆盖规则 |
|--------|------|---------|
| Low | 闲话、定时问候、待机气泡 | 可被任何高优消息覆盖 |
| Normal | 提醒、交互回应 | 不可被 Low 覆盖 |
| High | AI 主动回复 | 不可被任何消息覆盖 |

### 4.4 自动闲聊系统

`IdleChatGenerator` 在无交互时自动生成对话：

- **触发条件**：60-120s 无操作（可配置）
- **生成方式**：DeepSeek API 批量预生成（每次 10 条），本地 JSON 缓存
- **缓存淘汰**：按时间周期自动刷新（早晨/上午/下午/夜晚/深夜各一批）
- **回退机制**：API 不可用时使用硬编码问候语列表

### 4.5 底部输入栏

`BottomInputBar` 提供 Windows 搜索风格的简洁输入界面：

- **固定坐标**：`BAR_LEFT=265, BAR_TOP=635, BAR_RIGHT=1528, BAR_BOTTOM=1600`
- **淡入动画**：0.5s 缓动
- **Enter 发送**：直接提交到 ChatManager
- **焦点管理**：避免与 Unity 序列化竞态

---

## 5. 具身动作系统

### 5.1 架构总览

动作系统由四个阶段（Phase）演进构成，当前为 **Phase 9 (N32d)**：

```
Phase 1-5: 硬编码 10 个空闲动作（直接在 Live2DRenderer 中写死）
Phase 6:   动作参数提取为 KNOWN_PATTERNS 常量
Phase 7:   7 个动作迁移到 JSON 配置 + IdleActionScheduler
Phase 8:   MotionTranslator — LLM 翻译自然语言到关键帧
Phase 9:   闭环自评 — GLM-4V 视觉验证 + MotionMemoryManager 优化
```

### 5.2 空闲动作系统

#### 5.2.1 JSON 配置驱动

空闲动作定义在 `idle_actions.json` 中，每个动作包含多阶段参数目标：

```json
{
    "formatVersion": "v2",
    "actions": [
        {
            "id": 1,
            "name": "head_tilt",
            "displayName": "歪头",
            "weight": 10,
            "cooldown": 8.0,
            "phases": [
                { "duration": 0.5, "curve": "smooth",
                  "targets": { "ParamAngleZ": 5 } },
                { "duration": 0.5, "curve": "smooth",
                  "targets": { "ParamAngleZ": 0 } }
            ]
        }
    ]
}
```

| 属性 | 说明 |
|------|------|
| weight | 加权随机选取权重（0=不自发触发） |
| cooldown | 冷却时间（秒），期间不重复 |
| phases[].curve | 插值曲线类型 |
| phases[].targets | 参数名→目标值字典 |
| special | 特殊标记（"hardcoded_star_spin"/"hardcoded_magic_circle"） |

#### 5.2.2 动作列表

| ID | 名称 | 权重 | 冷却 | 类型 |
|----|------|------|------|------|
| 1 | 歪头 | 10 | 8s | JSON |
| 2 | 微笑 | 10 | 8s | JSON |
| 3 | 挑眉 | 8 | 12s | JSON |
| 4 | 星辉缠绕 | 5 | 30s | 硬编码 |
| 5 | 伸懒腰 | 8 | 15s | JSON |
| 6 | 委屈 | 6 | 20s | JSON |
| 7 | 法阵展开 | 4 | 45s | 硬编码 |
| 8 | 害羞 | 6 | 20s | JSON |
| 9 | 困惑 | 0 | 10s | JSON (AI 仅用) |
| 10 | 爱心 | 5 | 25s | JSON |

> 动作 #9 (困惑) `weight=0`，永远不会自发触发，仅为 AI 外部调用保留。

#### 5.2.3 插值曲线

`MotionGenerator` 支持 6 种插值曲线：

| 曲线 | 函数 | 用途 |
|------|------|------|
| linear | $f(t) = t$ | 匀速运动 |
| smooth | $f(t) = 3t^2 - 2t^3$ | 通用缓入缓出 |
| easeOut | $f(t) = 1 - (1-t)^2$ | 快入慢出 |
| easeIn | $f(t) = t^2$ | 慢入快出 |
| hold | $f(t) = 0$ | 锁定值 |
| bounce | 分段弹性曲线 | 弹性效果 |

### 5.3 LLM 动作翻译 (MotionTranslator)

#### 5.3.1 核心流程

```
自然语言动作描述
    │
    ▼
1. 构建 Body Schema（所有可用参数名/范围/语义）
    │
    ▼
2. 注入运动记忆（MotionMemoryManager 提供过去成功的模板）
    │
    ▼
3. 调用 DeepSeek API（temperature=0.3，STOP=30s）
    │
    ▼
4. 解析 JSON 响应 → MotionPlan（含关键帧序列）
    │
    ▼
5. MotionGenerator 协程播放
```

#### 5.3.2 Body Schema

发送给 DeepSeek 的 body schema 包含了每个 Live2D 参数的名称、范围、部位和语义描述，例如：

```
Parameter: head_angle_x, Range: [-30~30], Part: Head. Negative=left, Positive=right. 左右摇头.
Parameter: arm_right_upper, Range: [0~1], Part: Right Arm. 0=down, 1=raised up. 右臂上举.
...
```

#### 5.3.3 SPECIAL PATTERNS

内嵌在 System Prompt 中的姿势模板，帮助 DeepSeek 准确生成常见姿势：

| 模式 | 关键参数 |
|------|---------|
| Hands_on_hips/叉腰 | arm_right_upper=0.7~0.8, arm_right_lower=0.15~0.25 |
| Bowing/行礼 | body_angle_x=15~25, arm_right_lower=0.5~0.7 |
| Head_tilt_thinking/歪头思考 | head_angle_z=15~25, eye_ball_y=-0.5~-0.7 |
| Covering_face/捂脸 | arm_right_upper=0.8~1.0, hand_near_face=1.0 |
| Surprise/惊讶 | head_angle_y=8~15, eye_l_open=0.9~1.0, mouth_open_y=0.5~0.8 |
| Cowering/缩团 | head_angle_y=-15~-25, arm_right_upper=0.1~0.2 |

#### 5.3.4 运动记忆注入

`MotionMemoryManager` 维护一个运动记忆库，保留历史中视觉评分 ≥ 3/5 的成功动作模板：

```csharp
public string GetMotionMemories() {
    // 返回 Top-5 最佳动作模板描述
    // 格式: "成功案例: [描述] → 用了 [参数名]=[值]"
}
```

记忆库有容量上限（默认 50 条），超限时淘汰最低分记录。

### 5.4 动作生成器 (MotionGenerator)

#### 5.4.1 播放状态机

```csharp
public enum MotionState { Idle, Playing, Paused }
```

| 状态 | 行为 |
|------|------|
| Idle | 空闲，可接受新计划 |
| Playing | 逐帧插值播放中 |
| Paused | 暂停（调用 TogglePause 切换） |

#### 5.4.2 帧间插值

协程 `PlayAsync(MotionPlan plan)` 逐帧计算参数值：

```csharp
// 对每个关键帧段 [frame_i, frame_{i+1}]:
float t = (currentTime - frameStart) / duration;
float curveT = ApplyCurve(t, curveType);
float value = Mathf.Lerp(startValue, endValue, curveT);
mapper.SetParameterValue(paramName, value);
```

每帧检查 `State != Playing` 以支持暂停和停止。

### 5.5 动作锁定机制

AI 生成动作播放期间防止被空闲动作或走路覆盖：

```csharp
// AI 生成动作
_aiControlLocked = true;
_actionLocked = true;
yield return _motionGenerator.PlayAsync(plan);
_actionLocked = false;
_aiControlLocked = false;
```

- `_aiControlLocked`：阻止 Perlin 噪声和天气表情更新
- `_actionLocked`：阻止空闲动作调度器和拖拽动画

---

## 6. 闭环具身智能验证

### 6.1 架构设计

闭环具身智能验证系统是整个项目的核心创新之一，通过"生成→执行→观察→评估→优化"的循环，使 AI 的动作能力持续改进：

```
DeepSeek (动作生成)
   │
   ▼
Live2D 渲染 (动作执行)
   │
   ▼
截图 (状态捕获)
   │
   ▼
GLM-4V (视觉评估)
   │
   ▼
MotionMemoryManager (记忆学习)
   │
   ▼
下一次生成 (模板参考)
```

### 6.2 MotionVerifier — 动作验证器

#### 6.2.1 三级测试集

**Level 1: 对照组 (5 个)** — 应走硬编码模板

| ID | 描述 | 期望 |
|----|------|------|
| C1 | 开心地挥手 | 右手 0→1→0 摆动，微笑 |
| C2 | 点头同意 | head_angle_y 0→8→0→6→0 |
| C3 | 摇头 | head_angle_x ±8 交替 |
| C4 | 鞠躬行礼 | body_angle_x=25 + head_angle_y=10 |
| C5 | 伸个大懒腰 | 双臂抬起 + 抬头 + 张嘴 |

**Level 2: 测试组 (10 个)** — 应走 LLM 翻译路径

| ID | 描述 | 期望 |
|----|------|------|
| T1 | 害羞地捂脸 | 头低 + 眼垂 + 手在脸前 |
| T2 | 昂首挺胸叉腰 | 抬头 + 臂弯外摆 + 挺胸 |
| T3 | 惊讶地捂住嘴 | 头微仰 + 眼睁大 + 手在嘴前 |
| T4 | 忧郁地望向远方 | 头侧转 + 眼珠偏 + 微表情 |
| T5 | 俏皮地眨一下右眼 | 右眼单眨 + 歪头 + 微笑 |
| T6 | 标准地行一个礼 | 双手下摆 + 身体微倾 |
| T7 | 被吓到缩成一团 | 全身收缩 + 低头 + 臂夹紧 |
| T8 | 骄傲地抬起头 | 头仰 + 身体后倾 + 微笑 |
| T9 | 歪着头思考 | 头歪 + 眼珠上漂 |
| T10 | 双手合十祈祷 | 双手胸前合拢 + 头低 |

**Level 3: 边界测试 (4 个)**

| ID | 输入 | 期望行为 |
|----|------|---------|
| B1 | 空字符串 | 不崩溃，拒绝 |
| B2 | 乱码 | 回退泛用微动 |
| B3 | "右手摸摸左耳朵" | 多帧交叉动作 |
| B4 | "飞到天上转三圈" | 不崩溃，简化 |

#### 6.2.2 评估指标

| 指标 | 计算方式 |
|------|---------|
| 合规率 | 参数值在 [min, max] 范围内的比例 |
| 对称配比率 | 左右对称参数（眼/眉/臂）值是否匹配 |
| 关键帧数 | 动作是否包含足够帧数自然过渡 |
| 复位帧 | 是否在最后恢复默认姿态 |

### 6.3 VisionMotionVerifier — 视觉验证器

#### 6.3.1 执行流程

```
对每个测试用例:
  1. 生成动作（走 LLM 路径）
  2. 播放动作
  3. 在关键姿势峰值帧截图（RenderTexture → PNG）
  4. 发送截图 + 评价提示给 GLM-4V
  5. 解析 GLM 返回的评分(1-5) + 评语
  6. 记录结果到 InProgressResults
汇总 → 生成报告（通过率/平均分/失败案例详情）
```

#### 6.3.2 GLM-4V 评价提示

```text
你正在评估一个桌面宠物的动作效果。
动作描述: {description}
期望姿势: {expectedPose}
请判断这个姿势是否像描述的那样。
评分(1-5):
5 = 非常像，动作精准传达意图
4 = 比较像，主要特征符合
3 = 一般，部分特征符合
2 = 不太像，很多特征缺失
1 = 完全不像
请给出评分和具体理由。
```

#### 6.3.3 闭环学习

视觉验证结果反馈到 `MotionMemoryManager`：

```csharp
// 当评分 >= 3（通过阈值）
MotionMemoryManager.Instance.RecordSuccess(
    description, plan, score);

// 当评分 < 3
MotionMemoryManager.Instance.RecordFailure(
    description, plan, score, failureReason);
```

成功案例加入运动记忆库供后续生成参考；失败案例记录到 `VERIFIED FEEDBACK` 列表，下次 DeepSeek 调用时附带作为负面示例。

---

## 7. 交互系统

### 7.1 拖拽系统 (DragHandler)

#### 7.1.1 点击穿透管理

每帧根据鼠标位置和交互状态动态设置 `WS_EX_TRANSPARENT`：

```csharp
// 拖拽中或菜单打开 → 窗口可交互
if (isDragging || isMenuOpen)
    ClearWindowLong(hwnd, GWL_EXSTYLE, WS_EX_TRANSPARENT);
else
    SetWindowLong(hwnd, GWL_EXSTYLE, WS_EX_LAYERED | WS_EX_TRANSPARENT);
```

#### 7.1.2 抛掷物理

拖拽释放时计算速度向量，传递给 DesktopPet 物理引擎：

```csharp
Vector2 throwVelocity = (lastFramePos - currentFramePos) / Time.deltaTime;
desktopPet.SetVelocity(throwVelocity);
```

### 7.2 分区点击反馈

通过 Live2D Cubism Raycaster 实现分区检测：

| 区域 | 高度范围 | 反馈 |
|------|---------|------|
| Head | > 0.6 | 摸头眯眼锁定动画 |
| Body | 0.3~0.6 | 捂胸惊讶 |
| Leg | < 0.3 | 害羞踢腿 |

### 7.3 拖拽挣扎动作

拖拽过程中触发挣扎动画：

```
双臂交替划水 + 双腿交替 + 身体扭动 + 慌张表情
├── 左臂上抬/右臂下放（交替）
├── 左右腿前后摆动
├── 身体左右旋转
├── 眉头紧锁 + 嘴巴微张
└── 衣服/头发物理跟随
```

### 7.4 右键菜单 (ContextMenu)

四标签面板，全部通过 OnGUI 渲染：

| 标签 | 功能 |
|------|------|
| ⚙️ 设置 | 动作权重滑块、物理参数、API Key 显示 |
| 🎭 动作 | 10 表情 + 11 动作按钮、停止动作 |
| 💬 聊天 | 历史记录 + 输入框 |
| 📋 便签 | 待办/已完成切换、增删改 |

---

## 8. 感知与记忆系统

### 8.1 法眼活动追踪 (ActivityTracker)

#### 8.1.1 窗口轮询

每 2 秒通过 `GetForegroundWindow()` + `GetWindowText()` 获取前台窗口标题，按关键词分类：

```csharp
private ActivityCategory CategorizeWindow(string title) {
    if (title.Contains("Visual Studio") || title.Contains("Code"))
        return ActivityCategory.Coding;
    if (title.Contains("Unity") || title.Contains("Blender"))
        return ActivityCategory.Designing;
    if (title.Contains("Chrome") || title.Contains("Firefox"))
        return ActivityCategory.Browsing;
    // ...更多分类
}
```

#### 8.1.2 活动分类

| 分类 | 检测关键词 |
|------|-----------|
| 编程 | Visual Studio, Code, IntelliJ, IDEA |
| 设计 | Unity, Blender, Photoshop, Figma |
| 游戏 | (Game窗口标题关键词) |
| 学习 | Acrobat, Evernote, OneNote, 浏览器PDF |
| 浏览 | Chrome, Firefox, Edge, Opera |
| 办公 | Word, Excel, PowerPoint, WPS |
| 通讯 | WeChat, WhatsApp, Discord, Telegram |
| 音乐 | Spotify, 网易云音乐 |

#### 8.1.3 多窗口感知

扫描所有可见顶层窗口（`EnumWindows`），构建环境摘要：

```text
[多窗口环境] 当前有 5 个可见窗口：
  1. Visual Studio Code — 编程
  2. Google Chrome (3 个标签) — 浏览
  3. Spotify Free — 音乐
  4. WeChat — 通讯
  5. 符玄桌面宠物 — 桌面宠物
```

#### 8.1.4 浏览器标签读取

`BrowserTabReader` 通过 UI Automation API 读取主流浏览器的标签页标题（无需安装插件）：

| 浏览器 | UIA 搜索路径 |
|--------|-------------|
| Chrome | Chrome_WidgetWin_1 → Edit（地址栏 `about:blank` 旁） |
| Edge | Chrome_WidgetWin_1 → 同 Chrome |
| Firefox | MozillaWindowClass → 标签栏 |
| Opera | Chrome_WidgetWin_1 → 同 Chrome |
| Brave | Chrome_WidgetWin_1 → 同 Chrome |
| Vivaldi | Chrome_WidgetWin_1 → 同 Chrome |

### 8.2 长期记忆系统 (PetMemory)

#### 8.2.1 三层分层结构

```
忆境 (pet_memory.json)
├── 核心事实（Core Facts）
│   └── 用户偏好、重要承诺、关键信息
├── 重要记忆（Top-20）
│   └── 按重要性分数排序
└── 近期琐事（Recent Events）
    └── 时间衰减，自动淘汰
```

#### 8.2.2 记忆类别

| 类别 | 说明 | 初始重要度 |
|------|------|-----------|
| tool | 工具调用记录 | 0.3 |
| conversation | 对话摘要 | 0.5 |
| observation | 法眼观察 | 0.2 |
| reflection | 反思提炼 | 0.8 |

#### 8.2.3 重要性积分与反射

记忆系统累计重要性积分，达到阈值时触发反思：

```csharp
private void AccumulateMemory(float importance) {
    _reflectionScore += importance;
    if (_reflectionScore >= REFLECTION_THRESHOLD) {
        _reflectionScore = 0;
        TriggerReflection();  // 调用 LLM 提炼洞察
    }
}
```

#### 8.2.4 话题冷却系统

同一话题在冷却期间（默认 120s）不再重复记录：

```csharp
public void AddMemory(string summary, string topic = null) {
    if (topic != null && _topicCooldowns.ContainsKey(topic)) {
        if (Time.time - _topicCooldowns[topic] < TOPIC_COOLDOWN)
            return;  // 冷却中，跳过
    }
    // 记录并更新时间戳
}
```

### 8.3 时间与天气系统 (TimeWeatherController)

#### 8.3.1 昼夜时段

| 时段 | 条件 | 行为影响 |
|------|------|---------|
| 早晨 | 5-8点 | 早安气泡 |
| 白天 | 8-18点 | 正常活动 |
| 夜晚 | 18-22点 | 眼皮微垂 |
| 深夜 | 22-5点 | 犯困表情 + 低头频率增加 |

#### 8.3.2 天气获取

双数据源策略，QWeather 优先：

```csharp
// 尝试和风天气（需 API Key）
if (!string.IsNullOrEmpty(qweatherKey)) {
    weather = await FetchFromQWeather(qweatherKey);
} else {
    // 回退 wttr.in
    weather = await FetchFromWttrIn();
}
```

#### 8.3.3 AI 天气语录

当 QWeather 可用时，调用 DeepSeek 批量生成符玄风格的天气台词：

```text
输入: "晴天，28℃，潮湿"
输出: ["天朗气清，惠风和畅…不如来卜一卦？",
       "晴空万里，星轨也格外清晰…"]
```

---

## 9. 桌面物理引擎

### 9.1 核心物理模型

`DesktopPet` 实现了简化的 2D 物理引擎：

```csharp
// 重力
velocity.y += GRAVITY * Time.deltaTime;

// 地面检测
if (position.y <= groundY) {
    position.y = groundY;
    velocity.y = -velocity.y * BOUNCE_DAMPING;
    if (Mathf.Abs(velocity.y) < STOP_VELOCITY)
        velocity.y = 0;
}

// 空气阻力
velocity *= AIR_RESISTANCE;
```

### 9.2 地面任务状态机

```
        ┌──────────┐
        │  停止中   │
        └────┬─────┘
             │ 随机间隔到达
             ▼
     ┌───────────────┐
     │  选择下一任务   │
     └───┬───┬───┬───┘
         │   │   │
         ▼   ▼   ▼
    ┌─────┐┌───┐┌──────────┐
    │左行走││右行走││停留计时  │
    └──┬──┘└─┬─┘└────┬─────┘
       │     │       │
       ▼     ▼       ▼
    ┌──────────────────────┐
    │    到达边缘/时间到     │
    └──────────┬───────────┘
               │
               ▼
          ┌──────────┐
          │  停止中   │
          └──────────┘
```

| 状态 | 行为 |
|------|------|
| MoveLeftEdge | 向左行走至左边缘 |
| MoveRightEdge | 向右行走至右边缘 |
| StopTime | 停留随机时间（5-20s） |
| 停止中 | 物理等待（地面任务间隙） |

### 9.3 多显示器支持

`WindowOverlay` 通过 `VirtualScreen` 获取跨越所有显示器的完整桌面区域：

```csharp
int screenLeft = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Left;
int screenTop = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Top;
int screenWidth = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width;
int screenHeight = System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height;

// 物理边界 = 虚拟桌面边界
float leftBound = VirtualScreen.Left;
float rightBound = VirtualScreen.Right;
```

### 9.4 睡眠唤醒检测

通过时间间隙检测系统休眠/唤醒：

```csharp
float timeGap = Time.unscaledTime - _lastFrameTime;
if (timeGap > SLEEP_DETECT_THRESHOLD) {
    // 系统刚唤醒
    RebuildDWMWindow();
    ResetPhysicsState();
}
```

---

## 10. 窗口与系统集成

### 10.1 Win32 API 封装

#### 10.1.1 DWM 透明窗口

```csharp
[DllImport("dwmapi.dll")]
private static extern int DwmExtendFrameIntoClientArea(
    IntPtr hwnd, ref MARGINS margins);

// 重建窗口
public void RebuildWindow() {
    var style = GetWindowLong(hwnd, GWL_STYLE);
    style &= ~WS_CAPTION;  // 移除标题栏
    style |= WS_POPUP;     // 弹出窗口
    SetWindowLong(hwnd, GWL_STYLE, style);
    
    // 扩展玻璃
    MARGINS margins = new MARGINS { cxLeftWidth = -1 };
    DwmExtendFrameIntoClientArea(hwnd, ref margins);
}
```

#### 10.1.2 系统托盘

使用 Shell_NotifyIcon Win32 API：

```csharp
NOTIFYICONDATA nid = new NOTIFYICONDATA();
nid.cbSize = Marshal.SizeOf<NOTIFYICONDATA>();
nid.hWnd = messageWindowHandle;
nid.uID = 0;
nid.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
nid.uCallbackMessage = WM_TRAYICON;
nid.hIcon = iconHandle;
nid.szTip = "符玄桌面宠物";
Shell_NotifyIcon(NIM_ADD, ref nid);
```

| 交互 | 行为 |
|------|------|
| 左键单击 | 切换显示/隐藏 |
| 双击 | 切换显示/隐藏 |
| 右键 | 弹出菜单（显示/隐藏/开机自启/退出） |

#### 10.1.3 开机自启

通过注册表实现：

```csharp
RegistryKey rk = Registry.CurrentUser.OpenSubKey(
    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
rk.SetValue("DesktopPet", executablePath);
```

### 10.2 崩溃日志系统

```csharp
// Application.logMessageReceived 捕获
void HandleLog(string condition, string stackTrace, LogType type) {
    if (type == LogType.Exception || type == LogType.Error) {
        logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {condition}");
        logBuilder.AppendLine(stackTrace);
        logBuilder.AppendLine("---");
        
        // 超限截断（防日志爆炸）
        if (logBuilder.Length > MAX_LOG_LENGTH) {
            string trimmed = logBuilder.ToString(
                logBuilder.Length - MAX_LOG_LENGTH / 2,
                MAX_LOG_LENGTH / 2);
            logBuilder.Clear();
            logBuilder.Append("[TRUNCATED] ");
            logBuilder.Append(trimmed);
        }
    }
}
```

### 10.3 进程互斥

使用 Mutex 防止多实例：

```csharp
_mutex = new Mutex(false, "DesktopPet_Unity_SingleInstance");
if (!_mutex.WaitOne(0, false)) {
    Application.Quit();
    return;
}
```

---

## 11. 性能优化

### 11.1 三级性能档位

`PerformanceMonitor` 管理三级 FPS 自适应档位：

| 档位 | 目标帧率 | RenderTexture 缩放 | 触发条件 |
|------|---------|-------------------|---------|
| High | 60 fps | 100% | FPS > 45 且 CPU/GPU < 80% |
| Normal | 40 fps | 75% | FPS 30-45 或 CPU/GPU < 85% |
| Low | 20 fps | 50% | FPS < 30 或 CPU/GPU > 92% |

### 11.2 帧率采样

滚动 90 帧采样窗口：

```csharp
// 每帧记录
_fpsSamples[_sampleIndex] = 1f / Time.deltaTime;
_sampleIndex = (_sampleIndex + 1) % SAMPLE_COUNT;

// 评估（每 30 帧一次）
if (++_evalCounter >= 30) {
    float avgFps = _fpsSamples.Average();
    EvaluatePerformance(avgFps);
    _evalCounter = 0;
}
```

### 11.3 CPU/GPU 监控

- **GPU**：通过 NVML API 查询 NVIDIA GPU 利用率（可选，仅 NVIDIA）
- **CPU**：通过 `GetSystemTimes` Win32 API 计算整体 CPU 占用率

### 11.4 内存管理

```csharp
// 系统内存监控
if (systemMemoryUsage > 0.85f) {
    Resources.UnloadUnusedAssets();
    GC.Collect();
}
if (systemMemoryUsage > 0.93f) {
    PerformanceMonitor.ForceDowngrade();  // 强制 Low 档
    GC.Collect(2, GCCollectionMode.Forced);
}

// 应用内存监控
if (appMemoryMB > 800) GC.Collect();
if (appMemoryMB > 1200) {
    GC.Collect(2, GCCollectionMode.Forced);
    // 触发紧急内存清理
}
```

### 11.5 物理频率适配

低帧率时自动降低物理更新频率：

```csharp
if (currentFps < 30) {
    _physicsSkipCounter++;
    if (_physicsSkipCounter % 2 == 0)
        return;  // 跳帧物理更新
}
```

---

## 12. 便签与提醒系统

### 12.1 数据模型

```csharp
public class Reminder {
    public int id;
    public string title;        // 提醒内容
    public DateTime dueTime;    // 到期时间
    public RecurrenceType recurrence;
    public bool isDone;
    public DateTime createdAt;
    public string category;
    public int priority;        // 0-3
}

public enum RecurrenceType {
    None,       // 一次性
    Daily,      // 每日
    Weekday,    // 工作日
    Weekly,     // 每周
}
```

### 12.2 持久化

所有提醒序列化为 JSON 存储到本地：

```csharp
string path = Path.Combine(Application.persistentDataPath, "reminders.json");
string json = JsonUtility.ToJson(new ReminderList { reminders = _reminders });
File.WriteAllText(path, json);
```

### 12.3 推送链路

```
到期检测
  │
  ├──▶ 头顶气泡显示（ChatBubble.ShowMessage）
  │
  ├──▶ Windows Toast 通知（Win32 API）
  │
  └──▶ Server酱³ 推送（HTTP POST → 微信）
         └── 仅在已配置 SCKey 时生效
```

### 12.4 去重机制

AI 手动设置与服务器推送的同类提醒自动去重：

```csharp
// 创建前检查
if (HasPendingReminderContaining(courseName)) {
    // 跳过重复
    return;
}
```

---

## 13. 构建与部署

### 13.1 构建脚本

标准构建使用 `build.ps1`：

```powershell
# 完整构建（清空旧输出 → Unity batchmode → BuildDesktopPet）
.\build.ps1

# 仅编译验证（不生成可执行文件，快速检查语法错误）
.\build.ps1 -Quick
```

### 13.2 Unity BuildPipeline

`Editor/BuildScript.cs` 中的 `BuildDesktopPet()`：

```csharp
public static void BuildDesktopPet() {
    // 1. 扫描场景
    string[] scenes = EditorBuildSettings.scenes
        .Where(s => s.enabled)
        .Select(s => s.path)
        .ToArray();
    
    // 2. 配置构建选项
    BuildPlayerOptions options = new BuildPlayerOptions {
        scenes = scenes,
        locationPathName = "D:/Unity/projects/Desktop_per_pro/Build/DesktopPet.exe",
        target = BuildTarget.StandaloneWindows64,
        options = BuildOptions.None
    };
    
    // 3. 执行构建
    BuildPipeline.BuildPlayer(options);
}
```

### 13.3 构建后验证

```powershell
# 验证关键文件存在
Test-Path "Build/DesktopPet.exe"
Test-Path "Build/DesktopPet_Data/Managed/Assembly-CSharp.dll"
```

---

## 14. 附录

### A. 环境变量清单

| 变量名 | 必需 | 用途 |
|--------|------|------|
| `DEEPSEEK_API_KEY` | ✅ | DeepSeek Chat API |
| `GLM_API_KEY` | ❌ | GLM-4V 截图分析与动作验证 |
| `QWEATHER_API_KEY` | ❌ | 和风天气数据源 |

### B. 注册表项

| 位置 | 用途 |
|------|------|
| `HKCU\...\Run\DesktopPet` | 开机自启 |
| `PlayerPrefs _skip_dwm_rebuild` | DWM 崩溃安全模式 |

### C. 相关文件路径

| 用途 | 路径 |
|------|------|
| 构建日志 | `build_log6.txt` |
| 崩溃日志 | `code/desktop_unity/Build/crash_log.txt` |
| 长期记忆 | `{Application.persistentDataPath}/pet_memory.json` |
| 天机簿配置 | `{Application.persistentDataPath}/pet_config.json` |
| 提醒数据 | `{Application.persistentDataPath}/reminders.json` |
| 系统提示词 | `Assets/Resources/SystemPrompt.txt` |
| 空闲动作配置 | `Assets/Resources/IdleActions/idle_actions.json` |

### D. 版本路线图

```
v0.2 ─── PNG 渲染初步 + API Key 环境变量
  │
  ▼
v0.6 ─── Live2D + 物理 + 10 空闲动作 + 右键菜单
  │
  ▼
v0.8 ─── 物理直接驱动 + 分区点击 + 天气响应 + 挣扎动画
  │
  ▼
v0.9 ─── DWM 玻璃层透明 + 底部输入栏
  │
  ▼
N10 ─── 点击穿透 + 右键菜单修复 + 多轮工具调用
  │
  ▼
N12 ─── 性能监控 + 开机自启
  │
  ▼
N14 ─── 小程序课表数据打通 + 学业查询
  │
  ▼
N16 ─── Everything 毫秒级文件搜索
  │
  ▼
N32d ── 闭环具身智能 + GLM-4V 视觉验证 + 运动记忆
```

### E. 关键设计决策

| 决策 | 选择 | 原因 |
|------|------|------|
| 渲染方案 | DWM DwmExtendFrameIntoClientArea | 彻底解决色键绿边问题 |
| AI API | DeepSeek Chat（非本地） | 高质量对话 + Function Calling 原生支持 |
| 视觉模型 | GLM-4V（非 GPT-4V） | 国内可用 + 图像理解质量达标 |
| 动作生成 | LLM 翻译关键帧 | 无限动作多样性 vs 硬编码有限集 |
| 空闲动作 | JSON 配置驱动 | 扩展性，无需改代码即可添加新动作 |
| 记忆系统 | 三层分层 | 核心稳定 + 重要不丢 + 琐事自动淘汰 |
| 天气数据 | QWeather 优先 + wttr.in 回退 | 准确性 + 无需注册的双保险 |
| 文件搜索 | Everything CLI 优先 | 毫秒级 vs 分钟级的巨大差距 |
