# 图感知交接文档

这个文件夹是一组面向后续 Claude Code / Codex 协同开发的上下文包，用来支持在当前 Unity AGX 挖掘机项目中加入图感知能力。

建议阅读顺序：

1. `project_context.md` - 当前项目目标、架构、活跃场景、数据契约和重要文件。
2. `graph_perception_design.md` - Liu et al. 2026 L-GBND 论文如何映射到本项目，以及下一步应该构建什么。
3. `claude_code_task_brief.md` - 下一轮编码任务的具体拆分、约束和验收检查。

`knowledges/` 中已有的本地参考材料：

- `Liu et al. - 2026 - Localized Graph-Based Neural Dynamics Models for Terrain Manipulation.pdf`
- `图感知参考.png`

重要提示：当前 `.gitignore` 会忽略 `*.pdf`、`*.png` 和 `*.svg`，因此这些参考资料可能只存在于本地。若希望纳入版本管理，需要有意识地 `git add -f` 或调整忽略规则。
