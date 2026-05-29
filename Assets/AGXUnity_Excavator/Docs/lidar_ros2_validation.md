# AGX LiDAR ROS 2 Validation

Last updated: 2026-05-29

This note records the current AGX LiDAR setup used in `AGXUnity_Excavator.unity`.
It is a validation and handoff document, not a new implementation plan.

## Scope

Confirmed purpose:

- Validate AGX native LiDAR publishing into ROS 2.
- Keep LiDAR available for later perception, mapping, and real-machine migration.
- Do not block the RL trajectory-tracking controller on LiDAR heightmap quality.

Out of scope for the current close-out:

- Expanding `TerrainGraph` publishers.
- Implementing a new raycast-style LiDAR or depth sensor.
- Requiring FAST-LIO before RL controller work.
- Manually editing Unity serialized scene, prefab, asset, or ProjectSettings files.

## Current Unity Setup

Confirmed from Unity Editor inspection and user validation:

- Scene: `Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity`.
- Sensor environment object: `SensorEnvironment`.
- Active task LiDAR object: `DigAreaTaskLidar`.
- Cabin LiDAR publisher: disabled by user during the current LiDAR validation pass.
- Active ROS 2 point cloud topic: `/lidar/pointcloud`.
- Extended point cloud topic observed earlier: `/lidar/pointcloud_ex`.
- RViz fixed frame: `world`.
- RViz PointCloud2 reliability policy: `Best Effort`.

Latest user-provided Inspector snapshot for `DigAreaTaskLidar` showed:

| Field | Value |
| --- | --- |
| Lidar Model Preset | `Lidar Model Ouster OS2` |
| Channel Count | `Ch_128` |
| Beam Spacing | `Above Horizon` |
| Lidar Mode | `Mode_1024x20` |
| Lidar Range | `0.1` to `35` meters |
| Remove Ray Misses | enabled |

Interpretation notes:

- `Lidar Range` is in meters and controls valid hit distance, not angular field of view.
- `Above Horizon` / `Below Horizon` are relative to the LiDAR local frame. Because the task LiDAR has been tested in an inverted/under-boom pose, `Above Horizon` can be the setting that sends more beams toward world-down terrain.
- Ouster OS0 is still the preferred candidate if the goal is close-range, wide vertical coverage of the dig area. The current OS2 snapshot is recorded as the latest observed setting, not as a final recommendation.

## ROS 2 / RViz Validation

Confirmed by user during this validation pass:

- `ros2 topic list` included:
  - `/lidar/pointcloud`
  - `/lidar/pointcloud_ex`
- `ros2 topic hz /lidar/pointcloud` previously reported a stable non-empty stream around 50 Hz.
- `ros2 topic echo /lidar/pointcloud --once` produced a `sensor_msgs/msg/PointCloud2` with non-zero width/height and non-empty data.
- RViz displayed `/lidar/pointcloud` after setting PointCloud2 reliability to `Best Effort`.
- Moving/controlling the excavator changed the point cloud in real time.
- After LiDAR pose adjustment, RViz showed ground/terrain contours, but single-frame spinning LiDAR coverage remained sparse and view-dependent.

Recommended repeat validation commands:

```bash
source /opt/ros/jazzy/setup.zsh
ros2 topic list
ros2 topic hz /lidar/pointcloud
ros2 topic echo /lidar/pointcloud --once
rviz2
```

RViz display settings:

- Global Options / Fixed Frame: `world`
- Add: `PointCloud2`
- Topic: `/lidar/pointcloud`
- Reliability Policy: `Best Effort`
- Style: `Flat Squares`
- Size (m): tune only for display readability; this does not change LiDAR physics.

## Current Conclusion

The LiDAR stage is considered functionally open but good enough to close the initial ROS 2 smoke test:

- AGX native LiDAR can publish ROS 2 point clouds.
- ROS 2 and RViz can subscribe and visualize the point cloud.
- The point cloud is live and changes with machine motion.
- Single-frame LiDAR alone is not expected to provide dense, complete terrain heightmaps.

For RL trajectory tracking, LiDAR is not a blocking dependency. The controller can train against joint state, bucket pose, target trajectory error, and simulator reward signals.

For later terrain-aware planning, the likely perception split is:

- Cabin LiDAR: environment / coarse local mapping / real-machine migration.
- Arm-mounted depth camera or equivalent dense local sensor: close-range dig-area depth or heightmap.

## Known Limitations

- Current point cloud coverage depends strongly on LiDAR pose, vertical field of view, beam spacing, and occlusion.
- A single spinning LiDAR does not behave like a dense orthographic depth camera.
- Exact final LiDAR model and mounting pose are not frozen.
- PointCloud2 field contract still needs to be recorded, especially whether the AGX stream contains `intensity`, `ring`, and per-point `time` or `timestamp`.
- Latest high-density configuration should be rechecked with `ros2 topic hz` because OS2/Ch_128/1024x20 may have different runtime cost than earlier tests.

## Suggested Acceptance For This Stage

This LiDAR integration can be treated as stage-complete when:

- Only the intended LiDAR publishes `/lidar/pointcloud`.
- RViz shows a live non-empty point cloud with `Best Effort` QoS.
- `ros2 topic hz /lidar/pointcloud` is stable enough for inspection and downstream logging.
- `ros2 topic echo --once` confirms a non-empty `PointCloud2`.
- Current Inspector settings are captured in this document.
- Any future dense terrain observation work is routed through an explicit heightmap/depth-grid task instead of continued ad-hoc LiDAR pose tuning.

