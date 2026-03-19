# 临时下一步计划

**日期：** 2026-03-18  
**状态：** 临时执行文档，给当前联调用  
**当前协议真值：** `Docs/protocol.md`

## 1. 当前已经落地的内容

Unity 侧已经完成这些关键改动：
- step-ack transport 已从 JSON 改成 TCP binary framing
- `qpos` 已扩成 4 维：
  - `[swing_position_norm, boom_position_norm, stick_position_norm, bucket_position_norm]`
- FPV 已支持 `raw_rgb` 原始字节导出
- `GET_INFO / RESET / STEP` 已走同一套 binary protocol
- `reset_pose` 已走当前 reset 链路

这意味着下一步不再是“继续补协议骨架”，而是：
- 验证当前 Unity 实现
- 把 Python/testbed backend 接上
- 跑最小联调验收

## 2. 下一步优先级

### P0. Unity 本地验证

先确认当前改动在 Unity 里是可运行的，而不是只停留在源码层。

需要做：
1. 打开 Unity，确认工程可编译
2. 进入目标场景，确认这些引用都已正确挂线：
   - `AgxSimStepAckServer`
   - `ActObservationCollector`
   - `TrackedCameraWindow`
   - `SceneResetService`
   - `EpisodeManager`
3. 手动验证 `GET_INFO -> RESET -> STEP`
4. 确认 `STEP_RESP` 在空动作下能稳定返回：
   - `qpos.len == 4`
   - `qvel.len == 4`
   - `env_state.len >= 1`
   - `image_payload.len == image_w * image_h * 3`

完成标准：
- Unity 无编译错误
- server 能稳定接收并返回三类消息
- FPV 图像尺寸和字节数匹配

### P0. Python/Testbed 侧实现 binary client

当前最大的缺口已经转到 Python 端。

需要新增：
- `testbed/backends/agx/backend.py` 或等价模块
- binary frame reader / writer
- CRC32 校验
- 三种 payload parser：
  - `GET_INFO_RESP`
  - `RESET_RESP`
  - `STEP_RESP`

Python 侧最小目标：
1. 连接 Unity socket
2. 发送 `GET_INFO_REQ`
3. 发送 `RESET_REQ`
4. 连续发送 500 个 `STEP_REQ`
5. 把响应还原成 testbed 可消费的：
   - `qpos`
   - `qvel`
   - `images["fpv"]`
   - `env_state`

### P0. 跑第一轮联调验收

先只做最小验收，不做训练。

最小验收项：
1. 10 秒 @ 50Hz，共 500 步
2. `step_id` 单调连续，无重复，无乱序
3. `RESET` 后前几步状态稳定
4. 图像 payload 每步都满足：
   - `image_format == "raw_rgb"`
   - `image_payload.len == image_w * image_h * 3`
5. Python 解码后的图像 shape 正确：
   - `(H, W, 3)`

## 3. P1 任务

### P1. 归一化范围校准

虽然 4 维 `qpos` 已经接上，但归一化范围还需要按真实 prefab 校准。

当前需要校准：
- `m_swingRange`
- `m_boomRange`
- `m_stickRange`
- `m_bucketRange`

目标不是先改协议，而是先确认：
- 每个 `*_position_norm` 在真实运行时都能覆盖 `[0, 1]`
- reset 后起始位姿落在预期区间
- 极限动作时不会长期卡在异常常数值

建议产出：
- 一份 scene/prefab 对应的 range 记录
- 一次 reset 后静态截图和对应 qpos 记录

### P1. 增加最小调试输出

如果联调阶段出问题，最好能快速定位是 transport、图像还是 reset。

建议补的最小调试项：
- server 收到的 `msg_type`
- `step_id`
- `image_w / image_h / image_payload.len`
- warning 列表
- reset 是否真正 applied

这部分可以先做日志，不一定马上进 HUD。

## 4. P2 任务

### P2. 文档收口

现在仓库里仍然有一些历史文档会误导人。

后续需要做：
1. 把 `add_teleop.md` 里仍然停留在旧设计草稿的段落收口
2. 把 `agx_teleop_to_act_hdf5.md` 里已经过时的缺口判断清掉
3. 如果决定长期使用 4 维 `qpos`，明确：
   - 是否继续沿用 `agx-sim/v0`
   - 或者把协议版本显式 bump

### P2. 自动化测试

建议后续补两类自动化：
- Python 侧 protocol parser 单元测试
- Unity + Python 的 500-step integration smoke test

## 5. 当前最推荐的执行顺序

建议就按下面顺序推进：

1. 在 Unity 里做一次人工 smoke
2. 实现 Python binary backend
3. 跑 500-step integration test
4. 校准 4 维 qpos normalization range
5. 再决定是否开始 teleop 录制 / replay / evaluator 接入

## 6. 当前最大的真实风险

不是协议骨架，而是这三件事：
- Python 端还没有按当前 binary layout 接起来
- 4 维 `qpos` 的 range 还没做场景级校准
- 当前改动还没有经过真实 500-step 联调验证

## 7. 一句话结论

下一步最应该做的不是继续改 Unity 协议字段，而是：
- 先验证当前 Unity 实现
- 再把 Python binary backend 接上
- 然后用 500-step smoke test 把这条链路跑通
