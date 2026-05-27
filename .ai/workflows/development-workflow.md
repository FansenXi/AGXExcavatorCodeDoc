# 开发工作流

## 每日循环

1. **拉取**，然后检查 `git status` 是否有未跟踪的 AGX 许可证文件或未提交的 IDE 项目（两者都应被忽略，但要验证）。
2. **打开 Unity 2022.3.62f3。** 让它从 Algoryx 作用域注册表（`com.algoryx.agxunity.machines.*`）解析包。拉取后的首次导入可能需要几分钟。
3. **检查控制台。** 这里的错误通常意味着：AGX 许可证缺失、`Assets/AGXUnity/` 核心未填充，或脚本在意外的命名空间中。
4. **打开简单场景：** `Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity`。露天矿场景存在但不是集成目标。

## 按层编辑策略

| 层级 | 可编辑？ |
|---|---|
| `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/` | 是 — 项目代码 |
| `Assets/AGXUnity_Excavator/Docs/` | 是 — 团队维护的真实文档 |
| `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Prefabs/` | 是 — 通过 Unity Editor |
| `Packages/manifest.json` | 是 — 仅通过 Unity Package Manager |
| `ProjectSettings/*` | 是 — 仅通过 Unity Editor，除了微小的标量编辑 |
| `Assets/AGXUnity/` | 否 — 第三方，反正已 gitignore |
| `Packages/com.algoryx.*` | 否 — 固定的上游包 |
| `.csproj` / `.sln` | 否 — IDE 生成，已 gitignore |
| `Library/`、`Temp/`、`Logs/`、`UserSettings/` | 否 — Unity 生成，已 gitignore |

## 安全编辑规则

- 除了手术修复（例如在受控脚本重命名后重新指向单个 GUID），永远不要手动编辑 `.unity` 或 `.prefab` YAML。
- 永远不要提交 `mono_crash.mem.*.blob`、AGX 许可证文件或 `ExperimentLogs/` / `TerrainGraphSnapshots/` 中的任何内容。
- 不要在没有检查哪些场景/预制体通过 GUID 引用它们的情况下重命名或移动脚本（脚本 GUID 位于 `*.meta` 文件中；重命名脚本可以，在资产树内移动脚本可以，删除 `.meta` 不行）。
- 重构序列化字段名称时，优先使用 `[FormerlySerializedAs]`，以便场景/预制体值得以保留。

## 当您更改观察模式时

`env_state`、`qpos`、`qvel`、`STEP_RESP` 负载布局、ACT JSON 负载字段 — 这些是跨仓库合约。不要在没有更新 `AgxSimProtocolConstants.ProtocolVersion` 并与 Repo A / Repo C 协调的情况下重新排序或删除字段。参见 [data-collection-pipeline.md](../project/data-collection-pipeline.md)。

地形图观察有自己的模式版本（`Scripts/GraphPerception/TerrainGraphProtocol.cs` 中的 `TerrainGraphSchema.SchemaVersion`）。当字段被添加/删除/重新用途时更新该字符串。

## 当您更改重置行为时

`SceneResetService` 快照捕获是经过调优的：它在 `Awake` 中捕获，回退到第一个 `FixedUpdate`，包括 Transform 链加刚体，并在 AGX 预热步骤后重新应用快照一次。团队在 `Assets/AGXUnity_Excavator/README.md` 中仔细记录了这一点。在更改重置路径之前请阅读该文件。

## 没有 Repo A 时的测试

如果 Repo A 不可达，您仍然可以：

- 用键盘/游戏手柄来源手动驾驶设备。
- 通过附加 `TerrainGraphSnapshotExporter`（它自动创建提供器）并按导出键（当前场景连接中的 Alpha9）来触发离线地形图导出。
- 通过 `Tools/TerrainGraph/visualize_terrain_graph_snapshot.py` 或 `terrain_graph_snapshot_to_svg.py` 检查导出。

对于没有 Repo A 的二进制协议烟雾测试，您需要编写一个小型 Python 客户端 — 树中没有。协议记录在 `Assets/AGXUnity_Excavator/Docs/protocol.md` 和 `AgxSimProtocol.cs` 中。

## 分支

撰写本文时的活跃分支：`zs/add_open_pit_mine`。PR 落在 `main` 上。使用功能分支；仓库不在树中运行 CI。