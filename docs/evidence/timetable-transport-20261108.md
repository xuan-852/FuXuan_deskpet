# 课表服务 HTTP 传输边界收口（2026-11-08）

## 基线与变化

独立工作树 Build/w/main-review，分支 refactor/startup-ownership-20261108，开始 HEAD 4087262，工作区干净；main/origin/main 仍 bdc977 之前的 b3dc977（未推送本分支节点）。未触碰原混合工作区与探索分支。

ServerPollService 中轮询 + 四个只读学业查询原先各自重复 GetAsync、IsSuccessStatusCode、ReadAsStringAsync。现抽为该类内部唯一的 GetAsyncWithStatus，返回 (是否成功, 状态码, 响应体)。没有新增类、接口或服务，没有改 URL、请求头、超时、JSON 模型或任何查询格式化逻辑。

各调用点保留原有用户可见文本与异常处理：
- 轮询非成功仍输出 `[ServerPollService] 轮询失败: HTTP <code> <body>`；异常仍由 PollMessagesAsync 原有 catch 分类（连接失败静默、超时静默、其它 warning）。
- 四个查询非成功仍分别返回“查询考试/成绩/课表/用户信息失败，服务端未响应”；异常仍进入各自 catch 的“查询X出错: ...”。

行为差异：查询在非成功响应时现在也读取响应体（随后丢弃），此前直接返回；状态码日志以 long 输出，数字一致。属实现细节，未改变成功路径或提示文案。

生产文件 **+24 / -18**（净 +6），收益是五处传输/状态处理收为一条，而不是行数减少；没有为“看起来整洁”拆文件。

## 验证

没有新增测试（该类的既有测试为数据/装配层，未覆盖真实 HTTP）。复用 ToolAcademicQueryTests 的无服务路径，确保四个工具仍返回未就绪文案。

| 层级 | 结果 | 证据（主仓 logs/build） |
|---|---|---|
| Quick | 通过，19 秒 | timetable-transport-quick.log |
| EditMode | **397 total / 393 passed / 0 failed / 4 skipped** | timetable-transport-tests.log；独立 XML 于本工作树 logs/build/timetable-transport-test-results.xml |
| 完整 Player | 通过，22 秒，未覆盖正式 Build | timetable-transport-player.log |
| startup-only | 初始化/窗口切换/自退出/零异常/生产记忆存在性、hash、mtime 零污染通过 | timetable-transport-smoke.log |

Player：C:/Users/25295/AppData/Local/Temp/opencode/timetable-transport-20261108-player/DesktopPet.exe；隔离数据：C:/Users/25295/AppData/Local/Temp/opencode/fuxuan_smoke_test_timetable_transport_20261108。统一 -NoKill -MaxCores 4、增量缓存、BelowNormal。启动驱动来自原主工作区已有 startup-only 脚本。

未连接真实课表服务，未验证真实非 2xx 响应、超时与连接拒绝下的日志/文案，也未验证 401 轮询诊断在真实服务端的表现。该边界只覆盖传输与服务商响应的读取，不涉及真实 HTTP 端到端验收。

## 保存

仅本地节点，不推送；与同分支 benchmark、审批等待、学业查询三个未推送节点分开记录。同步 tool-engine 模块中直接相关的说明。
