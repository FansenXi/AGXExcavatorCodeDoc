# V2.1 分层规划-执行设计草案

## 摘要
- V2.1 采用“两层控制”：上层 `TaskPlanner` 负责根据地形演化选择下一铲 `patch + depth`，下层 `ACT` 只负责执行当前铲并完成面向下一铲的 `handover`。
- 第一版采用 `规则式 planner`，`每铲边界重规划`，`不修改现有 live STEP protocol`。这三点已固定。
- `fixed ready/home` 不再是目标状态；V2.1 将 `ready_anchor` 升级为 `handover_anchor`，语义改为“面向下一铲入口的可复用交接姿态”。
- V2.1 的最小闭环是：`TerrainBeliefMap -> PlannerGoal -> goal_tokens -> ACT -> CycleSummary -> TerrainBeliefMap`。

## 1. TerrainBeliefMap 定义
- `TerrainBeliefMap` 放在 Repo A，本地维护，不依赖第一版 live protocol 扩展。
- 坐标系使用 `DigArea scene-local frame`：
  - 原点：DigArea 中心。
  - 朝向：长边为 `u` 轴，短边为 `v` 轴。
  - 所有 patch center 归一化到 `[-1, 1]`。
- 第一版网格固定为 `3x3`，配置驱动；后续允许升到 `5x5`，但 V2.1 草案默认 `3x3`。
- 每个 patch 用下面的结构定义：

```python
PatchBelief = {
    "patch_id": int,
    "u_norm": float,              # [-1, 1]
    "v_norm": float,              # [-1, 1]
    "target_depth_m": float,      # 场景配置给定
    "estimated_depth_m": float,   # 本地 belief，不是直接真值
    "remaining_depth_m": float,
    "depth_confidence": float,    # [0, 1]
    "visit_count": int,
    "last_fill_peak_kg": float,
    "last_deposit_delta_kg": float,
    "last_peak_bucket_depth_m": float,
    "collision_risk": float,      # 由碰撞计数/法向力摘要更新
    "planner_score": float,
    "state": int,                 # 0 unknown, 1 candidate, 2 active, 3 done, 4 blocked
}
```

- 整体地图结构如下：

```python
TerrainBeliefMap = {
    "scenario_id": str,
    "grid_rows": int,
    "grid_cols": int,
    "cycle_id": int,
    "patches": list[PatchBelief],
}
```

- `TerrainBeliefMap` 初始化规则：
  - 从 `scenario_id` 对应的场景配置加载 `DigArea frame`、网格尺寸、每个 patch 的 `target_depth_m`。
  - `estimated_depth_m = 0`，`remaining_depth_m = target_depth_m`，`depth_confidence = 0`。
- 每铲结束生成 `CycleSummary` 并更新 patch：
  - `qualified_dig = min_distance_to_dig_area_m <= 0.05 and peak_bucket_depth_below_plane_m >= 0.02`
  - `fill_peak_kg = max(mass_in_bucket_kg)`
  - `deposit_delta_kg = deposited_mass_end - deposited_mass_start`
  - `collision_delta = hard_collision_count_end - hard_collision_count_start`
  - `peak_bucket_depth_m = max(bucket_depth_below_dig_area_plane_m)`
  - 若 `qualified_dig` 且 `fill_peak_kg >= load_mass_threshold_kg`，则
    - `estimated_depth_m = max(prev_estimated_depth_m, min(goal.cut_depth_cmd_m, peak_bucket_depth_m))`
    - `remaining_depth_m = max(0, target_depth_m - estimated_depth_m)`
    - `depth_confidence += 0.2`，上限 `1.0`
  - 若同一 patch 连续 `2` 次访问都满足 `fill_peak_kg < low_fill_thresh` 且 `deposit_delta_kg < low_deposit_thresh`，则将 `state = done`
  - 若 `collision_delta > 0` 或 `target_contact_max_normal_force_n` 超阈值，则提高 `collision_risk`
- 第一版 planner 打分函数固定为：

```python
score =
    0.45 * remaining_depth_norm
  + 0.20 * frontier_bonus_norm
  + 0.15 * continuity_norm
  - 0.10 * revisit_penalty
  - 0.10 * collision_risk_norm
```

- `continuity_norm` 取“从上一铲 patch 到候选 patch 的 scene-local 距离代价”的反向归一化值。
- `frontier_bonus_norm` 取该 patch 邻域内 `remaining_depth_m` 的均值，鼓励按区域连续推进，而不是随机跳点。

## 2. PlannerGoal / goal_tokens 具体字段
- 对外新增三个接口类型：`CycleSummary`、`PlannerGoal`、`TaskPlanner`。
- `TaskPlanner` 的最小接口固定为：

```python
class TaskPlanner:
    def reset(self, scenario_id: str) -> None: ...
    def bootstrap_first_goal(self, belief_map: TerrainBeliefMap) -> PlannerGoal: ...
    def replan_at_cycle_boundary(
        self,
        belief_map: TerrainBeliefMap,
        last_cycle: CycleSummary,
    ) -> PlannerGoal: ...
```

- `PlannerGoal` 固定为日志/回放/评测使用的结构化目标：

```python
PlannerGoal = {
    "cycle_id": int,
    "scenario_id": str,
    "src_patch_id": int,
    "src_u_norm": float,
    "src_v_norm": float,
    "cut_depth_class": int,       # 0/1/2
    "cut_depth_cmd_m": float,
    "dst_target_id": int,
    "handover_anchor_id": int,    # 左/中/右交接姿态，不再表示 fixed home
    "max_cycle_steps": int,
    "next_src_patch_id": int,
    "next_src_u_norm": float,
    "next_src_v_norm": float,
    "next_cut_depth_class": int,
    "next_cut_depth_cmd_m": float,
    "next_dst_target_id": int,
    "has_lookahead": bool,
    "plan_source": str,           # 固定为 "rule"
}
```

- `cut_depth_class` 第一版固定为 `3` 档，映射规则为：
  - `0 = shallow = 0.35 * target_depth_m`
  - `1 = medium  = 0.60 * target_depth_m`
  - `2 = deep    = 0.85 * target_depth_m`
- `handover_anchor_id` 第一版固定为 `3` 类：
  - `0 = left_entry`
  - `1 = mid_entry`
  - `2 = right_entry`
- `handover_anchor_id` 由 `next_src_patch_id` 的列索引决定，不再对应“回零位”，而对应“下一铲入口侧”的 qpos 模板。
- 下层 ACT 真实消费的是 `goal_tokens`，固定为 `10D float32`：

```python
goal_tokens = [
    curr_src_u_norm,
    curr_src_v_norm,
    curr_cut_depth_norm,     # cut_depth_cmd_m / patch.target_depth_m
    curr_dst_target_norm,
    handover_anchor_norm,
    next_src_u_norm,
    next_src_v_norm,
    next_cut_depth_norm,
    next_dst_target_norm,
    has_lookahead,           # 0.0 / 1.0
]
```

- `goal_tokens` 通过 `low_dim_keys = [qpos, qvel, goal_tokens]` 拼入现有 ACT `proprio` 路径，不新增独立 encoder。
- `StructuredState.as_policy_input()` 需要新增 `goal_tokens` 字段；planner 冻结当前铲目标，直到本铲结束才更新下一份 `PlannerGoal`。

## 3. 是否修改 protocol 的最小方案与推荐方案
- **最小方案，V2.1 正式采用：不改 live STEP protocol。**
- 保持 `STEP_REQ/STEP_RESP` 现状不变，继续使用现有 `qpos / qvel / env_state / fpv`。
- 利用已有 `RESET_REQ.scenario_id` 选择场景配置；场景配置在 Repo A 本地提供：
  - `DigArea frame`
  - `patch grid`
  - `target_depth map`
  - `target id vocab`
  - `handover_anchor qpos templates`
- 上层 planner 只依赖两类输入：
  - 当前 live 观测：`qpos / qvel / env_state / fpv`
  - 本地累计 belief：上一铲 patch、填充结果、depth proxy、碰撞摘要
- HDF5 扩展采用 add-only：
  - 新增 `/v2/step/*`
  - 新增 `/v2/cycle/*`
  - 不改现有主 schema 结构

- **推荐方案，作为 V2.2 的协议增强：新增低频 `PlannerSummary` side-channel，而不是扩展每步 STEP_RESP。**
- 推荐新增一组可选请求/响应，而不是给控制回路每步加大 payload：
  - `GET_PLANNER_SUMMARY_REQ`
  - `GET_PLANNER_SUMMARY_RESP`
- `GET_PLANNER_SUMMARY_RESP` 字段固定为：
  - `scenario_id: string`
  - `dig_area_origin_world_xyz: float32[3]`
  - `dig_area_yaw_rad: float32`
  - `bucket_pose_world: float32[7]`
  - `active_target_id: int32`
  - `patch_rows: int32`
  - `patch_cols: int32`
  - `patch_height_mean_m: float32[P]`
  - `patch_removed_mass_est_kg: float32[P]`
  - `patch_confidence: float32[P]`
- Repo A planner 仅在 `reset` 和 `cycle boundary` 轮询该摘要；若摘要不可用，则自动退回本地 `TerrainBeliefMap`。
- 不推荐把原始 terrain mesh、点云或每步全量 patch 图直接塞进 `STEP_RESP`。

## 4. 多 cycle 数据该怎么录和怎么评测
- 数据集拆成两类：
  - `single-cycle regression set`：保留单铲基线，验证新 goal_tokens 不退化。
  - `multi-cycle execution set`：主训练集，至少 `2-cycle`，建议补一小批 `3-cycle`。
- 录制原则固定：
  - Episode 内不做 pose reset。
  - 每一铲开始前，先由 planner 生成 `current_goal + next_goal`。
  - 操作者按当前 goal 执行，同时在 dump 尾段按 `next_goal` 做 handover。
  - 录制必须覆盖“第一铲 dump 尾段 -> 第二铲入口”这一段，不能只录到第一铲 dump 完成。
- `/v2/step` 固定新增：
  - `cycle_id`
  - `phase_id`
  - `phase_progress`
  - `goal_tokens`
  - `planner_replan_mask`
  - `boundary_mask`
  - `pause_mask`
- `/v2/cycle` 固定新增：
  - `start_step`
  - `end_step`
  - `src_patch_id`
  - `cut_depth_class`
  - `cut_depth_cmd_m`
  - `dst_target_id`
  - `handover_anchor_id`
  - `next_src_patch_id`
  - `next_cut_depth_class`
  - `next_cut_depth_cmd_m`
  - `fill_peak_kg`
  - `deposit_delta_kg`
  - `peak_bucket_depth_m`
  - `collision_count_delta`
  - `cycle_success`
  - `plan_source`
- 评测分三组：
  - 执行质量：
    - `cycle_success_rate`
    - `avg_fill_peak_kg`
    - `avg_deposit_delta_kg`
    - `avg_target_hard_collision_count`
  - 衔接质量：
    - `handover_gap_steps`
    - `boundary_jump_l1`
    - `boundary_jump_l2`
    - `mean_action_jerk`
    - `pause_ratio`
  - 规划效果：
    - `cycle2_success_rate`
    - `cycle3_success_rate`
    - `carry_over_drop`
    - `patch_coverage_ratio`
    - `depth_band_hit_rate`
- `depth_band_hit_rate` 第一版定义为：
  - `abs(peak_bucket_depth_m - cut_depth_cmd_m) <= depth_tolerance_m`
  - `depth_tolerance_m = 0.25 * patch.target_depth_m`
- 基线对照固定为三组：
  - `fixed-home ACT`
  - `lookahead ACT + 固定 patch 序列`
  - `rule-planner + lookahead ACT`
- 验收门槛固定为：
  - 单铲成功率不低于现有 V2.0.1 单 cycle 基线
  - `cycle2_success_rate >= 0.8 * cycle1_success_rate`
  - `handover_gap_steps` 相比 fixed-home 基线下降至少 `30%`
  - 非中心 patch 成功率相对中心 patch 的跌幅不超过 `15` 个百分点

## 测试与验收
- 单元测试：
  - `TerrainBeliefMap` 初始化、cycle 更新、patch 打分、patch 选择稳定可重复
  - `PlannerGoal -> goal_tokens` 编码维度和归一化范围固定为 `10D`
  - `/v2` HDF5 roundtrip 成功，旧 reader 不因新增 group 失效
- 集成测试：
  - 单铲场景下，加入 planner/goal_tokens 后不破坏现有 `qpos/qvel/env_state/fpv` 流程
  - 两铲场景下，planner 在 cycle boundary 正确切换 `current_goal/next_goal`
  - recorder、eval、video/log summary 都能产出 cycle 级字段和多铲指标
- 在线验证：
  - 固定场景 `s0` 做单铲回归
  - 固定 `2-cycle` 计划做 continuity 验证
  - `3x3` patch 切换场景做 patch 覆盖验证
  - pose jitter 场景做泛化验证

## 假设与默认值
- V2.1 上层 planner 固定为规则式，不训练 planner。
- planner 固定在每铲边界重规划，不做步级连续重规划。
- V2.1 正式方案不改 live STEP protocol。
- 深度命令第一版采用场景相对深度档位，不要求直接恢复真实连续地形高度。
- `handover_anchor` 取代固定 `ready/home`，语义是“面向下一铲入口的交接姿态模板”。
