# ACT Input API Draft v2

## 1. 文档目的

本文描述当前 Unity 侧已经实现的 ACT 接口，并明确 Python ACT backend 需要遵守的协议与语义。

本文以以下源码为准：

- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/Control/Core/OperatorCommand.cs`
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/Control/Sources/ActOperatorCommandSource.cs`
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/Control/Sources/ActObservationCollector.cs`
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/Control/Sources/ActProtocol.cs`
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/Control/Sources/TcpJsonLinesActBackendClient.cs`
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/Experiment/EpisodeManager.cs`

如果文档与代码冲突，以代码为准。

---

## 2. 系统边界

ACT 在当前工程中的角色边界如下：

- Unity/AGX 负责仿真、场景、控制解释、机器执行、回合生命周期
- Python backend 负责 ACT 推理，并返回操作员级命令
- ACT 不直接输出执行器速度，不直接控制回合开始/结束/重置

当前主链路为：

```text
ActObservationCollector
  -> TcpJsonLinesActBackendClient
  -> ActOperatorCommandSource
  -> OperatorCommandSimulator
  -> ExcavatorCommandInterpreter
  -> ExcavatorMachineController
  -> ExperimentLogger / ExperimentHUD / EpisodeManager
```

禁止的职责下放：

- ACT 输出键盘按键事件
- ACT 直接输出 `Boom/Bucket/Stick/Swing/Throttle`
- ACT 输出 `StartEpisode/StopEpisode/ResetEpisode`

---

## 3. 控制接口分层

### 3.1 `OperatorCommand`

ACT 唯一允许返回给 Unity 的控制对象是 `OperatorCommand` 的 6 个操作轴：

```csharp
public struct OperatorCommand
{
  public float LeftStickX;
  public float LeftStickY;
  public float RightStickX;
  public float RightStickY;
  public float Drive;
  public float Steer;

  public bool ResetRequested;
  public bool StartEpisodeRequested;
  public bool StopEpisodeRequested;
}
```

语义约束：

- ACT 只能设置前 6 个浮点轴
- 三个 episode bool 必须保持为 `false`
- Unity 会对 6 个轴统一做 `[-1, 1]` clamp
- Unity 会在接收后再次调用 `WithoutEpisodeSignals()`

### 3.2 `ExcavatorCommandInterpreter`

当前工程中 `OperatorCommand` 到挖机动作的真实映射为：

| `OperatorCommand` 字段 | 解释后的动作 | 当前缩放/规则 |
| --- | --- | --- |
| `LeftStickX` | `Swing` | `0.6` 缩放，且 `abs(value) < 0.3` 时回零 |
| `LeftStickY` | `Stick` | `-0.7` 缩放 |
| `RightStickX` | `Bucket` | `0.7` 缩放 |
| `RightStickY` | `Boom` | `0.3` 缩放 |
| `Drive` | `Drive` | 直接透传 |
| `Steer` | `Steer` | 直接透传 |

另外：

- `Throttle` 不由 ACT 输出
- `Throttle = 1` 当 `abs(Drive) + abs(Steer) > 0`
- 否则 `Throttle = 0`

### 3.3 `ExcavatorActuationCommand`

这是一层更低级的执行器命令：

```csharp
public struct ExcavatorActuationCommand
{
  public float Boom;
  public float Bucket;
  public float Stick;
  public float Swing;
  public float Drive;
  public float Steer;
  public float Throttle;
}
```

这层是 Unity 内部执行层接口，不是 ACT 对外协议。

---

## 4. Unity 侧核心接口

### 4.1 通用输入源接口

当前 `EpisodeManager` 已经不再写死键盘源，而是依赖：

```csharp
public interface IOperatorCommandSource
{
  string SourceName { get; }
  OperatorCommand ReadCommand();
}
```

Unity Inspector 中实际序列化类型为：

```csharp
public abstract class OperatorCommandSourceBehaviour : MonoBehaviour, IOperatorCommandSource
{
  public abstract string SourceName { get; }
  public abstract OperatorCommand ReadCommand();
}
```

因此键盘源、ACT 源都可以挂到同一条实验链上。

### 4.2 生命周期接口

ACT 源当前还实现了回合生命周期接口：

```csharp
public interface IEpisodeLifecycleAware
{
  void OnEpisodeStarted( EpisodeCommandSourceContext context );
  void OnEpisodeStopped( string reason );
}
```

其中上下文包含：

```csharp
public struct EpisodeCommandSourceContext
{
  public int EpisodeIndex;
  public float FixedDeltaTimeSec;
}
```

### 4.3 ACT 诊断接口

HUD 和实验管理层通过以下接口读取 ACT 状态：

```csharp
public interface IActCommandDiagnostics
{
  bool IsBackendReady { get; }
  bool IsCommandTimedOut { get; }
  int LastResponseSequence { get; }
  float LastInferenceTimeMs { get; }
  string CurrentSessionId { get; }
  string LastBackendStatus { get; }
}
```

### 4.4 `IActBackendClient`

当前 Unity 侧已经实现并使用如下 backend 抽象：

```csharp
public interface IActBackendClient
{
  bool IsReady { get; }

  void BeginEpisode( ActEpisodeConfig config, string sessionId );

  void EndEpisode( string reason, string sessionId, int seq );

  void SubmitObservation( ActStepRequest request );

  bool TryGetLatestResult( out ActStepResponse response );
}
```

实现要点：

- `BeginEpisode` 发送 `reset`
- `EndEpisode` 发送 `close`
- `SubmitObservation` 发送 `step`
- `TryGetLatestResult` 只返回“最近一条尚未被消费的新响应”

### 4.5 `ActOperatorCommandSource`

`ActOperatorCommandSource` 是 ACT 接入 Unity 的主入口，职责如下：

- 在回合开始时创建 `session_id`
- 周期性采样 observation
- 通过 backend client 发送 `step`
- 轮询 `step_result`
- 对返回值做校验、去除 episode bool、clamp
- 缓存最近一次有效命令
- backend 未就绪或命令超时时回零

主要序列化配置字段：

| 字段 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `m_taskName` | string | `excavator_dig_v1` | 写入 `reset.payload.task_name` |
| `m_observationRateHz` | float | `20.0` | 发送 observation 的目标频率 |
| `m_commandTimeoutMs` | int | `200` | 超时后回零 |
| `m_observationCollector` | `ActObservationCollector` | auto resolve | 观测采集器 |
| `m_backendClient` | `ActBackendClientBehaviour` | auto resolve | backend 通信组件 |
| `m_logInvalidResponses` | bool | `true` | 是否打印无效响应警告 |

运行时行为：

- 非回合状态：`ReadCommand()` 返回全零
- backend 未 ready：返回全零
- 有最近有效命令且未超时：返回该命令
- 超时或尚未收到第一条有效响应：返回全零

### 4.6 `ActObservationCollector`

`ActObservationCollector` 负责采样 `ActObservation`，当前字段来源如下：

| observation 字段 | 当前来源 |
| --- | --- |
| `sim_time_sec` | `Time.time` |
| `fixed_dt_sec` | `Time.fixedDeltaTime` |
| `base_pose_world` | `m_excavator.transform`，若为空则退回 `this.transform` |
| `base_velocity_local` | 基于最近两次 base pose 的有限差分，再转 local |
| `bucket_pose_world` | `ExcavatorMachineController.BucketReference` |
| `actuator_state.*_speed` | 对应 AGX 约束的 `GetCurrentSpeed()` |
| `actuator_state.*_position_norm` | 对应 AGX 约束值经过可配置范围归一化 |
| `task_state.mass_in_bucket_kg` | `ExcavationMassTracker.MassInBucket` |
| `task_state.excavated_mass_kg` | `ExcavationMassTracker.ExcavatedMass` |
| `task_state.mass_in_target_box_kg` | `TerrainParticleBoxMassSensor.MassInBox` |
| `task_state.deposited_mass_in_target_box_kg` | `TerrainParticleBoxMassSensor.DepositedMass` |
| `task_state.min_distance_to_target_m` | `BucketTargetDistanceMeasurementUtility` 基于 bucket target-distance proxy 与当前激活目标的 target distance geometry 计算 |
| `previous_operator_command` | 最近一次有效的 `OperatorCommand`，去除 episode bool 后序列化 |

补充说明：

- `task_state.mass_in_bucket_kg` / `task_state.excavated_mass_kg` 现在不仅包含 deformable terrain 返给 shovel 的动态土体质量，也会额外包含 bucket 测量体积内、被标记为 `HandleAsParticle` 的动态刚体质量
- `task_state.mass_in_target_box_kg` / `task_state.deposited_mass_in_target_box_kg` 同样同时覆盖 soil particles 与 `HandleAsParticle` 动态刚体，例如 `Dynamic Rock`
- `task_state.deposited_mass_in_target_box_kg` 现在表示“相对最新 reset 基线的净沉积质量”，不是历史累计正向流入量
- `task_state.min_distance_to_target_m` 现在表示 bucket target-distance proxy 到当前激活目标 target distance geometry 的近似最小距离；不可计算时为 `-1`

当前有三个归一化配置：

- `m_boomRange`
- `m_stickRange`
- `m_bucketRange`

其行为是：

- 使用 `Mathf.InverseLerp( min, max, value )`
- 输出范围固定为 `[0, 1]`
- 目前默认 `min=-1, max=1`

这意味着实际使用前应按当前 prefab 的真实行程做校准。

### 4.7 `TcpJsonLinesActBackendClient`

当前唯一已实现的 backend client 是本地 TCP JSON Lines 版本。

主要配置字段：

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `m_host` | `127.0.0.1` | Python backend 地址 |
| `m_port` | `5055` | 监听端口 |
| `m_connectOnEnable` | `true` | 组件启用时启动 worker |
| `m_autoReconnect` | `true` | 异常后自动重连 |
| `m_connectTimeoutMs` | `1000` | 连接超时 |
| `m_reconnectDelayMs` | `1000` | 重连间隔 |

运行特征：

- 后台线程名为 `ACT-TCP-Client`
- 每次 TCP 连接建立后都会先发送一次 `hello`
- 若当前存在 active episode，重连后会自动重发当前 `reset`
- `close` 等控制消息按队列顺序写入
- `step` 不做无限排队；断线期间只保留最新一条 observation 对应的待发送消息
- 客户端会主动检测对端关闭并触发重连
- 入站只解析 `type == "step_result"` 的 JSON 行
- 客户端只缓存“最新一条 step_result”

---

## 5. 协议版本与传输

### 5.1 当前协议

- 传输：`localhost TCP`
- 编码：`JSON Lines`
- 协议版本：`act-operator/v1`

统一头部格式：

```json
{
  "api_version": "act-operator/v1",
  "type": "step",
  "session_id": "ep_000001",
  "seq": 1,
  "payload": {}
}
```

字段约束：

| 字段 | 含义 |
| --- | --- |
| `api_version` | 固定为 `act-operator/v1` |
| `type` | `hello/reset/step/close/step_result` |
| `session_id` | 当前回合唯一 ID |
| `seq` | 当前回合内的步号 |
| `payload` | 消息体 |

### 5.2 消息类型

| `type` | 发送方 | 作用 |
| --- | --- | --- |
| `hello` | Unity | 连接握手，每次 TCP 连接建立后发送一次 |
| `reset` | Unity | 通知 backend 开始新回合并清空状态 |
| `step` | Unity | 发送一次 observation |
| `close` | Unity | 通知回合结束 |
| `step_result` | Python | 返回一步 ACT 推理结果 |

---

## 6. 数据结构定义

### 6.1 `ActEpisodeConfig`

```csharp
public class ActEpisodeConfig
{
  public string task_name = "excavator_dig_v1";
  public int seed = 0;
  public float fixed_dt_sec = 0.02f;
  public float observation_rate_hz = 20.0f;
  public int command_timeout_ms = 200;
}
```

### 6.2 `ActWireOperatorCommand`

这是线上 JSON 协议中的命令结构：

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

约束：

- 所有字段期望范围 `[-1, 1]`
- 不带任何 episode 控制字段
- Unity 仍会再次 clamp 和过滤

### 6.3 `ActObservation`

当前 Unity 发送的 observation 结构为：

```json
{
  "sim_time_sec": 1.84,
  "fixed_dt_sec": 0.02,
  "base_pose_world": {
    "position": [0.0, 0.0, 0.0],
    "rotation_xyzw": [0.0, 0.0, 0.0, 1.0]
  },
  "base_velocity_local": {
    "linear": [0.0, 0.0, 0.0],
    "angular": [0.0, 0.0, 0.0]
  },
  "bucket_pose_world": {
    "position": [8.1, 0.4, 1.2],
    "rotation_xyzw": [0.0, 0.7, 0.0, 0.7]
  },
  "actuator_state": {
    "boom_position_norm": 0.42,
    "boom_speed": 0.08,
    "stick_position_norm": 0.61,
    "stick_speed": -0.03,
    "bucket_position_norm": 0.55,
    "bucket_speed": 0.02,
    "swing_speed": 0.01
  },
  "task_state": {
    "mass_in_bucket_kg": 132.4,
    "excavated_mass_kg": 210.3,
    "mass_in_target_box_kg": 58.7,
    "deposited_mass_in_target_box_kg": 56.4,
    "min_distance_to_target_m": 0.38
  },
  "previous_operator_command": {
    "left_stick_x": 0.10,
    "left_stick_y": -0.20,
    "right_stick_x": 0.00,
    "right_stick_y": 0.35,
    "drive": 0.00,
    "steer": 0.00
  }
}
```

### 6.4 `ActStepRequest`

Unity 内部请求结构：

```csharp
public struct ActStepRequest
{
  public string SessionId;
  public int Seq;
  public ActObservation Observation;
}
```

### 6.5 `ActStepResponse`

Unity 内部响应结构：

```csharp
public struct ActStepResponse
{
  public string SessionId;
  public int Seq;
  public string Status;
  public OperatorCommand OperatorCommand;
  public float InferenceTimeMs;
  public float ModelTimeSec;
  public bool HasValue;
}
```

其中：

- `Status == "ok"` 才会被 `ActOperatorCommandSource` 接受为有效命令
- `OperatorCommand` 会被再次做有限值检查和 clamp
- `HasValue` 只是 Unity 内部标志，不在线上 JSON 中出现

---

## 7. 生命周期与时序约定

### 7.1 回合开始

当 `EpisodeManager.StartEpisode()` 被调用时：

1. `ActOperatorCommandSource.OnEpisodeStarted()` 被调用
2. 生成 `session_id = "ep_{episodeIndex:000000}"`
3. `m_nextSequence` 重置为 `1`
4. `m_lastValidResponseTime` 清空
5. 发送一条 `reset`，其 `seq = 0`

如果 backend 在回合运行期间重启或暂时离线：

- Unity client 会继续在同一 `host:port` 上重连
- 重连成功后会先重发 `hello`
- 若当前回合仍有效，会自动重发这一回合最近一次 `reset`

`reset` 示例：

```json
{
  "api_version": "act-operator/v1",
  "type": "reset",
  "session_id": "ep_000123",
  "seq": 0,
  "payload": {
    "task_name": "excavator_dig_v1",
    "seed": 123,
    "fixed_dt_sec": 0.02,
    "observation_rate_hz": 20.0,
    "command_timeout_ms": 200
  }
}
```

### 7.2 采样与发送

每次 `ReadCommand()` 调用时，ACT source 会：

1. 先轮询 backend 响应
2. 若回合未激活，直接回零
3. 若到达 `m_nextObservationTime`，采样一条 observation 并发送 `step`
4. 若 backend 未 ready，回零
5. 若命令已超时，回零
6. 否则返回最近一次有效命令

当前调度细节：

- 发送节拍基于 `Time.realtimeSinceStartup`
- observation 中的 `sim_time_sec` 仍使用 `Time.time`
- `step.seq` 从 `1` 开始递增
- 若 backend 离线，client 不会无限缓存历史 `step`，而是只保留最近一条待发送 observation

### 7.3 回合结束

当 `EpisodeManager.StopEpisode(reason)` 被调用时：

1. `ActOperatorCommandSource.OnEpisodeStopped(reason)` 被调用
2. 向 backend 发送 `close`
3. `close.seq = max(0, m_nextSequence - 1)`
4. 本地缓存清空

`close` 示例：

```json
{
  "api_version": "act-operator/v1",
  "type": "close",
  "session_id": "ep_000123",
  "seq": 42,
  "payload": {
    "reason": "episode_end"
  }
}
```

---

## 8. Python backend 需要满足的接口契约

### 8.1 连接层

Python backend 需要：

- 在 `127.0.0.1:5055` 或 Unity 配置的 host/port 上监听
- 接收 JSON Lines
- 能处理 `hello/reset/step/close`
- 对每条 `step` 返回 `step_result`

### 8.2 `step_result` 格式

Python 应返回：

```json
{
  "api_version": "act-operator/v1",
  "type": "step_result",
  "session_id": "ep_000123",
  "seq": 42,
  "payload": {
    "status": "ok",
    "operator_command": {
      "left_stick_x": 0.12,
      "left_stick_y": -0.28,
      "right_stick_x": 0.05,
      "right_stick_y": 0.31,
      "drive": 0.00,
      "steer": 0.00
    },
    "inference_time_ms": 7.4,
    "model_time_sec": 1.84
  }
}
```

要求：

- `session_id` 必须与对应 `step` 一致
- `seq` 必须回传同一步号
- `status` 推荐使用 `"ok"` / 其他错误状态字符串
- `operator_command` 必须是 6 轴命令

### 8.3 状态管理

Python backend 需要遵守：

- `reset` 时清空 RNN hidden state、历史窗口或内部缓存
- `step` 按 `session_id + seq` 处理
- `close` 到来时释放本回合资源
- 不负责 episode start/stop/reset 决策
- 不负责执行层映射

---

## 9. Unity 校验与兜底规则

当前 `ActOperatorCommandSource` 的响应处理规则如下：

### 9.1 会话校验

以下响应会被丢弃：

- `response.SessionId != m_sessionId`
- `response.Seq < 0`

### 9.2 状态校验

以下响应不会进入有效命令缓存：

- `Status != "ok"`
- 任一轴为 `NaN`
- 任一轴为 `Inf`

### 9.3 清洗规则

合法响应仍会执行：

- `WithoutEpisodeSignals()`
- 全轴 `ClampAxes()`

### 9.4 缓冲与超时

Unity 侧维护：

- `m_latestValidCommand`
- `m_lastValidResponseTime`
- `m_lastResponseSequence`
- `m_lastInferenceTimeMs`
- `m_lastBackendStatus`

行为为：

- 没有新响应时，保持上一条有效命令
- 从未收到过有效响应时，输出全零
- 超过 `command_timeout_ms` 后输出全零
- backend 未 ready 时输出全零

---

## 10. HUD、日志与调试信息

### 10.1 HUD 当前已显示

当 `CurrentSourceName == "ACT"` 时，`ExperimentHUD` 会显示：

- `Backend ready`
- `Timeout`
- `ACT seq`
- `Infer`
- `ACT session`
- `Status`

### 10.2 CSV 日志当前已记录

`ExperimentLogger` 当前仍主要记录：

- source name
- raw command
- simulated command
- final actuation command
- bucket pose
- `mass_in_bucket`
- `excavated_mass`

当前还没有写入 ACT 专用字段：

- `session_id`
- `seq`
- `backend_ready`
- `status`
- `inference_time_ms`
- `timeout_fallback`

如果需要系统联调回放，建议下一步扩展 CSV schema。

---

## 11. 当前版本的明确限制

以下内容不属于当前 v2 草案的已实现范围：

- 图像 observation
- 多模型并行推理
- 云端部署协议
- 多回合并行 session
- 直接 actuator 命令接口
- 仓库内置 Python backend

当前仓库只实现了 Unity 侧接口与本地 TCP 客户端，Python 服务端需要单独提供。

---

## 12. 推荐联调顺序

1. 在实验 rig 上挂载 `ActOperatorCommandSource`
2. 挂载并配置 `ActObservationCollector`
3. 挂载 `TcpJsonLinesActBackendClient`
4. 将 `EpisodeManager.m_commandSource` 指向 ACT source
5. 校准 `boom/stick/bucket` 归一化范围
6. 启动 Python backend 并监听正确端口
7. 通过 HUD 先验证 `backend_ready/session_id/seq/status`
8. 再验证命令回传、超时回零和日志落盘

---

## 13. 结论

当前 ACT 接口已经不只是“设计草案”，而是一个可以直接接入场景的 Unity 侧实现：

- 输入协议固定为 6 轴 `OperatorCommand`
- Unity 已实现 observation 采集、TCP JSON Lines 通信、命令清洗、超时兜底、HUD 诊断
- Python 侧只需按本文协议补齐 `step_result` 服务端

这也是当前工程里 ACT 接口的完整语义边界。
