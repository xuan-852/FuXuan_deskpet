# 点击轻回应 main 适配验收（2026-11-08）

## 范围

在 review/remaining-branches-20261108 上适配来源 dce7cbe，形成 96053b5。基线含 main cdc0968 与已复验的纯测试候选 ffb4c55；未覆盖原主工作区实验、未修改 main、未推送。

- 冲突处理保留 main 显式提供 ChatBubble/IdleChatGenerator、缺依赖报警停用；ResolveReferences 只查同宿主，不 AddComponent。补上 _idleGen 解析，保留拖拽及聊天事件订阅。
- 普通点击不再 SendMessage，改为低优先级本地短句；在播气泡、等待回复、动作锁、冷却期间不补播；审批确认优先，拖拽聊天策略未改。
- ClickLightResponseTests 改用 inactive 宿主，避免借测试环境假定生命周期不运行；十二项用例不执行危险工具，审批仅本地回调。
- 验收脚本显式 no-cloud、独占临时数据与 .test_mode、不可用模型名、关闭自主决策及活动采集，避免末尾拖拽走真实生成。模型健康探测仍可能发生，不宣称全程零 HTTP。
- 进程退出事件在 spawn 后即订阅；等待超时只清理本次 PID 并报错，不以强杀当成功。污染校验增加 mtime，并在 finally 内也执行，失败路径不会跳过校验。动作锁未覆盖则状态 partial/退出码 2，不再整体 passed。

## 新鲜验证

独立工程、示例生成的本地 ChatConfig.cs，未使用混合源码和主工程 Library。

| 层级 | 结果 | 证据 |
|---|---|---|
| JS 语法 | node --check 通过 | scripts/test/click_light_response_drive.cjs |
| Quick | 通过 | 主仓库 logs/build/click-review-quick.log |
| EditMode | **386 total / 382 passed / 0 failed / 4 skipped**，含十二项点击测试及三项重复恢复测试 | 主仓库 logs/build/click-review-tests.log；审核工作树 logs/build/click-review-test-results.xml |
| 完整 Player | 通过，36 秒，4 核 BelowNormal，峰值 CPU 31%；不覆盖正式 Build | 主仓库 logs/build/click-review-player.log |
| 专用动态 | passed，退出码 0 | C:/Users/25295/AppData/Local/Temp/fuxuan_click_light_GtGolh/click-light-response-result.json |

Player：C:/Users/25295/AppData/Local/Temp/opencode/click-review-20261108-player/DesktopPet.exe。

动态 JSON 全部 true：clickEmitsLocalResponse、clickIssuesNoAiRequest、clickKeepsAttentionEvent、cooldownSuppressesLightResponse、poseStillAppliesDuringCooldown、actionLockSuppressionVerified、cooldownReleaseRecoversResponse、noStaleReplayWhileIdle、noResidualLockOrPose、dragDoesNotEmitLocalResponse、exitRecovered、productionMemoryUntouched。生产只读检查 D:/DesktopPetData，复验后无 DesktopPet 残留进程。

“点击零 AI”依据静态无 SendMessage、EditMode 会话/等待/队列断言及动态点击窗口无 ChatManager 日志，不是网络抓包证明。真人短句自然度、真实 OS 鼠标、真实模型聊天及真实危险工具执行未验证。动态锁检查通过不等于所有动作类型遍历验收。

本候选已具备下一批 main 择取条件，但审核分支的 96053b5 与来源 SHA 不等价（包含上述必要适配），不可再原样 cherry-pick 来源覆盖。UI 主题与其他动作原型仍未获准转正。
