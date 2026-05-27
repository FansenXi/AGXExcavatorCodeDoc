# 图感知设计笔记

## 目标

在当前 Unity AGX 挖掘机项目中加入图结构地形感知能力，让后续学习代码可以消费更接近 Liu et al. 2026 "Localized Graph-Based Neural Dynamics Models for Terrain Manipulation" 的局部地形状态。

近期目标应该是观测导出，而不是把完整的 L-GBND 学习模型塞进 Unity。Unity 应负责产生稳定的 graph observation；Python 应负责训练、RoI learning、GNN rollout 和 planning。

## 论文要点

Liu et al. 2026 提出了 Localized Graph-Based Neural Dynamics，即 L-GBND，用于 terrain manipulation：

- 将 terrain 和 tool 表示为 graph 中的 particles。
- 通过局部 graph message passing 预测 particle displacement。
- 用 action-conditioned Region of Interest，即 RoI，避免对完整地形做预测。
- RoI 外的 particles 被视为静止。
- 加入 boundary-aware node features，让 RoI 边界附近的粒子感受到阻力，而不是穿过人工切出来的边界。
- 使用 3D particle-based representation，而不是只有 2D heightmap，因为 scooping 和 dumping 依赖体积结构、接触几何、质量守恒和尖锐局部地形特征。
- 在 simulation 中利用可追踪 particles 训练，再用真实 RGB-D point cloud 通过集合距离进行 fine-tune，因为真实数据通常没有显式 particle correspondence。

论文中值得迁移的实现细节：

- Terrain particles 从观测到的地形表面向足够深度体素化生成。
- Tool particles 在末端执行器表面采样。
- Edges 通过 nearest neighbors 加 distance threshold 连接附近 particles。
- Node features 包括最近若干步 particle velocity history、tool/action impulse，以及 particle class embedding。
- Edge features 包括相对 particle trajectory 和 class pair 信息。
- RoI proposer 使用 tool-centered heightmap crop 和当前 control input，预测 active particles 的 height/depth boundary。
- Boundary normals 作为 feature，用于提升泛化并减少 RoI boundary artifact。

## 映射到当前项目

### 已经具备的条件

当前 Unity 项目已经具备生成图观测所需的大部分原始仿真信息：

- AGX deformable terrain，包含 heightfield 和 dynamic soil particles。
- 挖掘机 bucket pose 和 actuator state。
- 可被视为 particles 的动态刚体。
- 通过 `AgxSimStepAckServer` 实现 fixed-step manual stepping。
- 已有 `TerrainGraphSnapshotExporter` 能导出 surface samples 和 dynamic particles。
- 已有 mass、target placement、DigArea、target collision 等任务指标。

### 目前缺失的部分

项目还没有 runtime graph observation：

- `ActObservation` 中没有 graph payload。
- 二进制 `STEP_RESP` 中没有 graph payload。
- 没有在线使用的 node/edge schema。
- 没有 tool particle sampling。
- 没有 graph edge generation。
- 没有 node velocity history。
- 没有 RoI selection、labels 或 boundary-normal features。
- Python 训练/数据流水线不在当前仓库中。

## 推荐架构

建议分层加入图感知：

```text
AGX terrain + bucket pose
  -> TerrainGraphObservationProvider
  -> ActObservationCollector
  -> optional offline JSON export
  -> optional step-ack graph payload or sidecar stream
  -> Python graph dataset / model code
```

Unity 侧负责：

- 读取 terrain state。
- 采样 graph nodes。
- 采样 tool nodes。
- 计算简单局部几何 feature。
- 导出稳定 schema。
- 保持 reset/step 行为确定且不被破坏。

Python 侧负责：

- 大规模 graph preprocessing。
- learned RoI proposer。
- GNN dynamics model。
- MPPI/planning。
- dataset storage。
- training/evaluation。

## 建议新增 Unity 组件

### `TerrainGraphObservationProvider`

建议路径：

`Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/GraphPerception/TerrainGraphObservationProvider.cs`

职责：

- 解析 `DeformableTerrain`、`ExcavatorMachineController`、bucket reference，以及可选的 DigArea/target reference。
- 从当前帧生成 graph observation object。
- 支持 Inspector 中可调的采样参数。
- 不修改 AGX 状态。
- 为重复 `STEP` 调用控制分配和性能成本。

初始采样参数：

- `surface_stride`
- `max_dynamic_particles`
- `max_surface_nodes`
- `roi_center_mode`：bucket、DigArea、active target、full terrain
- `roi_radius_m`
- `roi_height_margin_m`
- `include_surface_nodes`
- `include_dynamic_particles`
- `include_tool_nodes`
- `include_edges`
- `edge_radius_m`
- `max_edges`

### `TerrainGraphProtocol`

建议路径：

`Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/GraphPerception/TerrainGraphProtocol.cs`

定义可序列化数据类：

```text
TerrainGraphObservation
TerrainGraphNode
TerrainGraphEdge
TerrainGraphFrame
TerrainGraphBounds
```

建议 `TerrainGraphObservation` 字段：

```text
schema_version
frame
sim_time_sec
terrain_name
world_from_graph_position
nodes
edges
metadata
```

建议 node 字段：

```text
id
kind                  // surface, dynamic_soil, tool, dynamic_rigidbody
x, y, z               // graph-local 或 world position
vx, vy, vz            // v0 可先置零
radius
mass
height
surface_i, surface_j
is_dynamic
is_tool
is_in_roi
boundary_normal_x, boundary_normal_y, boundary_normal_z
```

建议 edge 字段：

```text
src
dst
dx, dy, dz
distance
kind_pair
```

默认应使用 graph-local coordinates，并提供能回到 world 的 transform。v0 的务实选择是以 bucket reference 或 DigArea center 为 graph 坐标原点。这符合论文强调的 translation invariance 和 tool-centered local frame。

### 离线导出

扩展或替换 `TerrainGraphSnapshotExporter`，让它复用同一个 provider。Exporter 应成为薄包装：

```text
TerrainGraphObservationProvider.Collect()
  -> JsonUtility.ToJson()
  -> TerrainGraphSnapshots/*.json
```

这样可以避免离线 schema 和在线 schema 分叉。

## 协议策略

### 推荐 v0：先做 sidecar JSON export

在修改二进制协议前，先将 graph observation 做成：

- Unity provider。
- 手动/context-menu exporter。
- 可选的 scripted rollout per-step JSONL sidecar。

原因：

- 当前二进制 `STEP_RESP` 返回固定数组和 raw RGB。
- Repo A / Repo C 很可能假设了现有 response layout。
- Graph payload 是变长的，而且可能很大。
- Sidecar 路径更适合快速迭代和检查数据集。

### v1：在 `GET_INFO` 中加入 graph capability

Sidecar schema 稳定后，再加入 feature flags：

```text
supports_terrain_graph
terrain_graph_schema_version
terrain_graph_node_fields
terrain_graph_edge_fields
```

### v2：选择 binary graph transport

在线训练/planning 需要 graph payload 时，可选：

- 协议版本升级后，将 graph bytes 追加到 `STEP_RESP`。
- 增加单独的 `GET_GRAPH` message type。
- 增加一个通过 `step_id` 对齐的并行 TCP JSONL 或 binary stream。

风险最小的在线路径是单独的 graph stream，并用 `step_id` 对齐，因为它不会破坏当前 image/action client。

## 当前挖掘任务中的 RoI 设计

论文使用 learned action-conditioned RoI proposer。本项目应先从更简单的方案开始：

### Stage 1：几何 RoI

使用 bucket-centered geometry：

- 中心放在 bucket cutting edge 或 bucket reference。
- 半径覆盖 bucket/DigArea 周边。
- 垂直范围覆盖地形表面与 bucket 附近。
- dynamic particles 只要在 RoI 内就纳入，不受 surface stride 限制。

这容易验证，也能给 Python 提供足够数据，后续训练 learned proposer。

### Stage 2：标注 moving particles

rollout 期间导出连续 graph frames，并根据 displacement 或 speed 阈值给 node 标注 moving。这与论文中使用 simulation supervision 训练 RoI 的思路一致。

### Stage 3：在 Python 中训练 learned RoI

训练 proposer 时使用：

- time `t` 的 graph frame。
- action `u_t`。
- `t -> t+1` 的 movement labels。
- 可选 bucket trajectory/window features。

### Stage 4：boundary features

为 RoI 边界附近 nodes 计算或近似 boundary normals：

- 对几何 RoI，boundary normal 初期可用 RoI center 到 node position 的 XZ 归一化向量；如果使用 height/depth window，则加入 vertical clipping normals。
- 对 learned RoI，normal 应来自 learned implicit boundary 的梯度，或在 RoI field 上做 finite-difference 近似。

## 建议的 Graph Observation v0

第一个可用数据集应包含：

- bucket-centered local crop 内的 surface height samples。
- crop 内的 dynamic soil particles。
- 从 bucket 若干 reference points 采样的 bucket/tool particles。
- 可选的 particle-like dynamic rocks。
- 在局部 XYZ 或 XZ+height 空间中通过 radius search 得到的 edges。
- node kind、mass、radius。
- action vector `[swing, boom, stick, bucket]`。
- `qpos`、`qvel` 和已有 `env_state`。
- `step_id`、`sim_time_sec`、`scenario_id`。

v0 不要试图完全复现论文中的百万粒子场景。当前项目是固定工位挖掘仿真，因此第一个里程碑应是围绕交互区域的稳定局部 graph。

## 接入点

最佳第一接入点：

- `ActObservationCollector.Collect()`

原因：

- 它已经拥有 ACT 和 step-ack 共享的当前 observation contract。
- 它已经知道 bucket pose、task state、actuator state 和 target/DigArea metrics。
- 在这里加入 nullable graph observation，可以让图感知与现有 step timing 对齐。

最佳离线 exporter 接入点：

- `TerrainGraphSnapshotExporter`

原因：

- 它已经处理路径创建和人工触发导出。
- 它与控制逻辑隔离。
- 它可以成为新 provider 的兼容包装。

后续最佳协议接入点：

- `AgxSimProtocol.cs`
- `AgxSimStepAckServer.cs`
- `Assets/AGXUnity_Excavator/Docs/protocol.md`

只有在明确 Repo A 如何消费 graph payload 后，再修改这些文件。

## 主要风险

- 大 payload 会让 step-ack 变慢，尤其是在 raw RGB 也开启时。
- Unity `JsonUtility` 能力有限，但对简单 serializable arrays 足够。
- AGX dynamic particle API 可能分配对象，且需要正确 `ReturnToPool()`。
- Surface height samples 和 dynamic particles 在 reset 之间未必有稳定 identity，需要谨慎定义 ID。
- Learned dynamics 不应依赖绝对 world height/position；应使用 local coordinates 和 boundary features。
- 当前 `.gitignore` 会忽略 `*.png`、`*.svg`、`*.pdf` 等可视化/参考输出。

## 推荐第一里程碑

里程碑名称：`terrain_graph_observation_v0`

交付物：

- `GraphPerception/TerrainGraphProtocol.cs`
- `GraphPerception/TerrainGraphObservationProvider.cs`
- `TerrainGraphSnapshotExporter` 改为复用 provider
- 在 `Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md` 中记录 JSON schema
- 一个从 play mode 导出的 sample JSON
- 基于 sample 生成的 SVG/PNG 可视化

验收标准：

- Unity 编译通过。
- 现有 `GET_INFO / RESET / STEP` 行为不变。
- 手动 graph export 在 play mode 中可用。
- 导出的 graph 包含 surface nodes；存在挖掘交互后能包含 dynamic particle nodes。
- 导出的 coordinates 使用一致的 local frame，并包含足够 metadata 还原 world positions。
