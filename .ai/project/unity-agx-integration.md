# Unity ↔ AGX 集成

## 什么是项目代码 vs 包代码

| 层级 | 路径 | 编辑策略 |
|---|---|---|
| AGX 核心（`AGXUnity`、`AGXUnityEditor`、`AGXUnityUpdateHandler`） | `Assets/AGXUnity/` | **不要在树中编辑。** 已 gitignore，第三方。补丁通过 `Assets/AGXUnity_Excavator/Docs/patches/`。 |
| AGX 机器包 | `Packages/com.algoryx.agxunity.machines.*` | **不要编辑。** 在 `manifest.json` 中固定；不可变。 |
| 项目粘合代码 | `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/` | 本仓库拥有。所有 AGX 交互都在这里。 |
| 项目场景 | `Assets/AGXUnity_Excavator/*.unity` | 本仓库拥有。 |
| 项目预制体 / 物理 / 地形 / 配置文件 | `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/{Prefabs,Physics,Terrains,Profiles}/` | 本仓库拥有。 |

## 项目代码在哪里接触 AGX

已确认的使用点（grep 证据）：

- **`AGXUnity.Model.DeformableTerrain`** — 地形重置、高度采样、AGX 动态粒子迭代：
  - `Scripts/ResetTerrain.cs`
  - `Scripts/ExcavationMassTracker.cs`
  - `Scripts/TerrainParticleBoxMassSensor.cs`（带辅助工具 `DeformableTerrainParticleMassUtility`）
  - `Scripts/TerrainGraphSnapshotExporter.cs`（遗留路径，现在很薄）
  - `Scripts/GraphPerception/TerrainGraphObservationProvider.cs`
- **`AGXUnity.Model.DeformableTerrainShovel`** — 设备上的铲斗耦合。
- **`AGXUnity.ScriptComponent` / `AGXUnity.Constraint`** — `ExcavatorMachineController` 通过 `TargetSpeedController` / `LockController`（`Control/Execution/`）读写 `SwingHinge`、`BoomPrismatics`、`StickPrismatic`、`BucketPrismatic`。
- **`AGXUnity.Excavator`** — 控制器托管的高级机器组件。
- **`AGXUnity.Simulation.Instance.DoStep()`** — 从 `AgxSimStepAckServer` 驱动，按 RPC 确定性地推进仿真。
- **`AGXUnity.Utils.Extensions.ToHandedVector3()`** — 将 `agx.Vec3` / `agx.Vec3f` 转换为 Unity `Vector3`。在读取 AGX 位置的地方使用。
- **`agx.GranularBodyPtr.ReturnToPool()`** — 每次 `particles.at(i)` 调用后必需。参见[颗粒粒子协议](#颗粒粒子协议)。

## 步骤语义

仿真从一个地方推进：**`AgxSimStepAckServer`** 消耗传入的 `STEP` 请求并调用 `AGXUnity.Simulation.Instance.DoStep()`。默认消耗点是 `Update`，有 `FixedUpdate` 和 `Realtime` 模式可用于实验。这些模式不改变线路协议；它们只改变*何时*消耗请求。

重置：

- `RESET` 请求通过 `SceneResetService.ResetScene(...)` 运行，它恢复缓存的 Transform 链、刚体姿态，以及（当请求时）地形。
- `scenario_id`（当提供时）通过 `ScenarioPreset` 表解析，其 v0 表面故意最小：`{reset_terrain, reset_pose, ActiveTargetIndex}`。
- 重置后，`SwitchableTargetMassSensor.SetActiveTargetByIndex(...)` 在 `ContainerBox` 和 `TruckBed` 之间切换。

重置路径是**承重的**：团队明确调优了 `Awake` 中的快照捕获（回退到第一个 `FixedUpdate`），以避免记录不正确的铲斗/履带姿态。在阅读 `Assets/AGXUnity_Excavator/README.md` 之前，不要移动该捕获点。

## 颗粒粒子协议

在 Unity 中迭代 AGX 动态土壤粒子时（规范模式）：

```csharp
var particles = terrain.GetParticles();   // agx.GranularBodyPtrArray
if (particles == null) return;
var n = particles.size();
for (uint i = 0; i < n; ++i) {
  var p = particles.at(i);
  if (p == null) continue;
  // 在返回池之前读取 p.getPosition(), p.getRadius(), p.getMass()
  p.ReturnToPool();                       // 强制性
}
```

每次调用 `particles.at(i)` 必须与 `p.ReturnToPool()` 配对，即使粒子被过滤掉。句柄在 `ReturnToPool` 后无效 — 先读取标量。跳过 `null` 句柄是允许的，无需 `ReturnToPool`（匹配 `TerrainParticleBoxMassSensor` 和 `TerrainGraphObservationProvider` 中的现有约定）。

## 坐标系注意事项

- `agx.Vec3 → UnityEngine.Vector3` 始终通过 `AGXUnity.Utils.Extensions.ToHandedVector3()` 来翻转手性。
- `DeformableTerrain` Y 位置在 AGX 初始化后漂移 `MaximumDepth`；`SceneResetService` 在重置期间因此不固定地形 `Transform.Y`。参见 `Assets/AGXUnity_Excavator/README.md`。

## 团队应用的 AGX 补丁

`Assets/AGXUnity_Excavator/Docs/patches/` 包含应用于 AGX 核心包的补丁（例如 `AGXUnity-DeformableTerrain-reset-native.patch`）。这些是**第三方包的外部变异**，在此跟踪以确保可重现性。不要就地应用它们；遵循 patches/README.md。

## 看起来像 AGX 编辑但不是的东西

- `*.csproj` / `*.sln` 是从 Unity 的 `Assets/Assembly-*` 布局 IDE 生成的。AGX 命名的 csproj 文件（`AGXUnity.csproj`、`AGXUnityEditor.csproj`、`AGXUnityUpdateHandler.csproj`）是 Unity 为 AGX 核心程序集定义重新生成的 IDE 项目 — 它们被 gitignore，不是项目代码。