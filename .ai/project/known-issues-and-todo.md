# 已知问题和待办事项

最后更新：2026-05-29。

## 已确认的问题 / 伪影

- **仓库根目录下的 `mono_crash.mem.200563.1.blob`**（10 MB）— 来自过去 Unity Editor 会话的 Mono 崩溃转储。未 gitignore。可以安全删除；留给团队进行分类。
- **`AGXUnity_Excavator/README.md` 部分过时** — 它仍将 `AGXUnity_Excavator_small.unity` 描述为"任务场景"，但该场景名称已不在仓库中。当前活跃场景是 `AGXUnity_Excavator.unity`。在依赖其场景描述之前更新 README。
- **`Assets/AGXUnity_Excavator/Docs/scene_report.md`** 时间戳为 2026-03-25，引用了较旧的 `100 kg / 25 step` 成功规则，团队 README 说已被 Repo A 的 `dump_complete_final_hold`（`retained mass ≥ 300 kg`，`residual bucket mass ≤ 100 kg`，连续 25 个控制步骤）取代。将 README 视为更新的来源。
- **图感知 v0 的场景连接未验证。** 昨天的会话提供了 C# 实现，存在新的 `terrain_graph_20260525_*.json` 导出，确认导出器在 Editor 中运行。提供器的附加 GameObject 和任何检查器调优未以文档可确认的方式进行版本控制；请在 Unity Editor 中验证。

## 不确定 / 需要 Editor 内验证

- `AgxSimStepAckServer` 附加到简单场景中的 GameObject，但 `cameras` 数组、绑定地址和步骤消耗模式（Update / FixedUpdate / Realtime）需要在 Editor 中确认。
- `DigAreaMeasurement.FindOrCreateInScene()` 按名称自动发现 `AGXUnity.RigidBody.DigArea` GameObject。如果该 GameObject 在任何场景中被重命名或缺失，DigArea 指标会静默回退到 `-1.0f`。
- 露天矿场景的组件连接尚未记录。其挖掘机设备、step-ack 服务器存在和图提供器附加在未打开场景的情况下未知。

## 文档差距

- `Assets/AGXUnity_Excavator/Docs/excavator_current_project_structure.md` 是 680 行大部分当前的内容，但混合了实现真相和任务/实验叙述。未来的传递可以将其拆分。
- 没有记录的 `ScenarioPreset` 定义集 — 只有名称 `s0_baseline`、`s0_truck`、`s1_pose_jitter` 出现在归档上下文中。Unity Editor 时的检查器转储将澄清 v0 表面。
- 在此次迁移之前，仓库根目录没有面向人类的 `README.md`；此次传递添加了一个，但故意保持简短。
- 2026-05-27 已纠正 handoff 中的方向漂移：此前建议继续深度图感知规划；当前以 roadmap 为准，Track 1 优先 AGX 原生 Lidar + ROS 2，Depth Camera 暂不作为主线实现。
- 2026-05-28 已完成 roadmap 验证 1：AGX 原生 `LidarSensor` + `LidarROS2Publisher` 能发布 `/lidar/pointcloud`，约 50 Hz，消息非空，RViz 设置 PointCloud2 Reliability 为 `Best Effort` 后可实时显示点云。
- 2026-05-28 已新增 Repo B 过渡图观察 ROS 2 publisher：`TerrainGraphJsonRos2Publisher` 发布 `/terrain_graph/json` (`std_msgs/String`)；正式 typed 目标仍是 Repo C `excavator_msgs/msg/TerrainGraph`。但用户已修订后续路线：当前暂停继续扩展或挂载 TerrainGraph publisher。
- 2026-05-28 后续规划修订：Track 1 主线改为 `/lidar/pointcloud` → local heightmap/depth/elevation grid → RViz/Python 可视化 → 固定落铲点/卸料点 → RRT* 轨迹规划 → RL 轨迹跟踪控制器 → 再回到下铲点 planner。
- 2026-05-28 Repo A 已新增 `tb-lidar-heightmap` 首版：纯 numpy 转换器、离线/ROS 2 live CLI、单元测试和 README 说明。已通过纯算法测试和离线 CLI smoke，尚未用真实 `/lidar/pointcloud` 做 live 验证。
- 2026-05-28 Repo A 已新增分阶段执行计划：`docs/lidar_heightmap_execution_plan.md`，后续按 Pre-condition / Action / Acceptance / STOP 执行。
- 2026-05-29 已将当前 AGX LiDAR + ROS 2 验证结果收口到 `Assets/AGXUnity_Excavator/Docs/lidar_ros2_validation.md`。当前 LiDAR 足以作为 ROS 2 smoke test 通过项；它不再阻塞后续 RL 轨迹跟踪控制器工作。

## 推荐的下一步调查

1. **提交 LiDAR 阶段文档与 Unity Editor 保存结果**：确认 `Assets/AGXUnity_Excavator/Docs/lidar_ros2_validation.md` 内容无误；如果要保留场景中的 LiDAR 挂载和姿态，请由 Unity Editor 保存 `.unity`/`.asset` 变更，不要手动编辑序列化文件。
2. **完成 Repo B / Unity LiDAR 接入**：先在 Unity Editor 中确认 `SensorEnvironment`、AGX `LidarSensor`、`LidarROS2Publisher`、topic、RViz QoS 和点云覆盖范围。不要手动编辑 `.unity` 文本。
3. **live 验证 LiDAR heightmap 转换**：在 Unity 发布稳定 `/lidar/pointcloud` 后运行 Repo A `tb-lidar-heightmap --topic /lidar/pointcloud --output-dir runs/lidar_heightmap/live_preview --max-frames 10 --png`，确认生成非空 `.npz` 和可读 depth PNG。
4. **调参并可视化 heightmap**：根据真实点云调整 RoI、分辨率、高度过滤和参考高度，确认挖掘区域、台阶/坡面和铲斗附近变化都能被表达。
5. **定义固定落铲点和固定卸料点**：先用配置/人工点启动闭环，不要提前实现 dig-point planner。
6. **轨迹规划最小闭环**：用 RRT* 或等价规划器生成铲斗参考轨迹，并可视化路径点。
7. **RL 轨迹跟踪控制器**：使用现有 `[swing, boom, stick, bucket]` 速度命令训练低层控制器跟踪参考轨迹；Track 1 不破坏 step-ack。
8. **只读结构审计**：围绕 roadmap 的保留/废弃/冻结清单，梳理当前 `Scripts/` 目录职责和迁移优先级；不要批量重命名或删除。
9. **暂停 TerrainGraph publisher 路径**：不要继续挂载、扩展或 typed 化 `TerrainGraphJsonRos2Publisher`，除非用户明确恢复 TerrainGraph 方向。
10. **在 Unity Editor 中**：记录托管 `AgxSimStepAckServer`、`TerrainGraphObservationProvider` 和 `TerrainGraphSnapshotExporter` 的确切 GameObject 层次结构。将截图捕获到 `.ai/project/scenes-assets-prefabs.md`。
11. **在 Unity Editor 中**：确认 `ScenarioPreset` 表内容，并在 `.ai/project/data-collection-pipeline.md` 下添加一个部分。
12. **在露天矿场景中**：决定是否在那里连接 step-ack 服务器和图提供器。如果是，在 `.ai/project/scenes-assets-prefabs.md` 中记录差异。
13. **补丁审查**：检查 `Assets/AGXUnity_Excavator/Docs/patches/AGXUnity-DeformableTerrain-reset-native.patch` 是否仍然适用于当前 AGX 包版本（升级 AGX 包会静默使补丁无效）。
14. **崩溃转储**：分类 `mono_crash.mem.200563.1.blob`，捕获重现器（Editor → AGX → 重现路径）或删除文件并将 `mono_crash.mem.*.blob` 行添加到 `.gitignore`。

## 刚完成的工作（2026-05-24..25）

- 图感知 v0（`Scripts/GraphPerception/*`，重构的导出器，模式文档）。参见 `.ai/handoff/latest-session-summary.md` 和 `Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md`。
- 场景清理：移除了 `AGXUnity_Excavator_measurements.unity` 和 `Assets/Scenes/SampleScene.unity`；将 `templateDefaultScene` 重新指向 `AGXUnity_Excavator.unity`。
- 文档迁移到 `.ai/`（此次传递）。
