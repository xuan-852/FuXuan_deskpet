# Live2D 探测评测准确性指导文档

> **状态**：已批准。
>
> **关联**：[通用探测器](generic-live2d-capability-probe.md)。

## 目标与非目标

**FR-ACC-01**：评测“探测结论是否可靠”，而不是让单次视觉判断替代事实。

非目标：不承诺在无人类语义标签的情况下得到 100% 身体部位命名准确率。

## 四层准确性门槛

1. **机械重复性**：同一参数基线/最小/中间/最大/复位至少重复 3 次；写入、读回、范围和复位必须一致。
2. **视觉稳定性**：每次序列的局部差异掩码、变化方向和影响面积须在容差内一致；不稳定即降级。
3. **已知控制样本**：每批包含明确可见的正控制、无效/隐藏参数负控制、以及复位控制；检测器无法区分正负控制则该批无效。
4. **独立仲裁**：当前探索批次按用户决定仅调用 DeepSeek V4 主评；GLM 因持续 429 限流不再作为本批前置条件。DeepSeek 单模型结果只能升为“探索语义候选”，不能升为 `Certified` 或自动写入映射；后续可在服务可用时补 GLM 交叉判。

## 分类规则

### 本地初筛阈值（仅排序，不作语义认证）

- `peakMeanPixelDifference = max(minMeanDifference, maxMeanDifference)`；像素指标是 0–255 RGB 通道平均绝对差。
- `>= 1.0` 标记 `visual-review-candidate`，`[0.25, 1.0)` 标记 `weak-visual-candidate`，`< 0.25` 标记 `no-local-visible-evidence`。
- 传统 `ParamBodyAngleX/Y/Z` 在冻结非渲染写入者时落入近零，固定标记 `requires-physics-recheck`，不能据此归类为 `Unsupported`。
- `resetStable=false` 必须标记 `unreliable-reset`。所有本地初筛结果的 `semanticStatus=unassigned`、`mapWriteAllowed=false`；只有后续独立视觉复核和语义/自然度门禁都通过，才可进入正式分类流程。
- 全局像素差较低不代表没有局部可见效果：例如手腕装饰开关可被视觉模型识别而落在近零像素档。因此本地近零项在有隔离帧和复位证据时仍可进入 DeepSeek 探索复核；视觉结果若为局部效果，则标为 `EffectOnly`，不进入人体骨架候选池。

### 云端复核缓存与限流

- `cloud_review.cjs` 默认缓存每个“模型 + 提示 + 帧哈希”结果，包含 429 等错误，避免自动重试造成额外外发或费用。
- 只有人工已经授权外发、并显式设置 `FU_XUAN_CLOUD_RETRY_ERRORS=1` 时，才允许重试缓存的失败项；必须逐项退避，不得批量轰击限流服务。
- `aggregate_cloud_reviews.cjs` 是 UTF-8 汇总入口，兼容 GLM 的 Markdown JSON 围栏；原始帧、单项响应和汇总都保留在隔离数据根，不能写入正式映射。
- 全参数 DeepSeek 探索使用 `batch_deepseek_review.cjs <隔离根> [start] [limit]` 分批执行，默认 20 项；每批记录下一索引并复用单项缓存，确保可恢复且不重复发送已成功的帧。

- `Certified` 必须四层通过，并由后续自然度认证确认。
- `Supporting` 机械/视觉通过但语义或组合尚不足。
- `Conditional` 受前置姿态、物理或其他参数影响。
- `Unreliable` 重复性、复位或视觉稳定性失败。
- `EffectOnly` 仅效果变化，不进入人体骨架。
- `Unsupported` 不可写、不可读或无可重复影响。

## 验收

| ID | 标准 |
|---|---|
| AC-ACC-01 | 每批报告含模型/适配器/映射哈希、控制样本、三次机械轨迹与差异指标 |
| AC-ACC-02 | 正负控制均被正确区分，否则整批结果无效 |
| AC-ACC-03 | 云端来源、预算、提示版本和图像哈希可追溯；探索批次允许仅 DeepSeek，但不得据此认证或写回映射 |
| AC-ACC-04 | 无证据参数不进入正式映射或运行时技能库 |
| AC-ACC-05 | `summarize_local.cjs` 对隔离报告产出候选清单，且所有条目保持 `semanticStatus=unassigned` 与 `mapWriteAllowed=false` |

## 实施授权边界

允许新增隔离控制样本、指标和证据报告；禁止因一次模型评价自动认证或写回映射。
