# 剩余分支审核第二批（2026-11-08）

## 范围

origin/main cdc0968；审核分支 review/remaining-branches-20261108 起点 21f2d04。主工作区 HEAD 7504413 和混合改动保持不动。仅 git show 读取历史源码/直接证据，不操作独立动作研究仓库、不运行预览、不联网模型、不合并或推送。本轮不做新动作设计。

因跨分支历史兼容审核，读取来源控制器、Renderer partial、预览脚本、测试、反馈和视频工具源码，超出单模块常规上下文；只读直接候选，不扩散历史文档树。当前主工作区 docs/agents/live2d-motion-agent.md 不存在（前轮已确认）；本轮不开展动作研究，审核以候选直接源码及绑定反馈为证据。

## 结论：目前无新增可直接合入 main 的生产代码

### 1. motion-review-hardening：清理存在可复现证据误删，阻断整体合并

定位 `7c18c4a:scripts/live2d/video-review/vreview/capture.py`，cleanup 的 framesDir 取 MP4 同目录 frames，apply 时删除其中所有文件。MP4 的文件级归属没有传播到帧文件；保留录制和未归属手工证据没有保护。

**离线实测复现**：从 git blob 动态加载 capture 模块，ffmpeg stub（本用例不调用），在批准临时目录 opencode 下 TemporaryDirectory 创建 old.mp4/new.mp4、各自空 sidecar、共享 frames/manual-evidence.txt，显式设置不同 mtime，cleanup(keep=1, apply=True)。结果 old MP4 删除、new MP4 保留、manual-evidence.txt 被删除。只删除本次创建的测试目录，无真实录制参与。输出为 `retained_mp4_exists=true, manual_frames_sentinel_exists=false, old_mp4_removed=true`。

此外 sidecar 存在即可授权 MP4，无需有效 JSON 或内容指名该路径；来源测试主动用 `{}` 作证明。此协议不是强归属证明，需与生成器配对审核；不能仅按“提交标题修了误删”放行。共享 frames 还可能重复计入回收字节。

建议：若继续回收工具，先按具体录制清单核对帧归属；无证据归属的帧不删，保留录制引用的共享帧不删。只做 dry-run 不足以覆盖 apply 删除路径。不要把修复扩成清理框架。

### 2. 评审结论覆盖仍有口径边界，不应称严格双通道通过

`7c18c4a:vreview/reviewer.py:decide_result` 只将某窗口**所有通道**均 needs-evidence 判为 inconclusive。静态提取该函数后执行纯数据样例：kimi 对 w1 给 no-major-issue-observed，gemini 给 needs-evidence，failures=[]，返回 no-major-issue-observed。

这是实测函数输出，不是网络模型请求。若产品口径要求两通道实质结论，该输出不足；若允许单通道实质结论，则报告必须明确另一通道不确定。不能仅凭本例断言整体汇总算法错误，需先确认预期策略，保留为 BlockedByDecision，不擅改投票规则。

`a957e98` 的无法判断不算已确认缺陷、修解析器重用缓存，以及 `7c18c4a` 的网关诊断保留有独立价值，但依赖 video-review 整组源码；main 尚无这组工具，不能孤立 cherry-pick 几个修复提交。下一步应将工具引入与上述缺陷修正作为单独边界，不混入桌宠启动职责。

### 3. 注意方向 99cb5ce：保留研究分支，生产接入未达门槛

正向点：目标数值有限检查，显式临时测试根/标记/重解析点 gate，CandidateTest 租约，120 秒会话上限，失去所有权或停止时收束；未注册正式技能或 LLM 工具。

阻断/风险：

- 来源绑定的人工反馈说明原型注意方向获认可，但独立四通道提取版尚未复评；原型报告的拖拽落地异常摆动、偶发卡手仍未定位。不能继承为生产视觉认证。
- Renderer Update/LateUpdate 新增早退，涉及物理、参数提交、观察与动作锁，生产编译耦合不只是一个纯控制器。虽默认测试 gate 关闭，也不能称对生产完全无影响。
- Stop 恢复失败后仍在 finally 释放租约和移动锁，保留 pending restore 阻止自身重入；现有停止入口不是事务性锁所有权系统。后续重试恢复与其他 writer 的竞争需专测，不声称已复现跨 writer 覆写。
- prepare 禁用 AutoChat 但事件订阅没有因 enabled=false 自动解除，且先于准备的启动窗口没有不可用模型/显式 no-cloud 配置。预览驱动不能仅凭注释宣称全程零模型服务。
- 当前测试主要覆盖纯控制器/隔离路径，不覆盖上述 Renderer 恢复失败交错；历史 Player 检查不代替组合集成。

### 4. 实时表达 845c549：保留原型，不与注意方向一起整枝合并

两者是平行提取：同样改 Renderer 的中断、Update/LateUpdate、观察进展、cleanup 和 IsActionLocked。简单叠加将产生两个潜在早退 writer，而非天然组合能力。先决定生产只需要哪个能力，不为合并两分支而新建调度器。

来源人工反馈没有确认 listen/explain 语义差异，think 可读性也未通过；这与控制器数值有界/可转向是不同门槛。因此不应把模式名作为已实现产品语义。

## 后续顺序

1. 点击轻回应与纯拖拽恢复测试已在上一批独立验证，仍待下一批 main 集成，不重复合入来源祖先。
2. 视频工具优先处理已复现清理误删；修复须补共享目录/保留录制/未归属帧夹具，再审其他写盘、付费与退出边界。
3. 对话表现家族下一轮只找可回收的生命周期修复，视觉原型保持研究状态；MMD/手臂全量依赖仍未逐个审完。

本轮没有 C# 或生产脚本变更，未运行 Quick/EditMode/Player；只运行上述两项隔离离线复现。未更新模块真相、未推进 main、未推送，不宣称剩余分支全部审核完成。
