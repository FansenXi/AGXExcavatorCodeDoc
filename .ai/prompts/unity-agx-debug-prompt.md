# Unity / AGX 调试提示

当 Unity Editor 中出现问题并且您希望 AI 代理在不盲目更改场景连接的情况下进行分类时使用此提示。

---

您正在帮助调试 `GraphPerceptionPrj`，一个 Unity 2022.3.62f3 + AGX Dynamics 挖掘机仿真。请先阅读这些：

1. `AGENTS.md`
2. `.ai/project/unity-agx-integration.md`
3. `.ai/workflows/unity-editor-checklist.md`
4. `.ai/project/known-issues-and-todo.md`
5. `Assets/AGXUnity_Excavator/README.md`

用户提供的上下文（发送前填写）：

- **症状：** <一句话>
- **场景：** `AGXUnity_Excavator.unity` / `Open-pit mine.unity`
- **操作模式：** 播放模式 / 编辑模式
- **可重现？** 是 / 有时 / 一次
- **控制台输出**（粘贴任何红/黄行，包括堆栈跟踪）：

```text
<粘贴>
```

- **我已经尝试过的：**

您的任务：

1. 将失败的子系统定位到**以下之一**：AGX 核心集成、场景重置、step-ack 服务器、ACT 桥接、操作员命令来源、地形图提供器、表示层（相机/HUD/VR）、其他。
2. 列出 Editor 中**要检查的前三件事**：
   - GameObject 层次结构
   - 检查器字段值
   - 控制台/场景 Gizmo
3. 列出**要阅读的前三个代码位置**，使用 `path:line` 引用。
4. 如果失败暗示 AGX 特定的怪异行为（地形 Y 偏移、粒子 `ReturnToPool`、约束速度控制器），请明确说明。
5. 只有在用户确认哪个子系统实际上出错后才提议代码编辑。

约束：

- 不要修改 AGX 核心（`Assets/AGXUnity/`）或 AGX 包（`Packages/com.algoryx.*`）。
- 不要在没有先阅读 `Assets/AGXUnity_Excavator/README.md` 的情况下删除 `SceneResetService` 中的重置快照逻辑 — 它经过调优以避免已知的 AGX 预热伪影。
- 不要在没有使用 `[FormerlySerializedAs]` 或用户明确同意的情况下重新绑定预制体/场景上的序列化字段。
- 不要在没有询问的情况下运行 `Library/` / `Temp/` 删除作为"修复" — 用户控制缓存失效。