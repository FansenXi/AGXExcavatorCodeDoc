# 仓库地图

已确认的顶层布局（2026-05-25）。

```text
GraphPerceptionPrj/
├── AGENTS.md                       # AI 代理操作规则（根目录）
├── README.md                       # 面向人类的项目入口点
├── .ai/                            # 面向 AI 的文档（此文件夹）
│   ├── README.md
│   ├── project/                    # 对代码库的稳定理解
│   ├── workflows/                  # 可重复的程序
│   ├── prompts/                    # 可复用的 AI 提示
│   ├── handoff/                    # 会话紧凑摘要
│   └── archive/                    # 已过时但有价值的文档
├── Assets/
│   ├── AGXUnity/                   # AGX 核心（已 gitignore，第三方）
│   ├── AGXUnity_Excavator/
│   │   ├── AGXUnity_Excavator.unity       # 简单场景 — 主要集成
│   │   ├── Open-pit mine.unity            # 露天矿场景（尚未集成）
│   │   ├── README.md               # 团队维护的子系统概述
│   │   ├── Docs/                   # 团队真实来源文档（不要移动）
│   │   │   ├── scene.md            # V0 场景和任务合约（英文）
│   │   │   ├── scene.zh-CN.md      # 中文镜像；冲突时 scene.md 优先
│   │   │   ├── scene_report.md     # 2026-03-25 进度说明
│   │   │   ├── protocol.md         # step-ack 二进制协议真实来源
│   │   │   ├── excavator_current_project_structure.md
│   │   │   ├── terrain_graph_observation.md  # 图感知 v0 模式
│   │   │   └── patches/            # 团队应用的 AGX 补丁
│   │   └── AGXUnity_Excavator_Assets/
│   │       ├── Scripts/            # 所有项目 C#（见下文）
│   │       ├── Prefabs/            # 挖掘机 CAT 365 Tracked, BedTruck*, 输入动作
│   │       ├── Physics/, Profiles/, Terrains/, materials/, models/
│   │       ├── SkySerie Freebie/, LightingData/
│   │       └── Scripts/Editor/     # 仅编辑器工具
│   └── （无 Scenes/ 文件夹 — 2026-05-24 移除）
├── Packages/
│   └── manifest.json               # Unity 包 + AGX 作用域注册表固定
├── ProjectSettings/                # Unity 项目设置（提交；不要手动编辑）
├── Tools/
│   └── TerrainGraph/               # Python 可视化工具（numpy / matplotlib / SVG）
├── TerrainGraphSnapshots/          # 离线图导出（已 gitignore）
├── knowledges/                     # 外部参考 PDF/PNG（已 gitignore）
├── ExperimentLogs/                 # 运行时日志（已 gitignore）
├── Library/, Logs/, UserSettings/  # Unity 生成（已 gitignore）
├── *.csproj, *.sln                 # IDE 生成（已 gitignore）
└── .gitignore
```

## 脚本子树

```text
Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/
├── Control/
│   ├── Core/                  # OperatorCommand, IOperatorCommandSource, 设备定位器
│   ├── Execution/             # ExcavatorMachineController, 命令解释器, 限制
│   ├── Simulation/            # OperatorCommandSimulator + 轴响应配置文件
│   └── Sources/               # 键盘, 游戏手柄, FarmStick, ACT, TCP 客户端
├── Editor/                    # FarmStick 诊断 + 自定义检查器
├── Experiment/                # EpisodeManager, ExperimentLogger, SceneResetService,
│                              # DigAreaMeasurement, SwitchableTargetMassSensor,
│                              # TruckBedMassSensor, TargetMassSensorBase,
│                              # BucketTargetDistanceMeasurementUtility,
│                              # TargetDistanceVolumeUtility
├── GraphPerception/           # v0 图观察（2026-05-24 添加）
│   ├── TerrainGraphProtocol.cs
│   └── TerrainGraphObservationProvider.cs
├── Presentation/              # TrackedCameraWindow (FPV RGB), ExperimentHUD,
│                              # VrMainCameraMirror, VrSpectatorBootstrap
├── SimulationBridge/          # AgxSimProtocol, AgxSimStepAckServer
├── ExcavationMassTracker.cs   # 铲斗内/挖掘质量跟踪器
├── ResetTerrain.cs            # AGX 地形重置包装器
├── TerrainGraphSnapshotExporter.cs  # G/Alpha9 键触发；委托给提供器
├── TerrainParticleBoxMassSensor.cs  # 定向盒子中的 AGX 粒子质量
├── FPSCamera.cs, LinkCamera.cs       # 相机工具
```

## 不要编辑的文件夹

- `Assets/AGXUnity/` — 第三方 AGX 核心。补丁位于 `Assets/AGXUnity_Excavator/Docs/patches/`，应用于包，而不是树内副本。
- `Packages/com.algoryx.agxunity.machines.*` — AGX 机器包，不可变。
- `Library/`、`Temp/`、`Logs/`、`Build[s]/`、`UserSettings/`、`Obj/` — Unity 生成，已 gitignore。
- `*.csproj`、`*.sln`、`.vscode/`、`.idea/` — IDE 生成，已 gitignore。
- AGX 许可证文件：`*.lfx*`、`*.lic`、`*.license`。
- `ExperimentLogs/`、`TerrainGraphSnapshots/`、`knowledges/*.{pdf,png,svg}`、`mono_crash.mem.*.blob` — 运行时/参考产物，已 gitignore。

## 在版本控制下但容易损坏的文件夹

- `ProjectSettings/` — 仅通过 Unity Editor 修改，除非故意手动编辑特定标量（例如 `templateDefaultScene`）。
- `Packages/manifest.json` — 让 Unity 包管理器编辑它。
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity` — 场景文件；除了手术修复外，永远不要手动编辑 YAML。
- `*.prefab` 和 `*.unity` — 与上述规则相同。
- `*.asset`（例如 Input Actions, Profiles）— Unity 管理。

## 文件计数（信息性）

- 项目 C# 脚本：`AGXUnity_Excavator_Assets/Scripts/` 下约 30 个文件。
- `Assets/AGXUnity_Excavator/Docs/` 下的团队文档：6 个 Markdown + 1 个 patches/。
- `.ai/` 下的 AI 文档：参见 [README.md](../README.md)。
- 活跃场景：2 个。