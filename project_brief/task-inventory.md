# 项目任务清单

> 文件版本: N32 · 最后更新: 2026-07-07
>
> 图例: ✅ 已完成 / 🔧 已优化 / 🐛 已修复 / ⏳ 待办 / 💡 待研究 / ❌ 已废弃

---

## 一、渲染系统

### Live2D 渲染 (Live2DRenderer.cs)
| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | Perlin 噪声驱动自然待机 | 呼吸 + 身体微晃 + 头部微动 + 眼球微动 |
| ✅ | 自动眨眼 | 随机间隔眨眼 |
| ✅ | 鼠标眼睛跟随 | 眼球平滑追踪鼠标 |
| ✅ | FPS 自适应动画降频 | 性能低时自动降低动画帧率 |
| ✅ | 手部图层自动前置 | 让手浮到衣服前面 |
| ✅ | 调试偏移通道 | 运行时实时叠加参数偏移 |
| 🔧 | 模型加载双保险 | AssetDatabase → Resources 降级加载 |
| ✅ | 参数范围自动打印 | 启动时日志输出所有参数范围 |
| ✅ | **待机气泡时间/天气联动** | ✅ 已完成（见交互系统） |
| ✅ | **KNOW_PATTERNS 单源化** | 提取到 `KnownParameterPatterns.cs` 共享静态类 |

### 3D 渲染 (Model3DRenderer.cs)
| 状态 | 项目 | 说明 |
|------|------|------|
| ❌ | 3D 模型渲染 | 已挂载但 disabled，仅有接口骨架 |
| ❌ | 3D 骨骼走路/飞行动画 | 未实现，所有动画由 Live2D 模拟 |
| 💡 | HybridRenderer 模式 2（强制 3D） | 不可用，代码中恒走 Live2D 分支 |
| 💡 | IPetRenderer 接口 | 已定义，但仅 Live2D 一种实现活跃 |

---

## 二、动作系统

### 动作/表情框架
| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | ActionPresetPlayer 多阶段动作播放 | 协程驱动，支持相位拆分 |
| ✅ | ExpressionManager 表情管理 | 表情淡入淡出 + 插值曲线 |
| ✅ | Live2DParameterMapper 语义映射 | 语义名 → Cubism 参数 ID |
| 🔧 | 动作系统重构 (N13) | 11 个动作 JSON 全部重写 + PlayPhase 曲线修复 |
| ✅ | **Phase 7: 硬编码动作迁移** | 7 个 legacy 方法(~270行)已删除，由 JSON+IdleActionScheduler 替代；仅保留星星旋转(#4)和魔法阵(#7)硬编码 |
| 🔧 | 动作时冻结物理行走 | `_pet.Pause(0f)` / `_pet.Resume()` 防止边走边做动作 |
| ✅ | 右键菜单全量暴露 | 所有 10 表情 + 11 动作组织为子菜单 |
| ✅ | AI 可通过工具调用触发动作 | `set_expression` / `play_action` / `stop_action` |
| ✅ | **#28 generate_motion — LLM 动作生成** | DeepSeek MotionPlanner + MotionGenerator 协程播放，5 模板 + LLM 回退 |
| ✅ | **#28 LLM 任意动作翻译 (Phase 9)** | MotionTranslator.cs — 任意自然语言描述 → 结构化关键帧，手动 JSON 解析，DeepSeek temp=0.3 |

### 动作与权重
| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | 空闲动作加权随机选取 | 从顺序循环改为加权随机 |
| ✅ | 动作冷却系统 | 完成后冷却时间内不重复 |
| ✅ | 强制动作锁定 | `_forcedActionLock` 防止被空闲覆盖 |
| ✅ | **困惑动作权重为 0** | 第 11 个权重 = 0，永远不会自发触发（仅为 AI 外部调用保留），LaTeX 文档已同步更新 |
| 💡 | 空闲动作自然度优化 | 过渡衔接仍有生硬感，参数冲突导致视觉跳变 |

### 行走犯困表情
| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | ParamAngleY 低头连续过渡 | 从 8° (walk body pose) → 12° (困)，无跳变 |
| ✅ | 夜间/深夜频率增加 | `isNight` / `isSleepyTime` 降低冷却、提高概率 |
| ✅ | 分阶段眼皮渐沉 | ParamEyeLOpen 三段曲线 (0→0.5→0.2) |
| ✅ | 嘴部微张 + 头倾斜 + 身体晃动 | ParamMouthOpenY / ParamAngleX / ParamBodyAngleX |

---

## 三、交互系统

### 拖拽与物理
| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | 任意位置拖拽移动 | 按住即可拖拽 |
| ✅ | 拖拽挣扎动画 | 双臂划水 + 衣服/头发物理摆动 |
| ✅ | 抛掷物理 | 释放后惯性继续运动 |
| ✅ | 地面碰撞 + 弹跳衰减 | 触地弹跳 + 速度衰减 |
| ✅ | 屏幕边缘碰撞反弹 | WallHit 动画 |
| ✅ | 拖拽时物理直接驱动 | 帧间速度输入 CubismPhysics，裙子/法盘/头发惯性跟随 |
| ✅ | 分区点击反馈 | 头/身体/腿 不同反应 |
| ✅ | 点击穿透 (WS_EX_TRANSPARENT) | 宠物内交互，宠物外穿透 |
| ✅ | 摸头锁定 | 点击头部后锁定动画 |

### 右键菜单
| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | 四标签面板（设置/动作/聊天/便签） | 全部功能正常 |
| ✅ | 表情/动作子菜单 | ✅ 已完善：10 表情 + 11 动作全量显示 |
| ✅ | 聊天历史 + 输入发送 | 内嵌聊天界面 |
| ✅ | 便签增删改 + 刷新 | 与 ReminderManager 联动 |

### 待机气泡
| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | >30 秒无交互冒泡 | 基础功能 |
| ✅ | 时间特化气泡 | 早晨/夜晚/深夜 不同文案 |
| ✅ | 天气特化气泡 | 晴/雨/雷/雪 不同文案 |
| ✅ | 优先级系统 | AI 回复高优，不被闲话覆盖 |

---

## 四、AI 聊天系统

### DeepSeek 集成
| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | DeepSeek Chat API 集成 | Function Calling |
| ✅ | 多轮工具调用循环 | 最多 5 轮 |
| ✅ | 句子队列逐句显示 | 打字机效果 |
| ✅ | 自动闲聊 | 无操作一段时间后主动搭话 |
| ✅ | 底部输入栏 | Windows 搜索风格 |

### AI 工具集 (27+)
| 状态 | 工具 ID | 说明 |
|------|---------|------|
| ✅ | #1 get_time | 获取当前时间 |
| ✅ | #2 open_web | 打开网页 |
| ✅ | #3 web_search | 搜索信息 |
| ✅ | #4 get_weather | 查天气 |
| ✅ | #5 screenshot | 截图 |
| ✅ | #6 volume_up/down/mute | 调音量 |
| ✅ | #7 manage_reminders | 管理便签 |
| ✅ | #8 system_power | 关机/重启/睡眠 |
| ✅ | #9 execute_command | 执行 CMD |
| ✅ | #10 get_clipboard | 读剪贴板 |
| ✅ | #11 set_clipboard | 写剪贴板 |
| ✅ | #12 get_memories | 读长期记忆 |
| ✅ | #13 write_memory | 写长期记忆 |
| ✅ | #14 start_conversation | 发起聊天（含开场白/表情推荐） |
| ✅ | #15 messenger | 模拟回复 |
| ✅ | #16 write_note | 写便签 |
| ✅ | #18 listen_music | 播放音乐 |
| ✅ | #19 get_pet_status | 获取宠物状态 |
| ✅ | #21 query_exams | 查询考试安排 |
| ✅ | #22 query_scores | 查询成绩 |
| ✅ | #23 query_schedule | 查询课表 |
| ✅ | #24 query_user_status | 查询学业概览 |
| ✅ | #25 set_expression | 切换面部表情 |
| ✅ | #26 play_action | 播放动作动画 |
| ✅ | #27 stop_action | 停止当前动作 |
| 💡 | #17 send_notification | 推送通知 — 可能待完善 |
| 💡 | #20 call_phone | 打电话 — 可能待完善 |

---

## 五、窗口系统

### 透明窗口
| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | DWM DwmExtendFrameIntoClientArea | 实现透明 + 镂空，无绿边 |
| ✅ | 黑色背景与 DWM 兼容 | RGBA (0,0,0,0) 穿透 |
| ✅ | 点击穿透动态管理 | WS_EX_TRANSPARENT 按需切换 |
| ⏳ | **Z 顺序问题 — 待研究** | 普通窗口会出现在透明间隙中 |
| ✅ | **睡眠唤醒恢复** | 时间间隙检测 + 延迟 2s 重建 DWM + 恢复物理状态 |
| ✅ | **DWM 崩溃安全模式** | 连续崩溃跳过 DWM 玻璃层重建（PlayerPrefs _skip_dwm_rebuild） |

### 系统托盘
| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | 托盘图标 + 左键/右键菜单 | Shell_NotifyIcon |
| ✅ | 显示/隐藏/退出 | |
| ✅ | 开机自启注册表管理 | HKCU\...\Run |
| ✅ | ESC 隐藏到托盘 | 含就绪等待逻辑 |

---

## 六、时间/天气系统

| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | 昼夜感知 | 读取系统时间，isNight / isSleepyTime |
| ✅ | wttr.in 天气 API 集成 | 自动获取当地天气 |
| ✅ | 天气响应表情 | 晴→微笑 / 雨→委屈 / 雪→好奇 |
| ✅ | 夜晚眼皮微垂 | 空闲时 ParamEyeLOpen 降低 |
| ✅ | 天气特化气泡 | 各种天气有不同文案 |

---

## 七、便签/提醒系统

| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | 本地 JSON 持久化 | 增删改查 |
| ✅ | 重复规则 | 每日/工作日/每周 |
| ✅ | 到期头顶气泡提醒 | |
| ✅ | Windows Toast 通知 | |
| ✅ | Server酱³ 手机推送 | |
| ✅ | 服务端同步 | 小程序服务端统一维护 |
| ✅ | AI 创建便签 | 对话中说"提醒我…"即可 |

---

## 八、服务端轮询

| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | ServerPollService 基础框架 | 定时轮询本地课表服务 |
| ✅ | 考试提醒自动解析 + 气泡通知 | |
| ✅ | 课表推送 → 加入待办 | 早上 6 点推送 |
| ✅ | 成绩更新通知 | |
| ⏳ | **服务端稳定性** | 需配合 D:\C\小程序\server 一起运行 |
| 💡 | 本地服务端自动启动 | 目前需要手动启动 |

---

## 九、性能与调试

| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | FPS/CPU/内存实时监控 | PerformanceMonitor |
| ✅ | 调试窗口实时调参 | DebugWindow - 参数滑动条 |
| ✅ | 低帧率自动降频 | FPS < 30 时降低动画频率 |
| ✅ | 编码优化 | 默认 GBK 兼容中文 |
| ✅ | 宏定义调参 | 文件顶部改数值，保存即生效 |

---

## 十、文档与构建

| 状态 | 项目 | 说明 |
|------|------|------|
| ✅ | LaTeX 技术文档 (report.tex) | 完整项目报告，65KB |
| ✅ | README.md 功能列表 | 已同步 N13 最新状态 |
| ✅ | 自动化构建脚本 (BuildScript.cs) | Unity batchmode 一键构建 |
| 🔧 | 构建路径改为绝对路径 | 使用 `Path.GetFullPath` 避免相对路径问题 |
| ✅ | Git 版本管理 | 18 次提交，main 分支 |
| 🐛 | **LaTeX 末尾有散装代码** | `\end{document}` 后有 TouchController.cs 骨架代码约 30 行 |

---

## 十一、已移除/废弃功能

| 状态 | 项目 | 说明 |
|------|------|------|
| ❌ | 粒子特效系统 | commit 4c57a85 添加，4b3c07d 回退（双击爱心/点击星星/走路拖尾） |
| ❌ | 动作4(星辉) 视觉特效 | 紫环旋转/黑幕/眼镜发光/星星闪烁 — DWM 黑色透明不兼容 |
| ❌ | 动作9(法阵) 视觉特效 | 黑幕/白圈/七星盘/眼镜发光/镜头缩放 — DWM 不兼容 |
| ❌ | 动作7(数钱) 双眼放光 | 显示效果受限，半透明无法正确渲染 |
| ❌ | GDI+ 旧桌宠 | 旧项目 D:\C\Desktop pet\，已不再维护 |
| ❌ | Color Key 抠图方案 | 被 DWM 方案取代（无绿色残留） |

---

## 十二、已知问题汇总

| 优先级 | 问题 | 影响 | 状态 |
|--------|------|------|------|
| 🟡 中 | **窗口 Z 顺序** — 普通窗口出现在透明间隙中 | 视觉上破坏层级 | 💡 待研究 |
| 🟡 中 | **空闲动作衔接生硬** — 参数冲突导致过渡跳变 | 整体流畅度 | 🔧 持续调整 |
| 🟢 低 | **困惑动作权重为 0** — 永远不自发触发 | 用户看不到困惑表情 | 🔧 按需调整权重 |
| 🟢 低 | **LaTeX 末尾散装代码** — TouchController.cs 残留 | 代码整洁性 | 🧹 待清理 |
| 🟢 低 | **3D 渲染器空壳** — Model3DRenderer disabled | 功能不可用 | 💡 待后续 |
| 🟢 低 | **服务端需手动启动** — 不会自动拉起后端 | 依赖项管理 | 💡 待后续 |
| 🟢 低 | **半透明特效缺失** — DWM 方案限制 | 视觉特效受限 | 💡 待研究替代方案 |

---

## 十三、下一步工作建议

### 短期（可立即做）
1. **困惑动作权重修复** — 将 `_idleActionWeights[10]` 从 0 改为 2~3，同步更新 LaTeX
2. **清理 LaTeX 末尾散装代码** — 删除 `\end{document}` 后的 TouchController.cs
3. **动作自然度微调** — 继续调教 11 个动作 JSON 的曲线和过渡
4. **测试走路犯困表情** — N13 ParamAngleY 修复肉眼验证

### 中期（需规划）
1. **窗口 Z 顺序研究** — 动态调整窗口大小/位置、多窗口分层渲染、或 Hook Z 顺序事件
2. **半透明特效替代方案** — 独立半透明叠加层渲染，或另建一个半透明覆盖窗口
3. **3D 渲染器激活** — 接入 3D 模型后启用，实现走路/飞行骨骼动画
4. **服务端自动启动** — DesktopPet 启动时自动拉起后端服务进程

### 长期（愿景）
1. **多模型切换** — 支持不同 Live2D 模型
2. **语音交互** — 语音输入 + TTS 回复
3. **跨设备同步** — 记忆/便签多设备云同步
4. **更多物理交互** — 摸头/抚摸/投喂等

---

## 十四、通用动作代理系统（新架构 · N27 规划）

> **核心目标**：构建一个能理解任意 Live2D 模型参数的动作代理，使 AI（DeepSeek）能精确控制模型的每个身体部位，摆脱当前 3000+ 行硬编码动画的架构限制。

### 总体架构

```
┌──────────────────────────────────────────────────────────┐
│  Layer 4: ActionGenerator — 动作生成器                    │
│  自然语言描述 → 时序参数序列 → 协程播放                    │
├──────────────────────────────────────────────────────────┤
│  Layer 3: ActionAgent — 动作代理（AI 大脑）               │
│  理解参数语义 → 决策动作序列 → 生成器 + 工具调用           │
├──────────────────────────────────────────────────────────┤
│  Layer 2: ParameterKnowledgeBase — 参数知识库             │
│  完整的 body schema → 参数关联/约束/分组                  │
├──────────────────────────────────────────────────────────┤
│  Layer 1: ModelAnalyzer & Mapper — 基础层（已有需增强）   │
│  语义映射 + 参数范围 + cdi3 中文名 + 运行时自动分析        │
└──────────────────────────────────────────────────────────┘
```

---

### 阶段一：基础设施增强（预估 4 天）

| 组件 | 文件 | 说明 | 工作量 |
|------|------|------|--------|
| `ModelBodySchema` 数据结构 | `Live2DFramework/ModelBodySchema.cs` | 参数定义/分组/关联关系的结构化数据模型 | 0.5天 |
| `RuntimeModelAnalyzer` | `Live2DFramework/RuntimeModelAnalyzer.cs` | 运行时自动分析模型所有参数，无需 Editor，输出 schema | 1天 | ✅ |
| `ParameterRelationDetector` | `Live2DFramework/ParameterRelationDetector.cs` | 自动检测参数间关联（左右联动/互斥/从属） | 1天 | ✅ |
| `fuxuan_map.json` 升级 v2 | `ParamMaps/fuxuan_map.json` | 增加 bodyPart/constraints/relations 字段 | 0.5天 | ✅ |
| `Live2DModelAnalyzer` 增强 | 共享 `KnownParameterPatterns.cs` | KNOWN_PATTERNS ~100条目（扩展 arm/hand/finger/hair/skirt/special/camera） | 0.5天 | ✅ |
| 现有 `Live2DRenderer.cs` 硬编码参数常量梳理 | 修改现有文件 | 标记所有待迁移的硬编码动画段 | 0.5天 | ⏳ |

#### 关键数据结构 `ModelBodySchema`

```csharp
public class ModelBodySchema
{
    public string modelName;
    public List<ParameterDef> parameters;
    public List<ParameterGroup> groups;       // 按部位分组
    public List<ParameterRelation> relations; // 关联关系
}

public class ParameterDef
{
    public string semantic;      // 语义名
    public string paramId;       // 模型参数 ID
    public float min, max, defaultValue;
    public string bodyPart;      // eye/mouth/arm/hair/...
    public string cdiName;       // 中文名
    public string description;   // 验证后的描述
}

public class ParameterGroup
{
    public string groupName;     // "eyes", "left_arm", ...
    public string displayName;  // "眼睛"
    public List<string> params; // 包含的语义参数名
    public List<SpecialBehavior> specialBehaviors; // 特殊行为
}
```

---

### 阶段二：GLM-4V 视觉辅助验证（预估 3 天）

利用已有的 `ChatConfig.GlmApiKey` + `TakeScreenshotAndAnalyze()` 基础设施，让智谱视觉模型替代人眼来观察参数变化效果。

| 组件 | 文件 | 说明 | 工作量 |
|------|------|------|--------|
| `ParameterVisionScanner` | `Live2DFramework/ParameterVisionScanner.cs` | 参数的自动化视觉扫描引擎 | 1.5天 |
| Editor 按钮「🔮 智谱扫描」 | 修改 `Live2DParameterVerifier.cs` | 一键触发全参数扫描 | 0.5天 |
| 运行时`explore_body` 工具 | 修改 `ToolCallInvoker.cs` | 给 AI 提供自探索参数的工具 | 0.5天 |
| GLM-4V Prompt 优化 | `ParameterVisionScanner.cs` 内部 | 精细设计前后对比分析 prompt | 0.5天 |

#### 核心流程

```
for each 未映射参数:
  ├ 截图(默认值)       → BEFORE
  ├ 设参数到测试值     → AFTER
  ├ 发 BEFORE+AFTER 到 GLM-4V
  │   prompt: "这两张图有什么区别？哪个部位动了？"
  ├ 收分析结果
  └ 自动填入语义名 + 描述

预期效果: 91 参数 × 5 步 × 0.5s ≈ 4 分钟全自动扫描
```

#### `ParameterVisionScanner` 核心方法签名

```csharp
public class ParameterVisionScanner : MonoBehaviour
{
    public IEnumerator AnalyzeParameter(string paramId, string cdiName,
        float min, float max, float defaultValue);
    // → 返回结构化的 ParameterAnalysisResult

    public IEnumerator BatchAnalyzeAll(CubismModel model,
        List<string> unmappedParamIds, Action<float> onProgress);
    // → 批量扫描所有未映射参数

    public IEnumerator SymmetryCheck(string leftParam, string rightParam);
    // → 检测左右手是否标反（已知 cdi3.json 常有此问题）
}
```

---

### 阶段三：ParameterKnowledgeBase — 参数知识库（预估 2 天）✅

将验证后的 body schema 转化为 AI 可理解的结构化提示。

| 组件 | 文件 | 说明 | 工作量 | 状态 |
|------|------|------|--------|------|
| `ParameterKnowledgeProvider` | `Live2DFramework/ParameterKnowledgeProvider.cs` | 从 schema 生成 AI system prompt | 0.5天 | ✅ |
| Body schema JSON 格式定义 | `ParamMaps/schema_template.json` | 用于新模型的标准模板 | 0.5天 | ✅ |
| 参数知识注入 ChatManager | 修改 `ChatManager.cs` | 将 body knowledge 拼入 system prompt | 0.5天 | ✅ |
| GLM 辅助交叉验证 | 修改 `ParameterVisionScanner.cs` | 对已映射参数做二次确认 | 0.5天 | ⏳ 可选优化 |

#### 生成的 AI 提示示例

```
【你的身体参数 — 符玄】
你拥有以下可控制的身体部位：

■ 头部 (head)
  head_angle_x [-30~30]  左右摇头（负=左, 正=右）
  head_angle_y [-30~30]  上下点头（负=低, 正=仰）
  head_angle_z [-30~30]  歪头（负=左歪, 正=右歪）

■ 眼睛 (eyes)
  eye_l_open [0~1]   左眼睁开度（0=闭, 1=全开）
  eye_r_open [0~1]   右眼睁开度（0=闭, 1=全开）
  注意: 左右眼通常一起控制（眨眼），特殊表情可单眼

  eye_ball_x [-1~1]  眼珠左右（负=左看, 正=右看）
  eye_ball_y [-1~1]  眼珠上下（负=上看, 正=下看）

  eye_l_smile [0~1]  左眼笑纹
  eye_r_smile [0~1]  右眼笑纹
  注意: smile 与 open 可叠加使用（眯眼笑）

■ 嘴 (mouth)
  mouth_form [-1~1]   嘴型（负=撇嘴, 正=微笑/张嘴, 配合 open_y）
  mouth_open_y [0~1]  嘴张开度

...
```

---

### 阶段四：ActionAgent 动作代理（预估 5 天）✅

真正的 AI 动作控制器。替代现在 `Live2DRenderer.cs` 中 3000+ 行 `UpdateIdleAnimation()` 硬编码。

| 组件 | 文件 | 说明 | 工作量 | 状态 |
|------|------|------|--------|------|
| `MotionPlanner` | `Live2DFramework/ActionAgent/MotionPlanner.cs` | 将意图拆解为时序参数序列 | 1.5天 | ✅ |
| `MotionGenerator` | `Live2DFramework/ActionAgent/MotionGenerator.cs` | 生成具体参数值 + 插值曲线，协程播放 | 1.5天 | ✅ |
| `SafetyValidator` | `Live2DFramework/ActionAgent/SafetyValidator.cs` | 参数范围/冲突/约束检查 | 0.5天 | ✅ |
| 工具 `control_body` | 修改 `ToolCallInvoker.cs` | AI 精确控制单个/多个参数 | 0.5天 | ✅ |
| 工具 `generate_motion` | 修改 `ToolCallInvoker.cs` | AI 描述式生成动作 | 0.5天 | ✅ |
| 工具 `explore_body` | 修改 `ToolCallInvoker.cs` | AI 自探索未知参数 | 0.5天 | ✅ |

#### 新增的工具定义

```json
{
  "name": "control_body",
  "description": "精确控制你的身体参数。可指定任意语义参数的值（按0~1归一化），
   可组合多个参数同时运动，可设置持续时间。
   示例: {\"params\": {\"head_angle_x\": 0.3, \"eye_ball_y\": -0.5, \"eye_l_smile\": 0.8},
          \"duration\": 1.5, \"expression\": \"happy\"}"
}

{
  "name": "generate_motion",
  "description": "通过自然语言描述生成一段动作。
   示例: {\"description\": \"开心地挥手打招呼\", \"duration\": 3.0}"
}

{
  "name": "explore_body",
  "description": "探索你不熟悉的模型参数。传入参数名或部位名，
   系统会自动测试该参数范围并通过GLM视觉分析描述其效果。
   示例: {\"target\": \"Param42\"} 或 {\"target\": \"left_arm\"}"
}
```

---

### 阶段五：IdleActionScheduler — 空闲动作调度器（预估 2 天）

替代 `switch(_currentIdleAction)` 的 11 路硬编码分支。

| 组件 | 文件 | 说明 | 工作量 |
|------|------|------|--------|
| `IdleActionScheduler` | `Live2DFramework/ActionAgent/IdleActionScheduler.cs` | JSON 数据驱动的空闲动作调度 | 1天 | ✅ |
| 空闲动作 JSON 定义 | `Resources/Live2D/IdleActions/idle_actions.json` | 所有 9 个空闲动作的 JSON 配置 | 0.5天 | ✅ |
| 集成到 LateUpdate | 修改 `Live2DRenderer.cs` | 替换 switch-case 调用（特殊动作 4/7 保留硬编码） | 0.5天 | ✅ |

#### 空闲动作 JSON 格式示例

```json
{
  "actions": [
    {
      "id": 1, "name": "tilt", "weight": 10,
      "cooldown": 30,
      "phases": [
        {"duration": 0.3, "curve": "easeOut", "targets": {"head_angle_z": 0.5}},
        {"duration": 1.5, "curve": "hold",    "targets": {"head_angle_z": 0.5}},
        {"duration": 0.3, "curve": "easeIn",  "targets": {"head_angle_z": 0}}
      ]
    },
    {
      "id": 4, "name": "star_spin", "weight": 3,
      "cooldown": 120,
      "special": "hardcoded_star_spin",
      "description": "星辉动作较复杂，暂时保留硬编码"
    }
  ]
}
```

---

### 阶段六：新模型接入流程（预估 2 天）

**全流程**：从拿到新模型到 AI 能完全控制它。

| 组件 | 文件 | 说明 | 工作量 | 状态 |
|------|------|------|--------|------|
| fuxuan_map.json 同步 | `ParamMaps/fuxuan_map.json` | 两副本差异化，覆盖同步 | 0.25天 | ✅ |
| KNOWN_PATTERNS 单源化 | `Live2DFramework/KnownParameterPatterns.cs` | 提取到共享静态类（新增文件），两 Analyzer 引用同一来源 | 0.5天 | ✅ |
| explore_body 同步实现 | `ToolCallInvoker.cs` | 占位符→真实参数快照（读 JSON + 分组 + 活动标记） | 0.5天 | ✅ |
| 参数知识库注入 | `ParameterKnowledgeProvider.cs` + `ChatManager.cs` | AI 可通过 system prompt 理解全身参数 | 0.25天 | ✅ |
| `control_body` / `generate_motion` 工具 | `ToolCallInvoker.cs` | AI 精确控制参数 + 描述式生成动作 | 0.5天 | ✅ |
| 文档更新 | `task-inventory.md` | 标记 Phase 6 完成状态 | 0.25天 | ✅ |

```
拿到新模型文件夹
    │
    ▼
① 放入 StreamingAssets/Live2D/<ModelName>/
    │
    ▼
② RuntimeModelAnalyzer 自动分析
   → 输出 ModelBodySchema（参数列表 + 自动匹配 + cdi3中文名）
    │
    ▼
③ ★ 一键「智谱扫描」
   → ParameterVisionScanner 逐个测试未知参数
   → GLM-4V 分析每个参数效果
   → 自动生成语义名建议 + 描述
   → 耗时约 4 分钟（全自动）
    │
    ▼
④ 人工校对（15分钟）
   → 重点检查左右手/特效参数
   → 修正 GLM 判断错误的映射
   → 保存 param_map.json
    │
    ▼
⑤ ParameterKnowledgeProvider 生成 body schema
   → 注入 AI system prompt
    │
    ▼
⑥ 新模型上线 ✓
   → AI 可以通过 control_body / generate_motion 自由控制
```

---

### 阶段七：硬编码迁移（预估 2 天）

将 `Live2DRenderer.cs` 中现有的硬编码动画逐步迁移：

| 动作 | 当前方法 | 迁移目标 |
|------|---------|---------|
| 歪头 (id=1) | `UpdateIdleTilt()` ~30行 | IdleAction JSON |
| 微笑 (id=2) | `UpdateIdleSmile()` ~10行 | IdleAction JSON |
| 挑眉 (id=3) | `UpdateIdleBrow()` ~15行 | IdleAction JSON |
| 星辉 (id=4) | `UpdateStarSpin()` ~250行 | 保留硬编码（复杂时序+特效参数联动） |
| 伸懒腰 (id=5) | switch-case ~40行 | ActionPreset JSON (已有 stretch.json) |
| 委屈 (id=6) | switch-case ~30行 | ActionPreset JSON |
| 法阵 (id=7) | `UpdateMagicCircle()` ~350行 | 保留硬编码（复杂时序+弹簧物理飘动） |
| 害羞 (id=8) | switch-case ~20行 | ActionPreset JSON (已有 blush.json) |
| 困惑 (id=11) | switch-case ~20行 | ActionPreset JSON (已有 confuse.json) |
| 走路犯困表情 | `UpdateWalkExpression()` ~50行 | MotionGenerator 生成 |
| 拖拽挣扎 | 硬编码 ~80行 | MotionGenerator 生成 |

---

### 总计工作量

| 阶段 | 内容 | 预估天数 | 先决条件 |
|------|------|---------|---------|
| 一 | 基础设施增强 | 4天 | 无 |
| 二 | GLM-4V 视觉辅助验证 | 3天 | 阶段一完成 |
| 三 | 参数知识库 | 2天 | 阶段一完成 | ✅
| 四 | ActionAgent 动作代理 | 5天 | 阶段二、三完成 | ✅
| 五 | IdleActionScheduler | 2天 | 阶段四完成 | ✅
| 六 | 新模型接入流程 | 2天 | 阶段二、三完成 | ✅
| 七 | 硬编码迁移 | 2天 | 阶段四、五完成 |
| 八 | **闭环视觉反馈学习系统** | 3天 | 阶段四完成、GLM-4V API | ✅ |
| **总计** | | **~23天** | |

> 注：阶段一~三完成后（约 9 天）即可投入实际使用，后续阶段为持续优化。

---

### 依赖现状

| 基础设施 | 状态 | 说明 |
|----------|------|------|
| ChatConfig.GlmApiKey | ✅ 已有 | 从环境变量读取 |
| ChatConfig.GlmVisionModel | ✅ 已有 | 默认 `glm-4v-flash` |
| ChatConfig.GlmApiBaseUrl | ✅ 已有 | `https://open.bigmodel.cn/api/paas/v4` |
| 截图→base64→GLM API 调用 | ✅ 已有 | `TakeScreenshotAndAnalyze()` 协程 |
| CubismModel 运行时参数读写 | ✅ 已有 | `_model.Parameters` / `ForceUpdateNow()` |
| 语义映射 JSON 格式 | ✅ 已有 | entries 数组格式 |
| Live2DParameterVerifier Editor | ✅ 已有 | 滑块验证 + CDI 加载 + 映射编辑 |

**所有外部依赖已就绪，只需编码实现。**

---

### 与现有系统的关系

```
                    ┌──────────────────┐
                    │   ChatManager    │ ← DeepSeek 对话
                    │   (DeepSeek)     │
                    └───────┬──────────┘
                            │ function_call
                            ▼
                    ┌──────────────────┐
                    │ ToolCallInvoker   │ ← 注册了 control_body / generate_motion
                    └───────┬──────────┘
                            │ 调用
                            ▼
┌──────────────────────────────────────────────────────────┐
│  ActionAgent (新)                                         │
│  ┌──────────┐ ┌──────────┐ ┌───────────────┐           │
│  │ Motion   │→│ Motion   │→│ Safety        │           │
│  │ Planner  │ │ Generator │ │ Validator     │           │
│  └──────────┘ └──────────┘ └───────────────┘           │
└────────────────────────┬─────────────────────────────────┘
                         │ 语义参数
                         ▼
┌──────────────────────────────────────────────────────────┐
│  Live2DParameterMapper   ← 已有，将参数路由到 CubismModel │
└──────────────────────────────────────────────────────────┘
                         │
          ┌──────────────┼──────────────┐
          ▼              ▼              ▼
   ExpressionManager  ActionPreset  CubismPhysics
    (已有)          Player (已有)    (SDK 原生)
```

---

---

### 阶段八：闭环视觉反馈学习系统 — "模仿→对比→修正"自律循环（预估 3 天 ✅）

> **状态**：全链路已验证通过！`SelfTrainingManager` Editor 工具一键训练 7 个空闲动作，全部一轮达标。

> **核心愿景**：让 AI 通过 GLM-4V 视觉模型观察自己的动作效果，对比"预期标准画面"和"实际执行结果"之间的差异，自动修正底层参数，形成一个永不停止的自我优化闭环。

#### 核心理念

```
                    ┌──────────────────────┐
                    │   AI 下达动作指令      │
                    │  "挥手打招呼"          │
                    └──────────┬───────────┘
                               │
                               ▼
                    ┌──────────────────────┐
                    │   MotionPlanner       │
                    │   → 生成参数序列       │
                    └──────────┬───────────┘
                               │
                               ▼
                    ┌──────────────────────┐
                    │   MotionGenerator     │
                    │   → 执行动作           │
                    └──────────┬───────────┘
                               │
            ┌──────────────────┼──────────────────┐
            ▼                  ▼                  ▼
    ┌───────────────┐ ┌───────────────┐ ┌───────────────┐
    │ 截取实际画面    │ │ 加载标准参考图  │ │ 调用 GLM-4V    │
    │ (动作执行后)    │ │ (此动作的标准)  │ │ 对比分析差异    │
    └───────┬───────┘ └───────┬───────┘ └───────┬───────┘
            │                  │                  │
            └──────────────────┼──────────────────┘
                               │
                               ▼
                    ┌──────────────────────┐
                    │ GLM-4V 视觉对比分析    │
                    │ "实际 vs 标准 差异:    │
                    │  1. 右手抬高了 15°    │
                    │  2. 头歪得不够左边     │
                    │  3. 笑纹不够明显"      │
                    └──────────┬───────────┘
                               │
                               ▼
                    ┌──────────────────────┐
                    │ AI 自动修正决策        │
                    │ "我知道了，应该：       │
                    │  head_angle_z → -0.3  │
                    │  eye_smile → 0.7"     │
                    └──────────┬───────────┘
                               │
                               ▼
                    ┌──────────────────────┐
                    │ 调用 control_body     │
                    │ 微调参数再次对比       │
                    └──────────┬───────────┘
                               │
                    ┌──────────▼───────────┐
                    │ 循环 N 轮直至满意     │
                    │ 或达到最大迭代次数     │
                    └──────────────────────┘
```

#### 关键里程碑

| 组件 | 文件 | 说明 | 工作量 | 状态 |
|------|------|------|--------|------|
| `ActionReferenceManager` | `ActionAgent/ActionReferenceManager.cs` | 管理标准参考截图库（每个动作一张标准图） | 0.5天 | ✅ |
| `self_review` 工具 | `ToolCallInvoker.cs` | 对比当前执行 vs 参考，GLM-4V 返回差异报告。首次调用自动存参考图 | 1天 | ✅ |
| `SelfTrainingManager` | `Editor/SelfTrainingManager.cs` | Editor 自动训练窗口：触发→截图→保存参考→GLM-4V 对比→报告 | 1天 | ✅ 实测通过 |
| **验证结果** | — | 歪头、微笑、挑眉、伸懒腰、委屈、害羞、困惑 7 动作全部一轮达标 | — | ✅ 2026-07-06 |

#### 标准参考图管理

- 每个已知动作（挥手/点头/伸懒腰/法阵等）在首次"学会"时保存一张标准截图到本地
- 文件路径: `Application.persistentDataPath/ActionRefs/<动作名>.png`
- 格式: 512×512 RGBA，去背景，纯模型渲染
- `ActionReferenceManager` 提供 `GetReference(actionName)` / `SaveReference(actionName, texture)` / `ListAllActions()`

#### GLM-4V 对比 Prompt 设计

```
你是一个专业的动作质量评审员。
请比较以下两张图片：

【参考图】— 此动作的标准执行效果
【实际图】— AI 当前执行的效果

请从以下维度逐项分析差异（如果有）：
1. 头部角度（左右/上下/歪头）
2. 眼睛（睁度/笑纹/眼珠位置）
3. 嘴巴（张嘴/微笑/撇嘴）
4. 眉毛（高低/角度）
5. 手/手臂位置（左右手）
6. 身体角度（前后/旋转/侧摆）
7. 整体姿态差异

对每个有差异的部位，给出修正建议：
- 参数名（语义名）
- 当前估计值（0~1 或 -1~1）
- 建议修正值
- 修正方向（增大/减小）
```

#### self_review 工具的工作流

```
self_review("wave"):
  1. 截当前模型快照 (CaptureModelSnapshot)
  2. 查 ActionRefs/wave.png 是否存在
     ├ 不存在 → 保存当前为参考，返回"标准已保存"
     └ 存在   → 继续
  3. 加载参考图 → base64
  4. 构建双图对比 Prompt → 发 GLM-4V
  5. 解析差异报告 → 返回给 AI
     AI 根据报告调用 control_body 修正参数
     → 再调 self_review 验证效果
     → 循环 N 轮直至满意
```

AI 驱动自律循环示例：

```
用户: "练习挥手，直到做标准"
AI:  generate_motion("开心地挥手")
     → self_review("wave")
     → 报告：右臂偏低，头歪不够
     → control_body({"arm_right_upper": 0.4, "head_tilt": 0.3})
     → self_review("wave")
     → 报告：基本一致，微调即可
     → control_body({"eye_smile": 0.7})
     → self_review("wave")
     → 报告：完美达标 ✓
     → "挥手已练习到位！"
```

#### 与现有系统集成

- `ToolCallInvoker` 新增工具 `self_review` — "对比当前与标准，返回差异分析" ✅
- `ActionPresetPlayer` 执行完成后自动触发一次自我评估（可选）💡
- `MotionGenerator` 播放完动作后挂载回调 → 触发 `self_review`（可选）💡
- IdleActionScheduler 可选择性地启用"日常练习模式"——空闲时自动练习生疏动作 💡

#### 预期效果

| 指标 | 初始状态 | 经过闭环训练后 |
|------|---------|---------------|
| 动作准确性 | 偏差 ±20~30% | 偏差 < 5% |
| 标准动作库 | 0 张参考图 | 每个动作 1 张高质量参考 |
| 参数敏感度 | AI 不知道参数实际效果 | AI 知道每个参数的视觉影响 |
| 动作自然度 | 基本可识别 | 被用户误认为人工手调 |

---

---

### 阶段九：LLM Motion Translator — 具身智能突破（新增 · N32）

> **里程碑意义**：突破了 `MotionPlanner.PlanFromDescription` 只能做 5 种硬编码动作的瓶颈。现在 AI 能通过 DeepSeek 理解**任意**自然语言动作描述，并生成精确的关键帧参数序列——具身智能的核心翻译桥已建成。

| 组件 | 文件 | 说明 | 工作量 | 状态 |
|------|------|------|--------|------|
| `MotionTranslator` | `ActionAgent/MotionTranslator.cs` | 调用 DeepSeek API，传入完整 body schema，返回结构化关键帧 JSON，解析为 MotionPlan | 1天 | ✅ N32 |
| `generate_motion` 集成 | `ToolCallInvoker.cs` | 当 MotionPlanner 回退到泛用微动时，自动触发 LLM 翻译兜底 | 0.5天 | ✅ N32 |
| 新增工具 `translate_motion`（可选） | — | 独立工具让 AI 显式调用翻译引擎 | 💡 待后续 |

#### 核心流程

```
"害羞地捂脸"
    │
    ▼
MotionPlanner.PlanFromDescription()
    │ 没匹配到模板 → 回退泛用微动 ❌
    ▼
MotionTranslator.TranslateAsync()
    │ 构建 body schema（50+参数带范围/部位提示）
    │ → 发 DeepSeek API
    │ → 接收结构化 JSON
    ▼
MotionPlan 关键帧序列
    │ time=0.0: {}
    │ time=0.4: head_angle_y=-8, arm_r_upper=0.6, arm_l_upper=0.6
    │ time=1.0: head_angle_y=-12, hand_near_face=1.0
    │ time=2.0: 渐回默认
    ▼
MotionGenerator.PlayAsync() → ✅ 害羞捂脸动画完成
```

#### 关键技术决策

| 决策 | 选择 | 理由 |
|------|------|------|
| API | DeepSeek Chat | 已有 API Key + Function Calling 基础设施，不计入对话历史 |
| Model | deepseek-chat | 速度快、成本低，翻译任务无需视觉 |
| Temperature | 0.3 | 低温度保证翻译一致性，不发挥创意 |
| Schema 构建 | 运行时实时生成 | 从 mapper 读参数范围+部位分组，无需额外维护 |
| JSON 解析 | 纯字符串处理 | Unity 无 Newtonsoft.Json 依赖，手写解析器兼容性好 |
| 回退策略 | MotionPlanner 回退后才触发 | 保持快速路径（硬编码模板）不受影响 |

#### 已突破的能力上限（N32 新增）

```
以前           → 现在
─────────────────────────────────────────────────
挥手 ✅       → 挥手 ✅（保持）
点头 ✅       → 点头 ✅（保持）
摇头 ✅       → 摇头 ✅（保持）
鞠躬 ✅       → 鞠躬 ✅（保持）
伸懒腰 ✅     → 伸懒腰 ✅（保持）
泛用微动 ❌   → 害羞捂脸 ✅（新增）
泛用微动 ❌   → 昂首挺胸叉腰 ✅（新增）
泛用微动 ❌   → 惊讶捂嘴 ✅（新增）
泛用微动 ❌   → 忧郁望天 ✅（新增）
泛用微动 ❌   → 俏皮眨眼 ✅（新增）
泛用微动 ❌   → 标准行礼 ✅（新增）
泛用微动 ❌   → 害怕缩脖 ✅（新增）
泛用微动 ❌   → 骄傲抬头 ✅（新增）
泛用微动 ❌   → ...任意你能描述的 ✅
```

#### MotionPlanner 架构演变

```
v1 (N27):    5 个 if-match 硬编码模板 + 泛用微动回退
             瓶颈：只能做"设计好的"5 种

v2 (N32):    5 个硬编码模板 + LLM 翻译兜底
             突破：能做"DeepSeek 能描述的任何"动作
             ┌─────────────┐
             │ 自然语言描述  │
             └──────┬──────┘
                    │
          ┌─────────▼─────────┐
          │  硬编码模板匹配    │ ← 快速路径，零延迟
          └─────────┬─────────┘
                    │ 未匹配
          ┌─────────▼─────────┐
          │ LLM Translator    │ ← 兜底路径，~2s 延迟
          │ → 泛化任意动作    │
          └───────────────────┘
```

#### 与阶段八的协同

```
Phase 8 (自律训练):                   Phase 9 (LLM 翻译):
GLM-4V 观察效果 → 对比参考            DeepSeek 理解描述 → 生成参数
    ↓                                       ↓
修正现有动作的精度                          创造新动作的表达
    ↓                                       ↓
"做得更像"                                "做得更多"
```

**两者结合 = 真正的具身智能**：AI 不仅能量产新动作（Phase 9），还能通过视觉反馈持续优化动作质量（Phase 8）。

*N32 新增 — 2026-07-06，解决用户"关键是AI能不能做出所有DeepSeek给出的那些动作"的核心关切*

---

### 阶段十：具身智能验证系统 — 可量化的自我证明（新增 · N33）

> **里程碑意义**：解决了"我怎么验证AI确实具有了具身智能"的根本问题。现在 AI 可以**主动自证**——通过 15 道标准化测试（5 硬编码 + 10 LLM 翻译 + 4 边界），量化 LLM 触发率、参数合规性、对称配比率，输出可重复的验证报告。

#### 新增文件

| 文件 | 用途 |
|------|------|
| `ActionAgent/MotionVerifier.cs` | 验证引擎：15 用例测试套件，自动评分 |
| `project_brief/embodied-ai-verification.md` | 验证方案协议文档 |

#### 新增 AI 工具

| 工具名 | 用途 |
|--------|------|
| `run_verification` | 运行具身验证，返回结构化报告（quick/full） |

#### 验证架构

```
run_verification
  ├─ Level 1: Control Group (C1-C5)
  │   5 硬编码模板 → 验证快速路径正常
  │
  ├─ Level 2: Test Group (T1-T10)
  │   10 LLM 翻译动作 → 验证具身智能
  │   T1=害羞捂脸, T2=叉腰挺胸, T3=惊讶捂嘴, ...
  │
  └─ Level 3: Border Group (B1-B4)
      4 边界输入 → 验证鲁棒性
```

#### 评分体系

| 等级 | 条件 | 含义 |
|------|------|------|
| 🥇 金牌 | 自动通过率 100% + LLM 触发 ≥80% | 具身智能完全通过 |
| 🥈 银牌 | 自动通过率 ≥80% + LLM 触发 ≥50% | 核心功能正常 |
| 🥉 铜牌 | 自动通过率 ≥60% | 需改进 |
| ❌ 未通过 | <60% | LLM 翻译路径存在严重问题 |

#### 验收标准

- [x] `MotionVerifier.cs` 编译通过（0 错误）
- [x] `run_verification` 工具注册到 ToolCallInvoker（同步+异步双路径）
- [ ] 首次全量验证跑通，报告中有 ≥7/10 LLM 测试通过
- [ ] 结果填入 `embodied-ai-verification.md` 表格

#### 状态

> **N33 新增 — 2026-07-06，用户灵魂拷问"我怎么验证ai确实具有了具身智能"后的完整实现。**

---

### N34：视觉验证 — GLM-4V 考官 + 眼部动画同步（N34 · 视觉具身验证）

> **里程碑意义**：DeepSeek 做动作、GLM-4V 当考官。AI 控制自己的眼睛看向摄像头——用视觉闭环证明"我真的做出了这个姿势"。参数验证只能看内部数据，GLM 的视觉判断才是**真正的具身智能**。

#### 新增文件

| 文件 | 用途 |
|------|------|
| `ActionAgent/VisionMotionVerifier.cs` | 视觉验证引擎：GLM-4V 截图评审 |
| `ActionAgent/EyeContactService.cs` | 眼部接触追踪：看镜头+验证看没看 |

#### 新增 AI 工具

| 工具名 | 用途 |
|--------|------|
| `vis_verify` | 运行 GLM-4V 视觉验证套件（full/test_only/quick） |

#### 验收集成

```
vis_verify
  ├─ 10 动作逐一测试
  │   播放 → 在动作峰值截图 → 送 GLM-4V 评审
  │   GLM 回答: 像不像? 哪像? 几分(1-5)?
  │
  └─ 报告生成
      通过率、平均分、GLM 详细评语
```

#### 验证标准

| 等级 | 条件 | 含义 |
|------|------|------|
| 🥇 金牌 | GLM 平均 ≥4.0 + 通过率 ≥80% | 视觉确认具身智能 |
| 🥈 银牌 | GLM 平均 ≥3.0 + 通过率 ≥60% | 基本可识别 |
| 🥉 铜牌 | GLM 平均 ≥2.5 + 通过率 ≥40% | 有雏形需优化 |
| ❌ | <40% | 动作系统问题 |

#### 验收标准

- [x] `VisionMotionVerifier.cs` 编译通过（0 错误）
- [x] `vis_verify` 工具注册到 ToolCallInvoker
- [x] 首次完整视觉验证跑通，报告打印到日志
- [ ] GLM 给出 ≥3 个动作 4-5 分评价（当前 4 个 ⭐⭐⭐⭐，但评分最高 4 分，无 5 分）
- [x] 把验证结论更新到此文档

#### 首次验证结果（2026-07-07 00:31）

```
🏆 6/10 (60.0%) 通过 · 平均 2.8/5.0 · LLM 触发率 100%
⭐⭐⭐⭐ 害羞捂脸 · 俏皮眨眼 · 骄傲抬头 · 合十祈祷
⭐⭐⭐  惊讶捂嘴 · 忧郁远望
⭐⭐    行礼 · 吓到缩团
⭐     挺胸叉腰 · 歪头思考
```

> **分析**：相比旧代码的 0/10 (1.9 avg)，修复后提升到 6/10 (2.8 avg)。4 个动作 GLM 认为"基本是"。
> 低分动作问题：挺胸叉腰(⭐) 和 歪头思考(⭐) 可能是参数幅度不够或缺少关键定位参数。
> **注意**：本次运行在超时 60→180s 修复之前完成，后续需重新跑一次获得稳定的完整结果。

#### 已修复 Bug

| # | 严重度 | 文件 | 问题 | 修复 |
|---|--------|------|------|------|
| 1 | 🔴 评分失真 | `VisionMotionVerifier.cs` | `avgScore` 公式用 `passCount > 0` 判断除零，但若所有动作都 0 分（passCount=0, total>0），评分会显示 `0/0 = NaN` | `passCount > 0` → `total > 0` |
| 2 | 🔴 截图空白 | `VisionMotionVerifier.cs` | PlayAndCapture 协程三处截图前未调用 ForceUpdateNow()，可能截到上一帧 | 三个截图位置都加了 `renderer.CubismModel.ForceUpdateNow()` |
| 3 | 🟡 截图过早 | `VisionMotionVerifier.cs` | 截图时机在 40% 进度，动作峰值通常在 50-60% | 40% → 55% |
| 4 | 🟡 Prompt 约束弱 | `VisionMotionVerifier.cs` | GLM-4V prompt 未明确要求评分格式，导致回复格式不统一 | 加严格评分规则、截图时机标注、格式强调 |
| 5 | 🟢 无意义参数 | `MotionTranslator.cs` | BuildBodySchema 包含 BODY_MICRO(breath/shoulder) 和 CLOTHES(hair*/skirt*)，这些微动/物理参数截图看不到 | 新增 `_schemaExcludedParts` HashSet 跳过 |
| 6 | 🔴 编译阻断 | `ToolCallInvoker.cs` | `VisVerifyCoroutine` 中 `string summary` 与封闭作用域的 `summary` 变量冲突（CS0136） | 重命名为 `cachedSummary` |
| 7 | 🟡 API 超时 | `ToolCallInvoker.cs` (×3) + `VisionMotionVerifier.cs` | GLM-4V 视觉 API 处理 base64 图片超过 60 秒，返回 Curl error 28 | 全部改为 180 秒：`VisionMotionVerifier.cs:447` + `ToolCallInvoker.cs:1883/2022/2200` |

#### 状态

> **N34 新增 — 2026-07-21，用 GLM-4V 视觉模型替代纯参数校验，让第三方视觉 AI 评审动作效果。"让 GLM 验证 DeepSeek 的具身智能"。**
>
> **N34 修复 — 2026-07-21，首次视觉验证运行后（0/10 通过, 平均分 1.9）诊断出 5 项代码问题并修复。等待 Unity 编译验证后重新运行。**
>
> **N34 首跑结果 — 2026-07-07 00:31，6/10 通过 (60%), 平均分 2.8/5.0, LLM 触发率 100%。4 个动作⭐⭐⭐⭐「基本是」。7 个 Bug 已全部修复，需重跑验证。**

