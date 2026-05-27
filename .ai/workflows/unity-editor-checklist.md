# Unity Editor 清单

无法从 CLI 完成的手动检查。在任何接触场景连接、AGX 组件、step-ack 服务器、观察收集或图感知路径的代码更改后运行它们。

## 0. 打开

- Unity Hub → Unity 2022.3.62f3。
- 打开项目根目录。等待包解析 + 首次导入编译。
- 开始前**控制台**必须干净。常见危险信号：
  - 缺少 AGX 核心（`Assets/AGXUnity/` 未填充）。
  - AGX 许可证缺失（`No valid AGX license found`）。
  - 最近重命名但没有 `[FormerlySerializedAs]` 导致的编译错误。

## 1. 场景烟雾测试（简单场景）

1. 打开 `Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity`。
2. 检查层次结构：
   - 挖掘机设备（`Excavator CAT 365 Tracked` 实例）存在。
   - `AgxSimStepAckServer` 主机 GameObject 存在；检查器显示所需的步骤消耗模式（默认 `Update`）和 `cameras` 数组。
   - 至少一个 `TrackedCameraWindow` 分配给 step-ack 服务器。
   - 可切换目标传感器连接了 `ContainerBox` 和 `TruckBed` 目标。
   - `DigArea` GameObject 存在；`DigAreaMeasurement` 显示透明填充 + 彩色轮廓。
3. **进入播放模式**约 30 秒。
   - 观察控制台是否有任何 AGX 警告/错误。
   - 确认设备可以通过活动操作员来源驾驶。
   - 确认 FPV 相机窗口填充。

## 2. 图感知导出烟雾测试

所需场景状态：场景中存在 `TerrainGraphSnapshotExporter` MonoBehaviour（如果不存在，会自动创建 `TerrainGraphObservationProvider`）。

1. 进入播放模式。
2. 按配置的导出键（当前场景连接：`Alpha9`）。
3. 确认出现新的 `TerrainGraphSnapshots/terrain_graph_<ts>_frame<N>.json` 文件。
4. 检查日志行：`TerrainGraphSnapshotExporter: exported N nodes (...) M edges -> path`。`N` 应 > 0，存在表面节点。
5. 将铲斗开入泥土中几秒钟，然后再次按导出键。新 JSON 现在应包含 `kind: "dynamic_soil"` 节点（即遗留数组中非空的 `particles[]`）。
6. （可选）在提供器上切换 `m_includeEdges`，设置 `m_edgeRadiusMeters = 0.5`，重新导出，确认 `metadata.edge_count` ≤ `m_maxEdges`。

## 3. 可视化导出（CLI，步骤 2 之后）

```bash
python3 Tools/TerrainGraph/visualize_terrain_graph_snapshot.py \
    TerrainGraphSnapshots/<latest>.json --out /tmp/graph.png
python3 Tools/TerrainGraph/terrain_graph_snapshot_to_svg.py \
    TerrainGraphSnapshots/<latest>.json --out /tmp/graph.svg
```

打开 PNG / SVG 确认表面样本 +（泥土交互后）动态粒子渲染正确。

## 4. Step-ack 服务器烟雾测试（可选，需要 Repo A）

如果 Repo A 在本地可用：

```bash
python scripts/agx_smoke.py --host 127.0.0.1 --port 5057 --steps 200 --strict
```

（该命令记录在 `Assets/AGXUnity_Excavator/README.md` 中。）

预期：

- `GET_INFO_RESP.protocol_version = "agx-sim/v0"`。
- `STEP_RESP.qpos.length = 4`，`qvel.length = 4`，`env_state.length = 13`。
- 连接相机时图像字节非零。

## 5. 重置语义

1. 在播放模式下，发送带有 `scenario_id = "s0_baseline"` 的 `RESET`（Repo A 或小型临时客户端）。
2. 确认：
   - 地形高度恢复。
   - 设备姿态返回缓存快照。
   - 活跃目标切换到索引 0。
3. 用 `s0_truck` 重复，确认活跃目标切换到索引 1。

## 6. 当检查失败时

- 捕获控制台输出以及哪个场景/组件触发了它。
- 在 `.ai/project/known-issues-and-todo.md` 中记录重现步骤。
- 避免通过禁用失败组件来静默"修复" — 该组件在那里是因为团队有意添加了它。