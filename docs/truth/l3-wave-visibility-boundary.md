# L3 手部可见性边界底座（2026-09-19）

## 状态

本证据包只建立隔离 Live2DProbe 的**机械主体可见性底座**，不认证招手，不判断手部语义，不修改正式映射，不注册技能，也不向 LLM 暴露参数。`visibility_pass=true` 仅表示扫描帧没有触发整体主体空白或明显主体面积突降；它不表示手部可见、动作自然或可以接入生产。

## 隔离链路

- 驱动：`scripts/test/live2d_visibility_boundary_drive.cjs`。
- 分析器：`scripts/live2d-probe/analyze_visibility_boundary.cjs`。
- 每次运行创建 TEMP 唯一目录并写入 `.test_mode`，只启动独立 `Live2DProbe.exe`；驱动在启动前拒绝已有多个桌宠实例，不管理生产桌宠进程。
- 探针使用模型 RenderTexture PNG、运行时原生参数范围、冻结 writer、基线/逐级扫动/复位帧；PNG 及报告保留 SHA-256。
- 本轮构建：`C:\Users\25295\AppData\Local\Temp\fuxuan_visibility_boundary_probe_20260919\Live2DProbe.exe`，`build.ps1 -ProbeWindow` 成功。
- 本轮输入清单：`C:\Users\25295\AppData\Local\Temp\fuxuan_visibility_boundary_20260919163220978_35356\visibility-boundary-input-manifest.json`。
- 本轮输出报告：`C:\Users\25295\AppData\Local\Temp\fuxuan_visibility_boundary_20260919163220978_35356\visibility-boundary-report.json`。

## 扫描范围与结果

| 扫描 | 运行时范围/缩放 | 帧数 | 最低主体面积比例 | 最大轮廓变化 | 机械分类 |
| --- | --- | ---: | ---: | ---: | --- |
| `Param94` | `[-15, 30]`，12 步往返 | 25 | `1.000000` | `0.052874` | `safe_candidate` |
| `Param97` | 原生 `[-30, 30]`，12 步往返 | 25 | `1.000000` | `0.023771` | `safe_candidate` |
| `Param94 + Param97` | 各自原生范围的 `0.25`，12 步往返 | 75 | `1.000000` | `0.038871` | `safe_candidate` |

三组扫描均未出现空白、整体主体面积突降或复位异常，故机械 `visibility_pass=true`。该结果只说明当前扫描幅度下整体模型仍可见，不能推断手部没有局部遮挡或图层丢失；分析器目前没有手部 landmark、手部框或可靠手部面积分割能力。

## 分类边界

- `safe_candidate`：机械指标未触发明显整体消失/主体突降，仍必须人工观看。
- `needs_human_review`：轮廓变化或主体面积变化较大，不能自动排除局部手部问题。
- `reject_obvious_loss`：整体主体空白或面积明显突降，禁止作为观看候选。

这些分类不是动作质量评分，也不是认证门。用户仍负责判断“手是否自然摆动、是否像招呼、是否友好以及收束是否自然”。本轮没有任何语义升级，保持 `semanticStatus=unassigned`、`mapWriteAllowed=false`、`llmExposed=false`。

## 限制与后续

此前大幅度运行中曾观察到手部消失而手臂/头部仍在动；整体主体面积检测可能无法捕获这种局部现象。因此本底座只能先筛掉明显整体失真，并为后续人工可见窗口观看提供低/中幅度候选。不得把本报告的安全候选直接写入 `fuxuan_map.json`、生产曲线、`CertifiedSkillRegistry` 或 LLM 路由；招手自然度仍由用户完整观看确认。
