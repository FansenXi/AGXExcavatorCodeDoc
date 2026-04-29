# AGXUnity 挖掘机场景 - 当前 V0 参考

**状态：** Unity / AGX 侧当前英文真值文档的中文镜像  
**最后更新：** 2026-03-25  
**权威版本：** `Docs/scene.md` 为英文真值源；本文件仅用于阅读。如果两者有差异，以英文版为准。

这份文档不再是实现计划，而是描述当前 Unity 仓库和已连接 Python testbed 工作流中**已经落地的场景与任务契约**。

如果本文件与旧草案冲突，优先级如下：

1. `Docs/scene.md`
2. `Docs/protocol.md`
3. 当前代码与场景资源

## 1. 范围

当前 V0 场景是一个固定 reset 的挖掘任务，具有：

- 固定初始姿态的挖掘机
- 固定土堆
- 运行时可切换的当前接料目标
- 仅 4 维臂控：`swing / boom / stick / bucket`
- FPV 图像导出
- 基于质量的任务信号
- 基于距离的目标接近 / 近碰撞信号
- 当前激活目标的硬碰撞摘要导出

当前 step-ack 契约明确**不包含**：

- `drive / steer / track` 进入动作空间
- 显式阶段标签导出
- 把完整 collision/contact 事件导出作为 V0 必选能力

## 2. 任务定义

当前任务定义为：

> 在固定 reset 场景中，仅通过
> `swing / boom / stick / bucket`
> 控制挖掘机，执行一次或多次
> scoop -> transport -> dump -> retain
> 循环，并让足够多的物料最终稳定保留在当前激活的倾倒目标内部。

当前可选目标有：

- `ContainerBox`
- `TruckBed`

这个任务定义是**以目标保留质量为中心**的，而不是以 bucket 质量为中心。bucket 质量仍然会导出，也仍然有分析价值，但当前 mission 的完成标准是“目标中的保留质量”。

## 3. 已实现内容

### 3.1 场景与目标

当前主场景已经提供：

- 固定挖机初始位姿
- 固定土堆 / dig zone
- 一个用透明填充加彩色轮廓高亮显示的场景 `DigArea` 引导区域
- 用于 step-ack 的 FPV 相机
- 静态刚性 `ContainerBox` 目标
- 支持运行时目标切换的 `BedTruck`

运行时目标路由已经实现，因此同一组导出字段名始终指向**当前激活目标**。
运行时 HUD 现在也会直接显示 DigArea good-start 状态、DigArea 接触状态，以及
bucket 低于 DigArea 平面的深度，方便人工联调时快速确认起挖是否合格。
针对 V2 phase-1 的 teleop 录制，HUD 现在还会显示一个 `Ready Anchor Guide`，
把当前归一化 `qpos` 和 phase-1 `anchor_mid` 的参考值
`qpos_ref = [0.52, 0.41, 0.63, 0.28]` 以及容差
`qpos_tol = [0.06, 0.05, 0.05, 0.05]` 做实时对比，并用颜色和各关节剩余偏差提示
操作者何时已经从 dump 回到可以停录的 ready 姿态。
场景里还会生成一个 3D `ReadyAnchorWorldMarker`，把最近一次确认命中的
ready-anchor bucket 世界位置标出来，并用彩色连线连接当前 bucket 和目标点，
这样操作者可以直接在空间里判断回位终点，而不只依赖 HUD 文字。
当 `AgxSimStepAckServer` 开始服务并临时禁用 `EpisodeManager.Update()` 时，
HUD 现在会回退到 `ActObservationCollector` 最近一次 task-state 采样，继续显示
实时的质量、目标距离、DigArea 和当前激活目标硬碰撞摘要，而不是停留在
EpisodeManager 侧已经不再刷新的缓存值。

### 3.2 目标质量测量

当前 Unity 已支持：

- 统计当前激活目标测量体积内的质量
- 导出相对 reset 基线的净沉积质量
- 在 `ContainerBox` 与 `TruckBed` 之间运行时切换目标
- 跨所有活跃 `DeformableTerrainBase` 实例聚合
- 计入 `HandleAsParticle` 动态刚体，例如 `Dynamic Rock`

truck 相关特殊处理也已实现：

- 在 AGX 初始化前禁用 truck bed 的 `MovableTerrain` 辅助对象，使倾倒土壤保持为动态粒子
- 重新启用现有车斗支撑 `Box` 碰撞
- 用车斗支撑 `Box` 几何加上顶部 headroom 推导 truck 测量体积

### 3.3 距离导出

当前 V0 契约已经导出：

- `min_distance_to_target_m`

其定义是：

- 当前 bucket 的 target-distance proxy 体积
- 到当前激活目标的 target-distance volume

之间的近似最小距离。

当前行为：

- 它是基于距离的
- 它不是基于碰撞事件的
- 当前场景默认使用 `ExcavationMassTracker` 上可在 Editor 里直接调参的 bucket proxy volume
- target 一侧现在优先使用当前目标自己的 hard box shapes；只有拿不到这些 shape 时，才会回退到 target-distance volume
- 对 `TruckBed` 来说，这意味着距离现在优先对 truck hard-body 的 box 几何测量，而不是对 bed 的接料 headroom 量测体积测量
- 如果场景里没有这套专用 proxy 配置，Unity 会回退到旧的 bucket 几何来源
- 与质量信号一起进入 `env_state`
- 无法计算时返回 `-1.0`

### 3.4 当前激活目标硬碰撞导出

当前 Unity 场景还会导出两个“当前激活目标硬碰撞摘要”信号：

- `target_hard_collision_count`
- `target_contact_max_normal_force_n`

当前行为：

- 源 shape 是 excavator root 下所有启用的 AGX `Collide.Shape`，覆盖 bucket / arm / chassis
- 目标 shape 由当前激活目标传感器提供的硬表面 shape 集合决定
- 当激活目标是 `TruckBed` 时，这个硬表面 shape 集合覆盖整台 `BedTruck` 的碰撞体，而不只是 bed / trunk 量测区域
- `target_hard_collision_count` 是当前 episode 内的累计硬碰撞计数
- 一次连续的 excavator-vs-target 接触 session，`target_hard_collision_count` 最多只会增加一次
- 只要 excavator 还持续贴着 target，这个计数就不会每帧继续上涨
- 只有在 excavator 先离开 target、之后再次发生新的合格接触时，计数才会再次增加
- 当前场景默认阈值是 `hard_collision_normal_force_thresh_n = 5000.0`
- `target_contact_max_normal_force_n` 记录刚完成这一步中，监控到的最大接触法向力标量
- 这两个字段是 reward / 诊断用摘要信号，不会替代当前基于质量的成功定义

### 3.5 Reset

当前 reset 路径已经会恢复：

- 挖机位姿与臂状态
- truck 刚体 / 约束状态
- terrain 状态
- 目标质量计数器
- bucket / target 的测量基线

当前 reset 实现现在会额外做两件事：

- 在重建 terrain 之前，先把 snapshot 里的 Transform 链和 reset 范围内的
  rigid body 一起恢复到开场 authored 基线
- 在 AGX warm-up step 之后，再重复一次 Transform 链和 rigid body 恢复

这样是专门为了解决两类偶发伪影：

- 上一回合 bucket / 履带的接触姿态泄漏进 terrain 重建流程，导致地面在错误
  位置鼓起或抬高
- reset 之后 terrain 和其他参考物已经不再共用同一条基线，视觉上像是“除了
  terrain 之外其他东西都跳了几米”

另外，初始 reset snapshot 现在也会优先在 `Awake` 抓取；只有 `Awake`
阶段仍然没有抓到可用 snapshot 时，才会退回第一帧 `FixedUpdate`。这样可以
避免把开场瞬间的临时 bucket / 履带姿态误记成 reset 基线。

这次的 Transform snapshot 也不再只盯着 terrain 自己。因为当前场景里有些
reset 相关参考物挂在带偏移的 `=== Scene ===` 父节点下面，而 `DigArea`、
control rig 等对象又是独立 root，所以 full reset 现在会把这些参考节点的
Transform ancestry 一起记下来并恢复，确保 mixed-root 场景也会回到同一个
authored baseline。

这里还有一个很重要的例外：`DeformableTerrain` 自己的 Transform 不能被当成
普通静态物体那样直接拉回 pre-initialize 的 authored 值。AGX terrain 在
runtime 初始化时会按照 `MaximumDepth` 主动把 Unity terrain 往下移，并在
后续 terrain recreate 流程里自己维护这条偏移链。当前场景的
`m_maximumDepth = 2`，所以如果 reset 再把 terrain 自己硬拉回 authored pose，
就会重新引入一个固定的 2 米竖向错位。现在 full reset 会恢复 terrain 周围的
共享父链，但不会再强制恢复 deformable terrain 自身的 Transform。

当前 reset 目标是**稳定基线复现**，不是严格的带种子确定性。

### 3.6 Step-Ack Bridge

当前 Unity bridge 已支持：

- `DoStep()` 手动步进
- 二进制 framed TCP step-ack
- FPV 原始 RGB 导出
- 4 维 `qpos`
- 4 维 `qvel`
- 9 维 `env_state`

step-ack 导出路径中的 DigArea 几何测量本来就走 `ActObservationCollector`，
并不依赖 `EpisodeManager` 在 server 监听期间保持启用。

V2 phase-1 录制时，场景里的 3D `ReadyAnchorWorldMarker` 现在会在进入 Play
Mode 后立刻用当前 ready / reset pose 的 bucket 世界位置做一次 bootstrap，
不再要求操作者先命中一次 ready anchor 才看得到。之后一旦真正命中 ready
anchor，这个 marker 会自动升级成 confirmed pose。HUD 里的 `World marker`
状态也会明确显示当前是 bootstrap 还是 confirmed。
另外，如果 collector 这时还没有留下上一帧 sample，HUD 现在会直接从当前
rig 的约束读取归一化 `qpos` 来计算 ready 状态，因此不会长期停在
`waiting for actuator sample`。
同时，为了降低人工录制难度，当前 phase-1 场景里的 V2 teleop ready gate
也已经放宽到 `mean(abs((qpos - qpos_ref) / qpos_tol)) <= 1.35`；也就是说，
HUD 会更早进入绿色，不再要求操作者把机械臂硬拉回一个过于苛刻的“完美中立”
姿态才能停录。

## 4. 当前导出契约

当前导出观测包括：

- `images["fpv"]`
- `qpos`
- `qvel`
- `env_state`

当前 `env_state` 顺序为：

`[mass_in_bucket_kg, excavated_mass_kg, mass_in_target_box_kg, deposited_mass_in_target_box_kg, min_distance_to_target_m, target_hard_collision_count, target_contact_max_normal_force_n, min_distance_to_dig_area_m, bucket_depth_below_dig_area_plane_m]`

字段语义：

- `mass_in_bucket_kg`：bucket 内动态物料估计质量
- `excavated_mass_kg`：bucket 侧 tracker 给出的当前挖掘进度信号
- `mass_in_target_box_kg`：当前激活目标中的实时保留质量
- `deposited_mass_in_target_box_kg`：相对 reset 基线的目标净保留质量
- `min_distance_to_target_m`：bucket target-distance proxy 到当前激活目标的近似最小距离
- `target_hard_collision_count`：当前 episode 内累计的监控 excavator-vs-active-target 硬碰撞次数
- `target_contact_max_normal_force_n`：每步中，监控 excavator-vs-active-target 接触的最大法向力（牛顿）
- `min_distance_to_dig_area_m`：bucket DigArea proxy volume 到场景 `DigArea` 的近似最小距离
- `bucket_depth_below_dig_area_plane_m`：当前 bucket DigArea proxy 相对 DigArea 中心平面的“有效下探深度”；Unity 会在 DigArea 局部坐标里对 proxy 采样，取低于平面的原始深度，再按样本点到 DigArea footprint 的 XZ 有符号距离做平滑加权，因此 bucket 在 footprint 外侧时该值会保持接近 `0`，接近并进入 DigArea 时才会连续上升

`min_distance_to_target_m` 现在优先使用 `ExcavationMassTracker` 上专门配置的
bucket target-distance proxy volume，并与当前激活目标自己的
distance geometry 计算距离。DigArea 相关距离字段则继续使用 bucket 质量统计
那套量测体积。step-ack 服务期间，这些 target / DigArea 指标会继续通过
`ActObservationCollector` 同步更新到 wire payload 和 HUD；真正暂停的只有
`EpisodeManager` 本地维护的 good-dig latch 逻辑。

精确 wire 细节请看 `Docs/protocol.md`。

## 5. Testbed 侧当前成功与奖励语义

当前已连接的 Python testbed 现在已经和“目标导向 mission”对齐。

当前 testbed 默认 AGX 成功规则：

- 信号：`deposited_mass_in_target_box_kg`
- 阈值：`100.0 kg`
- 保持时间：`25` 个 control step

这些只是**当前默认值**，不是最终调参结果。后续仍应通过 pilot target-mass run 再校准。

testbed 现在仍然在 Python 侧直接根据导出的 `env_state` 计算**主任务
reward**。

Unity 现在也会把主成功信号镜像到 `STEP_RESP.reward` 里，作为一个 backup
传输字段：

- `STEP_RESP.reward = deposited_mass_in_target_box_kg`

这个 Unity 侧 `reward` 是**备用成功代理信号**，不是 testbed 当前使用的主
shaped mission reward。

这个任务仍然被视为**单一连续 mission**，而不是要求 Unity 导出显式阶段
ID。reward 绑定在这个单一 mission 内的一些可观测子目标上：

1. `loading`
   bucket 在合格的 DigArea good start 之后开始获得有意义的土壤载荷。
   信号：`mass_in_bucket_kg`、`excavated_mass_kg`、
   `min_distance_to_dig_area_m`、`bucket_depth_below_dig_area_plane_m`
2. `approaching_target`
   已载荷的 bucket 向当前激活目标靠近。
   信号：`mass_in_bucket_kg`、`min_distance_to_target_m`
3. `depositing`
   当前激活目标中的保留质量开始增长。
   信号：`mass_in_target_box_kg`、`deposited_mass_in_target_box_kg`
4. `retained_success`
   当前激活目标中的净保留质量持续高于成功阈值足够久，任务判成功。
   信号：`deposited_mass_in_target_box_kg`

当前 reward 范围：

- `0.0` idle / 还没有有意义进展
- `0.0 - 1.0` loading 进展
- `1.0 - 2.0` 已载荷并向目标靠近
- `2.0 - 3.0` 正在向目标沉积
- `4.0` retained success 已满足

tracker 还会输出可选的逐步 success/fail 日志，例如
`good_dig_start`、`load_progress`、`approach_progress`、
`deposit_progress`、`load_outside_dig_area`、`spill_before_target`、
`unsafe_target_distance`、`hard_target_collision`，用于调试。这些日志属于
testbed 侧诊断信息，不属于 Unity wire protocol。

当前 testbed 惩罚行为：

- 如果某一步累计 `target_hard_collision_count` 发生增长，testbed 会施加一次固定 `hard_collision_penalty = 0.75`
- 这个惩罚不会改变成功定义
- Unity `STEP_RESP.reward` 仍然只镜像保留质量；碰撞惩罚仍放在 testbed 侧

## 6. 当前操作流程

当前 episode 流程应理解为：

1. reset 场景
2. 确认或设置当前激活目标
3. 从土堆装料
   当前期望的 good start 是：bucket 量测体接触 `DigArea` 区域，并在增载时进入 DigArea 平面下方
4. 将载荷搬运到选中目标
5. 向目标倾倒
6. 等待沉降并确认保留质量
7. 成功则结束，否则继续下一铲

当前任务**不要求** Unity 导出显式 stage ID。阶段解释应由以下信号共同推断：

- `mass_in_bucket_kg`
- `mass_in_target_box_kg`
- `deposited_mass_in_target_box_kg`
- `min_distance_to_target_m`
- 机械臂姿态与 FPV 图像

## 7. 已完成事项

以下过去属于计划项的内容，现在已经足够成熟，可以视为当前场景行为：

- 固定 V0 场景布局
- 二进制 step-ack 导出
- FPV 导出
- 目标质量导出
- reset-relative deposited mass 导出
- truck 目标接入
- 运行时目标切换
- truck-inclusive reset
- 距离导出
- 当前激活目标硬碰撞摘要导出
- testbed 侧 AGX mission reward
- testbed 侧按字段名配置成功判定

因为这些内容已经实现，本文件不再保留旧的“实现清单 / 验证计划”式结构。

## 8. 未决项与非目标

以下内容仍然是当前 V0 中有意保留的 open item 或非目标：

- 完整 collision/contact 事件导出还不是当前主契约的一部分；当前只导出面向当前激活目标的硬碰撞摘要信号
- `drive / steer / track` 不在当前 step-ack 动作空间中
- 不导出显式 phase labels
- 成功阈值仍需要通过 pilot data 继续调参
- 当前没有导出比“近似距离 + 当前激活目标硬碰撞摘要”更细的精确几何碰撞风险字段

## 9. 后续更新规则

后续所有场景 / 任务决策，必须先写入英文版 `Docs/scene.md`。

中文版：

- 仅用于阅读
- 必须与英文版保持同步
- 如果文字有冲突，不得取代英文版成为决策依据
