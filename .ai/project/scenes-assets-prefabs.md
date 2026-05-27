# 场景、资产、预制体

## 活跃场景（已确认）

磁盘上恰好保留两个场景：

### `Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity` — 简单场景

主要集成目标。包含：挖掘机 + 沙池 + 卡车。

- 所有 step-ack / ACT / 图感知 v0 工作都在此场景中完成。
- 在 `ProjectSettings/ProjectSettings.asset` 中设置为 `templateDefaultScene`。
- 在 TCP 端口 5057 上托管 `AgxSimStepAckServer`。
- 图感知提供器/导出器设计附加在此处。

### `Assets/AGXUnity_Excavator/Open-pit mine.unity` — 露天矿

在 `zs/add_open_pit_mine` 分支上添加。尚未连接到 step-ack 或图感知。用于后续多场景/更困难任务的实验。

### 已移除的场景（2026-05-24）

- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_measurements.unity`（历史）
- `Assets/Scenes/SampleScene.unity`（Unity 的默认模板）
- 空的 `Assets/Scenes/` 文件夹也被删除。

不要为临时测试重新创建 `Assets/Scenes/` — 在 `Assets/AGXUnity_Excavator/` 下创建任何临时场景，并在提交前删除。

## 预制体（已确认）

在 `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Prefabs/` 下：

| 资产 | 备注 |
|---|---|
| `Excavator CAT 365 Tracked.prefab` | 主挖掘机设备（来自 AGX 机器包的 CAT 365 模型） |
| `BedTruck.prefab` | 动态自卸卡车 |
| `BedTruck_mesh_static.prefab` | 卡车的静态视觉变体 |
| `Excavator CAT 365 Input Actions.inputactions` | 由 `KeyboardOperatorCommandSource` / `GamepadOperatorCommandSource` / `FarmStickOperatorCommandSource` 共享的 Unity Input System 绑定资产 |

## 其他资产子文件夹

- `Physics/` — AGX 物理材料、接触材料。
- `Terrains/` — 可变形地形资产。
- `Profiles/` — 控制器/响应配置文件（例如 `AxisResponseProfile`）。
- `materials/`、`models/` — 视觉资产。
- `SkySerie Freebie/` — 第三方天空盒包；仅视觉。
- `LightingData/` — Unity 烘焙的光照输出。
- `Scripts/Editor/` — 自定义检查器和诊断窗口（`FarmStickOperatorCommandSourceEditor`、`FarmStickInputDiagnosticsUtility`、`FarmStickProfileAssetUtility`）。

## 场景锚定的运行时组件

权威连接在简单场景中；这是逻辑图，不是 YAML 转储：

- **Step-ack 服务器** — `AgxSimStepAckServer` MonoBehaviour，拥有 TCP 5057，调用 `ActObservationCollector.Collect()` + `TrackedCameraWindow.TryCaptureRgb24()` + `ExcavatorMachineController.ApplyActuationCommand()`。
- **剧集管线** — `EpisodeManager` → 通过 `OperatorCommandSimulator` → `ExcavatorCommandInterpreter` → `ExcavatorMachineController` 路由操作员命令。还驱动 `ExperimentLogger` / `ExperimentHUD`。
- **质量/目标传感器** — `ExcavationMassTracker`，加上一个 `SwitchableTargetMassSensor` 主机，在 `ContainerBox` 和自卸卡车上的 `TruckBedMassSensor` 之间切换。
- **DigArea** — `DigAreaMeasurement.FindOrCreateInScene()` 解析 `RigidBody.DigArea` 节点并暴露几何信号。
- **FPV 相机** — `TrackedCameraWindow` 配置有渲染纹理 + `TryCaptureRgb24` 用于 step-ack 图像传输。
- **地形图 v0** — 在简单场景中的单个 GameObject 上附加 `TerrainGraphObservationProvider` 和 `TerrainGraphSnapshotExporter`。如果不存在，导出器会自动创建提供器。触发键默认为 `KeyCode.Alpha9`（之前是 `G`）。

## Editor 内需要验证的事项

grep 无法可靠检查的事项 — 打开 Editor 并确认：

- `Excavator CAT 365 Tracked` 设备存在，其铰接/棱柱连接到 `ExcavatorMachineController`。
- `SwitchableTargetMassSensor` 分配了 `ContainerBox` 和 `TruckBed` 目标。
- `AgxSimStepAckServer.cameras` 包含 `TrackedCameraWindow`，因此 `STEP_RESP.image_fpv` 非空。
- `DigArea` GameObject 有可见的填充 + 轮廓（`DigAreaMeasurement`）。