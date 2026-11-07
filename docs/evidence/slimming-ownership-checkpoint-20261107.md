# 动作服务装配本地检查点（2026-11-07）

## 范围

基于 `1acc6d7`，仅收口动作服务依赖所有权：删除 MotionAgent 自建验证器、验证器自建动作记忆、MotionAgent 自建 GPU 监控器三处隐藏创建分支；删除验证器无用渲染器字段及一次全局查找。监控器改由现有 ProductComposition 显式同宿主装配。独立 MotionAgent 宿主需自行提供游戏监控。

配套两项依赖所有权测试与既有产品装配/幂等测试；不加入 ReplyPerformanceDirector、诊断 Probe、场景、模型或动作实验。不新增框架。

## 隔离源码验证与基线缺口

独立工作树 `Build/w/slim-ownership` 从 HEAD 建立，仅应用本次七个代码/测试文件。没有复用主工程 Library。

1. 首次 Quick 失败：被忽略的 ChatConfig.cs 缺失。按已跟踪 ChatConfig.cs.example 生成本地配置后 Quick 通过，日志 `logs/build/slim-clean-ownership-quick-configured.log`。
2. EditMode **354 total / 348 passed / 2 failed / 4 skipped**。两项失败均为 CertifiedMotionLibraryTests 的 acknowledge_nod 资源缺失；本次依赖所有权与产品装配测试通过。
3. 主工程的 acknowledge_nod.json 被 .gitignore 排除。仅为定位问题复制至隔离工程后，两项失败变为 curve-hash-mismatch：本地文件 SHA-256 为 `2abed6fa1076624d1a6df6d07f92295f0b9f9238b8f02ea98877b1a852d652ba`，提交库登记为 `81a28f2627e00ea155a24644976bcd8b78df44ce2b279bd87b5d0df1c4effbf2`。没有改哈希、删测试或将资源加入提交。复跑仍为 **354 / 348 / 2 / 4**，日志 `logs/build/slim-clean-ownership-tests-resources-r2.log`，XML 位于隔离工作树 logs/build/test_results.xml。
4. 同一隔离源码完整 Player 构建通过，日志 `logs/build/slim-clean-ownership-player.log`，输出 `C:/Users/25295/AppData/Local/Temp/opencode/slim-clean-ownership-player/DesktopPet.exe`。
5. 使用主工作区现有 runtime_smoke.cjs 的 startup-only 模式驱动该隔离 Player，通过初始化、外置窗口切换、应用退出、零异常、生产记忆零污染。日志 `logs/build/slim-clean-ownership-smoke.log`；隔离数据保留于 `C:/Users/25295/AppData/Local/Temp/opencode/fuxuan_smoke_test_slim_clean_ownership`。该脚本本身不纳入节点，Player 使用了上述本地配置及诊断用动作资源。

因此这是**有已知基线失败的本地检查点，不是全绿发布节点**。裸 Git 树仍需配置初始化，动作资源与登记哈希不一致尚未解决。此前混合工作区 457/453/0/4 不代表本节点的独立测试结果。真实评分、游戏切换/冷却/托盘恢复、动作视觉效果未验证。下一步需单独确认资源来源与正确版本，不在瘦身中擅改认证数据。

## 后续

节点仅本地保存，不推送；其他混合差异保留。继续核查聊天服务装配入口，先验证独立宿主契约，不直接移除 ChatManager 的兼容行为。
