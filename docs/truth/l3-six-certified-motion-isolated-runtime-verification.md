# L3 六项认证动作隔离运行时复验

> **证据日期**：2026-09-18
>
> **范围**：使用当前源码构建的临时 Player，在六个独立 `FU_XUAN_DATA` 根目录中复验已公开的 `CertifiedMotionLibrary` 条目。此记录不替代生产曲线部署或真人自然度签字。

## 结果

| 技能 | 结果 | 运行时证据 |
|---|---|---|
| `external_Hiyori_Hiyori_m02` | 通过 | 准入、播放、姿势还原、完成释放、7 帧截图 |
| `external_Hiyori_Hiyori_m05` | 通过 | 准入、播放、姿势还原、完成释放、7 帧截图 |
| `external_Hiyori_Hiyori_m06` | 通过 | 准入、播放、姿势还原、完成释放、7 帧截图 |
| `external_Haru_haru_g_idle` | 通过 | 准入、播放、姿势还原、完成释放、7 帧截图 |
| `external_Haru_haru_g_m10` | 通过 | 准入、播放、姿势还原、完成释放、7 帧截图 |
| `external_Haru_haru_g_m20` | 通过 | 准入、播放、姿势还原、完成释放、7 帧截图 |

每项均未出现 `timed-out`、认证执行错误或取消终态；中段截图可见模型姿态/表情变化。

## 验证修正

原 `certified_motion_runtime_drive.cjs` 在采集固定数量截图后以固定等待时间退出。对 8.6 秒的 `external_Hiyori_Hiyori_m05`，该做法会先发送 `@@test:quit`，使动作以 `certified-motion-before-test-exit` 取消，形成完成验证假阳性。

驱动器现改为等待 `[EmbodiedRuntimeAdmission] released: certified-motion-completed` 后再截图并退出。因此，长于旧固定等待窗口的认证曲线也必须真实完成、姿势还原并释放资源才可通过。

## 边界

- 曲线仅复制到临时隔离数据根；未写入生产 `certified_motions/`，未修改生产记忆，也未调用云端。
- 认证登记的 `PacketSha256` 是冻结评审包标识，并非运行时候选曲线文件的 SHA-256；当前执行器尚未对加载曲线实施指纹绑定。
- 本次为隔离运行时功能复验。生产部署、自然语言真实聊天路径与真人目视自然度签字仍待 [认证动作曲线数据的生产部署与真人验收提案](../guides/proposed/certified-motion-data-deployment.md) 获批后执行。
