# 交接快速参考

## 交接流程（3 步）

```
1. 更新交接文档
2. 告诉下一个 agent
3. 下一个 agent 读取继续
```

## 何时交接

- ✅ 切换 agent 时
- ✅ 完成重要工作后
- ✅ 遇到阻塞时
- ❌ 不需要每次操作都交接

## 交接文档位置

```
.ai/handoff/
├── latest-session-summary.md  ← 主要交接文档
├── project-context-compact.md ← 项目上下文
└── handoff-template.md        ← 交接模板
```

## 快速模板

```markdown
# 会话交接摘要

**日期：** YYYY-MM-DD
**Agent：** [你的名字]
**状态：** [进行中/已完成/阻塞]

## 当前任务
[一句话]

## 已完成
- [事项]

## 未完成
- [事项]

## 下一步
1. [建议]
```

## 给下一个 agent 的指令

```
请阅读 .ai/handoff/latest-session-summary.md 继续工作
```

## 常见场景

### 场景 1：mimo → Claude Code
```
mimo：我已分析代码，需要设计方案
mimo：更新交接文档
用户：切换到 Claude Code
用户：请阅读交接文档，设计 step-ack 连接方案
```

### 场景 2：Claude Code → mimo
```
Claude Code：设计方案已完成
Claude Code：更新交接文档
用户：切换到 mimo
用户：请阅读交接文档，执行修改
```

### 场景 3：遇到阻塞
```
mimo：发现问题，需要帮助
mimo：更新交接文档（标记为"阻塞"）
用户：切换到 Claude Code
用户：请阅读交接文档，帮助解决问题
```