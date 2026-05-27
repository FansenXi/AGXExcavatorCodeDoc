# Claude Code 继续提示

将此复制到新的 Claude Code 会话中以恢复此仓库的工作。

---

您正在继续 `GraphPerceptionPrj` 的工作，这是一个 Unity 2022.3.62f3 + AGX Dynamics 挖掘机仿真项目，通过端口 5057 上的 TCP 二进制 step-ack 协议向 Python 提供观察。

在做任何其他事情之前，请按顺序阅读：

1. `AGENTS.md` — 根 AI 代理规则。
2. `.ai/README.md` — AI 文档地图。
3. `.ai/handoff/project-context-compact.md` — 紧凑项目上下文。
4. `.ai/project/overview.md` — 已确认的子系统和场景。
5. `.ai/project/known-issues-and-todo.md` — 当前开放的内容。
6. `Assets/AGXUnity_Excavator/Docs/protocol.md` — 跨仓库 step-ack 合约（不要在没有协调的情况下破坏）。
7. `Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md` — 地形图 v0 模式。

阅读后，用 5 个要点总结您认为当前状态是什么以及下一个明显的下一步是什么。然后问我选择哪个。**不要在第一个响应中编辑文件。**

硬约束（每次会话重新阅读）：

- 不要修改 `Assets/AGXUnity/`（第三方 AGX 核心，已 gitignore）。
- 不要修改 `Packages/com.algoryx.*`（固定的上游包）。
- 不要接触 `Library/`、`Temp/`、`Logs/`、`UserSettings/`、`*.csproj`、`*.sln`（Unity 生成，已 gitignore）。
- 不要在没有更新 `AgxSimProtocolConstants.ProtocolVersion` 并与 Repo A 和 Repo C 协调的情况下重新排序 `env_state`、`qpos`、`qvel` 或 `STEP_RESP` 负载布局。
- 在 `Docs/terrain_graph_observation.md` §11 中的 GET_INFO 能力握手计划达成一致之前，不要将图负载拼接到 `STEP_RESP` 中。
- 活跃场景：恰好两个 — `Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity`（简单，集成目标）和 `Assets/AGXUnity_Excavator/Open-pit mine.unity`。不要重新创建 `Assets/Scenes/SampleScene.unity` 或任何 `*_measurements.unity` 变体。

风格：

- 优先使用最小、有组织的编辑，范围限定在任务内。
- 在您编写的任何文档中区分已确认/推断/计划。
- 在 `.ai/` 下使用 kebab-case 文件名；`Assets/AGXUnity_Excavator/Docs/` 下的团队文档保持 snake_case。
- 不要在没有明确同意的情况下运行破坏性 git 操作（`reset --hard`、`push --force`、分支删除）。

会话结束时，更新：

- `.ai/handoff/latest-session-summary.md` — 您更改了什么。
- 相关的 `.ai/project/*.md` 文件 — 当前状态。
- `.ai/project/known-issues-and-todo.md` — 发现的任何新 TODO。