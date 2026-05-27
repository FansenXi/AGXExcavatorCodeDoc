# 已知问题和待办事项

最后更新：2026-05-25。

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

## 推荐的下一步调查

1. **在 Unity Editor 中**：打开 `AGXUnity_Excavator.unity`，记录托管 `AgxSimStepAckServer`、`TerrainGraphObservationProvider` 和 `TerrainGraphSnapshotExporter` 的确切 GameObject 层次结构。将截图捕获到 `.ai/project/scenes-assets-prefabs.md`。
2. **在 Unity Editor 中**：确认 `ScenarioPreset` 表内容，并在 `.ai/project/data-collection-pipeline.md` 下添加一个部分。
3. **在露天矿场景中**：决定是否在那里连接 step-ack 服务器和图提供器。如果是，在 `.ai/project/scenes-assets-prefabs.md` 中记录差异。
4. **补丁审查**：检查 `Assets/AGXUnity_Excavator/Docs/patches/AGXUnity-DeformableTerrain-reset-native.patch` 是否仍然适用于当前 AGX 包版本（升级 AGX 包会静默使补丁无效）。
5. **崩溃转储**：分类 `mono_crash.mem.200563.1.blob`，捕获重现器（Editor → AGX → 重现路径）或删除文件并将 `mono_crash.mem.*.blob` 行添加到 `.gitignore`。

## 刚完成的工作（2026-05-24..25）

- 图感知 v0（`Scripts/GraphPerception/*`，重构的导出器，模式文档）。参见 `.ai/handoff/latest-session-summary.md` 和 `Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md`。
- 场景清理：移除了 `AGXUnity_Excavator_measurements.unity` 和 `Assets/Scenes/SampleScene.unity`；将 `templateDefaultScene` 重新指向 `AGXUnity_Excavator.unity`。
- 文档迁移到 `.ai/`（此次传递）。