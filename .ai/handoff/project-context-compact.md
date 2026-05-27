# 项目上下文紧凑摘要

每次新 AI 会话开始时的紧凑摘要。请先阅读此文件。

## 摘要

`GraphPerceptionPrj` 是一个 **Unity 2022.3.62f3 + AGX Dynamics** 挖掘机仿真项目，通过 **端口 5057 上的 TCP 二进制 step-ack 协议** 将设备暴露给外部 Python 客户端（Repo A）。动作空间是固定的 **4 自由度机械臂控制器**（`swing / boom / stick / bucket`）；驱动/转向/履带故意不在合约中。有一个单独的 **ACT JSON 行 TCP 桥接** 用于推理时集成。截至 2026-05-24，有一个离线 **地形图观察 v0** 路径，将 JSON 快照导出到 `TerrainGraphSnapshots/`（不在二进制 step-ack 响应中）。

## 仓库中包含的内容（已确认）

- 两个活跃的 Unity 场景：
  - `Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity` — **简单场景**（挖掘机 + 沙池 + 卡车），主要集成目标。
  - `Assets/AGXUnity_Excavator/Open-pit mine.unity` — 露天矿场景，尚未连接到 step-ack 或图感知。
- Step-ack 服务器：`Scripts/SimulationBridge/{AgxSimProtocol,AgxSimStepAckServer}.cs`。
- ACT 桥接：`Scripts/Control/Sources/{ActProtocol,ActObservationCollector,ActOperatorCommandSource,TcpJsonLinesActBackendClient,ActBackendClientBehaviour}.cs`。
- 操作员来源：键盘、游戏手柄、FarmStick 操纵杆、ACT（全部在 `Scripts/Control/Sources/` 下）。
- 挖掘机驱动：`Scripts/Control/Execution/ExcavatorMachineController.cs`。
- 重置/剧集：`Scripts/Experiment/{EpisodeManager,ExperimentLogger,SceneResetService}.cs`。
- 质量/挖掘区域传感器：`Scripts/{ExcavationMassTracker,TerrainParticleBoxMassSensor}.cs`、`Scripts/Experiment/{SwitchableTargetMassSensor,DigAreaMeasurement,...}.cs`。
- FPV RGB：`Scripts/Presentation/TrackedCameraWindow.TryCaptureRgb24`。
- 图感知 v0：`Scripts/GraphPerception/{TerrainGraphProtocol,TerrainGraphObservationProvider}.cs` + 重构的 `Scripts/TerrainGraphSnapshotExporter.cs`。模式：`terrain_graph_observation_v0`（文档在 `Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md`）。

## 仓库中不包含的内容（已验证）

- **无 ML-Agents**（`com.unity.ml-agents` 不在 `manifest.json` 中；无代理代码）。
- **无 HDF5 / 数据集写入器 / ACT 训练器** — 这些在 Repo A 中。
- **二进制 `STEP_RESP` 中无图数据负载** — 图 v0 仅离线。
- **无 `Assets/Scenes/` 文件夹** — 2026-05-24 移除。

## 跨仓库合约（任何协议编辑前必读）

| 通道 | 方向 | 布局真实来源 |
|---|---|---|
| 二进制 step-ack `agx-sim/v0` 在 TCP 5057 上 | Repo A ↔ Repo B | `Assets/AGXUnity_Excavator/Docs/protocol.md` + `AgxSimProtocol.cs` |
| ACT JSON 行 | Repo A ↔ Repo B（推理） | `Scripts/Control/Sources/ActProtocol.cs` |
| 地形图 JSON（离线） | Repo B → 文件系统 | `Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md` |

**绝不重新排序的字段顺序，除非协议升级：**

- `qpos = [swing, boom, stick, bucket]_position_norm`
- `qvel = [swing, boom, stick, bucket]_speed`
- `env_state = [mass_in_bucket_kg, excavated_mass_kg, mass_in_target_box_kg, deposited_mass_in_target_box_kg, min_distance_to_target_m, target_hard_collision_count, target_contact_max_normal_force_n, min_distance_to_dig_area_m, bucket_depth_below_dig_area_plane_m, target_horizontal_distance_m, bucket_height_above_target_rim_m, bucket_over_target_footprint_mask, dump_clearance_ok_mask]`

## 绝不编辑的文件夹

- `Assets/AGXUnity/` — 第三方 AGX 核心，已 gitignore。
- `Packages/com.algoryx.*` — 固定的上游包。
- `Library/`、`Temp/`、`Logs/`、`UserSettings/`、`Build[s]/`、`Obj/`、`*.csproj`、`*.sln`、`.vscode/`、`.idea/` — Unity / IDE 生成，已 gitignore。
- AGX 许可证：`*.lfx*`、`*.lic`、`*.license`。

## 关键待处理事项

- 实时场景上的图感知 v0 检查器连接需要手动 Editor 检查（组件放置、调优）。
- `Open-pit mine.unity` 尚未连接到 step-ack 或图导出。
- `Assets/AGXUnity_Excavator/README.md` 提到了一个已移除的场景名称（`AGXUnity_Excavator_small.unity`），应更新。
- `SceneResetService` 中的重置路径经过精心调优以避免 AGX 预热伪影 — 在修改前请阅读团队 README。

完整列表请参阅 [`.ai/project/known-issues-and-todo.md`](../project/known-issues-and-todo.md)。

## 如何继续

使用 [`.ai/prompts/claude-code-continuation-prompt.md`](../prompts/claude-code-continuation-prompt.md) 来启动下一个会话，或使用 [`.ai/prompts/codex-review-prompt.md`](../prompts/codex-review-prompt.md) 进行仅审查检查。