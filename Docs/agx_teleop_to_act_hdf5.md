# 将 Unity 人类遥操作记录为 Testbed 可训练的 ACT HDF5 数据集

## 目标

这里真正要产出的不是“policy 文件”，而是一个能够被 testbed 现有 ACT 训练流程直接消费的 `episode_N.hdf5` 演示数据集。

在通读 `excavator_testbed` 和 `AGXUnity_Excavator` 之后，当前风险最低的路径是：

1. 用 Unity 采集人类遥操作 episode。
2. 由 Unity 导出一个逐步对齐的中间格式。
3. 用 testbed 里已经存在的 Python HDF5 写入器，把这些 episode 转成最终的 `episode_N.hdf5`。

除非有硬性要求，否则不建议先在 C# 里直接写 HDF5。因为数据 schema 已经在 Python 里定义好了，再在 Unity 里复制一份，后面很容易漂移。

## 先做一个关键决策

当前代码里其实存在两种不同的 ACT 接口。

### 方案 A：训练 ACT 输出 4 维挖机执行命令

这条路径和当前 testbed 训练栈是一致的。

- testbed 的 excavator ACT 模型对 `excavator_simple` 默认假设 `state_dim = 4`
- MuJoCo 挖机任务的 action 也是 4 维，顺序是 `[swing, boom, stick, bucket]`
- Unity 侧 `AgxSimStepAckServer` 现在也已经接受 4 维 action 向量

如果目标是“尽快把 Unity 人类操作录成 HDF5，然后在现有 testbed 里训练 ACT”，我推荐选这条路。

### 方案 B：训练 ACT 输出 6 维 `OperatorCommand`

这条路径和 Unity 里的 `ActOperatorCommandSource` 一致，但它**不**和当前 testbed 的 excavator ACT 训练代码一致。

如果走这条路，就不只是导出 HDF5 的问题了，还需要一起改：

- testbed 的 dataset 格式
- 模型 state/action 维度
- backend 协议

这不是最快得到可训练数据集的路径。

## 两边仓库当前已经有什么

### Unity 侧已有能力

- `EpisodeManager` 已经负责整个人类控制链路：读取输入源、解释成 actuation、下发控制，并在 `FixedUpdate` 里做逐步记录
- `ExperimentLogger` 已经能把每一步写成 JSONL/CSV
- `ActObservationCollector` 已经能读取：
  - boom/stick/bucket 的归一化位置
  - swing/boom/stick/bucket 的速度
  - `mass_in_bucket_kg`
- `TrackedCameraWindow` 已经维护了一路独立的 FPV `RenderTexture`
- `AgxSimStepAckServer` 已经提供了后续做远程 replay/eval 的 socket bridge

### testbed 侧已有能力

- `EpisodeRecorder.record()` 已经能缓存 `qpos`、`qvel`、`action`、`images`、`reward`
- `write_episode()` 已经能写标准 `episode_N.hdf5`
- ACT 训练流程已经直接消费这个 HDF5 布局

### 当前还缺什么

1. testbed 里还没有 AGX backend，所以只有 Unity 的 step-ack server 还不够
2. Unity 当前在 step-ack bridge 里导出的 `qpos` 长度是 3，不是 4
3. Unity 当前声明 `supports_images = false`，所以 ACT 需要的图像流还没打通
4. Unity 当前 `reset_pose` 没实现
5. testbed loader 会丢弃 `action_dim != qpos_dim` 的 episode，所以如果 Unity 导出 4 维 action + 3 维 `qpos`，训练前就会被过滤掉

## 为什么 HDF5 仍然应该由 Python 写

testbed 已经明确了数据集契约，核心文件就是：

- `testbed/data/recorder.py`
- `testbed/data/hdf5_io.py`
- `testbed/data/dataset.py`

真正被 ACT 训练消费的是这套 schema。Unity 现在已经有不错的 episode 管理和日志能力，但没有 HDF5 写入器。最干净的职责拆分是：

- Unity 负责采集遥操作
- Python 负责把采集结果落成标准 HDF5

这样只保留一份 HDF5 schema 实现。

## 推荐的数据采集链路

```text
人类输入设备
  -> EpisodeManager
  -> ExcavatorCommandInterpreter
  -> ExcavatorMachineController
  -> Unity 侧 teleop 导出缓存
  -> JSONL/NPY/图像帧目录（或 socket sidecar）
  -> Python 导入脚本
  -> EpisodeRecorder / write_episode
  -> episode_N.hdf5
  -> ACT train/eval
```

对于“人类遥操作演示采集”这个目标，**不需要**先从 `AgxSimStepAckServer` 走一圈。更合适的 hook 点就是现成的 `EpisodeManager` + `ExperimentLogger`，因为人类输入已经在这里被对齐成 episode 和 step 了。

## Unity 每一步应该导出什么

如果目标是兼容当前 testbed 的 ACT 训练栈，那么每一步应当导出：

- `action`: `float32[4]`
  - 顺序：`[swing, boom, stick, bucket]`
  - 来源：`LastActuationCommand`
- `qpos`: `float32[4]`
  - 顺序：`[swing_position_norm, boom_position_norm, stick_position_norm, bucket_position_norm]`
- `qvel`: `float32[4]`
  - 顺序：`[swing_speed, boom_speed, stick_speed, bucket_speed]`
- `images["fpv"]`: `uint8[H, W, 3]`
- `reward`: `float32`
  - 纯行为克隆场景下直接写 `0.0` 也可以
- metadata：
  - `task_name`
  - `sim_backend = "agx_unity_teleop"`
  - `seed`
  - `param_version`
  - `source_name`
  - `action_semantics = "actuator_speed_cmd"`
  - `camera_names = ["fpv"]`
  - `control_hz`
  - `dt`

### 为什么 action 应该用 `LastActuationCommand`

对于当前 testbed 的 excavator ACT 栈，policy 学习的应该是 4 维执行命令，而不是 6 维 `OperatorCommand`。

应该使用：

- `EpisodeManager.LastActuationCommand`

不应该使用：

- 原始键盘/手柄按键
- `OperatorCommand`，前提是这份数据是给当前 testbed ACT 训练流程用的

## Unity 侧需要改什么

### 1. 在 `ActObservationCollector` 里补上 swing position

当前状态：

- `ActObservationCollector` 只导出了 boom/stick/bucket 的归一化位置
- 它导出了 swing 的**速度**，但没有导出 swing 的**位置**

这是当前最关键的兼容性问题，因为 testbed 的 excavator 模型期望 4 维 state，而 dataset loader 会过滤掉 `action_dim != qpos_dim` 的数据。

建议改法：

- 增加一个 `m_swingRange`，和现有的 boom/stick/bucket normalization range 并列
- 在 teleop 导出路径里补上 `swing_position_norm`
- 顺序和 testbed 里的 excavator 常量保持一致：
  - `[swing, boom, stick, bucket]`

如果不补 `swing_position_norm`，那就必须反过来改 testbed loader 和 ACT 模型，让它们接受 `qpos_dim = 3`、`action_dim = 4`。这会更大、更散，不是最短路径。

### 2. 从 `TrackedCameraWindow` 暴露 FPV 帧抓取接口

`TrackedCameraWindow` 已经持有 `RenderTexture`。建议新增一个方法，比如：

```csharp
public bool TryCaptureRgb24( out byte[] rgb, out int width, out int height )
```

实现要点：

- 先确保 render texture 已经创建
- 必要时主动触发 camera render
- 从 `m_renderTexture` 读回像素
- 返回 RGB 数据，Python 侧再还原成 `(H, W, 3)`，或者同时返回 width/height 元信息

这里应该做成一个显式的采样 API，不要把 GUI 预览窗口本身当成数据源。

### 3. 在 `ExperimentLogger` 旁边新增一个专门的 teleop exporter

不建议直接替换 `ExperimentLogger`，它目前很适合保留做调试和诊断。

建议新增一个组件，例如：

- `TeleopEpisodeExporter`

职责：

- `BeginEpisode()`：清空缓存，写入 episode metadata
- `RecordStep()`：追加 `action`、`qpos`、`qvel`、`images`、`reward`
- `EndEpisode()`：把一个 episode flush 成可被 Python 导入的导出包

推荐的 hook 点：

- `EpisodeManager.StartEpisode()`
- `EpisodeManager.FixedUpdate()`
- `EpisodeManager.StopEpisode()`

这个 exporter 可以有三种实现方式：

- 每个 episode 输出一份 JSONL + 图像目录
- 每个 episode 输出一个紧凑的 `.npz` / 二进制文件
- 直接发给 Python sidecar 进程

第一版最简单的实现建议是：

- JSON metadata / JSONL step 数据
- 每 step 一张 PNG 或一份原始 RGB 数据
- 之后由 Python converter 统一转成 HDF5

### 4. `AgxSimStepAckServer` 保留给后续在线 backend

step-ack bridge 依然有用，但更适合下一阶段：

- 把 HDF5 action replay 回 AGX
- 用 testbed 对 AGX 做 eval
- 比较 MuJoCo 和 AGX rollout

在变成可用的 ACT replay/eval backend 之前，它还至少需要补齐：

- `qpos` 从 3 维扩到 4 维
- image 支持
- `reset_pose`

## 关于 reset：确实需要一个“全场景、无物理扰动”的重置

是的，做 imitation learning 时，必须尽量把每个 episode 的初始状态收敛到同一个可控分布，否则：

- 人类示教会混入大量“起始姿态误差补偿”
- ACT 学到的就不只是作业动作，还会学到“先把场景修回去”
- 训练和评估都会变得不稳定

对于这里的目标，reset 不需要“真实”，更应该追求：

- 初始状态一致
- 不引入 reset 过程本身的动力学扰动
- reset 完成后第一帧就是干净状态

也就是说，这里推荐的是一种 **magic reset**，而不是“靠物理慢慢回位”的 reset。

## 推荐的 reset 方法：冻结仿真 + 硬设置状态 + 再恢复

推荐流程如下：

```text
停止 episode
  -> 禁止新的控制输入
  -> 关闭自动 stepping / 暂停仿真
  -> 清空当前控制器输出
  -> 将挖机、铲斗、目标物体直接 teleport 到预设状态
  -> 清零线速度/角速度/关节速度
  -> 重建 terrain / 清空 bucket 质量统计
  -> 强制做一次同步或 forward
  -> 重新开始 episode
```

核心思想是：

- reset 期间不要让物理系统“自己过渡”
- 不要用速度控制把机器慢慢开回起始位
- 直接写目标状态，然后把速度清零

这样 reset 不会在数据里留下额外动力学痕迹。

## 为什么当前 reset 还不够

当前工程里的 reset 链路大致是：

- `EpisodeManager.ResetEpisode()`
  - `StopEpisode("reset")`
  - `m_sceneResetService?.ResetScene()`
- `SceneResetService.ResetScene()`
  - `MassVolumeCounter.ResetMeasurements()`
  - `ResetTerrain.ResetTerrainHeights()`
  - 或 terrain fallback reset

这条链路能做的主要是：

- 停止当前动作
- 重置 terrain
- 清空 bucket 质量计数

但它**没有**做：

- 挖机底盘位姿 reset
- boom/stick/bucket/swing 关节位姿 reset
- 目标物体 teleport reset
- 刚体速度 / 角速度 / 关节速度清零

所以它还不算 imitation learning 所需的“完整 reset”。

## 一个可落地的 reset 设计

建议把 `SceneResetService` 扩成两段式 reset：

### 第 1 段：FreezeReset

目标是把一切会产生物理演化的东西先冻结住。

建议动作：

1. 禁用 episode 输入链路
   - 停止 `EpisodeManager`
   - 清空 `LastRawCommand` / `LastActuationCommand`
2. 清零执行器目标
   - 调 `m_machineController.StopMotion()`
3. 关闭自动 stepping
   - 如果有 `Simulation.Instance`，把 `AutoSteppingMode` 设成 `Disabled`
4. 暂停 reset 期间的 log / exporter 采样
5. 暂停任何会自动刷新状态的脚本
   - 例如自动控制器、自动源、可能影响 transform 的脚本

### 第 2 段：MagicApplyResetState

目标是直接把场景写回一个预定义 snapshot。

建议把 snapshot 拆成这些部分：

- 机器根节点 pose
- swing / boom / stick / bucket 初始位置
- 所有关节速度 = 0
- 机器底盘线速度 / 角速度 = 0
- 目标物体 pose
- 目标物体速度 / 角速度 = 0
- terrain 初始形状
- `mass_in_bucket_kg = 0`

### 第 3 段：PostResetStabilize

目标是让 Unity/AGX 内部状态完成同步，但不让它经历一个真实动力学“回正过程”。

建议动作：

1. 重置 observation sampler
   - `m_observationCollector.ResetSampling()`
2. 强制一次场景同步
   - 如果 AGX 需要 forward/sync，就做一次
3. 如有必要，手动做 1 次空步进
   - 前提是 action = 0，且状态已经硬设置完成
4. 读取一次 observation，确认：
   - 所有关节位置在初始范围内
   - 所有速度接近 0
   - bucket 质量为 0
5. 只有确认状态干净后才允许重新开始 episode

## 具体推荐：用“快照重置”，不要用“回正重置”

推荐维护一份 `ResetSnapshot`，保存：

- 挖机根节点世界坐标
- swing/boom/stick/bucket 的初始值
- 目标物体世界坐标
- terrain 初始参数

reset 时直接应用这份 snapshot。

不推荐的方式是：

- 给关节一个反向速度，让它自己转回起点
- 让物体靠碰撞慢慢掉回去
- reset 后等待几秒自然稳定

这些做法都属于“物理回正”，会让 reset 本身变成数据分布的一部分。

## 实现层建议

建议新增一个显式接口，例如：

```csharp
public struct ExcavatorResetSnapshot
{
  public Vector3 MachinePosition;
  public Quaternion MachineRotation;
  public float SwingPositionNorm;
  public float BoomPositionNorm;
  public float StickPositionNorm;
  public float BucketPositionNorm;
  public Vector3 TargetPosition;
  public Quaternion TargetRotation;
}
```

然后在 `SceneResetService` 里增加一个更完整的方法，例如：

```csharp
public bool ResetSceneHard( ExcavatorResetSnapshot snapshot )
```

它内部可以分成：

- `BeginResetFreeze()`
- `ApplyResetSnapshot(snapshot)`
- `ResetTerrainAndCounters()`
- `ClearVelocities()`
- `FinalizeReset()`

## 清速度是必须的

如果只改 pose，不清速度，下一帧通常还是会“弹一下”。

因此 reset 时必须同时处理：

- 底盘刚体线速度
- 底盘刚体角速度
- swing/boom/stick/bucket 当前速度
- 目标物体刚体线速度
- 目标物体刚体角速度

原则上应该做到：

- pose 是初始 pose
- velocity 全部为 0
- actuator target 也是 0

这样 reset 完成后的第一步不会带着历史惯性。

## terrain 也应该看成 snapshot，而不是物理恢复

对挖机任务来说，terrain 是任务初始条件的一部分，不是必须靠物理自然恢复的对象。

所以推荐：

- reset 时直接重建 terrain 高度图
- 然后把 bucket 质量统计清零
- 不要让土堆通过若干秒仿真“慢慢稳定”

当前 `SceneResetService` + `MassVolumeCounter` 已经有这部分基础，可以继续沿用，但要把它并入完整 hard reset 流程里。

## 对 imitation learning 最重要的 reset 验收标准

不是“看起来像真实 reset”，而是下面这些条件：

1. reset 后第 0 帧观察值稳定
2. 连续做两次 reset，得到的初始 observation 基本一致
3. reset 过程不写入训练数据
4. reset 后第一步 action=0 时，系统不会明显漂移
5. bucket 内质量、关节速度、物体速度都接近 0

## 最直接的工程建议

如果你现在就要落地，我建议这样做：

1. 保留现有 `SceneResetService.ResetScene()` 负责 terrain 和计数器
2. 在它前后新增一层 `HardResetCoordinator`
3. `HardResetCoordinator` 负责：
   - freeze simulation
   - 停输入、停控制、停记录
   - teleport 机器和目标物体
   - clear velocity
   - 调 `ResetScene()`
   - 做一次 post-reset sync
4. `AgxSimStepAckServer.reset_req` 和 `EpisodeManager.ResetEpisode()` 最终都走这套 hard reset

这样你就能得到一个对 imitation learning 更合适的“魔法 reset”，而不是带物理副作用的 reset。

## testbed 侧需要改什么

### 1. 增加一个 Unity 日志导入器

建议增加一个很小的 Python importer，例如：

- `testbed/data/importers/agx_unity.py`
- `testbed/cli/import_unity_teleop.py`

它的工作流程应该是：

1. 读取 Unity 导出的一个 episode
2. 把 Unity 字段映射成 `EpisodeRecorder.record()` 需要的原始 observation dict
3. 调用 `EpisodeRecorder.save()`

导入时构造 observation 的方式可以像这样：

```python
obs = {
    "qpos": np.asarray(step["qpos"], dtype=np.float32),
    "qvel": np.asarray(step["qvel"], dtype=np.float32),
    "images": {"fpv": frame_uint8},
}
recorder.record(obs, np.asarray(step["action"], dtype=np.float32), reward=step["reward"])
```

### 2. 增加一份 AGX 挖机专用训练配置

建议为 AGX 遥操作数据单独写一份 ACT config，例如：

```yaml
task:
  task_name: excavator_agx_teleop
  equipment_model: excavator_simple
  dataset_dir: data_agx_excavator_teleop
  num_episodes: 100
  camera_names: ["fpv"]
policy:
  class: ACT
```

### 3. 如果 Unity 还保持 3 维 `qpos`，就不要指望当前 loader 能直接用

当前 loader 会明确丢弃 `action_dim != qpos_dim` 的 episode。

也就是说，这种数据一定会失败：

- `action.shape[-1] == 4`
- `qpos.shape[-1] == 3`

因此只能二选一：

- 要么修 Unity，让它导出 4 维 `qpos`
- 要么连 testbed dataset loader 和 excavator ACT model 一起改

前者更小、更干净。

## 这次 review 明确看到的具体不匹配点

### Unity 侧

- `AgxSimStepAckServer` 当前报告：
  - `action_order = [swing_speed_cmd, boom_speed_cmd, stick_speed_cmd, bucket_speed_cmd]`
  - `qpos_order = [boom_position_norm, stick_position_norm, bucket_position_norm]`
  - `supports_images = false`
  - `supports_reset_pose = false`
- `ActObservationCollector` 当前没有 swing position normalization
- `TrackedCameraWindow` 已经有 render texture，但没有导出 frame 的 API
- `ExperimentLogger` 已经能写 step record，但不是 HDF5

### testbed 侧

- `EpisodeRecorder` 和 `write_episode()` 已经是现成可复用的 HDF5 写入逻辑
- `get_norm_stats()` 和 `load_data()` 会拒绝 `action_dim != qpos_dim` 的数据
- excavator ACT 模型构建阶段假设 `state_dim = 4`

## 最小实现顺序

1. 在 Unity 里补 `swing_position_norm`
2. 在 Unity 里补 FPV 抓帧 API
3. 在 Unity 里加一个 `TeleopEpisodeExporter`
4. 先从 Unity 导出一个中间格式 episode
5. 在 testbed 里加一个 Python importer，把它转成 `episode_0.hdf5`
6. 检查生成结果：
   - `/observations/qpos` 存在，shape 是 `(T, 4)`
   - `/observations/qvel` 存在，shape 是 `(T, 4)`
   - `/action` 存在，shape 是 `(T, 4)`
   - `/observations/images/fpv` 存在
7. 再把 ACT 训练配置指向这份数据集

## 验收标准

满足下面这些条件，就算这条链路打通了：

- Unity 能完整录制一个人类控制 episode，不需要手工后处理
- Python importer 能用现有 testbed writer 生成 `episode_N.hdf5`
- `qpos_dim == action_dim == 4`
- 数据里有 `fpv` 图像
- testbed training loader 不再跳过 AGX episode
- `python -m testbed.cli.train --config <agx_excavator_config>` 能在这份数据集上正常启动

## 结论

如果目标是“尽快把 AGX 遥操作演示训练成可用的 ACT”，推荐方案是：

- 在 Unity 里按**执行命令层**记录人类操作
- 把 Unity `qpos` 扩成 4 维，补上 swing position
- 从 `TrackedCameraWindow` 采集 `fpv` 图像
- 用 Python 侧现有 `EpisodeRecorder` / `write_episode()` 把 Unity 导出结果转成 HDF5

不建议先做两件事：

- 不建议先让 Unity 直接写 HDF5
- 不建议先走 6 维 `OperatorCommand` ACT 方案，除非你准备同步重构 testbed ACT 栈
