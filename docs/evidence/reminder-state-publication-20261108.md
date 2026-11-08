# 提醒状态发布收口（2026-11-08）

## 范围与收益

基线 HEAD `1ae0c47`、暂存区为空，已有混合差异保持原样。依据 reminder-polling-boundary-audit-20261108.md，本步仅改 ReminderManager 和配套隔离测试，不改 ServerPollService、通知投递、动作审批、提醒时间算法或存储格式。

- 移除三条托盘刷新内部事件订阅，将更新托盘收至既有 NotifyDataChanged。新增和手动完成原先各刷新两次，现在各一次（正常无重入调用路径）。初次 Load 后仍显式刷新一次。
- 四个到期状态分支原有的四处 Save 合为一处共同 Save → NotifyDataChanged；重复提醒推进和一次性完成均发布已保存状态。
- 错过归档仍每条发布 Done、批次保存一次，新增批次 DataChanged 一次。Added/Done/Due 原有触发位置和次数保留；新增 DataChanged、托盘刷新时机变为保存尝试之后，是明确的行为变化。Save 失败仍沿用既有日志及后续通知行为，不改失败事务语义。
- 删除未使用 _chat 字段及 GetComponent/FindObjectOfType 两处解析。
- 只提取一个私有 CompleteReminderOccurrence，用于明确投递与状态迁移的边界，无新类型、管理器或框架。

## 验证

新增 ReminderStatePublicationTests 三个测试方法（含四种重复类型参数化，总计六例），inactive 宿主，_filePath 指向每例独占临时 JSON。CRUD、错过归档和状态完成只操作临时文件；绕过 TriggerReminder，未调用 Toast、手机、模型、网络或工具。每例销毁宿主、删除自己创建的文件，C# 带 BOM 并提供 .meta。

首轮 EditMode 463 / 454 passed / 5 failed / 4 skipped：夹具计数器未在 NUnit 复用实例时清零；修正 SetUp 后重跑 Quick 与全套测试，未修改生产代码掩盖失败。

| 层级 | 最终结果 | 证据 |
|---|---|---|
| Quick | 通过，15 秒 | logs/build/reminder-state-20261108-quick-r2.log |
| EditMode | **463 total / 459 passed / 0 failed / 4 skipped**，六例新测试全通过 | logs/build/reminder-state-20261108-tests-r2.log；reminder-state-20261108-test-results.xml（同目录） |
| 完整 Player | 通过，22 秒，未覆盖正式 Build | logs/build/reminder-state-20261108-player.log |
| startup-only | 初始化、窗口切换、自退出、零异常、生产记忆存在性/哈希/mtime 零污染通过；未截图未聊天 | logs/build/reminder-state-20261108-smoke.log |

统一 -NoKill -MaxCores 4，保留缓存和 BelowNormal；最终三个构建日志无 C# 编译错误。Player：`C:/Users/25295/AppData/Local/Temp/opencode/fuxuan-reminder-state-20261108-player/DesktopPet.exe`，隔离数据：`C:/Users/25295/AppData/Local/Temp/opencode/fuxuan_smoke_test_reminder_state_20261108/`，保留测试标记；脚本只读校验生产目录 D:/DesktopPetData。

## 限制与保存

以上是**混合工作区**验证，精确独立提交树未验证；此前动作资源基线失败未解决。真实托盘 tooltip 显示与原生调用次数、真实到期投递/手机推送/定时工具执行未动态验收；startup-only 不替代提醒端到端时序验证。托盘减少次数为源码调用路径结论，测试验证的是保存与事件次数，不宣称测量了原生刷新次数。

仅本地保存 ReminderManager.cs、测试 .cs/.meta、核查/实施证据及 chat-ui 模块对应段落，排除其他混合改动，不推送。
