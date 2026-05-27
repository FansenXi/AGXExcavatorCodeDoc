# RL / ML-Agents 笔记

## 当前状态 — 已确认

**本仓库未使用 ML-Agents。** 证据：

- `Packages/manifest.json` 不依赖 `com.unity.ml-agents`（或任何 ML-Agents 扩展）。
- 在项目脚本上运行 `grep -rn "Unity.MLAgents\|MLAgents\|MlAgents"` 没有匹配项。
- 没有 `*Agent.cs` 文件，没有 `behavior_parameters` 资产，没有训练器 YAML 配置。

**仓库内没有强化学习循环。** 模拟器暴露的奖励计算（`STEP_RESP.reward = deposited_mass_in_target_box_kg`）是*备用标量* — 主要任务奖励由 Repo A 从完整的 `env_state` 外部计算。

## 如果以后添加 RL 系统，本仓库提供什么

- **动作空间** — 4 自由度连续（`swing/boom/stick/bucket` 速度命令），已暴露。
- **观察空间** — `qpos (4) + qvel (4) + env_state (13) + FPV RGB` 通过二进制 step-ack 通道。加上离线地形图（不在 step-ack 中）。
- **重置** — `RESET` 接受 `seed` 和 `scenario_id`；v0 覆盖 `reset_terrain`、`reset_pose`、`ActiveTargetIndex`（参见[数据收集](data-collection-pipeline.md)）。
- **确定性步进** — `AgxSimStepAckServer` 每帧消耗一个 `STEP_REQ` 并同步推进 AGX。

## 如果以后添加 ML-Agents 支持

这是规划，不是实现：

- 通过 Unity Package Manager 将 `com.unity.ml-agents` 添加到 `Packages/manifest.json`。
- 将现有观察/动作基础设施包装在 `Agent` 子类中，而不是复制采样 — `ActObservationCollector` 是自然的观察来源。
- 保持 step-ack TCP 服务器和任何进程内 ML-Agents 训练器在每个场景中互斥；两者都想驱动 `Simulation.Instance.DoStep()`。
- 不要为了适应 ML-Agents 观察规范而改变 `env_state` 排序 — 如果需要，构建单独的 `VectorSensor`。
- ML-Agents YAML 配置将位于仓库根目录的新 `mlagents/` 文件夹下，而不是 `Assets/` 下。

## 相关外部工作

- ACT（Action-Chunked Transformer）模仿学习训练发生在 Repo A 中。参见 [act-imitation-learning-notes.md](act-imitation-learning-notes.md)。
- 地形图观察旨在为未来的 Localized Graph-Based Neural Dynamics 模型（Liu et al. 2026）提供数据；参见 `.ai/archive/graph_perception_handoff/graph_perception_design.md` 中的设计说明。这是用于规划的动态建模，不是 RL 本身。