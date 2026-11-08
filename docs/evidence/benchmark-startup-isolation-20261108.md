# Benchmark 启动边界收口（2026-11-08）

基线 main b3dc977 已推送，续作分支 refactor/startup-ownership-20261108。独立工程无混合实验。只修改 ToolBenchmarkRunner、既有 CompanionDependencyTests、本证据和 tool-engine 模块。

原全局 AfterSceneLoad hook 仅凭生产数据根 .benchmark 自建组件；手动挂载 Start 也直接启动 RunAll。本步两个入口共用同一私有判定：FU_XUAN_BENCHMARK=1、显式 FU_XUAN_DATA 与实际根一致、.test_mode、位于系统临时目录严格子路径，所有已有祖先无 reparse point。自动入口拒绝时不创建对象，手动入口拒绝时警告并停用。减少一条仅凭生产标记文件触发全量实验的启动路径，不创建 DiagnosticStartup 框架，不修改任何工具执行规则。

新增一项拒绝测试：inactive 宿主，暂时清除 opt-in 并恢复环境，反射调用 AutoLaunch 检查全局组件数不增加、调用 Start 检查停用，声明预期警告。不创建 benchmark 标记，不运行工具或 RunAll。临时根允许路径及 junction 拒绝矩阵尚未专项动态测试，不声称完整隔离边界验收。

验证：Quick、EditMode **388 total / 384 passed / 0 failed / 4 skipped**、临时 Player 构建、startup-only 冒烟全部通过。主仓库 logs/build/benchmark-isolation-{quick,tests,player,smoke}.log；XML 独立保存于本工作树 logs/build/benchmark-isolation-test-results.xml。统一 -NoKill -MaxCores 4，保留缓存、BelowNormal。

Player 输出 C:/Users/25295/AppData/Local/Temp/opencode/benchmark-isolation-20261108-player/DesktopPet.exe，未覆盖正式 Build。冒烟使用原主工作区现有启动专用脚本驱动该独立 Player，初始化/窗口切换/自退出/零异常及生产记忆存在性/hash/mtime 零污染通过。隔离根 C:/Users/25295/AppData/Local/Temp/opencode/fuxuan_smoke_test_benchmark_isolation_20261108 保留。未截图未聊天。

没有实际执行 benchmark；即使满足启动边界，工具清单仍涉及副作用和外部调用，不能把本步视为授权全量运行，未来必须另行审查、明确确认。代码仍参与生产编译，未实现程序集隔离。本步仅本地节点，未再次推送；不把此前主工作区诊断实验源码带入。
