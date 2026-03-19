# 挖机 ACT 接入软件结构设计

## 1. 设计目标

该设计面向以下需求：

- 输入设备逐步演进：键盘 -> 手柄 -> ACT -> 真实控制器。
- 保留“操作员接口仿真层”，而不是让模型直接控制底层执行器。
- 能稳定做实验、记录日志、展示输出、比较不同输入源。
- 尽量复用现有 AGX 挖机执行逻辑。

这里需要额外强调：

- ACT 的职责是模拟人类驾驶员的操作输出。
- ACT 不是键盘事件生成器。
- ACT 不应该通过“按哪个键”来控制挖机。
- ACT 应该直接输出统一的高层 `OperatorCommand`。

## 2. 推荐架构总览

建议的软件结构如下：

```text
Input Source Layer
  Keyboard / Gamepad / ACT / Real Controller
            |
            v
Operator Command Layer
  OperatorCommand
  IOperatorCommandSource
  OperatorCommandSimulator
            |
            v
Machine Interpretation Layer
  ExcavatorCommandInterpreter
            |
            v
Machine Execution Layer
  ExcavatorMachineController
  AGX Constraint Controllers
            |
            v
Experiment Layer
  EpisodeManager
  ExperimentLogger
  ReplayPlayer
  HUD / Visualization
```

这个结构的核心思想是：

- 输入设备只负责产生命令。
- 仿真层负责把命令变成“更像人”的操作输入。
- 执行层负责把命令变成 AGX 可以执行的机器动作。
- 实验层负责记录、展示和回放。

更准确地说：

- 键盘、手柄、真实控制器属于设备输入源。
- ACT 属于操作决策源。
- 两类源最终在 `OperatorCommand` 这一层汇合。
- 它们不应该在“键盘按键”这一层汇合。

## 3. 模块划分

### 3.1 输入源层

职责：

- 读取某一种输入设备的数据。
- 输出统一格式的 `OperatorCommand`。
- 不直接调用 AGX 控制器。

这一层分成两类来源更合理：

1. 设备源
   - 键盘
   - 手柄
   - 真实控制器
2. 决策源
   - ACT

区别在于：

- 设备源先读取硬件状态，再映射到 `OperatorCommand`
- ACT 直接输出 `OperatorCommand`

建议类：

- `KeyboardOperatorCommandSource`
- `GamepadOperatorCommandSource`
- `ActOperatorCommandSource`
- `RealControllerOperatorCommandSource`

统一接口建议：

```csharp
public interface IOperatorCommandSource
{
  OperatorCommand ReadCommand();
  string SourceName { get; }
}
```

### 3.2 操作员接口层

职责：

- 定义标准化的人类操作接口。
- 统一不同输入源的维度、范围、方向和语义。

建议数据结构：

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

设计说明：

- 范围建议统一为 `[-1, 1]`。
- 即使未来真实控制器维度不同，也先适配到同一结构。
- ACT 也应该输出这一层，而不是直接输出液压执行量。
- ACT 也不应该输出“键盘 W/S/Up/Down 是否按下”这一类离散设备信号。

### 3.3 操作员接口仿真层

职责：

- 模拟真实驾驶员接口与设备特性。
- 把离散输入和连续输入统一成可解释的“虚拟控制器量”。

建议类：

- `OperatorCommandSimulator`
- `AxisResponseProfile`
- `InputSmoothingFilter`

这一层建议支持的处理：

- 死区
- 灵敏度曲线
- 限速
- 回中
- 方向反转
- 延迟
- 噪声注入
- 键盘按键到虚拟摇杆的爬升/回落

这层非常关键，因为：

- 键盘本身是离散输入。
- 手柄是连续输入。
- ACT 输出频率和稳定性未必与 Unity 主循环一致。
- 真实控制器未来也会有硬件死区和抖动。

其中要注意：

- 键盘需要通过这层被转换成“虚拟人类控制量”。
- ACT 原则上应尽量直接工作在这层之后或这一层的输入端，而不是先降级成键盘离散事件。
- 如果未来为了对比实验需要，也可以做一个“ACT -> 虚拟键盘”基线，但它应被视为对照实验，不应是主架构。

### 3.4 机器解释层

职责：

- 将 `OperatorCommand` 转换为挖机可执行的机器命令。
- 负责定义左手柄/右手柄和 Boom/Bucket/Stick/Swing 的映射关系。

建议数据结构：

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

建议类：

- `ExcavatorCommandInterpreter`

建议职责边界：

- `Interpreter` 做语义映射。
- `MachineController` 做 AGX 执行。

不要把这两层继续混在一个类里。

### 3.5 机器执行层

职责：

- 将解释后的命令写入 AGX 约束控制器。
- 保留现有工作装置速度缩放、限加速度、安全限幅逻辑。

建议类：

- `ExcavatorMachineController`
- `ExcavatorActuationLimits`

建议对现有 `ExcavatorInputController` 的借鉴点：

- Boom/Bucket/Stick/Swing 的目标速度控制模式
- `CalculateSpeed` 一类的速度变化限幅
- `TargetSpeedController` 的实际写入方式

注意事项：

- 不建议继续直接改 `Library/PackageCache/.../ExcavatorInputController.cs`
- 应在 `Assets/.../Scripts` 下建立自己的执行控制器

## 4. 实验层设计

### 4.1 回合管理

职责：

- 控制实验开始、结束、重置。
- 记录当前回合编号、输入源、场景配置。

建议类：

- `EpisodeManager`
- `SceneResetService`

可复用现有能力：

- `ResetTerrain.cs`
- `ExcavationMassTracker.cs`

### 4.2 日志记录

职责：

- 逐帧记录输入、仿真命令、执行命令、关键状态、任务指标。

建议类：

- `ExperimentLogger`
- `ExperimentFrameRecord`
- `ExperimentSummaryRecord`

建议记录字段：

- 时间戳
- 当前输入源
- 原始输入
- 仿真后输入
- 最终执行命令
- 铲斗位姿
- 关键约束速度
- 质量与体积
- 回合结果

### 4.3 回放

职责：

- 用记录下来的 `OperatorCommand` 或 `ExcavatorActuationCommand` 重放实验。

建议类：

- `ReplayPlayer`

建议优先回放的内容：

- `OperatorCommand`

原因：

- 更接近真实人工操作层。
- 便于之后与 ACT、真实控制器统一比较。

### 4.4 可视化与展示

职责：

- 在运行时展示输入、过滤后输入、机器输出、任务指标。

建议类：

- `ExperimentHUD`
- `CommandCurveDebugView`
- `CameraPresentationController`

可复用现有能力：

- `LinkCamera.cs`

建议至少展示：

- 左手柄/右手柄虚拟值
- Boom/Bucket/Stick/Swing 输出
- 质量和体积
- 当前输入源
- 当前回合状态

## 5. 目录结构建议

建议在以下位置建立自己的脚本目录，而不是继续往 `PackageCache` 加逻辑：

```text
Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/
  Control/
    Core/
      OperatorCommand.cs
      ExcavatorActuationCommand.cs
      IOperatorCommandSource.cs
    Sources/
      KeyboardOperatorCommandSource.cs
      GamepadOperatorCommandSource.cs
      ActOperatorCommandSource.cs
      RealControllerOperatorCommandSource.cs
    Simulation/
      OperatorCommandSimulator.cs
      AxisResponseProfile.cs
    Execution/
      ExcavatorCommandInterpreter.cs
      ExcavatorMachineController.cs
      ExcavatorActuationLimits.cs
  Experiment/
    EpisodeManager.cs
    ExperimentLogger.cs
    ReplayPlayer.cs
    SceneResetService.cs
  Presentation/
    ExperimentHUD.cs
    CommandCurveDebugView.cs
    CameraPresentationController.cs
```

## 6. 现有脚本与新架构的关系

### `ExcavatorInputController`

定位：

- 参考实现
- 用于理解现有 AGX 执行方式

不建议：

- 继续把它当作主扩展入口

### `ExcavationMassTracker`

定位：

- 当前质量指标统计器

后续建议：

- 接入 `ExperimentLogger`
- 在回合结束时输出汇总指标

### `ResetTerrain`

定位：

- 当前最小重置能力

后续建议：

- 合并到 `SceneResetService`

### `LinkCamera`

定位：

- 当前展示层辅助工具

后续建议：

- 保留
- 以后可扩展为实验展示视角控制器

## 7. 关键设计决策

### 决策 1：ACT 输出操作员命令，不输出底层执行命令

原因：

- 更符合“AI 模拟人类操作”的目标。
- 更利于从手柄迁移到真实控制器。
- 更容易比较人工操作与模型操作。
- 也避免 ACT 被错误约束成“只会像键盘那样离散按键”。

### 决策 1.1：ACT 不通过键盘事件驱动主系统

原因：

- 键盘只是第一阶段的低成本验证设备，不是长期的人类操作接口上限。
- 如果让 ACT 伪装成键盘，模型会被迫学习离散按键时序，而不是连续人类控制意图。
- 这种设计会让后续手柄和真实控制器迁移变差。

### 决策 2：键盘阶段也保留操作员仿真层

原因：

- 键盘是离散输入，不经过仿真层会与后续输入设备脱节。
- 可以在第一阶段就建立统一的实验链。

### 决策 3：同时记录三套值

建议同时记录：

1. 原始输入值
2. 仿真后的操作员命令
3. 最终执行命令

原因：

- 后续分析控制问题时，可以定位问题到底出在输入源、仿真层还是机器层。

## 8. 最小实现建议

建议第一个可运行版本只包含以下内容：

1. `OperatorCommand`
2. `IOperatorCommandSource`
3. `KeyboardOperatorCommandSource`
4. `OperatorCommandSimulator`
5. `ExcavatorCommandInterpreter`
6. `ExcavatorMachineController`
7. `ExperimentLogger`
8. `ExperimentHUD`

这套最小版本完成后，就已经能支撑：

- 键盘实验
- 基础曲线展示
- 每回合日志记录
- 后续平滑接入手柄

## 9. 预期收益

采用这个结构后，后续扩展将具有下面的性质：

- 换输入设备时，不需要改机器执行层。
- 做 ACT 接入时，不需要重写实验层。
- 做真实控制器接入时，不需要推翻现有数据记录和展示。
- 团队可以稳定比较不同输入源在同一任务下的表现。

并且 ACT 的定位会保持正确：

- ACT 学的是“人如何操作挖机”
- 不是“人如何按键盘”
