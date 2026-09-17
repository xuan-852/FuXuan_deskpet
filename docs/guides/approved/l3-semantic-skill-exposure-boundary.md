# L3 认证技能的 AI 暴露边界

> **状态**：已批准；上位决策见 `docs/decisions/2026-09-15-product-direction-baseline.md` §4。

## 目标与非目标

**FR-L3-05**：将“已获运行时认证”和“允许模型经 `request_body_skill` 请求”拆为两个显式、可审计的白名单决定。

非目标：不新增技能、不修改参数映射或模型资源、不开放原始 Live2D 参数、不启用自主动作。

## 接口与约束

- `CertifiedMotionLibrary.Entry` 必须声明 `LlmExposed`；默认值为 `false`。
- 只有 `LlmExposed=true` 的条目可出现在 `ChatManager` 提示和 `RequestBodySkillTool` 描述中。
- 工具执行时必须再次拒绝未暴露技能，不能仅依赖模型提示。
- 既有已暴露条目必须显式标记 `true`，以保持现有行为；未来内部候选在获得单独产品授权前保持 `false`。
- 任何拒绝均不降级为原始参数写入，且不产生云端调用或生产数据写入。

## 验收

- **AC-L3-07**：默认未标记条目不可见、不可通过工具执行；显式公开的既有条目仍可见、可准入。
- 运行 `build.ps1 -Quick`、`build.ps1 -RunTests` 和文档索引生成；代码事实仅在测试通过后记录。

## 范围

允许：认证库、身体技能工具、聊天提示、对应 EditMode 测试、真相/模块文档与任务包。

禁止：`fuxuan_map.json`、Live2D 资源、任何运行时云端调用、原始参数或关键帧接口、主动动作调度。
