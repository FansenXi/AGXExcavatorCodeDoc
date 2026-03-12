# 挖机 ACT Input API 与当前游戏场景兼容性说明

## 1. 文档目的

本文说明当前 `AGXUnity_Excavator` 场景与 ACT 接口的实际兼容状态。

这里不再讨论“理论上应该怎么设计”，而是直接基于当前代码回答三件事：

1. 现在哪些 ACT 相关接口已经落地
2. 还有哪些环节仍然缺失或需要校准
3. 现有场景应如何接 ACT backend

---

## 2. 结论摘要

截至 2026-03-12，当前结论与早期版本已有明显变化：

1. 当前场景从 `OperatorCommandSourceBehaviour` 往后，到 `OperatorCommandSimulator`、`ExcavatorCommandInterpreter`、`ExcavatorMachineController`、`ExperimentLogger`、`ExperimentHUD`、`EpisodeManager` 的主链已经可以直接承接 ACT 输入。
2. `ActOperatorCommandSource`、`ActObservationCollector`、`ActBackendClientBehaviour`、`TcpJsonLinesActBackendClient` 都已经在 `Assets` 中实现。
3. `EpisodeManager` 已经泛化为 `OperatorCommandSourceBehaviour`，不再写死 `KeyboardOperatorCommandSource`。
4. ACT 响应的有限值检查、去除 episode bool、clamp、最新有效命令缓存、超时回零、HUD 诊断都已经实现。
5. 当前主要缺口已经不再是 Unity 控制链，而是：
   - Python backend 服务端仍不在本仓库内
   - observation 归一化范围需要按 prefab 校准
   - CSV 日志还没有写入 ACT 专用诊断字段
   - 场景 prefab 仍需要正确挂线和联调验证

一句话概括：

当前 Unity 侧 ACT 接口已经落地，兼容重点从“补架构”转成了“配线、校准、联调和日志细化”。

---

## 3. 当前已经具备的兼容基础

### 3.1 通用输入源链已经打通

当前 `EpisodeManager` 使用的是：

- `OperatorCommandSourceBehaviour m_commandSource`

这意味着输入源已经泛化，不再限定为键盘。场景层面可以直接切换：

- `KeyboardOperatorCommandSource`
- `ActOperatorCommandSource`

而不需要改后半段执行链。

### 3.2 ACT Source 已经实现

当前 `ActOperatorCommandSource` 已经实现以下能力：

- 作为 `OperatorCommandSourceBehaviour` 被 `EpisodeManager` 直接消费
- 作为 `IEpisodeLifecycleAware` 接收回合开始/停止事件
- 作为 `IActCommandDiagnostics` 向 HUD 暴露 backend 状态
- 以 `m_observationRateHz` 定期发送 observation
- 缓存最近一次有效命令
- backend 未 ready 时输出全零
- 响应超时后输出全零
- 非法响应直接丢弃

### 3.3 Observation Collector 已经实现

当前 `ActObservationCollector` 已经能采集：

- `sim_time_sec`
- `fixed_dt_sec`
- `base_pose_world`
- `base_velocity_local`
- `bucket_pose_world`
- `actuator_state`
- `task_state`
- `previous_operator_command`

这说明 observation 不是“还没做”，而是已经有一个可运行的第一版实现。

### 3.4 TCP JSON Lines backend client 已经实现

当前 `TcpJsonLinesActBackendClient` 已实现：

- `hello`
- `reset`
- `step`
- `close`
- `step_result` 解析
- 后台线程通信
- 自动重连
- 最近结果缓存

因此 Unity 侧通信层也已经具备。

### 3.5 HUD 对 ACT 已有专用诊断信息

当前 `ExperimentHUD` 在 ACT 模式下会显示：

- `Backend ready`
- `Timeout`
- `ACT seq`
- `Infer`
- `ACT session`
- `Status`

这已经足够支撑基础联调。

---

## 4. 当前场景的真实 ACT 接口语义

### 4.1 ACT 对 Unity 的唯一输出仍然是 6 轴 `OperatorCommand`

ACT 应输出：

```json
{
  "left_stick_x": 0.0,
  "left_stick_y": 0.0,
  "right_stick_x": 0.0,
  "right_stick_y": 0.0,
  "drive": 0.0,
  "steer": 0.0
}
```

ACT 不应输出：

- `ResetRequested`
- `StartEpisodeRequested`
- `StopEpisodeRequested`
- `Boom/Bucket/Stick/Swing/Throttle`

### 4.2 当前解释器映射仍然是接口真值源

当前场景实际执行的映射是：

- `LeftStickX -> Swing`
- `LeftStickY -> Stick`
- `RightStickX -> Bucket`
- `RightStickY -> Boom`
- `Drive -> Drive`
- `Steer -> Steer`

附加规则：

- `Boom` 缩放 `0.3`
- `Bucket` 缩放 `0.7`
- `Stick` 缩放 `-0.7`
- `Swing` 缩放 `0.6`
- `Swing` 死区 `0.3`
- `Throttle` 由 `Drive/Steer` 自动生成

因此训练和联调必须以当前 `ExcavatorCommandInterpreter` 为准。

### 4.3 生命周期边界仍由 Unity 掌控

ACT source 虽然能接收 `OnEpisodeStarted/OnEpisodeStopped`，但回合控制仍然属于 Unity：

- `EpisodeManager.StartEpisode()`
- `EpisodeManager.StopEpisode()`
- `EpisodeManager.ResetEpisode()`

ACT backend 只响应生命周期消息，不负责发起生命周期决策。

---

## 5. 当前实现状态总表

| 能力项 | 当前状态 | 说明 |
| --- | --- | --- |
| 通用输入源接口 | 已实现 | `EpisodeManager` 已使用 `OperatorCommandSourceBehaviour` |
| ACT Source | 已实现 | `ActOperatorCommandSource` 已可直接接入 |
| observation 采集 | 已实现 | `ActObservationCollector` 已可输出一版完整状态量 |
| backend client 抽象 | 已实现 | `IActBackendClient` / `ActBackendClientBehaviour` 已存在 |
| 本地 TCP 协议 | 已实现 | `TcpJsonLinesActBackendClient` 已实现 JSON Lines 客户端 |
| 响应清洗与超时保护 | 已实现 | finite check、clamp、latest valid、timeout fallback |
| HUD 诊断 | 已实现 | backend/seq/inference/session/status 已显示 |
| CSV ACT 诊断字段 | 未完成 | 目前日志未记录 session/seq/status/inference |
| Python backend 服务端 | 仓库外依赖 | 当前 repo 内未发现实现 |
| observation 校准 | 部分完成 | 范围对象已实现，但默认值仍需校准 |
| 场景配线验证 | 需要逐场景确认 | 组件是否完整挂到 rig 上需实机验证 |

---

## 6. 当前剩余的兼容缺口

### 6.1 Python backend 不在仓库内

这是当前最主要的缺口。

Unity 侧已经有：

- 协议定义
- TCP 客户端
- response 解析
- fallback 逻辑

但仓库内没有配套 Python 服务端，因此实际闭环仍依赖外部 backend。

### 6.2 归一化范围还需要真实标定

`ActObservationCollector` 虽然已经提供：

- `m_boomRange`
- `m_stickRange`
- `m_bucketRange`

但默认 `min/max` 只是占位值，联调前需要依据当前挖机约束实际范围完成标定。

否则：

- `*_position_norm` 的语义不稳定
- 不同 prefab / 场景间 observation 尺度会漂移

### 6.3 观测参考系仍需按实验需求确认

当前实现中：

- `base_pose_world` 默认取 `m_excavator.transform`
- 若挖机引用为空则退回 `ActObservationCollector` 所在 transform

这对第一版联调足够，但如果后续任务要求固定 observer frame、底盘 frame 或其他参考点，仍需进一步明确。

### 6.4 CSV 日志对 ACT 还不够完整

当前 `ExperimentLogger` 只记录：

- source
- raw command
- simulated command
- final actuation command
- bucket pose
- 质量 / 体积

还没有记录：

- `session_id`
- `seq`
- `backend_ready`
- `status`
- `inference_time_ms`
- `timeout_fallback`

因此如果要做系统级联调回放，日志字段仍需扩展。

### 6.5 场景与 prefab 的组件挂线需要实机确认

虽然代码已经有完整接口，但每个实际场景仍需检查：

- `EpisodeManager.m_commandSource` 是否指向 ACT source
- `ActOperatorCommandSource` 是否拿到 collector 和 backend client
- `ActObservationCollector` 是否解析到 `ExcavatorMachineController`
- `MassVolumeCounter` 是否存在
- bucket reference 是否有效

这属于工程配置问题，不再是架构缺失问题。

---

## 7. 推荐接入方式

推荐在当前 rig 上按以下顺序接入：

1. 保留 `OperatorCommandSimulator`、`ExcavatorCommandInterpreter`、`ExcavatorMachineController` 不变
2. 在同一 rig 上挂 `ActOperatorCommandSource`
3. 挂 `ActObservationCollector`
4. 挂 `TcpJsonLinesActBackendClient`
5. 将 `EpisodeManager.m_commandSource` 指向 `ActOperatorCommandSource`
6. 校准 observation normalization ranges
7. 启动 Python backend
8. 在 HUD 中验证 `backend_ready/session_id/seq/status`
9. 最后再做动作语义和任务指标联调

当前最不建议的做法仍然是：

- 让 ACT 去模拟键盘事件
- 绕过 `OperatorCommandSimulator`
- 直接改 AGX package 内旧控制器
- 让 ACT backend 直接输出执行器速度

---

## 8. 推荐后续工作优先级

按当前状态，建议优先级如下：

1. 提供可用的 Python `step_result` 服务端
2. 校准 `boom/stick/bucket` 归一化范围
3. 逐场景检查 rig 组件挂线
4. 扩展 `ExperimentLogger` 的 ACT 诊断字段
5. 如有需要，再细化 `base_pose_world` 的参考系定义

---

## 9. 结论

当前 `AGXUnity_Excavator` 对 ACT 的兼容性结论应更新为：

- Unity 侧 ACT 接口已经基本落地，不再是“只有设计没有实现”
- 当前后半段执行链和 source 接线都已经具备 ACT 接入能力
- 剩余问题主要集中在 Python backend、校准和联调细节，而不是架构重写

因此，下一阶段工作重点不应再是“重新设计 ACT 接口”，而应是“用当前接口把 ACT backend 接起来并完成验证”。
