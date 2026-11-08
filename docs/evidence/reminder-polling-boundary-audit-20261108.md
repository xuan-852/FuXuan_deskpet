# 提醒与服务轮询边界核查（2026-11-08，只读代码审计）

## 基线与范围

HEAD `1ae0c47`，暂存区为空；记录 git diff --name-only 后核查，原有混合工作区差异未改。读取 ReminderManager、ServerPollService、ProductComposition，以及直接消费者 BallPanel、RightPanel/工具引用、SystemTrayManager.UpdateTooltip 和现有测试。指导为 `docs/guides/approved/l6-user-services-and-release.md`。未读历史研究、未调用工具、未联网轮询、未读生产提醒数据、未触发通知或定时动作。

本轮仅新增本文，不修改 C#、模块真相文档，不提交或推送。以下均为静态代码结论，动态行为未验证。

## 调用链及职责

1. ProductComposition 同宿主装配 ReminderManager、ServerPollService、ChatBubble、ChatManager。
2. ServerPollService.Awake 建立 HTTP 客户端（供 Start 前查询）；Start 解析提醒/气泡/聊天依赖，有 Token 时 Update 定期调用 PollMessagesAsync。
3. 收到考试推送：立即气泡；按 examKey 进程内去重及课程名待办去重，添加未来复习/考试提醒。收到课表推送：添加当天 07:30 提醒并显示气泡。成绩只气泡，chat_message 显示收到消息并送 ChatManager。
4. ReminderManager 独立持有提醒 CRUD、持久化、到期状态迁移；到期显示气泡、系统通知、可选手机推送和可选定时动作。其托盘路径仅更新 tooltip 待办数，不是系统 Toast。

**不能据此合并的路径**：推送接收时气泡与未来到期气泡不是同一触发；examKey 去重与课程名去重分别针对推送/本地用户提醒，也不是同一职责。无需新建通知管理器、事件总线或共享去重框架。

## 可操作发现

### A. 状态变更发布路径重复且不完整（优先级最高）

ReminderManager.Start 135–137 行分别自订阅 OnReminderAdded、OnReminderDone、OnDataChanged，均调用 UpdateTrayBadge。

| 路径 | 当前状态/通知行为 |
|---|---|
| AddReminder | Save → Added → NotifyDataChanged；同一次新增刷新托盘两次 |
| MarkDone | Save → Done → NotifyDataChanged；同一次手动完成刷新托盘两次 |
| DeleteReminder / DeletePendingRemindersContaining | Save → NotifyDataChanged；刷新一次 |
| CheckDueReminders 错过归档 | 每条 Done，批次最后 Save；无 DataChanged |
| TriggerReminder 一次性到期 | Done → Save；无 DataChanged，且托盘在保存前刷新 |
| TriggerReminder 重复提醒推进时间 | Save；无 DataChanged |
| FinishAction | Save → NotifyDataChanged；只改动作结果也刷新待办数 |

建议下一块只处理**提醒状态变更发布**：移除三条内部事件自订阅，把托盘更新放入既有 NotifyDataChanged；补齐自动状态迁移的发布点，公开 Added/Done/Due 的含义与次数保持不变。无需增加管理器或新的事件类型。注意同一检查批次与逐条到期的通知次数需先定清，不应顺便改时间算法、保存格式或事件顺序。

### B. 无效聊天依赖（可与 A 同一模块小范围清理）

ReminderManager._chat 仅声明与 Start 赋值，没有读取。可删除一个字段、一次 GetComponent 和一次 FindObjectOfType，不需要注入替代品。

### C. 全局查找回退（不是本轮首选）

ServerPollService.Start 三处 GetComponent/FindObjectOfType 回退；ReminderManager.Start 两处回退（其中聊天依赖无用），确认动作另一次气泡回退，UpdateTrayBadge 每次重查托盘。产品同宿主装配已存在，但不建议仅为删查找再做一轮零碎提交。先处理 A，确认独立宿主兼容后再决定其余路径。

## 其他风险：记录但不混入瘦身

- ServerPollService.PollMessagesAsync 内已 catch 并记录 Exception，Update 外又加 OnlyOnFaulted continuation。异常边界有重叠，但不应无证据直接删外层；请求没有显式 in-flight gate、取消和销毁后回调保护，需受控延迟响应验证，不能只加开关宣称解决。
- examKey 在创建提醒前加入集合；课表消息没有相同去重，轮询游标先于逐条处理更新。失败重试语义未验证，不能改造成通用可靠消息框架。
- ReminderManager.Save 没有测试模式判断。任何 CRUD/状态测试必须显式设置临时 _filePath 或隔离 FU_XUAN_DATA，不能因有 .test_mode 就假定不落盘。
- TriggerReminder 会发系统/手机通知并可能执行工具；不可直接把它当无副作用测试入口。需隔离测试通知边界，禁止发送真实手机推送、执行危险工具。
- BallPanel.Start 仍可 AddComponent<ReminderManager>，但产品装配已禁用遗留 BallPanel。该兼容入口先保留，不删除大模块。
- 已批准指导包含逾期重试和低打扰约束，当前代码为窄窗口触发/错过归档；这里只登记差异，不在瘦身中重写产品策略。

## 引用与验证缺口

全 Assets C# 搜索没有发现外部 Added/Done/Due/DataChanged 订阅，内部仅上述三条；公开字段依旧可能被外部集成使用，不能据此删除公共事件。用 ReminderManager、ServerPollService 的 .meta 精确 GUID 搜索 .unity/.prefab/.asset 未发现挂载，不覆盖外部 AssetBundle。现有 Editor 测试覆盖提醒数据/动作 schema 与产品装配，未发现 CRUD、自动到期及通知次数测试。

建议实施 A+B 后验证：隔离文件 CRUD，Added/Done/DataChanged 次数和持久化内容，重复提醒推进/错过归档，确保不触发系统/手机/工具副作用；随后 Quick、全套 EditMode、临时 Player 与隔离启动。真实 Toast、手机推送及真实定时动作作为另外授权的动态验收，不用 startup-only 替代。

本轮编译 / EditMode / 动态冒烟均**未运行**（没有代码改动）；没有新增已完成瘦身收益，只有核查结论。
