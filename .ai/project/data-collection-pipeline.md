# 数据收集管线

## 本仓库包含的内容（已确认）

本仓库是**观察表面和实时仿真主机**。它不拥有规范数据集格式。规范数据集记录（例如用于 ACT 训练的 HDF5 文件）由 **Repo A** 使用此处暴露的二进制 step-ack 协议执行。

### Step-ack 服务器（二进制，端口 5057）

- 文件：`Scripts/SimulationBridge/AgxSimProtocol.cs`、`AgxSimStepAckServer.cs`。
- 协议版本：`agx-sim/v0`。
- 消息类型：`GET_INFO`、`RESET`、`STEP`。
- 帧：固定 16 字节头（magic `0xA6A6A6A6`，version 1，type，payload length，CRC32）+ 负载（二进制小端原语，长度前缀字符串/字节数组/字符串数组）。
- `STEP_RESP` 负载布局（不得在没有协议升级的情况下更改）：`success / error / step_id / qpos / qvel / env_state / image_fpv (format + WH + bytes) / reward / sim_time_ns / warnings`。
- 字段顺序（v0 中不可变）：
  - `qpos: [swing_position_norm, boom_position_norm, stick_position_norm, bucket_position_norm]`
  - `qvel: [swing_speed, boom_speed, stick_speed, bucket_speed]`
  - `env_state: [mass_in_bucket_kg, excavated_mass_kg, mass_in_target_box_kg, deposited_mass_in_target_box_kg, min_distance_to_target_m, target_hard_collision_count, target_contact_max_normal_force_n, min_distance_to_dig_area_m, bucket_depth_below_dig_area_plane_m, target_horizontal_distance_m, bucket_height_above_target_rim_m, bucket_over_target_footprint_mask, dump_clearance_ok_mask]`
- 真实来源：[`Assets/AGXUnity_Excavator/Docs/protocol.md`](../../Assets/AGXUnity_Excavator/Docs/protocol.md)。

### ACT JSON 行桥接（独立）

- 文件：`Scripts/Control/Sources/ActProtocol.cs`、`ActObservationCollector.cs`、`ActOperatorCommandSource.cs`、`TcpJsonLinesActBackendClient.cs`、`ActBackendClientBehaviour.cs`。
- 观察收集器聚合二进制协议暴露的相同任务状态（基础姿态、执行器状态、质量跟踪器、目标传感器、DigArea 指标、目标碰撞监视器）。
- 在推理时用于将观察推送到 Python ACT 后端并接收动作；不用于本仓库中的数据集记录。

### 实验记录器（仅本地）

- 文件：`Scripts/Experiment/ExperimentLogger.cs`。
- 输出目录：`ExperimentLogs/`（已 gitignore）。
- 写入每剧集 CSV 风格摘要，对仓库内调试有用。
- 这**不是**规范的 Repo A 数据集 — Repo A 写入自己的 HDF5。

### 地形图快照（仅离线）

- 文件：`Scripts/TerrainGraphSnapshotExporter.cs` + 新的提供器。
- 输出目录：`TerrainGraphSnapshots/`（已 gitignore）。
- 每次导出一个 JSON，命名为 `terrain_graph_<timestamp>_frame<N>.json`。
- 触发：按键（根据最新场景连接默认为 `KeyCode.Alpha9`；之前是 `KeyCode.G`）、上下文菜单或 `m_exportOnStart`。
- 这是*快照*路径，不是连续记录器。

### 重置语义

- `RESET` 请求通过 `SceneResetService.ResetScene(...)` 运行。
- `scenario_id` 通过 `ScenarioPreset` 表解析。v0 预设按名称覆盖：`s0_baseline`、`s0_truck`、`s1_pose_jitter`（姿势抖动尚未实现 — 根据归档项目上下文目前等同于基线目标选择）。
- 重置恢复在 `Awake` 期间捕获的 Transform 链 + 刚体姿态快照（带 `FixedUpdate` 后备），然后重置地形高度，然后在 AGX 预热步骤后重新应用快照一次。

## 本仓库不包含的内容（已验证）

- **无 HDF5 写入器。** Repo A 负责数据集格式。
- **Python 数据集工具无 `requirements.txt`**。
- **实时场景上无 metadata.json / steps.jsonl 伴随导出器。** Repo B README 明确指出这些已从主场景中移除，以支持 Repo A 的录制路径。
- **本仓库中无重放工具。** Repo A 处理遥操作重放（`tb-replay`，根据团队 README）。

## 典型端到端循环如何运行（信息性）

1. Repo A（外部）连接到 Unity 的 `127.0.0.1:5057`。
2. Repo A 发送 `GET_INFO`，了解 dt / 顺序 / 能力。
3. Repo A 发送带有 `scenario_id` 的 `RESET`。
4. Repo A 进入 STEP 循环：`STEP_REQ(action)` → Unity 推进 AGX → Repo A 接收 `qpos/qvel/env_state/image_fpv/reward`。
5. Repo A 本地计算任务奖励 + 写入 HDF5 轨迹。
6. （可选）操作员同时在 Unity Editor 中按 Alpha9 以转储 `TerrainGraphSnapshots/*.json` 快照用于离线图分析。

地形图导出是**带外的**：在 v0 中未与步骤 ID 对齐。

## 更改此表面的成本

- **任何**对 `env_state` 顺序、`qpos` 顺序或 `STEP_RESP` 负载形状的更改都是跨仓库破坏性更改。与 Repo A 和 Repo C 协调，并更新 `AgxSimProtocolConstants.ProtocolVersion`。
- 新观察字段首先属于 `GET_INFO_RESP` 作为能力元数据，或属于伴随通道。
- 在 GET_INFO 能力握手和传输计划在外部达成一致之前，不应将图负载拼接到 `STEP_RESP` 中。