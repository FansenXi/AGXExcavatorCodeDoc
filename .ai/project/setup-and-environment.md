# 设置和环境

## Unity Editor — 已确认

- **版本：** `2022.3.62f3`（来自 `ProjectSettings/ProjectVersion.txt`）。
- 在 Unity Hub 中使用该确切版本打开项目根目录。不同的 2022.3.x 补丁版本通常可以工作，但切换版本会静默重写 `ProjectVersion.txt`，该文件已提交 — 不要随意这样做。

## AGX Dynamics — 已确认

- AGX 核心位于 `Assets/AGXUnity/` 下，**已 gitignore**。团队必须在项目编译前放入自己获得许可的 AGXUnity 副本。
- AGX 机器包从作用域 npm 注册表拉取，固定在 `Packages/manifest.json` 中：

```json
"scopedRegistries": [
  {
    "name": "AGXUnity Registry",
    "url": "https://registry.npmjs.org",
    "scopes": ["com.algoryx.agxunity.machines"]
  }
]
```

  固定版本（全部 v1.1.1）：`bedtruck`、`cat365`、`dl300`、`e85`。

- AGX 许可证文件（`*.lfx`、`*.lic`、`*.license`）**已 gitignore**。每位开发者根据 Algoryx 的条款管理自己的许可证。

## Unity 包 — 来自 `Packages/manifest.json`

已确认（非内置）包：

- `com.unity.collab-proxy` 2.12.4
- `com.unity.device-simulator.devices` 1.0.1
- `com.unity.feature.development` 1.0.1
- `com.unity.inputsystem` 1.14.2  ← 所有 `*OperatorCommandSource` 使用
- `com.unity.shadergraph` 14.0.12
- `com.unity.textmeshpro` 3.0.7
- `com.unity.timeline` 1.7.7
- `com.unity.ugui` 1.0.0
- `com.unity.visualscripting` 1.9.4
- `com.unity.xr.interaction.toolkit` 2.6.5  ← VR 观众脚本使用
- `com.unity.xr.openxr` 1.14.3

加上一长串标准 `com.unity.modules.*` 运行时模块。

**清单中不存在**（已验证）：`com.unity.ml-agents`、任何 ROS、任何 HDF5。

## 首次打开项目

1. 安装 Unity 2022.3.62f3。
2. 确保您的 AGX Dynamics 安装 + 许可证已设置（遵循 Algoryx 文档；此仓库不复制它们）。
3. 将 AGXUnity 核心分发复制/克隆到 `Assets/AGXUnity/`。
4. 打开项目。Unity 将解析作用域注册表并下载 AGX 机器包。首次导入需要几分钟。
5. 默认场景是 `Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity`（在 `ProjectSettings/ProjectSettings.asset → templateDefaultScene` 中设置）。

## 外部 Python 工具（可选）

- `Tools/TerrainGraph/visualize_terrain_graph_snapshot.py` 需要 `numpy` 和 `matplotlib`。
- `Tools/TerrainGraph/terrain_graph_snapshot_to_svg.py` 是无依赖的 Python 3。

没有 `requirements.txt`，因为 Python 不是此仓库的主要交付物。

## 端口和运行时配置

- **Step-ack TCP 服务器：** 默认绑定 `0.0.0.0:5057`（`AgxSimStepAckServer` 检查器字段；检查场景的实际设置）。
- **ACT JSON 行桥接：** 按场景中的 `TcpJsonLinesActBackendClient` 实例配置；不绑定到固定端口。

## 输出位置

- `TerrainGraphSnapshots/` — 离线图导出，已 gitignore，由 `TerrainGraphSnapshotExporter` 在按键触发/上下文菜单/`exportOnStart` 时写入。
- `ExperimentLogs/` — 来自 `ExperimentLogger` 的每剧集日志，已 gitignore。
- `Logs/`、`Library/`、`Temp/`、`UserSettings/` — Unity 生成，已 gitignore。

## 分支 / git

- 活跃分支是 `zs/add_open_pit_mine`（根据 `git status`）；主分支是 `main`。简单场景是集成目标；露天矿场景是此分支上的最新添加。