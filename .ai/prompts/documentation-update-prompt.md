# 文档更新提示

当项目状态与文档偏离并且您希望 AI 代理使文档重新同步时使用此提示。

---

您正在更新 `GraphPerceptionPrj` 的文档。请先阅读这些：

1. `AGENTS.md`
2. `.ai/README.md`
3. `.ai/workflows/documentation-workflow.md`

目标：使文档与实际仓库状态重新同步。仓库是真实来源；文档跟随。

步骤：

1. **清单。** 运行：

   ```bash
   find .ai -maxdepth 3 -type f | sort
   find Assets/AGXUnity_Excavator -maxdepth 3 -name "*.md" | sort
   git log --oneline -n 30
   ```

   识别哪些文档可能过时（最旧的，或与最新提交矛盾的）。

2. **通过检查确认。** 对于每个过时的声明，打开源文件或场景相关脚本。不要信任文档与代码不符的说法。

3. **最小化编辑。** 优先手术编辑而非重写。在有用的地方添加"最后更新：YYYY-MM-DD"行。保留 `Assets/AGXUnity_Excavator/Docs/` 下团队文档的原始中文/英文风格。

4. **区分**已确认/推断/计划。在每次编辑中明确标记不确定性，而不是断言。

5. **更新交接线索：**
   - `.ai/handoff/latest-session-summary.md` — 覆盖更改了什么。
   - `.ai/handoff/project-context-compact.md` — 仅当紧凑摘要内容变化时。
   - `.ai/project/known-issues-and-todo.md` — 添加您发现但未修复的新 TODO。

6. **归档**被取代的文档到 `.ai/archive/`。不要删除 — 首先将有用内容提取到新位置，然后将原件归档并在 `.ai/archive/README.md` 中添加条目。

7. **验证**根据 `.ai/workflows/verification-checklist.md`：
   - 未引入 0 字节占位符。
   - 所有链接的文件路径存在。
   - 未编辑 `Assets/AGXUnity/`、`Packages/com.algoryx.*`、`Library/`、`Temp/`、`Logs/`、`UserSettings/`。
   - `git status --short` 看起来合理。

禁忌：

- 不要捏造关于训练结果、性能数字或实验结果的声明。
- 不要将团队文档移出 `Assets/AGXUnity_Excavator/Docs/` — `protocol.md` 和 `terrain_graph_observation.md` 从 C# 源代码和团队 README 中链接。
- 不要创建新的顶级文件夹，除非用户要求。
- 不要仅为风格重新整理团队文档；团队有自己的约定。

交付物：列出更改的文件（带路径）、归档的文件和剩余 TODO 的简短报告。