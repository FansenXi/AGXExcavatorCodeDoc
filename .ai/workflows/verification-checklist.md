# 验证清单

在任何文档迁移或非平凡代码更改后运行这些。

## A. Markdown / 文档健全性

```bash
# 每个 .ai/ 文件应该是跟踪友好的（没有 0 字节占位符留下）。
find .ai -type f -name "*.md" -size 0

# 树中列出的所有 .ai/ 文档。
find .ai -maxdepth 3 -type f | sort

# 内部链接完整性（粗略 — 标记到 .md/.cs 文件的损坏相对链接）。
grep -REho '\]\(([^)]+\.md|[^)]+\.cs)\)' .ai AGENTS.md README.md 2>/dev/null \
  | sed -E 's/.*\(([^)]+)\)/\1/' | sort -u
```

对于 grep 打印的每个路径，目视检查它是否存在。（尚无 CI。）

## B. 路径完整性 vs 实际仓库

```bash
# 确认两个活跃场景存在，没有多余的。
find Assets -name "*.unity" -not -path "*/Library/*"

# 确认图感知 v0 源文件就位。
ls Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/GraphPerception/

# 确认团队文档仍在本仓库文档所说的位置。
ls Assets/AGXUnity_Excavator/Docs/

# 确认空的 .ai/ 占位符已消失。
ls .ai/
```

## C. Git 卫生

```bash
git status --short
git diff --stat
```

- 没有意外跟踪的 `mono_crash.mem.*.blob`、`*.lfx*`、`*.lic`、`*.license`、`Library/`、`Logs/`、`UserSettings/`、`ExperimentLogs/`、`TerrainGraphSnapshots/`。
- `*.csproj` / `*.sln` 不应出现在 `git status` 中。它们被 gitignore 但 Unity 不断重新生成它们。

## D. 未接触 Unity 生成或 AGX 包文件

```bash
# 应该不打印 Assets/AGXUnity/ 下的文件。
git status --short Assets/AGXUnity/ 2>/dev/null

# 应该不打印 Packages/com.algoryx.* 下的文件（那些是固定的包）。
git status --short Packages/ 2>/dev/null | grep -v manifest.json
```

## E. AGENTS.md / .ai/README.md 交叉引用

```bash
# AGENTS.md 应该引用 .ai/README.md 和至少 project-context compact。
grep -nE "README\.md|project-context-compact\.md" AGENTS.md
```

## F. 仅 Editor 检查（当 Unity 可用时）

参见 [unity-editor-checklist.md](unity-editor-checklist.md)。

这些无法从 CLI 运行：

- 编译项目（Unity 控制台必须干净）。
- 打开 `AGXUnity_Excavator.unity` 并进入播放模式。
- 触发地形图导出（默认 Alpha9）并确认 `TerrainGraphSnapshots/` 中有新的 JSON。
- 可选择对二进制 step-ack 服务器运行 Repo A 烟雾测试。

## G. 文档更改后快速扫描

当您只接触 `.ai/` 时：

- 从头到尾重新阅读 `.ai/README.md`；每个链接的文件必须存在。
- 重新阅读 `AGENTS.md`；每个部分仍然相关。
- 更新 `.ai/handoff/latest-session-summary.md`，以便下一个 AI 会话看到更改了什么。