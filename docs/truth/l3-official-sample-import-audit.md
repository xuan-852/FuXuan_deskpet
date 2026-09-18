# L3 官方样例动作离线导入覆盖审计

> **证据日期**：2026-09-18
>
> **输入**：本地 Cubism Web Samples `Samples/Resources`；隔离能力目录 `%TEMP%\\fuxuan_probe_all_full_20260916\\parameter-capability-catalog.json`。

## 执行结果

在带 `.test_mode` 的临时根执行 `scripts/live2d-probe/import_motion3.cjs`，输出仅留在 `%TEMP%\\fuxuan_official_reference_import_20260918`。

| 指标 | 结果 |
|---|---:|
| 扫描动作数 | 78 |
| 参数完全覆盖动作数 | 0 |
| 未匹配参数曲线 | 2,376 |
| 被范围钳制的采样点 | 720 |
| 生产数据/映射写入 | 0 |
| 云端调用 | 0 |

Haru/Hiyori 动作虽各有 23 个匹配参数，但均含未匹配曲线且出现范围钳制；其他样例模型的缺口更大。

## 结论

该结果否定“外部 `.motion3.json` 可直接转植为符玄动作”的假设。现有导入器的输出只能保持 `LegacyCandidate` 取证用途，不能直接送认证或运行时。

后续最小原型必须先输出模型无关的 `MotionFeaturePacket`（相位、头/躯干/手臂方向、节奏和停顿），再仅用符玄已认证能力重建候选曲线；缺失部分必须降级而非以范围钳制伪装完成。
