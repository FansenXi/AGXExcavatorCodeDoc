# AGENTS.md

在 `GraphPerceptionPrj` 中工作的 AI 代理（Claude Code、Codex、Gemini、Copilot）的稳定操作规则。每次会话请先阅读此文件。

详细的上下文、历史和操作指南位于 [`.ai/`](.ai/README.md)。此文件故意保持简短。

## 项目范围

- Unity 2022.3.62f3 + AGX Dynamics 挖掘机仿真。
- 角色：三仓系统中的"Repo B"。通过 **端口 5057** 上的 TCP 二进制 step-ack 协议向外部 Python（Repo A）提供观测数据。
- 活跃场景：
  - `Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity` — 简单场景，主要集成目标（挖掘机 + 沙池 + 卡车）。
  - `Assets/AGXUnity_Excavator/Open-pit mine.unity` — 露天矿场景，尚未连接 step-ack 或图感知。
- 动作表面（v0 中不可变）：`[swing, boom, stick, bucket]` 速度命令。驱动/转向/履带不在合约中。

完整的子系统列表请参阅 [`.ai/project/overview.md`](.ai/project/overview.md)。

## 仓库规则

- **可编辑** — `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/`、`Assets/AGXUnity_Excavator/Docs/`（团队维护）、`Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/{Prefabs,Physics,Terrains,Profiles,materials,models}/` 通过 Unity Editor、`.ai/`、根目录 `AGENTS.md` / `README.md`。
- **仅通过 Unity 工具可编辑** — `Packages/manifest.json`、`ProjectSettings/*`、`*.unity`、`*.prefab`、`*.asset`。
- **不要修改** — 参见 [不要修改](#不要修改)。

## Unity / AGX 安全规则

- 不要重新排序 `STEP_RESP`、`qpos`、`qvel` 或 `env_state` 中的字段，除非升级 `AgxSimProtocolConstants.ProtocolVersion` *并*与 Repo A / Repo C 协调。字段顺序记录在 [`Assets/AGXUnity_Excavator/Docs/protocol.md`](Assets/AGXUnity_Excavator/Docs/protocol.md) 中。
- 在 [`Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md`](Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md) §11 中的分阶段计划达成外部共识之前，不要将图数据负载拼接到 `STEP_RESP` 中。
- 不要重命名或移动序列化字段而不使用 `[FormerlySerializedAs]`（场景/预制体值按名称引用）。
- AGX 颗粒粒子：每个 `particles.at(i)` 必须与 `p.ReturnToPool()` 配对 — 参见 [`unity-agx-integration.md`](.ai/project/unity-agx-integration.md)。
- `SceneResetService` 快照捕获是经过精心调优的（Awake 时捕获，FixedUpdate 作为后备，Transform 链 + 刚体快照，AGX 预热步骤后重新应用）。在阅读 [`Assets/AGXUnity_Excavator/README.md`](Assets/AGXUnity_Excavator/README.md) 之前，不要更改捕获时机。

## 文档规则

- 面向 AI 的文档 → `.ai/`。团队文档 → `Assets/AGXUnity_Excavator/Docs/`。
- `.ai/` 使用 kebab-case 文件名；团队文档保持其 snake_case 文件名。
- 在每个声明中区分**已确认**、**推断**和**计划**。
- 不要捏造训练指标、性能数字或实验结果 — 这些在 Repo A 中。
- 将过时的文档归档到 `.ai/archive/`，并在 [`.ai/archive/README.md`](.ai/archive/README.md) 中添加条目。除非文件是真正的空占位符，否则不要删除。
- 完整指南：[`.ai/workflows/documentation-workflow.md`](.ai/workflows/documentation-workflow.md)。

## 验证规则

- 任何更改后，运行 [`.ai/workflows/verification-checklist.md`](.ai/workflows/verification-checklist.md)：
  - Markdown 链接/路径完整性检查。
  - 无 0 字节占位符。
  - `git status --short` 仅显示预期的更改。
  - 未编辑 `Assets/AGXUnity/`、`Packages/com.algoryx.*`、`Library/`、`Temp/`、`Logs/`、`UserSettings/`。
- 对于场景/代码更改，还需在 Unity Editor 中运行 [`.ai/workflows/unity-editor-checklist.md`](.ai/workflows/unity-editor-checklist.md)（无法仅从 CLI 完成）。
- 如果检查失败：停止并报告失败。不要通过删除失败组件来"修复"。

## 首选 AI 工作流

1. **先检查。** 阅读 `AGENTS.md` → [`.ai/handoff/project-context-compact.md`](.ai/handoff/project-context-compact.md) → [`.ai/project/overview.md`](.ai/project/overview.md) → [`.ai/project/known-issues-and-todo.md`](.ai/project/known-issues-and-todo.md)，然后再接触任何文件。
2. **计划。** 说明假设，区分已确认与推断。对于非平凡的更改，在编辑前提出方法。
3. **最小化编辑。** 将编辑范围限定在当前任务内。不要"顺便"重构相邻代码。
4. **验证。** 运行验证清单；记录结果。
5. **交接。** 更新 [`.ai/handoff/latest-session-summary.md`](.ai/handoff/latest-session-summary.md) 和任何受影响的 `.ai/project/*.md` 文件。

可复制的提示：[`.ai/prompts/`](.ai/prompts)。

## 关键文档入口点

- [`.ai/roadmap/architecture-roadmap-2026-2029.md`](.ai/roadmap/architecture-roadmap-2026-2029.md) — **2026-2029 PhD 架构主线（AI 代理必读，含执行协议）**。
- [`.ai/README.md`](.ai/README.md) — AI 文档地图。
- [`.ai/handoff/project-context-compact.md`](.ai/handoff/project-context-compact.md) — 单页项目快照。
- [`.ai/handoff/latest-session-summary.md`](.ai/handoff/latest-session-summary.md) — 上次会话进度。
- [`.ai/project/overview.md`](.ai/project/overview.md) — 已确认的子系统。
- [`Assets/AGXUnity_Excavator/Docs/protocol.md`](Assets/AGXUnity_Excavator/Docs/protocol.md) — step-ack 合约（Track 2 收尾后归档）。
- [`Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md`](Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md) — 图感知 v0 模式。
- [`Assets/AGXUnity_Excavator/README.md`](Assets/AGXUnity_Excavator/README.md) — 团队维护的子系统概述。

## AI 代理工作前必读

**任何 AI 代理（Claude Code / Codex / Gemini / Copilot）在本仓库动手前**，必须先读 [`.ai/roadmap/architecture-roadmap-2026-2029.md`](.ai/roadmap/architecture-roadmap-2026-2029.md) 中的"Agent 执行协议"节。该节定义了：
- 阅读顺序
- 严格 Track 顺序（不跳级）
- 任务三段式（Pre-condition / Action / Acceptance）
- 偏离方案的合法路径

不读 = 不要动手。

## 不要修改

- `Assets/AGXUnity/` — 第三方 AGX 核心（已 gitignore）。
- `Packages/com.algoryx.*` — 固定的上游包。
- `Library/`、`Temp/`、`Logs/`、`UserSettings/`、`Build/`、`Builds/`、`Obj/` — Unity 生成，已 gitignore。
- `*.csproj`、`*.sln`、`.vscode/`、`.idea/` — IDE 生成，已 gitignore。
- AGX 许可证：`*.lfx*`、`*.lic`、`*.license`。
- 生成的产物：`ExperimentLogs/`、`TerrainGraphSnapshots/`、`knowledges/*.{pdf,png,svg}`、`mono_crash.mem.*.blob`。

## 如何交接

### 何时更新交接文档

- 切换到另一个 agent 时（mimo → Claude Code → Codex）
- 完成重要工作后
- 遇到阻塞或需要帮助时

### 交接流程

1. **更新交接文档**：编辑 [`.ai/handoff/latest-session-summary.md`](.ai/handoff/latest-session-summary.md)
   - 使用模板：[`.ai/handoff/handoff-template.md`](.ai/handoff/handoff-template.md)
   - 包含：当前状态、已完成工作、未完成工作、下一步建议

2. **通知下一个 agent**：告诉它读取交接文档
   ```
   请阅读 .ai/handoff/latest-session-summary.md 继续工作
   ```

3. **下一个 agent 启动时**：
   - 先读 `AGENTS.md`（本文件）
   - 再读 `.ai/handoff/latest-session-summary.md`
   - 根据交接内容继续工作

### 交接文档规则

- **保持简洁**：只写关键信息，不要冗长
- **及时更新**：不要等到会话结束才写
- **使用模板**：遵循 `.ai/handoff/handoff-template.md` 格式
- **状态清晰**：明确标记"进行中/已完成/阻塞"

### 跨 agent 协作

当多个 agent 协作时：

```
Agent A 完成工作 → 更新交接文档 → Agent B 读取交接文档继续
```

交接文档是 agent 之间的**共享通信通道**，所有 agent 读写同一个文件。

### 关键文档

- [`.ai/handoff/latest-session-summary.md`](.ai/handoff/latest-session-summary.md) — 当前交接状态
- [`.ai/handoff/project-context-compact.md`](.ai/handoff/project-context-compact.md) — 项目上下文
- [`.ai/handoff/handoff-template.md`](.ai/handoff/handoff-template.md) — 交接模板
- [`.ai/handoff/quick-reference.md`](.ai/handoff/quick-reference.md) — 交接快速参考
- [`.ai/prompts/claude-code-continuation-prompt.md`](.ai/prompts/claude-code-continuation-prompt.md) — 继续提示
- [`.ai/prompts/claude-code-project-review-prompt.md`](.ai/prompts/claude-code-project-review-prompt.md) — 项目检查与代码整理
- [`.ai/prompts/depth-map-perception-planning-prompt.md`](.ai/prompts/depth-map-perception-planning-prompt.md) — 深度图感知规划