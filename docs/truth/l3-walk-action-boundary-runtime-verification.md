# L3 步行与认证动作边界：运行时验证

本轮验证使用 `FU_XUAN_DATA` 指向隔离临时目录，不读取或写入生产记忆。步行基线驱动在桌宠落地后启动向右行走，连续采集 17 帧模型截图，随后收到停止行走指令并正常退出。

针对 `external_Hiyori_Hiyori_m06`，先确认测试状态为 `velocity=(1,0)`，再请求认证动作。运行时返回“当前未处于稳定静止状态，动作已拒绝”，日志没有 `CertifiedMotion started`。这证明认证动作不会在行走中叠加播放。此前的静止正向复验已证明 `velocity=(0,0)` 后可正常开始、完成、恢复姿态并释放资源。

验证脚本：`scripts/test/walk_baseline_drive.cjs` 与 `scripts/test/certified_motion_walk_rejection_drive.cjs`。边界规则：移动中拒绝；用户或系统停止并确认静止后才可请求认证动作；动作完成后由现有移动锁释放机制恢复后续步行能力。
