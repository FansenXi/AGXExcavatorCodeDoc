# 文档工作流

## 文档位置

| 受众 | 位置 |
|---|---|
| 人类/团队 | `Assets/AGXUnity_Excavator/Docs/*.md`、`Assets/AGXUnity_Excavator/README.md`、根目录 `README.md` |
| AI 代理（Claude Code / Codex / Gemini / Copilot） | `.ai/**` |
| AI 代理操作规则 | `AGENTS.md`（根目录） |
| 历史/已过时文档 | `.ai/archive/**` |

## 新文档放在哪里

- **面向团队的运行场景行为描述** — `Assets/AGXUnity_Excavator/Docs/`。（示例：`scene.md`、`protocol.md`、`terrain_graph_observation.md`。）
- **为未来 AI 会话描述项目状态** — `.ai/project/`。简洁，链接到真实来源而不是重复。
- **可复用的 AI 提示** — `.ai/prompts/`。
- **"本次会话更改了什么"说明** — `.ai/handoff/latest-session-summary.md`（每次会话覆盖）和相关的 `.ai/project/*.md`。
- **下次会话的紧凑交接** — `.ai/handoff/project-context-compact.md`（当项目上下文变化时覆盖）。
- **已过时但可能仍有用的旧文档** — `.ai/archive/`，并在 `.ai/archive/README.md` 中添加说明。

## 禁忌

- 不要在 `.ai/` 和 `Assets/AGXUnity_Excavator/Docs/` 之间重复大块内容。团队文档是运行时行为的真实来源；`.ai/` 应该链接到它们，而不是镜像它们。
- 不要编写推测性文档。将任何无法从当前仓库直接验证的内容标记为 `planned` / `inferred` / `unknown`。
- 不要创建空的填充文档来满足文件夹模板。如果没有具体内容可说，则省略该主题。
- 不要将团队文档移出 `Assets/AGXUnity_Excavator/Docs/` — `protocol.md` 和 `terrain_graph_observation.md` 从 C# 源代码注释和团队 README 中引用。

## 当您发现团队文档过时时

1. 根据实际仓库状态（代码、场景、manifest）确认过时。
2. 编辑团队文档以匹配 — 这是规范修复。在顶部附近添加"最后更新：YYYY-MM-DD"行。
3. 如果无法编辑（例如广泛的跨领域更改），在 `.ai/project/known-issues-and-todo.md` 中添加说明标记过时。

## 当您归档文档时

1. 将其移到 `.ai/archive/` 下（保留原始文件名或 kebab-case 版本）。
2. 在 `.ai/archive/README.md` 中添加条目，包括：原始路径、归档路径、日期、原因，以及仍然有用的内容迁移到了哪里。
3. 除非是真正的重复空文件，否则不要删除原件。

## 风格

- 在 `.ai/` 中使用 kebab-case Markdown 文件名（例如 `repository-map.md`）。`Assets/AGXUnity_Excavator/Docs/` 下的团队文档遵循团队的 snake_case 约定；不要重命名它们。
- 保持 `.ai/` 文档可扫描：短小节、合适的表格、链接到详细信息。
- 明确区分"已确认"与"推断"/"计划"。
- 使用与受众一致的语言：代码标识符保持原样；中文描述在团队文档中是可以的（团队 README 和几个 `Docs/*.md` 是双语/中文）。

## 文档更改后的验证

运行 [`.ai/workflows/verification-checklist.md`](verification-checklist.md)。至少：

- 所有链接的文件路径存在（或标记为计划/缺失）。
- 所有文档间链接可解析。
- `.ai/README.md` 列出了您添加/删除的每个文件。
- `AGENTS.md` 仍然链接到正确的入口点。