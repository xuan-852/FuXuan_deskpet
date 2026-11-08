# MotionAgent 同宿主依赖解析收口（2026-11-08）

## 基线与边界

开始 HEAD 为 `0adede9`，暂存区为空；既有混合工作区差异原样保留。不合并探索分支、不推送，不处理独立动作研究仓库。

ProductComposition 已在同一产品宿主提供 ChatManager、MotionAgent、DualModelValidator；同宿主 HybridRenderer.Awake 获取或创建 Live2DRenderer。此前 MotionAgent/监控器精确 GUID 的场景、prefab、asset 核查和独立宿主入口搜索见 action-dependency-ownership-20261107.md 与 gpu-monitor-ownership-20261107.md。本步没有搬动或删除组件。

## 实际减少

MotionAgent.ResolveComponents 的 **3 处全场景 FindObjectOfType**（Live2DRenderer、ChatManager、DualModelValidator）改为 GetComponent。同宿主所有权显式化，独立宿主缺依赖时不再借用场景中任意对象；原有 null 条件处理保留。ActivityTracker 全局服务仍取 Instance，BeginAiMotionHandoff 的 DesktopPet 全局查找未动；不全仓替换单例或查找，不改评分、准入、动作和模型算法。

仅修改：

- `code/desktop_unity/Assets/Scripts/Live2DFramework/ActionAgent/MotionAgent.cs`：三个解析调用。
- `code/desktop_unity/Assets/Editor/Tests/ActionDependencyOwnershipTests.cs`：扩展既有用例，在 inactive 宿主显式提供三个组件后解析，检查字段绑定至该宿主实例；不执行 Awake、不请求模型、不读写记忆、不增加用例数量。inactive 对象不被默认 FindObjectOfType 找到，因此该断言区分同宿主解析与旧全局路径。

独立集成若把这三项服务拆在不同对象上，需调整宿主装配；不新增全局回退或注入框架。

## 新鲜验证（当前混合工作区）

统一 `-NoKill -MaxCores 4`，保留增量缓存与 BelowNormal；三个构建日志未检出 error CS 或 Scripts have compiler errors。

| 层级 | 结果 | 证据 |
|---|---|---|
| Quick | 通过，19 秒 | `logs/build/action-host-20261108-quick.log` |
| EditMode | **457 total / 453 passed / 0 failed / 4 skipped**，18 秒 | `logs/build/action-host-20261108-tests.log`、`logs/build/action-host-20261108-test-results.xml` |
| 完整 Player | 通过，45 秒，未覆盖正式 Build | `logs/build/action-host-20261108-player.log` |
| 动态启动 | startup-only 通过：产品初始化、外置窗口切换、应用自退出、零异常、生产记忆存在性/哈希/mtime 零污染；未截图未聊天 | `logs/build/action-host-20261108-smoke.log` |

Player 为 `C:/Users/25295/AppData/Local/Temp/opencode/fuxuan-action-host-20261108-player/DesktopPet.exe`。隔离数据与日志保留于 `C:/Users/25295/AppData/Local/Temp/opencode/fuxuan_smoke_test_action_host_20261108/`；生产只读校验目录为 `D:/DesktopPetData`。Player 日志 15:25:18.097：`renderer=True, chat=True, tracker=True, validator=True`。目标代码 diff --check 通过。

结论仅覆盖混合工作区；精确提交树独立复建**未验证**，此前 873b78e 的两项动作资源基线失败没有被修复。跨宿主外部集成、真实评分、动作视觉、真实聊天**未验证**。本步保存时逐块选择两个代码/测试文件、本文及动作模块对应段落，不纳入旧实验与其他文档段落；仅本地节点，不推送。
