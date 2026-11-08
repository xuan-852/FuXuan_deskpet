# 对话表现分支生命周期审核（2026-11-08，第三批）

## 范围与基线

origin/main 仍为 cdc0968；审核分支起点 130a2d8。原工作区 HEAD 7504413 及混合差异保留。只读取主仓 Git 对象中的候选源码、测试和直接脚本；没有启动预览、发送模型请求、访问独立动作研究仓库、修改生产代码或推进 main。

审查候选 82c2e7d、d3c4489、318d046、c507948、03bef3d、f2b283e，按依赖读取对应 ConversationPresence、TypingAttention、Continuity、ChatManager 预览扩展及 ApiClient。属于用户授权的历史兼容审核，超过普通单模块上下文的原因是跨分支依赖核对。

## 主要发现（按风险排列）

### 高：可变预览地址复用真实 API Key

82c2e7d 及 f2b283e 的 ChatManager.ConversationPreview.ConfigureConversationPreview 允许 FU_XUAN_CONVERSATION_API_BASE 指定任意 HTTPS 地址（仅拒绝 userinfo/query），随后 SetConfig(endpoint, ChatConfig.ApiKey, model)。ApiClient 按该 baseUrl 发送请求，并设置 Authorization: Bearer apiKey。

因此启用真实云端预览后，改预览端点也会把原本 ChatConfig 的密钥送到该主机。环境变量由本机操作者设置，不是网页直接远程注入；风险是跨服务地址与凭证身份不匹配，不宣称已发生泄漏。进入产品前应使用预览专属凭证或端点/凭证绑定确认，不能通过“都是 HTTPS”视作安全。

### 高：进程级测试禁云开关缺乏对称恢复及幂等性

ConversationPresence.prepare 每次记录 _conversationOldCloudBlock，然后可能将 ApiClient.BlockCloudInTestMode=false。DisableConversationPresence、renderer OnDisable 没有恢复，仅 presence:quit 恢复。

重复 prepare 时可能发生 true→保存 true→关闭 false→再次保存 false→quit 恢复 false。源代码顺序的离线布尔状态模型得到 restored=false；这不是 Unity 动态复现，条件是 CloudRequestsEnabled=true 且准备成功。prepare 的 enable 虽幂等，外层保存全局配置并不幂等。

进入产品前须让会话配置所有权只保存一次，在 disable/退出/异常/失去隔离时一致恢复；也需还原 enableTools、端点/模型等预览配置。不建议引入通用配置框架。

### 中：中断验收脚本的退出/污染字段未必影响退出码

c507948 的 conversation_presence_interrupt_check.cjs.shutdown 记录 evidence.playerExited、productionMemoryUnchanged，但没有把 false 纳入 evidence.failures；主 finally 仅在 failures 非空时设失败码。退出超时也没有清理自己启动的残留 PID。控制台 stopped:true 不以进程实际退出为条件。

因此报告字段可记录异常，但不能把“脚本退出码 0”当作所有验收项通过。需在任何失败/异常路径检查污染、使失败进入退出码，严格验证正常退出，兜底仅清理本脚本 PID；不能用强杀作为应用正常退出证据。

### 中：局部序号不等于跨请求事件身份

ConversationPresenceController.Sentence(index,total) 以索引去重，Start 重置 _lastSentence；Renderer 回调没有 request generation/token。当前在中断后到下一次 Start 之前会 Suppress 迟到句，这是有价值的局部保护；但不能因此宣称能分辨“新请求开始后到达的旧请求事件”。是否上游已经保证不发旧事件需单独沿请求代际验证，本轮未认定实际可发生串轮。

## 修复可回收性

- d3c4489 的停步等待、两秒交接窗口、早到句元数据缓冲有实际意义，但直接依赖 UpperBodyGoal/UpperBody 状态、AttentionDirectionActive、ArmSpatialActive 和额外参数基线。main 没有这些原型，不能孤立 cherry-pick 修复，也不能为凑依赖而引入整条实验链。
- 318d046 与 c507948 的 EditMode 测试实例化 ConversationPresenceController，验证该原型的阶段/去重，不是 main 当前协调器接口。不同于前批纯拖拽恢复测试，尚不能只合测试而不引入新控制器。
- OnEnable 绑定、OnDisable 解绑及“先 Unbind 再 Bind”是合理模式，但都是为新增 ConversationPresence 订阅服务；main 已有自身聊天订阅生命周期，未发现可以直接替换的公共缺陷修复。
- f2b283e 的回复后停留、转回输入、5.5 秒移开视线/8.8 秒离开是新增产品策略，不是中性 bugfix；还依赖 TypingAttention 的 heartbeat 和输入几何。可保留研究成果，不以瘦身/修复名义默认合入。

## 暂定结论与下一步

本组暂无可直接合并到 main 的独立生产修复。保留全部分支，不否定其视觉/交互价值；先解决配置/凭证/验收可信度，再谈是否需要这组生产能力。

下一批可以回到纯工具安全修复（如 video-review 清理归属）或审核工具迁移/门禁的最小依赖闭包；不要继续顺着原型祖先把手臂、透视、动作映射一并拉入。

本轮只跑源代码顺序状态模型，未跑 C# 编译、EditMode、Player、网络或原生 UI。未宣称发现已被动态复现、未修改模块真相、不合并不推送。前批点击轻回应的 386/382/0/4 结论仍只属于其审核版本，不能套到本组原型。
