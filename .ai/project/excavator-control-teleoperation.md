# 挖掘机控制和遥操作

## 动作表面（已确认）

step-ack 动作空间暴露一个固定的 4 自由度机械臂控制器：

```text
action_order: [swing_speed_cmd, boom_speed_cmd, stick_speed_cmd, bucket_speed_cmd]
action_semantics: actuator_speed_cmd
```

驱动/转向/履带运动故意**排除**在动作空间之外。操作员仍然可以在遥操作/VR 会话中驾驶设备；只有 step-ack 合约锁定为 4 自由度。

## 控制管线

```
操作员来源                       动作消费者
─────────────────                     ──────────────────────
KeyboardOperatorCommandSource ─┐
GamepadOperatorCommandSource   │
FarmStickOperatorCommandSource ┼─► OperatorCommandSourceBehaviour
ActOperatorCommandSource       │       │
                                ─┘       ▼
                              OperatorCommandSimulator
                                         │
                                         ▼
                            ExcavatorCommandInterpreter
                                         │
                                         ▼
                            ExcavatorMachineController
                                         │
                                         ▼
                  AGXUnity.Excavator 约束 (Swing / Boom / Stick / Bucket)
```

所有来源实现 `IOperatorCommandSource`（`Control/Core/`）并馈送 `OperatorCommand` 结构体（`Control/Core/OperatorCommand.cs`）。解释器将其塑造成 `ExcavatorActuationCommand`（`Control/Core/`）；机器控制器（`Control/Execution/ExcavatorMachineController.cs`）是实际写入 AGX 约束控制器的东西。

`ExcavatorMachineController` 解析 `Excavator` 组件 + 铲斗参考 `Transform`（按名称 "Bucket" 自动找到）并暴露 `BucketReference` 供下游观察者使用。

## 已确认的操作员来源

| 来源 | 文件 | 备注 |
|---|---|---|
| 键盘 | `Control/Sources/KeyboardOperatorCommandSource.cs` | 使用 Input System；通过共享的 `Excavator CAT 365 Input Actions` 资产重新绑定 |
| 游戏手柄 | `Control/Sources/GamepadOperatorCommandSource.cs` | Xbox360 风格映射记录在 `Assets/AGXUnity_Excavator/README.md` |
| FarmStick | `Control/Sources/FarmStickOperatorCommandSource.cs` | 高精度摇杆配置文件（`FarmStickControlProfile`）；`Scripts/Editor/` 中的自定义检查器 |
| ACT 后端 | `Control/Sources/ActOperatorCommandSource.cs` | 从 `TcpJsonLinesActBackendClient` 接收命令 |

键盘/Xbox 控制的右手规则来自 `Assets/AGXUnity_Excavator/README.md`：

> 右摇杆 X → 动臂升降，右摇杆 Y → 移动铲斗，
> 左摇杆 X → 左右回转，左摇杆 Y → 斗杆升降，
> 方向键 → 驱动/平移。

## 驱动限制

`Control/Execution/ExcavatorActuationLimits.cs` 暴露 `MaxRotationalAcceleration` 和 `MaxLinearAcceleration`。机器控制器在每个仿真步骤中使用它们来限制应用于每个约束的速度变化（避免发送会使 AGX 不稳定的不可能跳跃）。

## 发动机 + 空档状态

`ExcavatorMachineController` 暴露 `StartEngine() / StopEngine() / StopMotion()`。发动机停止时，所有轴被迫进入"空档"状态 — 控制器禁用，锁定控制器锁定在当前位置。这是重置的安全状态。

## VR / 观众（信息性）

- `Presentation/VrMainCameraMirror.cs` 和 `Presentation/VrSpectatorBootstrap.cs` 存在用于基于 OpenXR 的遥操作会话 VR 观看。
- 这些仅用于展示，不影响控制管线。

## 如何添加新的操作员来源（模式）

1. 在 `Scripts/Control/Sources/` 下的新 MonoBehaviour 中实现 `IOperatorCommandSource`。
2. 如果需要标准优先级/激活处理，继承自 `OperatorCommandSourceBehaviour`。
3. 在场景中注册新来源，以便 `EpisodeManager` 链可以通过活动来源解析器拾取它（无静态列表 — 在检查器中拖放）。

## 不在此层

- 奖励计算（部分在 `ExperimentLogger` 中，部分在 Repo A 中）。
- 剧集重置（由 `EpisodeManager` + `SceneResetService` 处理）。
- 观察收集（`ActObservationCollector` 读取，不写入）。