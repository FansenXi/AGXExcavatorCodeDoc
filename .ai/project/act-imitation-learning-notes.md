# ACT / 模仿学习笔记

## 本仓库包含的内容 — 已确认

到外部 ACT（Action-Chunked Transformer）后端的**客户端桥接**。训练和数据集记录在 Repo A 中；只有运行时粘合代码在这里。

### Unity 侧 ACT 集成

| 文件 | 角色 |
|---|---|
| `Scripts/Control/Sources/ActProtocol.cs` | JSON 行桥接的 DTO/线路类型 |
| `Scripts/Control/Sources/ActObservationCollector.cs` | 聚合发送到 Python 的观察负载（基础姿态、铲斗姿态、归一化 qpos、执行器速度、任务状态质量/目标/DigArea/碰撞信号） |
| `Scripts/Control/Sources/TcpJsonLinesActBackendClient.cs` | TCP JSON 行客户端 |
| `Scripts/Control/Sources/ActBackendClientBehaviour.cs` | MonoBehaviour 包装器，在场景中泵送客户端 |
| `Scripts/Control/Sources/ActOperatorCommandSource.cs` | 实现 `IOperatorCommandSource`；将 ACT 返回的动作转换为操作员命令 |

管线：

```
ActObservationCollector
  → TcpJsonLinesActBackendClient
  → Python ACT 后端（外部，Repo A）
  → ActOperatorCommandSource
  → EpisodeManager 链 → ExcavatorMachineController
```

此桥接与二进制 step-ack 服务器**分离**。ACT 在自己的 JSON 行 TCP 通道中运行；二进制 step-ack 服务器用于 Repo A 的录制/评估工作流。

### 观察一致性

`ActObservationCollector` 是**规范的 Unity 观察来源**。step-ack 服务器重用其 `LastCollectedObservation` / `LastTaskState` 来填充二进制 `env_state`。这意味着：

- 添加新的任务标量应首先落在 `ActObservationCollector` 中，然后投影到二进制 `env_state` 中（需要协调跨仓库批准 — 参见[数据收集](data-collection-pipeline.md)）。
- 图感知观察故意尚未在此收集。设计说明建议 `ActObservationCollector` 作为最终钩子，但 v0 保持图提供器分离。

### 执行器归一化

`ActObservationCollector` 暴露每轴 `ActuatorNormalizationRange`（最小/最大）加上 `ActuatorCalibrationDebugInfo`，以便团队可以在 Editor 中验证原始约束角度/位置范围是否匹配 Python 策略的期望。这是集成中最脆弱的部分 — 超出范围的原始值在观察中静默裁剪到 [0, 1]。

## 本仓库不包含的内容（已验证）

- **无 ACT 模型权重、无训练脚本、无数据集写入器。** 全部在 Repo A 中。
- **树中无 `Tcp*` 桥接的 Python 侧。** ACT 后端是外部进程。
- **ACT JSON 通道中无图像传输。** 仅结构化观察。图像通过二进制 step-ack 通道传输。

## 有用参考

- `Assets/AGXUnity_Excavator/README.md` — 描述 Repo A 在此 Unity 表面上使用的 V2.1 Stage-1 多周期原始录制工作流，以及应忽略的已弃用 ready-anchor HUD。
- `Assets/AGXUnity_Excavator/Docs/scene.md`（英文真实来源）和 `scene_report.md` — 描述 V0 任务和奖励合约在 ACT 集成之前如何最终确定。
- `.ai/archive/graph_perception_handoff/project_context.md` — 捕获截至 2026-05-24 的跨仓库职责划分。

## 禁忌

- 不要在树中添加 HDF5 写入以"匹配 Repo A"。团队已故意将数据集写入移出 Unity。
- 不要在没有协调的情况下破坏 `ActObservationCollector` 字段名称 — Repo A 和 Repo C 消费它们。
- 不要在没有重新运行校准跟踪检查器（`ResetCalibrationTracking` 上下文菜单）的情况下更改 `ActuatorNormalizationRange` 默认值。