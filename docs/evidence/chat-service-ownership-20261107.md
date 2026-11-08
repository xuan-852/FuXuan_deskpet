# 聊天服务装配所有权续作（2026-11-07；2026-11-08 混合工作区复验通过）

前置本地节点：`873b78e`，详情见 slimming-ownership-checkpoint-20261107.md。该节点保留独立工程两项动作资源测试失败的限制，不是全绿发布节点。

## 核查与实施范围

Assets C# 搜索仅发现 ProductComposition 和 inactive Editor 测试创建 ChatManager。用 ChatManager.cs.meta 精确 GUID 搜索 .unity/.prefab/.asset 未发现独立挂载；不保证外部工程或 AssetBundle 兼容。

本步工作区试改三个文件：

- ProductComposition.cs：在 Ensure<ChatManager> 前显式 AttachChatServices，并说明独立宿主需先装配后激活。
- ChatManager.cs：删除 Awake 中 AttachChatServices 调用，只绑定已有 ActivityTracker。
- ProductCompositionTests.cs：复用现有测试检查六项共享服务均存在，重复装配检查扩展至整个宿主子树；不新增用例。

减少一条“创建聊天组件隐式启动六项共享服务”的路径。独立宿主须显式调用既有 AttachChatServices，不新增接口、回退 gate 或框架。2026-11-08 已在混合工作区完成下述验证。

## 阻断与状态

两次 Quick 预检查分别发现 Tuanjie PID 44440、39944 活动，按 -NoKill 安全退出，日志 chat-ownership-quick.log / chat-ownership-quick-r2.log。事后只读检查时对应进程已不可见，尚未确定是否存在其他构建链路，未强杀、未并发启动编辑器。

因此本步编译、EditMode、Player、动态冒烟全部**未验证**；未更新模块文档、未暂存、未提交。git diff --check 针对三个目标文件通过，不替代编译。

下一步先确认本机无并行 Unity 构建，再顺序执行 Quick、EditMode、临时输出 Player 和隔离 startup-only 冒烟；特别核对服务先于 ChatManager 绑定初始化、既有单例复用、没有增加请求或记忆污染。原有混合工作区差异保持原样。

## 2026-11-08 复验（替代上节的未验证状态）

开始时 HEAD 为 `873b78e`，暂存区为空，无 Tuanjie 进程。复核目标三个文件后，未再修改生产代码，按顺序运行以下验证，统一 `-NoKill -MaxCores 4`，保留缓存和 BelowNormal 节流。

| 层级 | 结果 | 证据（仓库根目录相对路径） |
|---|---|---|
| Quick | 通过，25 秒，CPU 峰值 31% | `logs/build/chat-ownership-20261108-quick.log` |
| EditMode | **457 total / 453 passed / 0 failed / 4 skipped**，21 秒，CPU 峰值 28% | `logs/build/chat-ownership-20261108-tests.log`、独立保留的 `logs/build/chat-ownership-20261108-test-results.xml` |
| 完整 Player | 通过，33 秒，CPU 峰值 24%，未覆盖正式 Build | `logs/build/chat-ownership-20261108-player.log` |
| 动态启动冒烟 | `--startup-only --keep-artifacts --verbose` 通过：产品初始化、外置窗口切换、应用自退出、零异常、生产记忆存在性/哈希/mtime 零污染；未截图、未聊天 | `logs/build/chat-ownership-20261108-smoke.log` |

三份构建日志均未检出 `error CS...` 或 `Scripts have compiler errors`。EditMode 时间为 `2026-11-08 07:06:35Z` 至 `07:06:39Z`；三项既有 ProductCompositionTests 均 Passed，测试数未增加。

- 临时 Player：`C:/Users/25295/AppData/Local/Temp/opencode/fuxuan-chat-ownership-20261108-player/DesktopPet.exe`。
- 隔离数据与 Player 日志：`C:/Users/25295/AppData/Local/Temp/opencode/fuxuan_smoke_test_chat_ownership_20261108/`，保留 `.test_mode`。脚本只读校验生产目录 `D:/DesktopPetData`，结束后未检测到残留 DesktopPet 进程。
- Player 日志 15:07:35.244 载入隔离 PetConfig，15:07:35.255 添加 ChatManager，15:07:35.346 完成产品初始化，15:07:47.571 由测试命令进入退出清理。服务构建顺序由装配代码明确保证，已有 inactive 测试验证六项服务存在和整个子树重复装配不增殖；本次没有新增动态服务数量探针。

### 验证边界与保存状态

以上结论**仅适用于当前混合工作区**，不代表仅含本步的提交树在干净工程独立通过。上个节点缺少动作资源及哈希不匹配的两项基线失败不因此消失。真实聊天、本地/云端请求、记忆反思、已有全局单例跨宿主复用及外部独立聊天宿主的动态行为仍未验证。

验证后逐块选择三个代码/测试文件、本证据和聊天模块顶部装配说明，保存为本地检查点；暂存版本排除了 ReplyPerformanceDirector，未包含其他既有模块文档段落与实验差异。`git diff --cached --check` 通过。节点的精确独立树未验证，不将 457 项混合结果写成独立节点结论；节点 ID 可由 Git 日志查询，未推送。
