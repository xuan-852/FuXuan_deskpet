# 学业查询协程边界收口（2026-11-08）

## 基线与变化

独立工作树 Build/w/main-review，分支 refactor/startup-ownership-20261108，开始 HEAD 37aefbf，暂存区干净；main/origin/main 仍 b3dc977。未触碰原混合工作区、探索分支或独立动作仓库。

`ReminderAcademicTools.cs` 的 query_exams / query_scores / query_schedule / query_user_status 原各自复制：查找 ServerPollService、未就绪提示、Task.Run、WaitUntil、Faulted/Canceled/Result 分发。现收进 BaseAcademicTool.RunQuery，仅保留各自的查询调用与“查询考试/成绩/课表/学业信息”标签；课表周参数解析仍在工具内。

该文件净 **-23 行**（+25/-48），四处相同异常/取消/空服务路径变为一处。未新增类、管理器、接口或配置；没有改 ToolRegistry、ServerPollService 查询、工具 schema、注册或危险审批。空服务的文案与原四处一致，仍为“❌ 课表传讯服务未就绪”。

## 验证

新增 ToolAcademicQueryTests 四例，仅在场景无 ServerPollService 时经 ToolRegistry.ExecuteAsync 驱动，断言四个工具均返回原未就绪文案且不抛异常；若环境已有服务则 Ignore，避免访问 localhost 课表端点。未发起网络、未读写提醒、未执行副作用工具。C# 带 BOM，meta 显式提供。

| 层级 | 结果 | 证据（主仓 logs/build） |
|---|---|---|
| Quick | 通过，20 秒 | academic-query-quick.log |
| EditMode | **397 total / 393 passed / 0 failed / 4 skipped**，四例均 Passed | academic-query-tests.log；独立 XML 在本工作树 logs/build/academic-query-test-results.xml |
| 完整 Player | 通过，26 秒，未覆盖正式 Build | academic-query-player.log |
| startup-only | 初始化/窗口切换/自退出/零异常/生产记忆存在性、hash、mtime 零污染通过 | academic-query-smoke.log |

Player：C:/Users/25295/AppData/Local/Temp/opencode/academic-query-20261108-player/DesktopPet.exe；隔离数据：C:/Users/25295/AppData/Local/Temp/opencode/fuxuan_smoke_test_academic_query_20261108。启动驱动仍来自原主工作区已有 startup-only 脚本，本步未纳入其差异。统一 -NoKill -MaxCores 4、增量缓存、BelowNormal。

失败路径文案（含 `{label}时出错`、`{label}超时`）由同一模板生成，只验证了未就绪路径的等价性；真实 HTTP 故障/取消路径未动态测试，Task.Run 无取消词时 IsCanceled 通常不成立，这是原有行为，本步未改变。真实课表服务、真实 Player 查询与 UI 展示未验证。

## 保存

仅本地节点，不推送；与同分支 benchmark、审批等待两个未推送节点一并区分记录。更新 tool-engine 模块直接相关段落。
