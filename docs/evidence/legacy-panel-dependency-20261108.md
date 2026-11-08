# 遗留面板存储依赖收口（2026-11-08）

基线 main 553bd90，分支 refactor/main-slimming-next-20261108，独立工作树无混合实验。探索回收已结束并 push，不删除历史分支。

BallPanel.Start 不再 AddComponent<ReminderManager>，减少一处 UI 消费者隐藏创建持久化服务的分支。仍优先读取既有 Instance，再查同宿主，缺失时沿用现有便签页“未初始化”处理；不删除 BallPanel、PanelType 或 DragHandler 兼容路径。产品由 ProductComposition 装配提醒服务并禁用遗留面板。精确 GUID 搜索 Assets 的 unity/prefab/asset 未发现 BallPanel 挂载，不覆盖外部集成。

新增一项 inactive 宿主测试，手动调用 Start 检查没有新建提醒组件或增加全局数量，不读写提醒数据。独立旧宿主若依赖隐式自建需显式提供 ReminderManager，此契约变化明确保留。

验证统一 -NoKill -MaxCores 4：

- Quick 通过：主仓库 logs/build/legacy-panel-quick.log。
- EditMode **387 total / 383 passed / 0 failed / 4 skipped**：主仓库 logs/build/legacy-panel-tests.log；XML 独立保留于本工作树 logs/build/legacy-panel-test-results.xml。
- 临时 Player 构建通过：主仓库 logs/build/legacy-panel-player.log，输出 C:/Users/25295/AppData/Local/Temp/opencode/legacy-panel-20261108-player/DesktopPet.exe，未覆盖正式 Build。
- 原主工作区现有 startup-only 冒烟脚本驱动上述新 Player，通过初始化、窗口切换、自退出、零异常及生产记忆存在性/哈希/mtime 零污染；主仓库 logs/build/legacy-panel-smoke.log。脚本来源不属于本步提交。隔离根 C:/Users/25295/AppData/Local/Temp/opencode/fuxuan_smoke_test_legacy_panel_20261108 保留。

旧面板实际 GUI、外部宿主及真实提醒投递未验证，startup-only 不替代这些行为。仅本地保存本步四文件，不推送此瘦身节点；不会把原混合工程的测试数套用到独立 main。
