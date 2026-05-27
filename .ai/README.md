# `.ai/` — AI 文档地图

此文件夹是 `GraphPerceptionPrj` 的 **面向 AI 的知识库**。面向人类的团队文档位于其他位置（参见 [团队文档](#团队文档)）。

## 从这里开始

如果您是 Claude Code / Codex / Gemini / Copilot 会话，开始在此仓库中工作：

1. **阅读** 仓库根目录的 [`AGENTS.md`](../AGENTS.md) — 操作规则。
2. **阅读** [`handoff/project-context-compact.md`](handoff/project-context-compact.md) — 单页项目快照。
3. **阅读** [`project/overview.md`](project/overview.md) — 已确认的子系统和不存在的内容。
4. **阅读** [`project/known-issues-and-todo.md`](project/known-issues-and-todo.md) — 待处理事项，包括文档/代码漂移。
5. 然后：
   - 对于继续工作 — 将 [`prompts/claude-code-continuation-prompt.md`](prompts/claude-code-continuation-prompt.md) 复制到您的会话中。
   - 对于仅检查审查 — 将 [`prompts/codex-review-prompt.md`](prompts/codex-review-prompt.md) 复制到您的会话中。

## 文件夹地图

```text
.ai/
├── README.md                       ← 您在这里
├── project/                        对代码库的稳定理解
│   ├── overview.md                 已确认的子系统，不存在列表，推断/计划
│   ├── repository-map.md           顶层文件夹，脚本子树，gitignore 边界
│   ├── setup-and-environment.md    Unity 版本，AGX，包，端口，首次打开步骤
│   ├── unity-agx-integration.md    项目 ↔ AGX 接触点，颗粒粒子协议
│   ├── scenes-assets-prefabs.md    两个活跃场景，预制体清单，连接图
│   ├── excavator-control-teleoperation.md   动作表面，操作员来源，驱动
│   ├── vision-perception-pipeline.md         FPV RGB，地形图 v0，不存在的内容
│   ├── data-collection-pipeline.md           step-ack 协议，ACT 桥接，不存在的内容
│   ├── rl-mlagents-notes.md         "ML-Agents 未使用" + 规划说明
│   ├── act-imitation-learning-notes.md       ACT 桥接代码，观测一致性，禁忌
│   └── known-issues-and-todo.md     漂移，差距，下一步调查
├── workflows/                      可重复的程序
│   ├── development-workflow.md     每日循环，按层编辑策略
│   ├── documentation-workflow.md   新文档放在哪里，何时归档
│   ├── verification-checklist.md   CLI 检查（markdown，路径，git，AGX/Library 未动）
│   └── unity-editor-checklist.md   Editor 内手动烟雾测试
├── prompts/                        可复制的 AI 提示
│   ├── claude-code-continuation-prompt.md
│   ├── claude-code-project-review-prompt.md  项目检查与代码整理
│   ├── codex-review-prompt.md      仅审查，"不要编辑文件"
│   ├── depth-map-perception-planning-prompt.md  深度图感知规划
│   ├── unity-agx-debug-prompt.md
│   └── documentation-update-prompt.md
├── handoff/                        每次会话的紧凑摘要
│   ├── project-context-compact.md  会话开始时阅读的简短摘要
│   ├── latest-session-summary.md   每次会话覆盖 — 更改了什么
│   ├── handoff-template.md         交接文档模板
│   └── quick-reference.md          交接快速参考
└── archive/                        已过时但有价值的文档
    ├── README.md                   索引 + 原因
    └── graph_perception_handoff/   2026-05-25 从 knowledges/ 移动
```

## 团队文档

由团队维护的真实来源文档（不要移动；某些文档从 C# 源代码注释中链接）：

- [`Assets/AGXUnity_Excavator/README.md`](../Assets/AGXUnity_Excavator/README.md) — 子系统级概述，控制映射，重置说明。
- [`Assets/AGXUnity_Excavator/Docs/scene.md`](../Assets/AGXUnity_Excavator/Docs/scene.md) — V0 场景和任务合约（英文真实来源；中文镜像在 `scene.zh-CN.md`）。
- [`Assets/AGXUnity_Excavator/Docs/scene_report.md`](../Assets/AGXUnity_Excavator/Docs/scene_report.md) — 2026-03-25 进度说明（可能略过时；参见 `project/known-issues-and-todo.md`）。
- [`Assets/AGXUnity_Excavator/Docs/protocol.md`](../Assets/AGXUnity_Excavator/Docs/protocol.md) — step-ack 二进制协议真实来源。
- [`Assets/AGXUnity_Excavator/Docs/excavator_current_project_structure.md`](../Assets/AGXUnity_Excavator/Docs/excavator_current_project_structure.md) — 项目结构（中文）。
- [`Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md`](../Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md) — 图感知 v0 模式。
- [`Assets/AGXUnity_Excavator/Docs/patches/README.md`](../Assets/AGXUnity_Excavator/Docs/patches/README.md) — 团队应用的 AGX 核心补丁。

## 约定

- `.ai/` 中的文件名是 **kebab-case** Markdown。
- 团队文档保持其现有的 **snake_case** 文件名 — 不要重命名。
- 在每个文档中区分**已确认**、**推断**和**计划**。
- 不要捏造关于训练结果、性能或实验结果的声明 — 这些在 Repo A 中。
- 自由编辑 `.ai/`；AGENTS.md 管理您可以接触的非文档路径。