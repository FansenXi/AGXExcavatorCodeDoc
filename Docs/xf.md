Control/Core 文件夹里面装的是控制系统的公共定义，是一种规则。

OperatorCommand.cs 定义“上游输入长什么样”。
IOperatorCommandSource.cs 定义“谁有资格成为输入源”。
OperatorCommandSourceBehaviour.cs 定义“输入源如何以 Unity 组件形式存在，并参与 episode/诊断”。
ExcavatorActuationCommand.cs 定义“下游机器执行命令长什么样”。
ExcavatorRigLocator.cs 定义“这些组件在场景里如何彼此找到”。

输入源 -> OperatorCommand -> 解释/映射 -> ExcavatorActuationCommand -> 机器控制器


Control/Sources 文件夹里面装的是输入源，一句人话总结就是把所有输入源（本地，python端）的输入都转换成OperatorCommand统一处理。

三个Source结尾的脚本分别处理键盘，手柄，和专门的工程手柄作为本地输入源的情况，然后工程手柄又专门有一个Profile定义输入，因为它比较复杂。核心目的都是整理成项目
统一能懂的OperatorCommand，让复杂硬件稳定地变成标准输入命令。

剩下的ACT/Tcp脚本都是用于处理跟python之间通信的，unity这边给到python当前collect到的观测，然后python那边返回unity一个新的OperatorCommand。


Control/Simulation 文件夹里面装的是仿真模拟相关，不是物理上的仿真，而是操作者的输入相应仿真，就是处理输入，更加平滑之类的。
AxisResponseProfile描述了单个轴怎么响应，单个轴的相应配置，有死区，从小变大，从大变小的速度，回0的速度等等属性。
OperatorCommandSimulator就是用来把单个轴的配置应用到完整的OperatorCommand上了，总共有6个轴（注意这里的轴是指输入轴而不是机械上绕轴转动那个轴）。

Control/Execution
Limits限制了执行动作的加速度，也就是变化速度。Interpreter负责语义映射，把“输入设备语义”翻译成“挖机动作语义”。
关键的是ExcavatorMachineController，它是真正去控制挖机约束的脚本，接收挖机控制command，然后以固定的动作顺序去设置约束（ApplyActuation函数），约束就是指
两个刚体之间的一种固定的相对运动，定死了只能在这个约束运动，比如液压缸是滑动约束，然后挖掘机身旋转是铰链约束。通过获取Excavator组件去设置约束比如里面的速度speed，就可以控制挖机的各种运动。





Boom 是动臂/大臂控制。它控制主臂升降，也就是挖机最靠近车身的那段大臂。

Stick 是斗杆/小臂控制。它控制 boom 前面的第二段臂，通常表现为斗杆伸出或收回。

Bucket 是铲斗控制。它控制铲斗 curl/uncurl，也就是铲斗向内卷、向外张开的动作。

Swing 是回转控制。它控制上车体绕竖直轴旋转，也就是驾驶室和工作装置左右转。

Drive 是行走前后控制。正负值通常表示前进/后退。

Steer 是转向控制。对履带车来说，转向往往通过左右履带速度差实现。

Throttle 是油门/动力大小。代码里它是 0..1，更像发动机或驱动系统的动力输出强度。




EpisodeManager回合调度器主流程（负责“开始回合 -> 每帧读输入并驱动机器 -> 更新任务状态 -> 记录日志 -> 结束/重置回合”）：
进入 Play
  -> Awake / Start
  说明：初始化引用、关闭旧控制器；如果配置了自动开始，就直接启动 episode

StartEpisode()
  -> 开启一个新回合
  说明：设置回合编号，启动 engine，重置输入平滑状态，开始记录日志

每帧 Update()
  -> 读取当前输入源的 OperatorCommand
  说明：输入可能来自键盘、FarmStick、ACT/Python

  -> 处理 start / stop / reset 按钮请求
  说明：如果这一帧收到了开始、结束或重置命令，就先处理这些流程控制

  -> 如果回合正在运行
     -> 输入平滑 (OperatorCommandSimulator)
     -> 命令解释 (ExcavatorCommandInterpreter)
     -> 执行到机器 (ExcavatorMachineController)
  说明：这是每一帧真正控制挖机的主链

  -> 更新任务状态
  说明：例如 DigArea good-start、bucket 是否接触区域、质量变化等

  -> 记录日志
  说明：把当前输入、执行命令、bucket 位姿、质量、目标状态等写入 CSV

StopEpisode()
  -> 结束当前回合
  说明：停止 engine，清空当前命令，结束日志记录

ResetEpisode()
  -> 结束当前回合
  -> 调用 SceneResetService.ResetScene()
  -> 视配置决定是否重新 StartEpisode()
  说明：把场景、机器、传感器恢复到初始状态，然后可自动开始下一回合

创建每一个约束时都会新建一个game object然后自动挂载上constraint组件，创建约束可以在rigid body里面创建，也可以在AGXUnity菜单里创建，重要的属性是
约束的Pari对，也就是两个物体，以及约束类型，约束的一些控制器比如速度控制器（真正用来控制运动）等。

当前 AGXUnityExcavator 项目使用 Built-in Render Pipeline，不是 URP/HDRP。

本地比如想用键盘控制的时候就把AgxSimStepAckServer组件禁掉，在ACTRig里面，如果它启动的话会切断EpisodeManager。


我的任务：
1、让新导入进来的模型最好能够适配当前的控制逻辑，这需要我给新模型加Excavator脚本组件，然后配置每个物体的agx组件比如Rigidbody，在里面配置质量等属性，以及约束也很重要，约束有特别多，不知道导入进来的模型会不会有这个信息，没有的话在unity里配置起来会很麻烦。
*****************************************************************
1.1、现在新的任务是在agx这个示例demo的基础上去改，因为示例这个是约束控制系统，不是液压控制系统，我需要去大改为液压控制系统，搭建液压网络（节点，连接器），都是代码上的，因为agx没有提供inspector可视化的东西。示例里面主要是直接设置约束的目标速度，要改为液压系统里面比如控制阀或者泵，然后液压缸就会带着连接在上面的约束一起动。（我不再控制运动，而是控制条件，由液压系统网络去计算运动）。

2、搭建新的场景，跟厂房一样，颜色，灯光，厂房与挖掘机的比例以及其它物品的比例，都要符合显示，总之就是越真实越好，还得配置一下这些物体的Rigidbody组件来达到好的物理效果，以及尽量去unity商店找合适的材质和贴图，然后升级一下管线（记得备份），处理一下光照，还可以加后处理让场景看起来更真实。


