# L3 认证曲线完整性绑定

> **证据日期**：2026-09-25
>
> **任务包**：`l3-certified-curve-integrity-v1`

## 已验证事实

- `CertifiedMotionLibrary` 为六项已公开的外部认证技能分别登记独立 `CurveSha256`；它与既有冻结评审包 `PacketSha256` 分离。
- `Live2DRenderer.PlayCertifiedMotion` 在读取 JSON 前执行 SHA-256 校验。曲线缺失仍报告未安装；哈希缺失、读取失败或不匹配均拒绝执行，且不会降级为原始参数控制。
- EditMode 新增篡改曲线夹具，断言返回 `curve-hash-mismatch`；`build.ps1 -Quick` 和 `build.ps1 -RunTests` 均通过，后者报告 `failed=0`。
- 使用最新临时 Player 在六个独立 `FU_XUAN_DATA` 数据根复验所有未篡改曲线：均通过哈希门禁并到达 `certified-motion-completed`，未出现完整性失败。
- 2026-09-25 新增项目自有 `acknowledge_nod`：内置 `Resources` 候选与数据根覆盖均经过相同 SHA-256 门禁；新鲜隔离 Player 驱动验证了数据根候选加载、运行时准入和完整生命周期。该技能的视觉自然度仍未完成认证，且当前不向 LLM 暴露。

## 边界

外部认证曲线仍只从隔离数据根加载，未部署到生产根，也未进入版本库。本事实不构成外部素材的再分发授权或生产部署验收。`acknowledge_nod` 是项目自有内置候选，允许从 `Resources` 回退，但其 LLM 暴露和视觉自然度仍未通过独立人工/双模型验收。
