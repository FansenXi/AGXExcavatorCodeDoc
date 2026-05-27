# 交接文档模板

当切换 agent 时，使用此模板更新 `latest-session-summary.md`。

## 模板

```markdown
# 会话交接摘要

**日期：** YYYY-MM-DD HH:MM
**Agent：** [mimo / Claude Code / Codex]
**状态：** [进行中 / 已完成 / 阻塞]

## 当前任务
[一句话描述正在做什么]

## 已完成的工作
- [具体完成的事项1]
- [具体完成的事项2]

## 未完成的工作
- [需要继续的事项1]
- [需要继续的事项2]

## 关键发现
- [重要发现或决策]
- [遇到的问题或限制]

## 下一步建议
1. [建议的下一步操作1]
2. [建议的下一步操作2]

## 相关文件
- [已修改的文件列表]
- [需要参考的文件]

## 上下文
[其他 agent 需要知道的信息]
```

## 示例

```markdown
# 会话交接摘要

**日期：** 2026-05-27 11:00
**Agent：** mimo
**状态：** 进行中

## 当前任务
分析露天矿场景的 step-ack 连接状态

## 已完成的工作
- 搜索了 `Open-pit mine.unity` 场景文件
- 发现场景中缺少 `AgxSimStepAckServer` 组件
- 检查了简单场景的连接方式作为参考

## 未完成的工作
- 需要设计露天矿场景的 step-ack 连接方案
- 需要决定是否复用简单场景的服务器配置

## 关键发现
- 简单场景使用 `AgxSimStepAckServer` 组件连接到端口 5057
- 露天矿场景的挖掘机设备结构与简单场景类似
- 需要添加图感知提供器

## 下一步建议
1. 设计露天矿场景的 step-ack 连接方案
2. 参考 `Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity` 的配置
3. 决定是否需要单独的服务器实例

## 相关文件
- `Assets/AGXUnity_Excavator/Open-pit mine.unity`
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity`
- `Scripts/SimulationBridge/AgxSimStepAckServer.cs`

## 上下文
用户希望露天矿场景也能通过 step-ack 协议与 Python 通信
```

## 使用规则

1. **何时更新**：切换 agent 时，或完成重要工作后
2. **谁来更新**：当前工作的 agent
3. **谁来读取**：下一个接手的 agent
4. **保持简洁**：只写关键信息，不要冗长
5. **及时更新**：不要等到会话结束才写