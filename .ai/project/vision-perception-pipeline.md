# 视觉 / 感知管线

## 已实现的内容（已确认）

### FPV RGB 捕获

- 文件：`Scripts/Presentation/TrackedCameraWindow.cs`。
- API：`bool TryCaptureRgb24(out byte[] rgb24, out int width, out int height)`。
- 由 Unity `RenderTexture` 加跟踪 `Camera` 支持；窗口可以切换可见性。
- 由 `AgxSimStepAckServer` 消费，以填充二进制 step-ack 协议中的 `STEP_RESP.image_fpv`（`pixel_format = raw_rgb`，`row_order = top_to_bottom`；参见 `Assets/AGXUnity_Excavator/Docs/protocol.md`）。
- ACT 侧：`ActObservationCollector` 当前不将 RGB 嵌入 ACT JSON 负载。只有二进制 step-ack 通道携带图像。

### 地形图观察（v0）

- 文件：
  - `Scripts/GraphPerception/TerrainGraphProtocol.cs` — 模式类。
  - `Scripts/GraphPerception/TerrainGraphObservationProvider.cs` — 采样器。
  - `Scripts/TerrainGraphSnapshotExporter.cs` — 按键触发 + 上下文菜单导出器（委托给提供器）。
- 输出：`TerrainGraphSnapshots/` 中的 JSON 文件（已 gitignore）。模式版本 `terrain_graph_observation_v0`，记录在 [`Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md`](../../Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md)。
- 坐标系：图局部，以铲斗 / DigArea / 地形中心 / 世界 / 自定义为中心（可配置）。`metadata.world_from_graph_origin_*` 中包含回到世界的刚体变换。
- 采样：带步长的高度场 + 可选 RoI XZ 过滤器；AGX 动态土壤粒子，带可选 RoI 半径/高度边距过滤器；可选单铲斗工具节点；可选有界半径边。
- 向后兼容：保留世界坐标中的遗留 `surface_nodes[]` 和 `particles[]` 数组，以便 `Tools/TerrainGraph/` 下的现有可视化工具继续工作。

### 几何信号（无图像）

已通过 step-ack `env_state` 导出：

- `min_distance_to_target_m` — 铲斗代理 → 活跃目标距离。
- `target_hard_collision_count`、`target_contact_max_normal_force_n` — 来自 `ActiveTargetCollisionMonitor` + `SwitchableTargetMassSensor`。
- `min_distance_to_dig_area_m`、`bucket_depth_below_dig_area_plane_m` — 来自 `DigAreaMeasurement`。
- `bucket_over_target_footprint_mask`、`dump_clearance_ok_mask`、`bucket_height_above_target_rim_m`、`target_horizontal_distance_m` — 也来自 `SwitchableTargetMassSensor`/目标几何。

这些是标量信号，不是感知本身，但它们是策略当前接收的唯一非图像观察。

## 未实现的内容（已验证）

- **无深度/点云导出。** Unity 使用 AGX `DeformableTerrain` 高度数据和 AGX 粒子迭代，而不是深度相机或模拟 LIDAR。
- **二进制 step-ack 响应中无实时图负载。** 图观察 v0 仅离线（文件导出）。参见 `Docs/terrain_graph_observation.md` §11 了解添加伴随通道的分阶段计划。
- **Unity 中无图像预处理/编码器。** 原始 RGB 字节通过线路传输；任何嵌入都在 Python 侧。
- **无多相机立体/视差。** `TrackedCameraWindow` 每个实例一次处理一个 FPV 相机，尽管原则上可以向场景添加多个窗口。
- **无分割/实例掩码。** 不存在。

## Python 在哪里摄取感知输出

- **RGB 图像：** Repo A 通过二进制协议消费 `STEP_RESP.image_fpv`（Repo A 是外部的）。
- **图 JSON：** 新消费者应读取 `nodes[]` + `edges[]` 加上 `metadata.world_from_graph_origin_*`；遗留的 `Tools/TerrainGraph/*.py` 消费世界坐标中的遗留 `surface_nodes[]` / `particles[]` 数组。

## 计划（根据设计说明；尚未实现）

来自 `.ai/archive/graph_perception_handoff/graph_perception_design.md`：

- **节点速度历史**（`vx/vy/vz` 在 v0 中是保留的零字段）。
- **工具网格采样**，超越单个铲斗参考点。
- **学习的 RoI 提议器**（Liu et al. 2026 风格）；v0 使用几何 RoI。
- **边界法线**，用于 RoI 边界附近的节点。
- **移动粒子标签**，来自连续帧。
- **在线图传输**（伴随 JSONL → GET_INFO 能力标志 → 并行 TCP 流）。

以上都不阻碍进一步的 Unity 侧工作 — 它们是可选的后续行动，旨在保持 v0 稳定，而 Python 探索图模型。