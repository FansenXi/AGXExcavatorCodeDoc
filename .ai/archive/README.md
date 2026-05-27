# 归档

此文件夹包含**被取代但有价值的**文档。此处不应被视为当前真实来源。请先阅读链接的替换内容；仅在此处获取历史上下文。

## 归档内容

### `graph_perception_handoff/`（归档于 2026-05-25）

最初位于 `knowledges/graph_perception_handoff/`。这四个文件是 Claude Code 定向的交接包，用于建立地形图观察 v0 里程碑（2026-05-24 交付）。

| 归档文件 | 原始目的 | 仍然有用的内容现在的位置 |
|---|---|---|
| `README.md` | 交接包索引 | （无）— 仅指针，参见 `.ai/README.md` |
| `project_context.md` | 项目快照 2026-05-24，跨仓库角色，观察合约回顾 | 被 `.ai/project/overview.md`、`.ai/project/data-collection-pipeline.md`、`.ai/handoff/project-context-compact.md` 取代 |
| `graph_perception_design.md` | 设计说明，将 Liu et al. 2026 (L-GBND) 映射到此项目；推荐的架构、RoI 策略、协议分阶段 | 仍然是*规划*原理的最佳来源。新表面说明总结在 `.ai/project/vision-perception-pipeline.md` 和 `Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md` |
| `claude_code_task_brief.md` | 2026-05-24 会话中执行的任务简报 | 结果捕获在 `.ai/handoff/latest-session-summary.md`（以及 git 历史中的先前会话摘要）。实现位于 `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/GraphPerception/` |

**为什么归档而不是删除：** 设计原理（特别是 `graph_perception_design.md` 中的 Liu et al. 映射和分阶段协议集成计划）是*为什么* v0 看起来像这样的最清晰阐述。未来关于速度历史、学习 RoI 或在线图传输的工作应在提议替代方案之前阅读这些说明。

这些文档引用的外部参考材料（`knowledges/Liu et al. - 2026 - Localized Graph-Based Neural Dynamics Models for Terrain Manipulation.pdf`、`knowledges/图感知参考.png`）未归档 — 它们保留在 `knowledges/` 下的原始路径，已 gitignore（`*.pdf`、`*.png`）。

## 何时归档新文档

1. 文档被 `.ai/project/` 或 `Assets/AGXUnity_Excavator/Docs/` 下的条目取代。
2. 原件仍包含值得保留的原理或历史。
3. 仍然有用的内容的迁移已经完成。

然后：

- 将文档移到 `.ai/archive/<original-or-kebab-case-name>.md` 或主题子文件夹下（如 `graph_perception_handoff/`）。
- 在上表中添加一行：归档文件、原始目的、实时内容现在的位置。
- 更新 `.ai/handoff/latest-session-summary.md`。

## 何时不归档

- 文档是团队维护的真实来源，当前从 C# 源代码或 `AGENTS.md` 引用。（就地编辑。）
- 文档确实是 0 字节占位符。（直接删除。）