# L3 轻回应式抬手隔离人工审核包

> **验证日期**：2026-09-18  
> **状态**：运行时链路通过；人工审核为“正常”；未部署到生产数据根，未向 LLM 公开。

## 受审对象

- 技能：`screen_side_arm_raise`
- 语义边界：画面侧单臂平缓抬起、短暂停顿后回落；不是招手、问候、舞蹈或人体左右归属声明。
- 曲线：自制单通道 `Param94`，时长 2.4 秒；SHA-256 为 `569e88a39a32f6fd292706039438cda886d453d6df189850ed822880614060a9`。
- 曲线仅写入隔离 `.test_mode` 数据根；`LlmExposed=false`。

## 运行时证据

临时 Player 完整播放并产生日志链：

```text
admitted: screen_side_arm_raise
CertifiedMotion started: screen_side_arm_raise (2.40s, 1 params)
pose-restored: Param94 (certified-motion-completed)
released: certified-motion-completed
CertifiedMotion cleanup: certified-motion-completed
```

审核包位于隔离目录 `C:\Users\25295\AppData\Local\Temp\fuxuan_certified_motion_screen_side_arm_raise\candidate-review-packet-screen_side_arm_raise.json`，含 7 帧、各帧 SHA-256；审核包 SHA-256 为 `8e5c619745b1a2118cb63aabde93f522bde3b1655ab96ce7ac52f60f9c6aa7f7`。

## 人工审核结论

验收者在可见的隔离桌宠窗口中观看完整连续播放后确认：手臂确实抬起，动作作为轻度抬手是正常的；但抬起幅度不足，不能表达或命名为招手。

因此该曲线只可保留为“轻回应式抬手”的已人工复核候选。它不获得招手语义、生产部署或 LLM 动作授权。若后续需要招手，必须重新选择更明确的节奏与幅度、独立采集时序证据，并进行新的人工审核。

审核结束后，临时 Player 已通过 `@@test:quit` 关闭并记录安全收束；生产数据根未写入。
