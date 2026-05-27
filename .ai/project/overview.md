# 项目概述

## 这个项目是什么 — 已确认

`GraphPerceptionPrj` 是一个 Unity 2022.3.62f3 + AGX Dynamics 仿真项目，托管挖掘机设备，并通过 TCP 将其作为观察/控制表面暴露给外部 Python 客户端。该仓库在内部被描述为三仓系统中的 **"Repo B"**：

- **Repo B**（本仓库）— Unity 场景 + AGX 仿真 + TCP 二进制 step-ack 服务器 + 观察导出。
- **Repo A**（外部）— 遥操作录制、HDF5 数据集、ACT 模仿学习训练和评估。
- **Repo C**（外部）— 共享的 `sim-protocol` 定义 / 线路合约。

只有 Repo B 在此树中。Repo A 和 Repo C 被引用但不存在。

## 已确认的主要子系统

| 子系统 | 位置 | 备注 |
|---|---|---|
| AGX 仿真主机 | `Assets/AGXUnity/`（已 gitignore，第三方） | AGXUnity 核心包；不要编辑 |
| AGX 机器包 | `Packages/manifest.json` → `com.algoryx.agxunity.machines.{bedtruck,cat365,dl300,e85}` v1.1.1 | 上游挖掘机/卡车资产 |
| TCP 二进制 step-ack 服务器 | `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/SimulationBridge/{AgxSimProtocol,AgxSimStepAckServer}.cs` | `GET_INFO / RESET / STEP`，端口 5057，协议 `agx-sim/v0` |
| ACT JSON 行 TCP 桥接 | `Scripts/Control/Sources/{ActProtocol,ActObservationCollector,ActOperatorCommandSource,TcpJsonLinesActBackendClient,ActBackendClientBehaviour}.cs` | 与二进制协议分离 |
| 操作员命令来源 | `Scripts/Control/Sources/{Keyboard,Gamepad,FarmStick,Act}OperatorCommandSource.cs` | 通过 `IOperatorCommandSource` 统一 |
| 挖掘机驱动 | `Scripts/Control/Execution/{ExcavatorMachineController,ExcavatorCommandInterpreter,ExcavatorActuationLimits}.cs` | 4 自由度机械臂：swing / boom / stick / bucket |
| 剧集/实验循环 | `Scripts/Experiment/{EpisodeManager,ExperimentLogger,SceneResetService}.cs` | 驱动重置、目标切换、日志记录 |
| 质量/目标传感器 | `Scripts/{ExcavationMassTracker,TerrainParticleBoxMassSensor}.cs`，`Scripts/Experiment/{SwitchableTargetMassSensor,TargetMassSensorBase,TruckBedMassSensor}.cs` | `env_state` 质量字段的来源 |
| DigArea 几何信号 | `Scripts/Experiment/{DigAreaMeasurement,BucketTargetDistanceMeasurementUtility,TargetDistanceVolumeUtility}.cs` | `min_distance_to_dig_area_m`，平面下深度 |
| FPV 相机捕获 | `Scripts/Presentation/TrackedCameraWindow.cs`（`TryCaptureRgb24`） | step-ack 图像传输 |
| 地形重置 | `Scripts/ResetTerrain.cs` + `SceneResetService` | AGX `DeformableTerrain` 重置路径 |
| 地形图观察 v0 | `Scripts/GraphPerception/{TerrainGraphProtocol,TerrainGraphObservationProvider}.cs` + `Scripts/TerrainGraphSnapshotExporter.cs` | 离线 JSON 导出；不在 step-ack 中 |
| 图可视化工具 | `Tools/TerrainGraph/{visualize_terrain_graph_snapshot,terrain_graph_snapshot_to_svg}.py` | 纯 Python，依赖：numpy/matplotlib（仅 `.py`） |

## 已确认的目标（来自现有团队文档）

- 运行**固定工位 4 自由度挖掘任务**（仅 swing/boom/stick/bucket；驱动/转向/履带故意排除在 step-ack 动作空间之外）。
- 通过二进制 step-ack 合约向 Repo A 导出确定性观察，包括 `qpos`、`qvel`、`env_state`、FPV RGB 图像，以及镜像 `deposited_mass_in_target_box_kg` 的奖励标量。
- 提供单独的离线**地形图观察**通道（v0），用于后续 GNN / Localized Graph-Based Neural Dynamics 工作，基于 Liu et al. 2026（`knowledges/` 中的 PDF）。图数据负载**不在** step-ack 响应中。

## 场景（已确认，参见 [scenes-assets-prefabs.md](scenes-assets-prefabs.md)）

- `Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity` — **简单场景**（挖掘机 + 沙池 + 卡车）。主要集成目标。
- `Assets/AGXUnity_Excavator/Open-pit mine.unity` — 露天矿场景。尚未连接到 step-ack 服务器或图提供器。

历史 `AGXUnity_Excavator_measurements.unity` 和 Unity 默认的 `Assets/Scenes/SampleScene.unity` 于 2026-05-24 移除。

## 不存在的内容（通过检查验证）

- **无 ML-Agents** — 任何地方都没有 `Unity.MLAgents` 导入；包不在 `Packages/manifest.json` 中。RL 配置不在树中。
- **无树内 ACT 训练** — 仅存在 Unity 侧的 TCP 桥接；ACT 训练器 / 数据集写入器在 Repo A 中。
- **无树内 HDF5 / 数据集写入器** — `ExperimentLogger` 写入本地实验日志（`ExperimentLogs/`，已 gitignore），而不是规范数据集。
- **step-ack 中无图数据负载** — 图 v0 仅离线 JSON；二进制 `STEP_RESP` 布局未变。

## 推断 / 计划

- **在线图观察通道** — 已设计（附属 JSONL → GET_INFO 能力标志 → 并行 TCP 流或协议版本升级）。参见 [`Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md`](../../Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md) §11 和归档的设计说明。
- **露天矿场景集成** — 场景存在但尚无图提供器 / step-ack 连接。
- **学习的 RoI 提议器 / 边界法线 / 速度历史** — 在设计说明中列为 v0 后续工作；未实现。