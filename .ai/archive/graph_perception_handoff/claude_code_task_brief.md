# Claude Code 任务简报：加入图感知

## 任务目标

为当前 Unity AGX 挖掘机项目加入第一版可用于生产迭代的 terrain graph observation 层。实现要小而稳、便于测试，并且兼容当前 step-ack 协议。

## 编辑前必须阅读

请先阅读：

- `knowledges/graph_perception_handoff/project_context.md`
- `knowledges/graph_perception_handoff/graph_perception_design.md`
- `Assets/AGXUnity_Excavator/Docs/protocol.md`
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/TerrainGraphSnapshotExporter.cs`
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/Control/Sources/ActObservationCollector.cs`
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/SimulationBridge/AgxSimStepAckServer.cs`

## 约束

- 不要破坏现有二进制 `GET_INFO / RESET / STEP` layout。
- 不要改变 `env_state` 顺序，除非明确进行协议升级。
- Unity 侧 graph export 应定位为观测/数据管道，不负责模型训练。
- 优先使用以 bucket reference 或选定 RoI center 为中心的 local coordinates。
- 如果 graph payload 可能影响 step 性能，应保持可选并默认关闭。
- 保持当前 reset 行为不被破坏。
- 不依赖被忽略或生成的 `Library/`、`Temp/`、`Logs/` 内容。
- 避免硬编码绝对机器路径。

## 建议实现任务

### Task 1：新增 graph 数据类

创建：

`Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/GraphPerception/TerrainGraphProtocol.cs`

包含可序列化 classes：

- `TerrainGraphObservation`
- `TerrainGraphNode`
- `TerrainGraphEdge`
- `TerrainGraphMetadata`

尽量使用 arrays、primitive fields 和 strings，确保 Unity `JsonUtility` 可以序列化。

### Task 2：新增 graph observation provider

创建：

`Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/GraphPerception/TerrainGraphObservationProvider.cs`

初始行为：

- 解析 `DeformableTerrain`。
- 通过 `ExcavatorMachineController` 解析 bucket reference。
- 使用 stride 和可选 local crop 采样 heightfield surface nodes。
- 采样 AGX dynamic soil particles，并正确处理 `ReturnToPool()`。
- 可选导出 radius edges。
- 导出 frame、time、terrain size、element size、local origin 和 coordinate frame 等 metadata。

初期让 provider 独立于 `AgxSimStepAckServer`。

### Task 3：重构 snapshot exporter

更新：

`Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/TerrainGraphSnapshotExporter.cs`

目标：

- 复用 `TerrainGraphObservationProvider`。
- 保留现有 menu/key export 工作流。
- 输出仍放在 `TerrainGraphSnapshots/`。
- 保留或记录 schema version 从 `terrain_graph_snapshot_v0` 到新 graph schema 的迁移。

### Task 4：编写 Unity graph schema 文档

创建：

`Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md`

说明：

- schema version。
- node kinds。
- coordinate frame。
- edge semantics。
- sampling controls。
- known limitations。
- 后续 protocol integration plan。

### Task 5：可选接入 observation collector

只有在 Task 1-4 已经可用后再做：

- 在 `ActObservationCollector` 中加入可选 `TerrainGraphObservationProvider` 引用。
- 保存 last graph observation 作为 nullable state。
- 在 Repo A 消费方式明确前，不要把 graph 序列化进现有 ACT JSON 或 step-ack binary response。

## 建议测试与检查

由于当前仓库没有明显自动化 Unity 测试，建议用分层 smoke checks：

- Unity 中 C# 编译通过。
- 打开 `AGXUnity_Excavator.unity` 并进入 play mode。
- 通过配置的按键或 context menu 触发 graph export。
- 确认 JSON 出现在 `TerrainGraphSnapshots/`。
- 运行：

```bash
python3 Tools/TerrainGraph/terrain_graph_snapshot_to_svg.py TerrainGraphSnapshots/<sample>.json --out /tmp/terrain_graph.svg
```

- 如果 Repo A 可用，运行既有 smoke test：

```bash
python scripts/agx_smoke.py --host 127.0.0.1 --port 5057 --steps 200 --strict
```

## 建议验收标准

- 现有场景仍可运行。
- 现有 `GET_INFO / RESET / STEP` responses 不变。
- graph JSON export 包含非空 surface nodes。
- 发生挖掘交互后，dynamic particles 能出现在导出中。
- edge generation 受 `max_edges` 约束。
- 没有 dynamic particles 时 export 不报错。
- export 清楚记录 node/edge counts。
- 新文档说明 Python 应如何消费 graph。

## v0 之后的后续工作

- 在 bucket mesh 或若干 reference points 上采样 tool particles。
- 通过缓存最近 graph frames 加入 velocity history。
- 从连续帧中生成 moving-particle labels。
- 加入按 `step_id` 对齐的 sidecar JSONL export。
- 增加 Python dataset converter。
- 在 learned RoI 前先训练 geometric-RoI baseline。
- 加入 boundary-normal features。
- 考虑为在线 graph observations 增加单独 graph stream 或协议版本升级。
