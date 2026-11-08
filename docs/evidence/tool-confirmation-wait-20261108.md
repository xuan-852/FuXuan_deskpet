# 聊天与定时提醒审批等待职责收口（2026-11-08）

## 基线与实际变化

独立工作树 Build/w/main-review，分支 refactor/startup-ownership-20261108，开始 HEAD 7d5755a，工作区/暂存区干净；main/origin/main b3dc977。未访问原混合实验或独立动作仓库。

ChatManager.ToolLoop.WaitForDangerousToolConfirmation（本地聊天与云端回环共用）和 ReminderManager.ConfirmReminderAction 原本各自维护 confirmed/resolved 回调、Request、60 秒等待、超时 Resolve(false)。本步将这两套机制归入**已有 ToolConfirmManager** 的 WaitForDecision。没有新增管理器、事件总线、调度器或配置框架。

三个生产文件：ToolConfirmManager.cs 新增共享协程；ChatManager.ToolLoop.cs、ReminderManager.cs 删除各自 resolved 状态和等待/超时循环。气泡文案/优先级、提醒已有审批时的告警跳过、聊天成功提示和结果消费仍在调用端；点击允许/ESC 拒绝/同时只允许一个审批不改。生产代码总体不是净删行，收益是两条职责实现变为一条，且加入未决等待释放的保护。

沿用 Time.time 60 秒时钟，不顺便改变为 unscaled。超时边界从 > 改为 >=；空工具名直接拒绝，避免原协议 PendingTool=null 无法表示待审批。枚举器被 Dispose 时若仍未决则拒绝当前审批，已完成等待/因 busy 被立即拒绝者不会清除后续审批。这不保证 Unity 所有 StopCoroutine/Destroy 情况都会 Dispose 嵌套枚举器，不宣称完整生命周期问题已修复。

## 专项验证与限制

新增 ToolConfirmationWaitTests，5 例：允许/拒绝各一、busy 不替换、零时限超时拒绝、显式 Dispose 清理；允许/拒绝测试中先建立后续审批再推进旧等待，确认旧等待不清后续状态。仅调用审批状态机，不执行任何工具、模型、网络、原生 UI或记忆写入。C# 带 BOM，meta 显式保存。

| 层级 | 本次结果 | 证据（主仓 logs/build） |
|---|---|---|
| Quick | 通过，22 秒 | confirmation-wait-quick.log |
| EditMode | **393 total / 389 passed / 0 failed / 4 skipped** | confirmation-wait-tests.log；独立 XML 位于本工作树 logs/build/confirmation-wait-test-results.xml |
| 完整 Player | 通过，26 秒，4 核/BelowNormal；不覆盖正式 Build | confirmation-wait-player.log |
| startup-only | 初始化/窗口切换/自退出/零异常/生产记忆存在性、hash、mtime 零污染通过 | confirmation-wait-smoke.log |

Player C:/Users/25295/AppData/Local/Temp/opencode/confirmation-wait-20261108-player/DesktopPet.exe；隔离数据 C:/Users/25295/AppData/Local/Temp/opencode/fuxuan_smoke_test_confirmation_wait_20261108。启动驱动来自原主工作区已有未提交 startup-only 脚本，本步未纳入其差异。Player/测试源码来自独立分支，非混合程序集。

未动态测试真实 60 秒等待、点击/ESC 审批 UI、真实聊天/到期动作、暂停/销毁下 Unity 嵌套协程释放；启动冒烟不能替代这些审批端到端验证。无实际危险工具执行。原有广播回调抛错、Time.time 暂停时等待不推进等行为本步不改。

测试通过后仅同步 tool-engine 模块和本证据。保存独立本地节点，不默认推送；此前 benchmark 节点仍在同分支，交付须区分本步和前序改动。
