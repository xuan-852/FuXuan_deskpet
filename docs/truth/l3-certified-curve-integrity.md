# L3 认证曲线完整性绑定

> **证据日期**：2026-09-18
>
> **任务包**：`l3-certified-curve-integrity-v1`

## 已验证事实

- `CertifiedMotionLibrary` 为六项已公开的外部认证技能分别登记独立 `CurveSha256`；它与既有冻结评审包 `PacketSha256` 分离。
- `Live2DRenderer.PlayCertifiedMotion` 在读取 JSON 前执行 SHA-256 校验。曲线缺失仍报告未安装；哈希缺失、读取失败或不匹配均拒绝执行，且不会降级为原始参数控制。
- EditMode 新增篡改曲线夹具，断言返回 `curve-hash-mismatch`；`build.ps1 -Quick` 和 `build.ps1 -RunTests` 均通过，后者报告 `failed=0`。
- 使用最新临时 Player 在六个独立 `FU_XUAN_DATA` 数据根复验所有未篡改曲线：均通过哈希门禁并到达 `certified-motion-completed`，未出现完整性失败。

## 边界

曲线仍只从隔离数据根加载，未部署到生产根，也未进入版本库。本事实不构成外部素材的再分发授权或生产部署验收。
