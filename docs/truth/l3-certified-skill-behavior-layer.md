# L3 认证技能行为层接入（空闲时机触发）

> **证据日期**：2026-09-18  
> **任务包**：`l3-certified-skill-behavior-layer-v1`  
> **状态**：行为层接入已验证；主动 LLM 提案与反射层仍属后续任务。

`Live2DRenderer` 的空闲槽位（原 `PickNextIdleAction` 触发点）现在优先尝试行为层：从 `CertifiedMotionLibrary.LlmExposedEntries` 随机挑选一项认证技能，经生产执行器 `PlayCertifiedMotion`（静止门禁 → 输入租约 → 准入 → 曲线播放 → 姿势还原）播放。行为层不产生新动作、不调用 LLM、不写原始参数。

- 冷却：成功后 90 秒内不再占用空闲槽位；失败（数据缺失/准入拒绝）30 秒后重试，启动后有 45 秒初始延迟。冷却期内与失败时回退旧空闲调度，旧行为完全不变。
- 该触发只复用已认证且已向 AI 暴露的技能；未暴露条目对行为层同样不可见。

## 可复核证据

- `build.ps1 -RunTests`：EditMode **227 total、226 passed、failed=0、1 ignored**。
- 运行时日志链：`[EmbodiedBehaviorLayer] idle-certified: <skillId>` → `[CertifiedMotion] started` → `certified-motion-completed`；失败路径输出 `idle-certified-unavailable: <原因>`。

## 明确未完成项

- 注意力驱动（鼠标停留注视）与反射层触发未实现。
- 行为画像（安静/平衡/活跃）与用户可调频率未接设置页。
- 主动 LLM 提案（闲聊/情绪联动）仍待 >4 秒阈值决策。
