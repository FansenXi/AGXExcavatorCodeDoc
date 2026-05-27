# 图感知开发项目上下文

最后整理时间：2026-05-24

## 一句话概括

`GraphPerceptionPrj` 是一个 Unity 2022 / AGX Dynamics 挖掘机仿真项目，当前用于固定工位挖掘、遥操作、ACT 风格策略接入，以及通过二进制 step-ack 协议与 Python 训练/评测栈交互。

## 在大系统中的角色

项目文档中将本仓库描述为三仓系统中的 Repo B：

- Repo B，也就是当前 Unity 项目，负责运行 AGX 挖掘机场景并导出观测。
- Repo A 似乎负责遥操作、HDF5 数据集录制、ACT 训练、回放和评测。
- Repo C 似乎负责共享协议定义或 Python 侧协议期望。

当前本仓库主要负责：

- 运行 `AGXUnity_Excavator.unity`。
- 通过 AGXUnity 仿真可变形地形与挖掘机交互。
- 在 `5057` 端口提供 TCP 二进制 `GET_INFO / RESET / STEP` 协议。
- 返回 `qpos`、`qvel`、`env_state` 和 FPV RGB 图像观测。
- 重置场景姿态、地形、目标传感器和任务指标。
- 提供地形图快照导出与可视化的本地工具。

## 主 Unity 场景与资源

当前仓库保留两个场景：

- `Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity`
  简单场景，包含挖机 + 沙池 + 卡车。图感知（terrain graph observation）相关
  开发和导出都在这个场景中完成。
- `Assets/AGXUnity_Excavator/Open-pit mine.unity`
  露天矿场景，用于更接近真实工况的演示与后续多场景训练实验，不承载 v0 图感知工作。

历史场景 `AGXUnity_Excavator_measurements.unity` 和 Unity 默认的
`Assets/Scenes/SampleScene.unity` 已经从仓库中移除。

重要资源目录：

- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/`
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Physics/`
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Prefabs/`
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Terrains/`
- `Assets/AGXUnity_Excavator/Docs/`

项目依赖包括 AGX machine packages、Unity Input System、ShaderGraph、TextMeshPro、XR Interaction Toolkit 和 OpenXR。AGXUnity 核心包位于 `Assets/AGXUnity/`，但当前被 git 忽略。

## 当前任务边界

当前 baseline 任务是固定工位挖掘：

- 受控 action space 是 4 维机械臂控制。
- action 顺序是 `[swing_speed_cmd, boom_speed_cmd, stick_speed_cmd, bucket_speed_cmd]`。
- drive、steer 和 tracks 当前刻意不进入 step-ack action space。
- `STEP_RESP.reward` 镜像 `deposited_mass_in_target_box_kg`；Python 侧 reward 逻辑可能使用完整 `env_state`。

`AgxSimStepAckServer.cs` 中当前的 scenario preset：

- `s0_baseline`：重置地形和姿态，激活目标 index 为 `0`。
- `s0_truck`：重置地形和姿态，激活目标 index 为 `1`。
- `s1_pose_jitter`：目前等价于 baseline 目标选择，pose jitter 尚未实现。

## 运行链路

### 交互 / Episode 链路

该链路由 `EpisodeManager` 驱动：

```text
Keyboard / Gamepad / FarmStick / ACT source
  -> OperatorCommandSourceBehaviour
  -> OperatorCommandSimulator
  -> ExcavatorCommandInterpreter
  -> ExcavatorMachineController
  -> ExperimentLogger
  -> ExperimentHUD
```

关键文件：

- `Control/Core/OperatorCommand.cs`
- `Control/Core/IOperatorCommandSource.cs`
- `Control/Simulation/OperatorCommandSimulator.cs`
- `Control/Execution/ExcavatorCommandInterpreter.cs`
- `Control/Execution/ExcavatorMachineController.cs`
- `Experiment/EpisodeManager.cs`
- `Experiment/ExperimentLogger.cs`

### ACT 在线推理链路

这是面向策略推理的 JSON Lines TCP 桥：

```text
ActObservationCollector
  -> TcpJsonLinesActBackendClient
  -> Python ACT backend
  -> ActOperatorCommandSource
  -> EpisodeManager chain
```

关键文件：

- `Control/Sources/ActProtocol.cs`
- `Control/Sources/ActObservationCollector.cs`
- `Control/Sources/TcpJsonLinesActBackendClient.cs`
- `Control/Sources/ActOperatorCommandSource.cs`

### 二进制 Step-Ack 链路

这是当前主要的在线 Python 仿真接口：

```text
Python client
  -> AgxSimStepAckServer
  -> ExcavatorMachineController.ApplyActuationCommand()
  -> AGX Simulation.Instance.DoStep()
  -> ActObservationCollector.Collect()
  -> TrackedCameraWindow.TryCaptureRgb24()
  -> STEP_RESP
```

关键文件：

- `SimulationBridge/AgxSimProtocol.cs`
- `SimulationBridge/AgxSimStepAckServer.cs`
- `Presentation/TrackedCameraWindow.cs`

## 当前 Step-Ack 数据契约

`GET_INFO_RESP` 报告：

- protocol version：`agx-sim/v0`
- action semantics：`actuator_speed_cmd`
- image pixel format：`raw_rgb`
- row order：`top_to_bottom`

`qpos` 顺序：

```text
[swing_position_norm, boom_position_norm, stick_position_norm, bucket_position_norm]
```

`qvel` 顺序：

```text
[swing_speed, boom_speed, stick_speed, bucket_speed]
```

`env_state` 顺序：

```text
[
  mass_in_bucket_kg,
  excavated_mass_kg,
  mass_in_target_box_kg,
  deposited_mass_in_target_box_kg,
  min_distance_to_target_m,
  target_hard_collision_count,
  target_contact_max_normal_force_n,
  min_distance_to_dig_area_m,
  bucket_depth_below_dig_area_plane_m,
  target_horizontal_distance_m,
  bucket_height_above_target_rim_m,
  bucket_over_target_footprint_mask,
  dump_clearance_ok_mask
]
```

任何图观测扩展都应保持这个顺序，除非明确升级协议版本，并同步更新 Repo A / Repo C。

## 当前观测来源

`ActObservationCollector` 已经采集：

- 挖掘机 base pose 和局部速度。
- bucket pose。
- 归一化 actuator 位置和 actuator 速度。
- bucket mass 和 excavated mass。
- 当前激活目标质量与 deposited mass。
- bucket 到目标的距离和 clearance geometry。
- 目标硬碰撞次数和每步最大法向力。
- bucket 到 DigArea 的距离，以及相对 DigArea 平面的有效下探深度。

对于图感知，`ActObservationCollector` 是最自然的接入点，因为 ACT 和 step-ack 都已经把它作为共享观测源。

## 地形与质量相关组件

重要文件：

- `ExcavationMassTracker.cs`
- `TerrainParticleBoxMassSensor.cs`
- `Experiment/TruckBedMassSensor.cs`
- `Experiment/SwitchableTargetMassSensor.cs`
- `Experiment/TargetMassSensorBase.cs`
- `Experiment/DigAreaMeasurement.cs`
- `Experiment/SceneResetService.cs`
- `ResetTerrain.cs`

重要行为：

- bucket 质量包括 AGX terrain dynamic mass，以及标记为 `HandleAsParticle` 的动态刚体。
- 目标质量可以来自静态容器或 truck bed，由 `SwitchableTargetMassSensor` 选择。
- reset 会恢复地形、Transform、刚体、约束、质量 tracker 和目标质量基线。
- DigArea metrics 是显式几何信号，用于描述应从哪里开始挖掘。

## 现有图感知雏形

项目已经有一个初步的 terrain graph 导出路径：

- `TerrainGraphSnapshotExporter.cs`
- `Tools/TerrainGraph/visualize_terrain_graph_snapshot.py`
- `Tools/TerrainGraph/terrain_graph_snapshot_to_svg.py`
- `TerrainGraphSnapshots/terrain_graph_20260514_150654_260_frame1966.json`

当前 snapshot schema：

- `schema_version`：`terrain_graph_snapshot_v0`
- 元数据：frame、time、terrain name、terrain resolution、terrain size、element size、maximum depth、surface stride
- `surface_nodes`：采样后的 terrain heightfield cell，包含 grid index 和 world position
- `particles`：AGX dynamic soil particles，包含 position、radius 和 mass

当前限制：

- 它是手动/离线 exporter，不属于 step-ack 或 ACT 观测。
- 不导出 graph edges。
- 不包含 robot/tool particles。
- 不包含 node velocity history。
- 不包含 RoI label、boundary normal 或 action-conditioned feature。

## 当前验证现实

项目树中没有发现明显的自动化 Unity 测试套件。后续实现时，务实的检查应包括：

- Unity Editor 中 C# 编译通过。
- 手动打开场景并进入 play mode smoke test。
- 从 Repo A 运行 `GET_INFO / RESET / STEP` 二进制 client smoke test。
- play mode 中执行 terrain graph export smoke test。
- 对导出的 JSON 做离线 schema validation。
- 使用 `Tools/TerrainGraph/terrain_graph_snapshot_to_svg.py` 或 `visualize_terrain_graph_snapshot.py` 生成可视化。

## Claude 后续应优先阅读的文件

后续做图感知开发前，建议先读：

1. `Assets/AGXUnity_Excavator/README.md`
2. `Assets/AGXUnity_Excavator/Docs/protocol.md`
3. `Assets/AGXUnity_Excavator/Docs/excavator_current_project_structure.md`
4. `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/TerrainGraphSnapshotExporter.cs`
5. `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/Control/Sources/ActObservationCollector.cs`
6. `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/SimulationBridge/AgxSimProtocol.cs`
7. `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/SimulationBridge/AgxSimStepAckServer.cs`
8. `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/Experiment/SceneResetService.cs`
9. `Tools/TerrainGraph/visualize_terrain_graph_snapshot.py`
10. `Tools/TerrainGraph/terrain_graph_snapshot_to_svg.py`
