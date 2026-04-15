# ROI Train 模块迁移计划

## Summary

把 ROI 正式采集/训练主线迁移到 `excavator_testbed_tx_dev_agxunity`，以 **“ROI Train” 概念 + testbed 风格 CLI 家族** 落地。  
正式路径由 testbed 负责：控制输入、episode 生命周期、session/benchmark 元数据、数据批次管理、QC、训练与评测编排。  
Unity 保留 ROI 推理与真值生成能力，但不再作为正式训练流程的主控端；`ManualExport` 降级为只用于补 hard-case 的旁路工具。

默认决策：
- 目标仓库：`excavator_testbed_tx_dev_agxunity`
- 模块形态：CLI 家族
- 数据归属：独立 ROI 数据集，复用 teleop 的 episode/session id 与 metadata

## Implementation Changes

### 1. 模块边界与入口
新增一组 testbed CLI，统一归入 “ROI Train” 模块：
- `tb-roi-record`：正式 ROI 数据采集入口，复用 AGX teleop backend 和键盘/摇杆输入
- `tb-roi-dataset-qc`：ROI 数据集质量检查
- `tb-roi-train`：导出、训练、ONNX 导出、TensorRT engine 构建编排
- `tb-roi-eval`：离线 benchmark / validation 评测
- `tb-roi-benchmark-record`：可选，冻结 benchmark episode 采集入口；若不单独做命令，则作为 `tb-roi-record --mode benchmark`

模块组织默认放在 testbed 包内新增 `testbed/roi/` 子包，CLI 入口放在 `testbed/cli/`，配置放在 `testbed/configs/roi_*.yaml`。

### 2. 数据与 episode 归属
ROI 数据集保持独立目录，不并入现有 teleop HDF5：
- `data/roi_train/<session>/...`
- `data/roi_benchmark/<session>/...`
- 每个 ROI episode 一个 HDF5，沿用现有 add-only 风格
- episode/session/operator/config snapshot 与 teleop 保持同一套命名与 metadata 语义

记录策略：
- testbed 是 episode id、session id、split、benchmark tag 的唯一来源
- ROI HDF5 记录：
  - 图像帧
  - 4 类检测标签：`bucket / excavator_arm / truck / container`
  - 规则 ROI 元数据：`dig_area` 不作为检测类，只作为 rule ROI 记录
  - 去重与质量特征：dHash、is_kept、label_area_ratio、candidate/final stats
- 旧 5 类 ROI 数据不进入新主训练集；保留为 legacy 资产，不自动混入

### 3. 采集与 Unity/AGX 接口
正式采集路径改为 testbed 驱动：
- 复用 `tb-record-teleop` 的 AGX backend、键盘/摇杆输入与 reset/episode 循环
- `tb-roi-record` 在每个 step 读取 AGXUnity 返回的 FPV 图像和同步状态
- 真值生成不再依赖 Unity 的 `ManualExport` 落盘流程，而是通过统一接口从 Unity/AGX 获取 ROI 标注所需信息

实现决策：
- 优先复用现有 step-ack 协议可获得的观测；若当前协议不足以生成 4 类真值，则扩展 AGXUnity 返回的 ROI/scene annotation payload，而不是继续依赖 Unity 本地散文件导出
- `dig_area` 在 testbed 侧只作为 rule ROI 元数据保存，不进入视觉检测标签
- `ManualExport` 保留，但标记为 `ad-hoc hard-case capture`，不再作为默认正式采集模式

### 4. 训练与评测主线
`tb-roi-train` 统一编排：
1. ROI HDF5 -> YOLO 导出
2. 4 类训练
3. ONNX 导出
4. TensorRT FP16 engine 构建
5. 评测结果落盘

默认训练档位：
- 采集分辨率：`1920x1080`
- 训练/部署输入：`640x640`
- 视觉类：4 类
- `dig_area`：规则 ROI，不训练
- 部署主线：Unity native TensorRT，不再把 Python sidecar 当正式低时延路径

`tb-roi-eval` 输出：
- per-class AP / precision / recall
- benchmark 集单独结果
- 推理耗时统计
- 版本历史 CSV / JSON

### 5. 迁移与兼容策略
迁移按三段收口：
- 第一段：把现有 ROI Python 工具能力迁入 testbed 侧，形成新 CLI 和配置，但不立即删除 Unity 旧工具
- 第二段：让 testbed 成为正式采集与训练入口，Unity 旧 `roi_dataset_tool.py / roi_train.py / roi_eval.py` 变为兼容包装或 legacy 文档入口
- 第三段：冻结旧 5 类流程，仅保留读取和回溯能力，不再继续产出新数据

兼容默认：
- 新旧数据集分目录存放，绝不自动混用
- Unity runtime 继续消费最终 ONNX/engine 产物
- ROI runtime 调试窗口保留，用于 live 观察，不承担正式训练流程编排职责

### 6. 文档交付
迁移完成后更新/新增简洁文档：
- testbed 侧新增 `ROI Train` 使用手册：采集、QC、训练、评测、benchmark
- Unity 侧 ROI 文档改成“runtime/inference + rule ROI + 如何接入 testbed 产物”
- 新增迁移说明：旧 `ManualExport` / 旧 5 类数据 / sidecar 路线的定位与限制

## Test Plan

- **采集链路**
  - `tb-roi-record` 能用键盘和摇杆录制 ROI HDF5
  - episode reset、discard、quit、session metadata 与 teleop 行为一致
  - train/val/benchmark 标记稳定且可复现
- **标签语义**
  - 新数据集中只出现 4 个视觉类
  - `dig_area` 只作为 rule ROI 元数据，不进入检测标签
  - 新旧数据不会被同一次训练自动混用
- **训练链路**
  - `tb-roi-train` 能从 ROI HDF5 产出 YOLO 导出、`best.onnx`、TensorRT engine
  - 默认 640 模型可被 Unity native TensorRT 直接加载
- **评测链路**
  - `tb-roi-dataset-qc` 产出 summary JSON/CSV
  - `tb-roi-eval` 输出 4 类 AP/precision/recall 和 benchmark 结果
- **运行时回归**
  - Unity native runtime 中 `Unknown` 常规为 0
  - `dig_area` 仅显示为 RuleRoi
  - ROI count 回到真实目标数附近
  - 推理延迟保持在 native TensorRT 目标区间

## Assumptions

- ROI 正式主线接入的是 `excavator_testbed_tx_dev_agxunity`，不是基础 testbed。
- “ROI Train” 是产品概念名，用户表面看到的是一组 testbed 风格命令，而不是单一巨型入口。
- 正式训练集从头按 4 类重建；旧 5 类数据仅作历史参考。
- `ManualExport` 不删除，但只保留为补 hard-case 的辅助通道，不再作为主流程。
