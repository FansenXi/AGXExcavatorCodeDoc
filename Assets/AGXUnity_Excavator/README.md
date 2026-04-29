# AGXUnity_Excavator

这个仓库是三仓系统中的 Repo B，负责 Unity / AGX 场景、step-ack server、相机与任务侧观测导出。

当前 Repo B 在三仓里的职责：
- 运行 AGXUnity 挖掘机场景与物理步进
- 接收 Repo A 发来的 `GET_INFO / RESET / STEP`
- 返回 `qpos / qvel / fpv / env_state`
- 维护场景 reset、目标区度量、DigArea 几何信号和 target hard collision 统计

当前与 Repo A 对接的主任务范围：
- 固定工位挖掘
- 4 维机械臂动作
- 不包含行走底盘控制

当前导出的关键观测字段：
- `qpos (4,)`
- `qvel (4,)`
- `env_state (9,)`
- `fpv` 相机图像

当前 phase-1 新增的最小 reset 扩展：
- `RESET_REQ.scenario_id` 现在会在 Unity 侧查 `ScenarioPreset`
- preset 第一版只覆盖 `reset_terrain`、`reset_pose`、`ActiveTargetIndex`
- preset 命中后会先执行 `SceneResetService.ResetScene(...)`，再调用 `SwitchableTargetMassSensor.SetActiveTargetByIndex(...)`
- phase-1 明确不支持 `ExcavatorPoseOffset`、`DigAreaOffset`、`DigAreaYawDeg`
- `SceneResetService` 现在会在 full reset 时优先恢复 snapshot 里的 Transform 链和 rigid body pose，再重建 terrain；AGX warm-up step 后会再恢复一次 Transform 链和 rigid body pose，减少“地面在错误位置鼓起/抬高”以及“整套参考物相对 terrain 漂移”的 reset 伪影
- 初始 reset snapshot 现在优先在 `Awake` 抓取；只有 `Awake` 阶段仍没有抓到任何可用 snapshot 时，才会退回第一帧 `FixedUpdate`，避免把开场瞬间的错误 bucket / track 姿态误记成 reset 基线
- reset snapshot 现在不只记录 rigid body，也会记录参与 reset 的 Transform 链，包括 `excavator / truck / DigArea / rig roots` 这类参考节点；对于 `DeformableTerrain`，reset 会恢复它的共享父链，但不会把 terrain 自己的 Transform 硬拉回 pre-initialize posed 值，因为 AGX terrain 运行时会按 `MaximumDepth` 自带一个 Y 偏移

当前 Repo A 主线已经切到 V2.1 Stage-1 multicycle raw 录制：
- 主录制入口是 `teleop_v2_1_multi_raw`
- 主停止条件是第 `3` 次 `dump_end`
- 不再要求操作者回 fixed ready pose 或 ready-anchor 才停录

Unity 侧原有的 ready-anchor HUD / 3D marker 代码目前只保留为历史调试残件：
- 它们不再代表当前主工作流
- 它们不再是 Repo A 主线录制的必经条件
- 如果保留在场景里，应只把它们当成调试辅助信息，而不是 V2.1 Stage-1 的操作准则

当前 step-ack server 的运行方式：
- 默认推荐使用 `Update`
- `FixedUpdate` 保留给对照实验或调试
- `Realtime Mode` 只在专门的时延实验里使用

这些运行模式不会改变 Repo C 定义的线协议格式，只影响请求在 Unity 侧何时被消费。

共享协议、字段顺序和数据契约以 Repo C `sim-protocol` 为准。

## 场景说明

### `AGXUnity_Excavator.unity`

这个场景展示了带行走机构的挖掘机场景。

挖掘机模型由 Algoryx Momentum 导出后导入 AGXUnity。
`Excavator.cs` 和 `ExcavatorInputController.cs` 负责发动机、传动和执行器控制。

### `AGXUnity_Excavator_small.unity`

这是当前与 Repo A 配合最紧密的任务场景。

它具备以下特点：
- 工位固定
- 底盘锁定
- 小范围可挖地形
- 目标区质量统计
- 独立 terrain reset 路径

当前 Repo A 的固定工位挖掘任务主要依赖这个场景导出的状态量。

## 控制方式

### Gamepad (XBox360)

- Right Stick X     - Boom up/down
- Right Stick Y     - Move bucket
- Left Stick X      - Swing left/right
- Left Stick Y      - Stick up/down
- D-Pad (X/Y)       - Drive forward/backward/left/right

### FarmStick (current default)

- Left Main Stick Y  - Swing
- Left Main Stick X  - Stick
  Swing / Stick 两个轴在 FarmStick source 里都会额外反向一次，用来对齐当前真机手感。
- Right Main Stick Y - Boom
- Right Main Stick X - Bucket
  Bucket 轴会额外反向一次，用来对齐当前真机 bucket 手感。
- Left/Right Rocker  - Drive / Steer

### Keyboard

- PageUp/Down       - Boom up/down
- Insert/Delete     - Move bucket
- T/U               - Swing left/right
- Home/End          - Stick up/down
- Up/Down           - Forward/Backward
- Left/Right        - Turn Left/Right

## 任务与重置相关组件

- `AgxSimStepAckServer.cs`
  Unity <-> Python 的 step-ack server，负责请求消费与响应封包

- `ActObservationCollector.cs`
  导出 `qpos / qvel / env_state / fpv`

- `ExcavationMassTracker.cs`
  统计 bucket、目标区和相关任务状态

- `ResetTerrain.cs` / `SceneResetService`
  负责 terrain 与场景 reset

当前 terrain reset 路径已经与 excavation metrics 统计解耦，按服务路径单独处理。

## phase-1 scenario preset

Repo B 当前内置了最小 `scenario_id -> preset` 路由，主要用于让 Repo A 在不改 wire 版本的前提下切换 reset 组合和目标容器。

当前内置 preset：
- `s0_baseline` -> `ActiveTargetIndex = 0`
- `s0_truck` -> `ActiveTargetIndex = 1`
- `s1_pose_jitter` -> 目前仍只走 baseline 同级别 reset/target preset，pose jitter 字段留到后续阶段

warning 行为：
- 未知 `scenario_id` 只写 warning，例如 `unknown_scenario_id:<id>`
- 非法 target index 只写 warning，例如 `invalid_active_target_index:<index>`
- 不会因为 preset 缺失而把 `RESET_RESP.success` 直接打成失败
