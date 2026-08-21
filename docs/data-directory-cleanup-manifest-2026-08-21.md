# D:\DesktopPetData 整理清单（2026-08-21）

## 目的

`D:\DesktopPetData` 是符玄运行时的唯一用户数据根目录。整理只收拢可识别的测评、诊断和旧备份产物，不删除内容，不移动当前运行所需的数据文件。

## 保留在根目录的内容

- `pet_memory.json`、`pet_personality.json`、`pet_preferences.json`、`reminders.json`
- `knowledge_base.json`、`motion_memory.json`、`activity_log.json`
- `validation_log.json`、`quality_log.jsonl`、`usage_log.jsonl`、`tool_benchmark_results.json`
- `ActionRefs/`、`Documents/`、`logs/`
- `dock_state.json`、`inbox.txt`、`task_esp32s3.txt`

这些文件可能包含忆境、人格、用户文档、动作参考或运行诊断数据，本轮不做删除、合并或重命名。

## 仅移动、不删除的历史产物

| 原路径 | 整理后路径 | 类型 |
|---|---|---|
| `_backup_20260814/` | `archive/legacy-backups/_backup_20260814/` | 旧备份 |
| `_test_backup_20260808/` | `archive/legacy-backups/_test_backup_20260808/` | 测试备份 |
| `compare_*` | `archive/measurements/compare_*/` | 本地/云端对照 |
| `quality_*` | `archive/measurements/quality_*/` | 质量测评 |
| `local_architecture_*` | `archive/measurements/local_architecture_*/` | 本地架构测评 |
| `diag_*` | `archive/measurements/diag_*/` | UI/运行诊断 |
| `glm_collages/` | `archive/measurements/glm_collages/` | 动作视觉评测截图 |

## 回退方式

所有移动均在同一数据根目录内完成；将 `archive/` 下的对应目录移回原路径即可恢复旧布局。当前记忆另有独立备份：

- `backups/memory_confirmed_20260821_195440/`
- `backups/memory_review_20260821_195124/`

## 安装/卸载约定

- 安装器先复用已有 `FU_XUAN_DATA`，否则复用已有默认目录 `D:\DesktopPetData`，两者均不存在时才创建。
- 卸载选择“保留”时原地保留数据目录，并保留 `FU_XUAN_DATA` 指向；不会复制或创建第二个活动数据目录。
- 卸载选择“删除”时只删除已记录的专用数据目录；删除失败则保留环境变量入口并提示用户。
