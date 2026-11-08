# 剩余分支审核第一批（2026-11-08）

## 基线与审查方法

origin/main 为 cdc0968。原主工作区仍为 7504413，混合差异原样保留。fetch 后用 `git log --cherry-pick --right-only --no-merges origin/main...<branch>` 排除 patch 等价提交，不将 cherry-pick 后 SHA 不同误判为尚未合入。分支之间共享祖先，唯一提交数不可相加。

独立工作树 Build/w/main-review 从 main 切为 review/remaining-branches-20261108；main 与远端不移动。本轮只将 9e45a39 择取到审核分支做测试，不合并任何完整探索分支。不访问独立动作研究仓库、不改生产数据。历史分支比较是用户明确授权的审核范围。

## 第一批详细结论

### 点击轻回应 dce7cbe：有价值，需适配及安全验收修正后再合并

- 普通点击不再 SendMessage，低优先级本地回应、冷却、在播气泡与动作占用让位；审批确认优先。可减少非用户输入引发的模型请求。
- 来源 AutoChat.Start 仍自建 ChatBubble、IdleChatGenerator，早于 main 装配收口；整文件替换会回退 main。若择取必须保留当前缺依赖报警停用语义。
- 来源 ClickLightResponseTests 创建 active 宿主，依赖“EditMode 不触发生命周期”的注释；当前项目已有 inactive 宿主规范，集成需核实并适配，而非直接信任历史环境。
- 验收脚本末尾模拟拖拽会走既有自动聊天，未显式禁云/禁真实模型；不能按“点击零 AI”就认定全脚本零请求。
- 脚本 finally 等待 exit 或超时但不验证并终止自己创建的残留进程；若进程已退出才订阅 exit，可能额外等待。生产文件仅比哈希/存在性，未比 mtime。actions 锁无法观测时仍输出整体 passed（虽然 JSON 有未验证说明），需单独区分部分覆盖。
- 尚未改代码、编译或动态验证该功能，不直接合并。

### UI 皮肤 feat/chat-ui-skin-refresh：暂缓

- 扣除等价构建补丁后只剩 08a7c58、65ce1f5、c45dcfd 三项。
- ThemeComposer 和 ink 为 opt-in，未改变五个旧主题；但新增配色派生抽象，不是瘦身必需依赖。源指导明确 proposed、未完成视觉验收，且要求人工签字后才合并。
- 对源码颜色按 WCAG sRGB 相对亮度计算，在不透明 Surface 上混合文字 alpha：TextMuted 约 6.73:1，TextDim 约 3.68:1，Placeholder 约 3.20:1。这只是理论背景近似，不代表真实渲染或整体无障碍认证；不足以支持“小字可读性已改善”的结论。
- 正式主题截图脚本将 ink 加入主题表，默认 trial 从原新春变成 ink，改变已有试跑目标；同步说明尚不完整。
- 未运行截图或人工评审，不转正、不合并。

### 拖拽恢复 9e45a39：可独立择取的测试候选

- 分支另含 dce7cbe、99cb5ce，但本提交仅 .cs/.meta 两文件，使用 main 已有 Live2DInputCoordinator 公共接口；不必连带注意方向生产功能。
- 三项各 64 轮，共 192 轮，覆盖释放、重复清理、walking→drag、打断→drag、stale release/handoff/interrupt 与重新取得；没有模型、鼠标、场景或持久化操作。
- 轮次/耗时阈值引用被忽略 .workflow 证据，不适合作为长期唯一依据；应在合并说明中将可复现常量写清：64 轮、单轮 250ms、单测试 10 秒。此耗时断言有慢机器抖动风险，未擅自放宽。
- 择取为审核分支 ffb4c55。Quick 通过，EditMode **374 total / 370 passed / 0 failed / 4 skipped**，三项均 Passed；日志主仓库 logs/build/remaining-review-drag-quick.log、remaining-review-drag-tests.log，XML 独立保留在审核工作树 logs/build/remaining-review-drag-test-results.xml。
- 本次没有改生产运行时，未重新构建 Player 或运行真实拖拽。meta 有三处尾随空格，需清理后通过 diff --check 才能进入 main。本候选尚未推送。

## 其余分支的分组筛查（不是详细代码审查结论）

- 已无 patch 独有提交：codex/main-backport-ready、fix/build-worktree-isolation、fix/airborne-certified-motion、codex/motion-video-review-workflow、chore/embodied-phase-b-checkpoint、backup/main-before-live2d-merge、旧 pogget 分支；不用重复合并，不删除分支。
- 注意/实时表达：99cb5ce、845c549 都新增控制器、Renderer partial、测试命令及预览驱动。需要入口与生产 gate 审核，不能因其有独立测试就直接接入产品。
- 对话表现族：conversation-presence/continuity/handoff/hand/gesture/palm/pointing 等为共享祖先链，必须先找最小共同依赖与真人认可/否决记录，再拆可回收生命周期修复；不可把每个 tip 依次整枝合并。
- 手臂/透视/MMD/补丁族：native-arm、secondary-arm、upper-body、wrist-body、mmd-*、patch-arm 等含渲染/资源/映射及实验执行器，暂留研究分支。标题含 fix 不等于适用于 main。
- head_tilt/embodied：涉及新技能认证、资源版本及历史否决，应独立审核，不混同已恢复的 acknowledge_nod 原资源。
- chore/live2d-tool-standardization 除已择取瘦身外，还有工具迁移、门禁、文档归档等 67 个 patch 独有提交；不能将其整体视为“未完成瘦身”。cd7de6a 的哈希绑定修复有候选价值，但依赖脚本迁移和能力标定文档，尚未完成依赖抽取。
- motion-review-hardening：网关诊断/视频漏检等需独立工具安全审查，尚未详细审核。

## 本轮交付边界

完成分支盘点、三组详细初审及一项测试候选独立复验，不宣称全部探索分支审完。没有推送 main，不覆盖原工作区。下一步先修正点击候选的装配兼容和安全验收，再独立验证；视觉主题与动作新能力仍须各自证据门槛。
