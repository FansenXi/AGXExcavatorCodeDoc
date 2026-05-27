# 整体架构建设方案：感知中心的 Unity+AGX 挖掘机仿真栈（2026–2029 PhD）

## Context

**为什么做这个方案。**
你接手深圳团队留下的 Repo B（Unity+AGX 挖掘机仿真），整体理解吃力。在我们的对话里你确认了 4 个决定性事实：

1. PhD 主线是**感知中心**（graph / depth / 多模态世界表示）。
2. **缩比挖掘机样机已经存在**（遥控/操纵杆 + Jetson Orin）—— 真机集成不是远期问题，是近期问题。
3. **AGX 必须用、Unity 可以让步**。
4. **6 个月内必须出第一篇论文**（开题/中期压力）—— 这是唯一硬约束。其他都不是硬约束。

在仓库里我证实了两件**改变方案方向**的事：

- **AGX 自带原生 ROS 2 集成**：`Assets/AGXUnity/Plugins/x86_64/libagxROS2.so` + `Assets/AGXUnity/AGXUnity/Sensor/LidarROS2Publisher.cs` 已经在以 `sensor_msgs/PointCloud2` 格式发布到 `lidar/pointcloud`、`lidar/pointcloud_ex`、`lidar/instance_id` 三个标准话题。你**不需要自己造 ROS 桥**。
- **AGX 自带正经 GPU 光线追踪 Lidar**：`Assets/AGXUnity/AGXUnity/Sensor/{LidarSensor,LidarSurfaceMaterial,LidarRayDistortion,SensorEnvironment,AmbientMaterial}.cs` + `libagxSensor.so` + `libAlgoryxGPUSensorsImpl.so`。你刚在 `LidarPerception/LidarSnapshotExporter.cs` 里写的 `Physics.Raycast` 版本是**重复劳动且物理上更弱**，应当废弃。
- **你目前的 step-ack TCP 协议是手搓的二进制 + CRC32**，没有任何 protobuf / gRPC / ROS 痕迹。这是一个**未使用 AGX 原生能力**的孤岛。

**目标产出。**
一套能用到 2029 年、易扩展、易理解、对论文友好、能直接对接 Jetson Orin 缩比样机的**三仓 + 双轨**架构。

---

## Agent 执行协议（给 Codex / Claude Code / 其他 AI 代理）

**任何 AI 代理在本仓库工作前，必须先读完这份 roadmap 再动手。** 本节是硬性纪律,不是建议。

### 阅读顺序（每次新会话）
1. `AGENTS.md`（仓库根）
2. `.ai/roadmap/architecture-roadmap-2026-2029.md`（本文件,主线）
3. `.ai/handoff/latest-session-summary.md`（上次进度）
4. 与当前任务相关的 `.ai/project/*.md`

### 执行纪律（七条）
1. **严格按 Track 顺序**：Track 1 未完成不动 Track 2；Track 2 未完成不动 Track 3。**验证 1（AGX-Native Lidar + ROS 2）未通过不进入 Track 1 实施**。
2. **每个任务采用三段式**：在动手前自行写出
   - **Pre-condition**：可开始的前置条件（具体可检验）
   - **Action**：要执行的具体操作 / 命令 / 文件改动
   - **Acceptance**：完成后如何验证（具体可观测的结果）
3. **Pre-condition 不满足必须 STOP**：不要"凑合往下做"、不要擅自改方案、不要绕路。把缺口报告给用户。
4. **跨仓改动必须先动 Repo C**：任何修改 sim ↔ real 通信契约的变更，先在 `excavator-msgs`（Repo C）里定义/升版,再动 Repo B 或 Repo A。
5. **保留/废弃清单是合同**：本文件"关键文件与可复用资产"一节里的"保留"列表中的文件**不准动**；"废弃"列表中的文件**不准新增功能**（等 Track 2 统一替换）。
6. **不绕过 `AGENTS.md` 的"不要修改"清单**：`Assets/AGXUnity/`、`Packages/com.algoryx.*`、`Library/`、`*.unity` 等永远不动。
7. **文档与代码同步**：任何 protocol / 传感器 / 消息变更，同步更新本 roadmap 和 `.ai/project/*.md` 对应文件；漂移就是 bug。

### 偏离方案的合法路径
如果实际工作发现 roadmap 错了或不可行，**不要默默修改代码绕过**。流程：
1. 把发现的偏差写进 `.ai/handoff/latest-session-summary.md` 的"偏离方案"节
2. 在会话里向用户报告，等用户决定
3. 用户批准后才更新 roadmap 本身

### 给自动化代理的额外约束
- 不要批量重命名、不要全仓库 grep-replace
- 不要为"清理"删除你不理解的代码（尤其是 AGX、`SceneResetService` 时序、AGX 粒子池相关）
- 单次会话只推进一个 Track 内的一个任务
- 任何 `Physics.Raycast` 风格的传感器新增都拒绝（已被 AGX 原生方案废止）

---

## 战略判断（一句话版本）

**不要推倒重来，也不要继续在现有 Unity 代码上叠床架屋。**
应该**保留 Unity+AGX 作为仿真主体**（因为 AGX-Native 能力远超当前代码使用程度,且 license 锁在 Unity 集成里），**砍掉手搓 TCP 协议层**，**全面切到 ROS 2**（因为 AGX 已经原生说 ROS 2,且真机在 Jetson Orin 上）。

这把"理解费劲"的根源消除了：未来的 Repo B 是**薄薄一层 Unity + 标准 ROS 2**，主要复杂度在 AGX 内部（你本来就不该读），不在团队自写的胶水代码（这才是费劲的来源）。

## 主感知通道决策（Lidar 优先）

**Track 1 主感知 = Lidar**（用户已确认真机端首选 Lidar）。理由：
- 真机端 Lidar 已有/可负担,sim 端 AGX 原生 GPU Lidar + ROS 2 publisher 现成,**sim/real 共用 `sensor_msgs/PointCloud2`,sim2real 入口最干净**。
- 这也是 AGX 投资最大的传感器子系统（`libagxSensor.so` + `libAlgoryxGPUSensorsImpl.so`）,不用是浪费。

**Depth Camera 降为可选项**:
- 短期内 sim 端不投入。真机端若后期加 Realsense/Orbbec,再启用 Unity Camera `depthTextureMode` + 标准 `sensor_msgs/Image` publisher。
- 这一选择**不锁死架构** —— ROS 2 自描述消息让你随时加传感器,Repo A 端只是订阅多一个 topic。

**其他感知通道并行保留**:
- FPV RGB(`TrackedCameraWindow`)→ `sensor_msgs/Image`
- 地形图 v0(`TerrainGraphObservationProvider`)→ 自定义 `TerrainGraph.msg`
- 关节状态 → `sensor_msgs/JointState`

---

## 三仓重新定义

### Repo B — `excavator-sim` (Unity + AGX + ROS 2)

**职责**：仿真世界。以 ROS 2 节点身份存在。
**核心改动**：
- **协议层从 TCP 二进制 step-ack → ROS 2 topics/services/actions**。废弃 `AgxSimProtocol.cs` / `AgxSimStepAckServer.cs`。
- **传感器全部使用 AGX 原生组件**：
  - Lidar → `AGXUnity.Sensor.LidarSensor` + `LidarROS2Publisher`（已存在,未启用）
  - Depth Camera → AGX 原生 depth camera（如果 AGX 5.5.1 包含,否则用 Unity Camera + `Camera.depthTextureMode`,废弃当前的 `Physics.Raycast` 版本）
  - IMU / 关节编码器 → 从 AGX RigidBody / Constraint 状态导出
  - 地形图 v0（Graph Perception）→ 保留你已经做的 `TerrainGraphObservationProvider`,但发布为自定义 ROS 2 message
- **动作命令通过 ROS 2 action 接收**：替换 step-ack。
- **场景/任务配置外置为 YAML/ROS param**（取代当前 `ScenarioPreset.cs` 的硬编码 if-else）。
- **可选保留**：Unity Editor 的可视化、teleop 操纵杆输入（用 ROS 2 `joy` 消息）。

**砍掉的**：
- `Scripts/SimulationBridge/AgxSimProtocol.cs`、`AgxSimStepAckServer.cs`（被 ROS 2 替代）
- `Scripts/DepthPerception/DepthCameraSnapshotExporter.cs`、`Scripts/LidarPerception/LidarSnapshotExporter.cs`（被 AGX 原生传感器 + ROS 2 发布替代）
- `Scripts/TerrainGraphSnapshotExporter.cs` 的离线 JSON 路径（保留 Provider,把输出端改成 ROS 2 publisher）
- 整套 `Scripts/Control/Sources/Act*.cs` 的 JSON 行 TCP（被 ROS 2 action 替代）

**保留的**：
- `Scripts/Control/Execution/ExcavatorMachineController.cs` —— 挖掘机驱动,与协议无关
- `Scripts/Experiment/SceneResetService.cs` —— AGX 预热重置时序调好的,不要碰
- `Scripts/GraphPerception/TerrainGraphObservationProvider.cs` —— 采样逻辑,只换输出端
- `Scripts/Presentation/TrackedCameraWindow.cs` —— FPV 渲染
- 所有 `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Prefabs/*`、`Physics/`、`Terrains/`、`Profiles/`
- AGENTS.md、.ai/ 文档体系（迁移而非丢弃）

### Repo C — `excavator-msgs` (ROS 2 接口包)

**职责**：B 和 A 之间的协议契约。
**形态**：标准 ROS 2 ament_cmake 包,含：
- `msg/*.msg` —— 自定义消息（如 `TerrainGraph.msg`、`ExcavatorState.msg`、`EnvState.msg`）
- `srv/*.srv` —— 服务（如 `ResetEpisode.srv`、`SetScenario.srv`）
- `action/*.action` —— 动作（如 `ExecuteStep.action` —— 取代当前 step-ack）
- `excavator_msgs/` —— Python + C++ 自动生成的 stub
**版本管理**：semver,breaking change 强制 major bump。
**为什么用 ROS 2 message 而不是 protobuf**：因为 sim 端 (AGX) 和 real 端 (Jetson Orin) 都活在 ROS 2 生态里,再套一层 protobuf 是多余翻译；而且 ROS 2 message 本身就是 IDL + 代码生成。

### Repo A — `excavator-research` (Python 研究栈)

**职责**：数据、训练、推理、评估、论文产出。
**核心结构**：
```
excavator-research/
├── data/              # rosbag2 录制、scripted teleop、随机策略
│   └── converters/    # rosbag2 → LeRobot dataset / HDF5
├── perception/        # 你 PhD 的主战场:graph encoder、depth encoder、多模态融合
├── policy/            # IL (ACT / Diffusion Policy)、RL (PPO / SAC)
├── world_model/       # 学习的动力学（Liu et al. 风格 GNN 动力学）
├── eval/              # 闭环评估,sim + real 同一套代码
├── deploy/            # TensorRT 优化 → Jetson Orin
└── configs/           # Hydra
```
**关键模式**：Repo A 不知道环境是 sim 还是 real,只订阅 ROS 2 topics、发 actions。**这就是 sim2real 的入口**。

### Real Robot — `excavator-real-orin` (Jetson Orin ROS 2 节点)

**职责**：缩比样机的 ROS 2 化封装。
**包含**：
- 操纵杆/遥控输入 → `sensor_msgs/Joy`
- 关节编码器 → `sensor_msgs/JointState`
- 真实 IMU → `sensor_msgs/Imu`
- 真实深度相机（Realsense/Orbbec）→ `sensor_msgs/Image` + `sensor_msgs/CameraInfo`
- 真实 Lidar（如果有）→ `sensor_msgs/PointCloud2`（与 sim 同 topic 名）
- 控制下发 → 现有遥控/操纵杆通道的低层封装
**关键设计**：**topic 命名空间和消息类型与 sim 完全一致**。Repo A 不改一行代码即可切换 sim ↔ real。

---

## ROS 2 决策（明确回答你的问题）

**答案:必须集成 ROS 2,理由超过 6 条。**

1. AGX 已经**原生说** ROS 2（`libagxROS2.so` + `LidarROS2Publisher.cs`）,不集成 = 浪费已购能力。
2. Jetson Orin 对 ROS 2 Jazzy 是一等公民支持（Ubuntu 24.04 配对）。
3. 真机传感器（Realsense、Orbbec、Velodyne、Livox 等）的 SDK **大部分先发 ROS 2 driver**。
4. **`rosbag2` 直接给你数据集采集** —— 不需要自己写 dataset writer。
5. **RViz / Foxglove Studio** 给你免费的多模态可视化 —— 对感知论文写作友情值拉满。
6. Sim 端和 Real 端共用 topic 名字 → Repo A 一套代码两边跑 → sim2real 实验门槛大幅降低。
7. 学术圈机器人 paper 的**事实标准**,开源社区也会更接受。

**不内嵌到 Unity 主线程**:ROS 2 节点跑在 AGX 同进程（因为 `agxROS2` 就是这么用的）,但 Repo A 跑在不同机器/进程。

**版本**:ROS 2 Jazzy Jalisco（与 Ubuntu 24.04 LTS 官方配对,EOL 2029-05,覆盖你 PhD 毕业）。**不要装 Humble**（Humble 配 Ubuntu 22.04,在 24.04 上会踩依赖坑）。

---

## 双轨时间线（对齐 6 个月论文死线）

### Track 1 — 论文产出轨（M1–M6,主线）

**目标**:6 个月内提交一篇感知方向论文,**不依赖架构重构**。

| 月份 | 任务 | 触碰范围 |
|---|---|---|
| M1 | 在现有 Unity 场景里启用 AGX 原生 LidarSensor + LidarROS2Publisher（替代你刚写的 `Physics.Raycast` 版本）。挂载到挖掘机驾驶舱上方 | Unity Editor + 5 行 C# |
| M1 | 把 `TerrainGraphObservationProvider` 的输出加一条 ROS 2 publisher 分支（保留离线 JSON 不动）；定义 `TerrainGraph.msg` 草案 | Repo B 增量 + Repo C 启动 |
| M2 | 用 `rosbag2 record` 在挖掘任务里录 50-100 个 episode,得到第一份多模态数据集（lidar pointcloud + terrain graph + 关节状态 + FPV RGB） | Repo A 启动 |
| M2 | 在 Repo A 里写 rosbag2 → LeRobot/HDF5 转换器 | Repo A |
| M3 | 训练第一个感知模型（baseline:纯 lidar 点云 vs. 纯地形图 vs. 多模态融合 → 预测下一步 mass_in_bucket / dig_area depth） | Repo A |
| M4 | 加 domain randomization:terrain seed、土壤参数、传感器噪声、相机位姿。重新采数据 | Repo B 增量 |
| M5 | 论文初稿:感知表示对挖掘动力学预测的影响（benchmark 论文方向） | Repo A |
| M6 | 投稿（ICRA/IROS/CoRL 25 deadline 对应 9 月/3 月,按你的窗口选） | — |

**Track 1 不做的事**:废弃 step-ack TCP、废弃 ScenarioPreset、改 Repo A 的 ACT 接口。**保持现状,只新增不删除**。

### Track 2 — 架构基础轨（M2–M12,并行）

**目标**:在 Track 1 论文投出去后,新架构已经准备好接管。

| 月份 | 任务 |
|---|---|
| M2 | Repo C 正式立项:写完 `ExcavatorState.msg` / `EnvState.msg` / `TerrainGraph.msg` / `ExecuteStep.action` 完整 IDL |
| M3 | Repo B 加 ROS 2 action server（与 step-ack TCP 并行运行,双轨） |
| M4 | Repo A 加 ROS 2 client（与 ACT JSON 行并行）,跑通 hello-world 闭环 |
| M5 | Real adapter 启动:Jetson Orin 上跑一个最小 ROS 2 节点,发 joint_state + 假数据 |
| M7 | Real adapter 接入真实操纵杆/IMU/编码器 |
| M8 | 第一次 sim2real:用 Track 1 训出的感知模型在真机上推理,看预测 vs. 真值 |
| M10 | 废弃 step-ack TCP；废弃 ACT JSON 行；统一到 ROS 2 |
| M12 | 第二篇论文素材:sim2real 感知迁移 |

### Track 3 — Headless AGX（可选,年 2+）

**仅在 Unity+AGX 成为速度瓶颈时启动**。AGX C++ `.so` 在仓库里现成,但 license 是否覆盖 standalone 用法需联系 Algoryx 确认。**当前不推荐**,因为:
- Unity+AGX 的速度对单机训练数据采集是足够的（每秒 ~30 步,rosbag2 直接落盘）
- 切到 headless 会失去 Unity Editor 的可视化调试便利
- 6 个月论文压力下,不值得这个风险

---

## 关键文件与可复用资产

**Repo B 保留（不动）**:
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/Control/Execution/ExcavatorMachineController.cs` —— 挖掘机驱动
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/Experiment/SceneResetService.cs` —— AGX 预热重置（精心调过的）
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/GraphPerception/TerrainGraphObservationProvider.cs` —— 地形图采样（换输出端）
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/Presentation/TrackedCameraWindow.cs` —— FPV 渲染
- 所有 Prefab、Physics 材料、Terrain 资产

**Repo B 启用（新激活的 AGX 原生能力）**:
- `Assets/AGXUnity/AGXUnity/Sensor/LidarSensor.cs` —— **GPU 光线追踪 Lidar**
- `Assets/AGXUnity/AGXUnity/Sensor/LidarROS2Publisher.cs` —— **原生 PointCloud2 发布**
- `Assets/AGXUnity/AGXUnity/Sensor/SensorEnvironment.cs` —— 传感器场景配置
- `Assets/AGXUnity/AGXUnity/Sensor/LidarSurfaceMaterial.cs` —— Lidar 材质响应

**Repo B 废弃（替换为 AGX 原生 + ROS 2）**:
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/SimulationBridge/AgxSimProtocol.cs`
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/SimulationBridge/AgxSimStepAckServer.cs`
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/DepthPerception/DepthCameraSnapshotExporter.cs` —— **删掉,刚写的也删,用 AGX 原生 depth 或 Unity Camera depthTextureMode**
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/LidarPerception/LidarSnapshotExporter.cs` —— **删掉,用 LidarSensor + LidarROS2Publisher 替代**
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/Control/Sources/ActProtocol.cs` 等 ACT TCP/JSON 系列 —— Track 2 后期统一到 ROS 2 action

**Repo B 新增**:
- `Scripts/Ros2Bridge/*` 子目录（Track 2 期间逐步建立）
- `Scripts/Ros2Bridge/Publishers/{TerrainGraphPublisher,EnvStatePublisher,JointStatePublisher,DepthImagePublisher}.cs`
- `Scripts/Ros2Bridge/ActionServers/ExecuteStepActionServer.cs`
- `Scripts/Ros2Bridge/Services/{ResetEpisodeService,SetScenarioService}.cs`

**文档迁移**:
- 现有 `.ai/` 全部保留,但更新内容指向新架构
- `Assets/AGXUnity_Excavator/Docs/protocol.md` Track 2 收尾时归档为 `protocol_v0_legacy.md`
- 新增 `excavator-msgs/README.md` 描述 ROS 2 接口

---

## 风险与开放问题

1. **AGX-Native Lidar 是否真的开箱即用**:需要在 Editor 里实际把 `LidarSensor` 挂到挖掘机上、配 SensorEnvironment、勾上 LidarROS2Publisher,跑一次 `ros2 topic echo /lidar/pointcloud` 验证。**M1 的第一周必须验证这个**,否则整个 Track 1 时间线要重算。
2. **AGX 是否提供原生 Depth Camera 传感器**:未确认。如果没有,回退方案是 Unity Camera 配 `depthTextureMode = Depth` + 自定义 RenderTexture 抓取 + 一个手写 ROS 2 publisher。
3. **学校 AGX license 的 ROS 2 模块是否激活**:`libagxROS2.so` 在仓库里,但运行时是否要单独的 ROS 2 license feature flag,需要查 license 文件。
4. **Jetson Orin 上的 ROS 2 与 sim 端的 DDS 配置**:跨机网络 DDS 调参偶尔有坑,提前留 2 天 buffer。
5. **TerrainGraph.msg 的字段稳定性**:你 PhD 主线在感知,这个消息的 schema 会频繁演化。建议第一年只在 Repo C 标 `0.x.y`,breaking change 自由；进入第二年 sim2real 阶段后再冻结到 `1.0`。
6. **现有 ACT 桥接的迁移**:你说"没有硬约束",但如果实际上 Repo A 的某个学生/合作者在用 ACT 桥接,需要先确认。如果是,先双轨并行。

---

## Verification（如何确认这个方案的可行性）

按以下顺序在**真实环境**里验证关键假设。每一步失败都意味着方案要调整。

### 验证 1 — AGX-Native Lidar 是否真能用（M1 第 1 周,最高优先级）

```bash
# 在 Unity Editor 里:
# 1. 打开 AGXUnity_Excavator.unity
# 2. 在挖掘机驾驶舱顶部新建 GameObject "LidarMount"
# 3. AddComponent: AGXUnity.Sensor.LidarSensor (设 16 ring, 360°)
# 4. AddComponent: AGXUnity.Sensor.LidarROS2Publisher (默认 topic)
# 5. 场景里加 SensorEnvironment 组件（如果还没有）
# 6. 进入 Play 模式
```
```bash
# 在终端:
source /opt/ros/jazzy/setup.bash
ros2 topic list                    # 期望看到 /lidar/pointcloud
ros2 topic hz /lidar/pointcloud    # 期望 ~10 Hz
ros2 run rviz2 rviz2               # 期望能可视化点云
```
- **如果点云非空且能在 RViz 看到挖掘机周围地形** → AGX-Native 验证通过,按方案走
- **如果 topic 出来了但点云全空** → 需要配 `LidarSurfaceMaterial` 给地形和挖掘机
- **如果 topic 都没出来** → 检查 license feature flag、检查 `libagxROS2.so` 加载日志

### 验证 2 — Jetson Orin 能否订阅 sim 的 topic（M2 第 1 周）

在 Jetson Orin 上装 ROS 2 Jazzy,配同一 DDS domain id,跑 `ros2 topic echo /lidar/pointcloud`,看能否收到 PC 仿真机发出的数据。

### 验证 3 — rosbag2 能否完整录制多模态数据（M2 第 2 周）

```bash
ros2 bag record /lidar/pointcloud /joint_states /fpv/image_raw /terrain_graph
# 跑一个 episode
# 然后回放:
ros2 bag play <bag>
# 用 Foxglove Studio 打开 bag,期望多通道时间同步
```

### 验证 4 — TerrainGraphObservationProvider 出 ROS 2 message 不破坏现有 JSON 路径

- 在 `TerrainGraphObservationProvider.Collect()` 之后加一条 publisher 分支
- 同时按 `Alpha9` 仍然能导出 JSON（确认现有工具链未被破坏）
- 同时 `ros2 topic echo /terrain_graph` 看到节点和边

### 验证 5 — Repo A 切换 sim/real backend 只改一行 launch 配置

设计期目标。M5 时验证:同一个 perception inference Python 脚本,靠 `--remap-ns sim:=real` 或 ROS 2 namespace 切换即可对接 sim 或真机。

---

## 给"理解费劲"的根治方案（额外）

你说"整体理解起来比较费劲",本方案天然消除其中 60-70%:

| 当前痛点 | 新架构如何消除 |
|---|---|
| 三仓通过手搓 TCP + CRC32 + 二进制布局通信,字段顺序硬编码 | ROS 2 自描述消息 + IDL 代码生成,字段语义在 `.msg` 文件里一目了然 |
| `ScenarioPreset` 在代码里 if-else | YAML/ROS param,外置 |
| `Scripts/Control/Sources/Act*.cs` 一堆桥接代码 | 整块删掉,用 ROS 2 action 替代 |
| 自己写的 Lidar 是 `Physics.Raycast` 简化版,不真实 | AGX-Native GPU 光线追踪 |
| 自己写的 Depth 也是 Raycast | AGX-Native depth 或 Unity Camera + 标准 publisher |
| `.ai/` 文档漂移（你已经发现） | ROS 2 消息 IDL 自动同步语义,文档漂移可能性大幅降低 |
| 三仓 A/B/C 的边界靠口头协议 | Repo C 是真正的 ROS 2 接口包,semver + breaking change 强制 major bump |

剩下 30-40% 的复杂度集中在 AGX 内部（土壤动力学、reset 时序、粒子池）—— 这部分**本来就不该你读**,是 AGX 的责任。

---

## 不在本方案里的事

- **不重写 SceneResetService**。AGX 预热时序是调好的,碰它会引入物理 bug。
- **不脱 Unity 做 headless**。Track 3 是可选项,6 个月内不考虑。
- **不切 Isaac Sim / MuJoCo**。AGX 颗粒土壤是你 PhD 的物理基底,换掉就没意义。
- **不为开源投入 CI/tutorial**。你说"前期自用,后期可能开源",前 12 个月先把架构对齐,开源工程化放到第 2 年。
- **不集成 LLM/VLM**。如果第二/第三篇论文是 VLA 方向再加,第一年纯感知。
