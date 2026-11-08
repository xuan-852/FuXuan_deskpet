# main 首批择取集成审查（2026-11-08）

## 基线与选择

fetch origin 后远端 main 为 `1d056f8`（2026-09-27）。主工作区 HEAD `7504413`，相对远端 73 个提交，另有大量已跟踪及未跟踪实验。没有整枝合并、暂存或覆盖这些差异。独立工作树 `Build/w/main-review` 从 main 创建集成分支 `integration/main-reviewed-20261108`，不复用主工程 Library。

这不是所有探索分支逐提交审查完成报告；先按分支/提交路径分组，再对以下首批低耦合候选查看 diff、依赖与测试并组合验证。其他候选保持原分支，后续继续评审。

| 来源 | 选择理由 |
|---|---|
| 4d1af08 | 构建路径相对化、无密钥配置示例，独立工程验证必需 |
| dbeb115 | 云端工具只允许本轮实际提供的名单，门禁先于执行/审批；不调用真实危险工具的准入测试 |
| a98d6d4 | 冒烟脚本迁移并保留兼容转发入口 |
| 3f68c9e | 四个认证测试驱动消除固定目录预删除与 skillId 越界，配套边界回归 |
| e1cc4d3 | request_body_skill 按真实异步注册路径执行，修复同步入口误报 |
| f09c0b2 | 上述迁移直接关联文档口径，历史日期数值不改写为本轮结果 |
| 1acc6d7 | 产品装配、动作启停语义、本地模型配置与探测职责收口 |
| 873b78e | 动作依赖和 GPU 监控装配所有权 |
| 0adede9 | 聊天组件不再隐式创建整组共享服务 |
| 1ae0c47 | 动作服务同宿主依赖解析 |
| 7504413 | 提醒状态发布与托盘重复刷新收口 |

集成采用逐提交 cherry-pick，未发生代码冲突。未选择点击轻回应（产品交互变化需另验）、UI 皮肤、head_tilt 新能力、补丁手臂、MMD、实时表现原型、整批文档迁移或工作区 DiagnosticStartup/ReplyPerformanceDirector。不能把“暂不选择”理解为否定其价值或已完成其详细审查。未访问独立动作研究仓库文件。

## 审查发现与处理

- 独立工程用已跟踪 ChatConfig.cs.example 生成被忽略的 ChatConfig.cs；无复制真实密钥，不提交本地配置。
- 首轮 EditMode 371 / 365 / 2 / 4，两项失败都是 main 原本缺失 acknowledge_nod 资源。
- 找到历史 `1ec451a` 的该 JSON 及 meta，SHA-256 `81a28f2627e00ea155a24644976bcd8b78df44ce2b279bd87b5d0df1c4effbf2` 与 main 已登记认证值逐字节匹配。仅提取两文件，单独提交 `8a32db1`，没有引入该来源的其他动作实验，也没修改认证登记或使用主工作区已变化的资源。
- 清除两份模块文档中 1acc6d7 引入的纯问号损坏摘要；保留正常历史说明及节点验证限制，以本报告补充当前集成结果。
- 对变更文件扫描常见 sk-/GitHub Token/私钥标头，无匹配；这不等于完整秘密检测审计。ChatConfig.cs 未跟踪，候选无 .env、模型二进制或真实生产数据。
- 由跨分支整合、历史兼容及构建验证触发，读取的模块/历史证据超过常规单任务上下文预算；只读取直接候选与依赖，不加载完整研究文档树。

## 新鲜验证

| 层级 | 结果 | 证据（主仓库 logs/build） |
|---|---|---|
| Quick | 独立工程通过 | main-review-quick.log |
| EditMode | 恢复原资源后 **371 total / 367 passed / 0 failed / 4 skipped** | main-review-tests-r2.log；XML 在集成工作树 logs/build/test_results.xml |
| 脚本目录安全 | runtime 69、exit 85、handoff 123、walk-rejection 137，共 **414 检查**通过 | 四个 scripts/live2d/test/certified_motion_*_root_regression.cjs 的本轮输出 |
| 完整 Player | 独立工程通过，42 秒，4 核/BelowNormal，CPU 峰值 23% | main-review-player-r2.log |
| 启动冒烟 | 产品初始化、外置窗口切换、应用自退出、零异常、生产记忆存在性/哈希/mtime 零污染通过 | main-review-smoke.log |

曾因另一 Unity 作业活动被 -NoKill 预检查阻挡，未终止或干预；确认无活动编辑器后正常构建。正式 Build 未覆盖。

Player：`C:/Users/25295/AppData/Local/Temp/opencode/main-reviewed-20261108-player/DesktopPet.exe`。启动冒烟由**原主工作区现有未提交版本** scripts/test/smoke/runtime_smoke.cjs 的 startup-only 模式驱动，此脚本差异不纳入本次集成，不谎称候选脚本支持 startup-only。隔离数据保留于 `C:/Users/25295/AppData/Local/Temp/opencode/fuxuan_smoke_test_main_reviewed_20261108/`，只读检查生产目录 D:/DesktopPetData；未截图未聊天。

## 边界与发布方式

候选运行时代码、测试与原哈希资源在独立工程验证，不再借用混合源码；本地配置初始化和外部冒烟驱动依赖如上明确登记。真实聊天/云端、动作视觉效果、点击轻回应、原生提醒投递与定时工具未验证；完整功能冒烟未跑，不称为发行版验收。

仅将此首批已选择提交及审查文档推进 main，正常快进和 push origin main；若远端 main 变化则停止重审，不使用 force。主工作区原分支和未提交内容保留，不删除工作树或探索分支，不创建发布包或标签。最终提交及远端状态以 git log / ls-remote 为准。
