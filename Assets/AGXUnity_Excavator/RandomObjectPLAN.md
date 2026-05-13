# V2.1 Episode Start 场景对象随机化设计草案

## 摘要
- 新增一个**中心管理器式**的通用脚本，用于在 episode/reset 开始时，对**任意注册对象 root**施加小范围随机扰动。
- 默认只改**注册对象**，不改 excavator；首批用例是 `Target`、`DigArea` 等场景对象。
- 扰动规则固定为：**相对基准 local transform** 采样，位置走 `local XYZ` 范围，旋转只随机 `local Yaw`。
- 扰动需要对 `RESET_REQ.seed` **可复现**；手动 reset 则走管理器内部递增 seed。
- 设计目标是：**可复用、可配置、非累积、对 AGX 刚体安全**。

## 关键改动
### 1. 新增通用随机化管理器
- 新增组件：`EpisodeStartObjectRandomizer`
- 新增配置类型：`EpisodeRandomizationEntry`
- 每个条目固定绑定**一个 root Transform**，默认整棵子树一起移动，不支持“一个条目多个离散对象”。
- 每个条目字段固定为：
  - `id: string`
  - `root: Transform`
  - `enabled: bool = true`
  - `local_position_min: Vector3`
  - `local_position_max: Vector3`
  - `local_yaw_min_deg: float`
  - `local_yaw_max_deg: float`
  - `sync_child_agx_rigid_bodies: bool = true`
  - `zero_child_body_velocity: bool = true`
- 管理器在 `Awake` 或首次可用时捕获每个条目的**基准 localPosition/localRotation**。
- 每次 episode/reset 都**从基准位姿重新采样**，不做累积偏移。
- 默认采样公式：
  - `newLocalPosition = baselineLocalPosition + sampledOffset`
  - `newLocalRotation = baselineLocalRotation * Quaternion.Euler(0, sampledYawDeg, 0)`

### 2. AGX/普通对象兼容策略
- 管理器默认支持两类对象：
  - 仅 `Transform`/Renderer/Shape 子树：直接移动 root。
  - 带 AGX `RigidBody` 的对象子树：移动 root 后同步所有子 `RigidBody`。
- AGX 同步规则固定为：
  - 枚举 `root` 下全部 `AGXUnity.RigidBody`
  - 临时记录并切到 `KINEMATICS`
  - 对 root 施加新 local pose
  - 对每个 body 执行 `SyncNativeTransform()`，并清零线速度/角速度
  - 恢复原 `MotionControl`
- 不允许随机化 excavator root；配置和默认场景都不包含 excavator。

### 3. Reset 链路集成
- `SceneResetService` 新增可选 seed 入口：
  - `ResetScene(bool resetTerrain, bool resetPose, int? randomizationSeed = null)`
- `AgxSimStepAckServer` 在处理 `RESET_REQ` 时，将现有 `request.seed` 透传给 `SceneResetService`。
- `EpisodeManager` 的手动 reset 路径新增内部递增 `manual_randomization_seed`，保证编辑器内重复调试可复现。
- 随机化调用时机固定为：
  - `ResetTerrains(...)` 之后
  - `resetPose` 的 snapshot restore 完成之后
  - `ResetMeasurementTrackers()` 之前
- 这样可确保：
  - 地形先复原
  - 目标/DigArea 在最终 episode 初始位姿上被扰动
  - 质量传感器/距离量测以新位置重新建立 baseline

### 4. 可复现性与稳定哈希
- 管理器使用 `baseSeed + stableEntryHash(id or hierarchyPath)` 生成每个条目的独立 RNG。
- 同一个 reset seed 下：
  - 同一对象条目采样结果恒定
  - 条目顺序变化不应改变其他条目的结果
- 若 `randomizationSeed == null`：
  - step-ack 路径不允许发生
  - 手动路径使用 `EpisodeManager` 内部递增 seed
- 日志/HUD 至少记录：
  - 本次 randomization seed
  - 每个 entry 的 sampled local position offset
  - sampled yaw deg

## 公开接口 / 配置
- 新增 `EpisodeStartObjectRandomizer.ApplyEpisodeStartRandomization(int seed)`
- 新增 `EpisodeStartObjectRandomizer.CaptureBaselines()`
- 新增 `EpisodeRandomizationEntry[]` 作为中心管理器的 Inspector 主配置
- `SceneResetService` 新增对随机化管理器的可选引用；若未显式指定，则可自动发现场景中的单实例
- 不修改现有 wire protocol 结构；只消费已存在的 `RESET_REQ.seed`

## 测试与验收
- 同一 `seed` 连续 reset 两次，所有 entry 的位姿扰动完全一致。
- 不同 `seed` 下，同一 entry 的扰动发生变化。
- 连续多次 reset 后，对象始终围绕**原始基准位姿**采样，不发生累计漂移。
- excavator 的 root/qpos 初始状态保持不变。
- `ContainerBox`、`TruckBed`、`DigArea` 三类对象都能通过同一管理器配置工作。
- 带子 `RigidBody` 的对象在随机化后：
  - 不出现 native transform 不同步
  - 不出现明显残余速度/爆振
- `SwitchableTargetMassSensor`、`DigAreaMeasurement`、target distance/collision 量测在随机化后仍正确工作。
- step-ack reset 与手动 reset 都能触发随机化。
- `resetTerrain=true/false`、`resetPose=true/false` 的组合下，不会导致空引用或错误恢复；默认验证重点是常用 episode 启动路径。

## 假设与默认值
- 第一版只做**位置 + Yaw** 扰动，不做 pitch/roll。
- 第一版只支持**中心管理器配置**，不做“每对象自挂组件”模式。
- 每个条目绑定**一个 root**，通过子树移动覆盖该对象的全部关联可视/碰撞/测量体。
- 当前场景中的 `RESET_REQ.seed` 已在协议层存在，但 Unity 侧尚未实际消费；本次设计会把它正式接到 randomizer 链路。
- 不引入 `scenario_id` 级别的随机化策略分流；如后续需要，再在管理器条目上追加场景过滤。
