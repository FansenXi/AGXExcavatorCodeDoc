# Claude Code 项目检查与代码整理 Prompt

将此复制到 Claude Code 会话中执行。

---

你正在检查 `GraphPerceptionPrj`，一个 Unity 2022.3.62f3 + AGX Dynamics 挖掘机仿真项目。

## 任务

1. **检查项目状态**
2. **整理代码**（重构、清理、优化）
3. **给出深度图感知的后续规划**

## 步骤 1：阅读项目文档

按顺序阅读：

1. `AGENTS.md` — AI 代理操作规则
2. `.ai/handoff/latest-session-summary.md` — 当前交接状态
3. `.ai/handoff/project-context-compact.md` — 项目上下文
4. `.ai/project/overview.md` — 项目概述
5. `.ai/project/known-issues-and-todo.md` — 已知问题
6. `.ai/project/repository-map.md` — 仓库结构
7. `.ai/project/vision-perception-pipeline.md` — 当前感知管线

## 步骤 2：检查项目状态

运行以下检查：

```bash
# 检查 Unity 场景文件
find Assets -name "*.unity" -not -path "*/Library/*"

# 检查项目脚本
find Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts -name "*.cs" | sort

# 检查图感知相关代码
ls -la Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/GraphPerception/

# 检查 git 状态
git status --short
git log --oneline -10

# 检查文档完整性
find .ai -name "*.md" -size 0
```

## 步骤 3：代码整理

检查并整理以下方面：

### 3.1 代码质量
- 检查是否有重复代码
- 检查是否有未使用的变量或导入
- 检查命名是否一致
- 检查注释是否清晰

### 3.2 项目结构
- 检查文件组织是否合理
- 检查是否有孤立的文件
- 检查命名约定是否一致

### 3.3 文档同步
- 检查代码与文档是否一致
- 标记过时的文档
- 更新 `.ai/project/known-issues-and-todo.md`

## 步骤 4：深度图感知规划

基于当前项目状态，给出深度图感知的实现规划：

### 当前状态
- 项目已有地形图观察 v0（基于高度图）
- 已有 FPV RGB 图像捕获
- 已有 AGX 可变形地形

### 规划内容

1. **技术方案**
   - 如何获取深度图（Unity 渲染 vs AGX 高度数据）
   - 深度图格式和分辨率
   - 与现有图观察的集成方式

2. **实现步骤**
   - 阶段 1：深度图捕获
   - 阶段 2：深度图处理
   - 阶段 3：与图观察集成
   - 阶段 4：Python 端消费

3. **架构设计**
   - 新增组件设计
   - 与现有管线的集成点
   - 性能考虑

4. **验收标准**
   - 功能验收
   - 性能验收
   - 集成验收

## 输出要求

1. **项目检查报告**
   - 当前状态总结
   - 发现的问题
   - 建议的改进

2. **代码整理结果**
   - 已完成的整理
   - 需要手动处理的项目

3. **深度图感知规划**
   - 技术方案
   - 实现步骤
   - 架构设计
   - 验收标准

## 约束

- 不要修改 `Assets/AGXUnity/`（第三方 AGX 核心）
- 不要修改 `Packages/com.algoryx.*`（固定上游包）
- 不要修改 `Library/`、`Temp/`、`Logs/`、`UserSettings/`（Unity 生成）
- 不要在没有说明的情况下删除文件
- 保持向向兼容性

## 交接

完成工作后，更新：
- `.ai/handoff/latest-session-summary.md` — 你做了什么
- `.ai/project/known-issues-and-todo.md` — 新发现的问题
- `.ai/project/vision-perception-pipeline.md` — 深度图规划