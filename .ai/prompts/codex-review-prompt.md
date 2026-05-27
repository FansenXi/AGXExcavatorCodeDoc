# Codex 仅审查提示

当您希望 AI 代理（Codex、Claude Code、Gemini 等）检查仓库并产生发现而不修改它时使用此提示。

---

您正在审查 `GraphPerceptionPrj`，一个 Unity 2022.3.62f3 + AGX Dynamics 挖掘机仿真。**不要编辑文件。**

在形成意见之前按此顺序阅读：

1. `AGENTS.md`
2. `.ai/README.md`
3. `.ai/handoff/project-context-compact.md`
4. `.ai/project/overview.md`、`.ai/project/repository-map.md`
5. `Assets/AGXUnity_Excavator/Docs/protocol.md`
6. `Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md`
7. `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/`（所有 .cs）
8. `git log --oneline -n 20`

产生包含以下部分的报告：

1. **我验证了什么** — 列出通过直接检查确认的事实，包括文件路径和行号。
2. **我无法验证什么** — 列出文档中您无法从当前代码/场景确认的声明，以及原因。
3. **风险/不一致** — 文档和代码之间的具体差异，特别是在 step-ack 合约、`env_state` 排序、场景连接和图感知模式方面。
4. **代码气味或正确性问题** — 限定在 `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/` 下的项目自身脚本。不要审查 `Assets/AGXUnity/` 或任何 AGX 上游包。
5. **建议的下一步调查** — 按影响排序。不要自动修复。

约束：

- **在此过程中不要修改任何文件。**
- 不要打开 Unity Editor 或运行 AGX 仿真。
- 不要对端口 5057 或任何其他端口运行任何网络命令。
- 不要对训练指标、性能或实验结果发表声明 — 这些在 Repo A 中。
- 如果您发现值得跟踪的 TODO，在报告中提议将其添加到 `.ai/project/known-issues-and-todo.md`（但不要写入编辑）。

以 Markdown 形式交付报告，引用的文件路径使用 [`path`](path) 链接形式。