# Terrain Graph Observation Schema

**Status:** v0 (offline export only); volumetric subsurface + bucket-mesh tool
sampling landed 2026-05-25. Context background nodes + tool↔soil KNN edges
landed 2026-05-25 (same day, follow-up).
**Schema version string:** `terrain_graph_observation_v0` (additive update —
new fields default to 0 / empty; new `kind` values added; surface node kind
renamed `surface` → `surface_soil`).
**Replaces:** `terrain_graph_snapshot_v0` (offline snapshot exporter)
**Last updated:** 2026-05-25

This document specifies the JSON schema produced by the Unity-side
`TerrainGraphObservationProvider`. The first deliverable is an *offline export*
written by `TerrainGraphSnapshotExporter` to `TerrainGraphSnapshots/*.json`.
The same provider is intended to feed later online graph observation channels;
see the "Future protocol integration" section.

## 0. Target scene

Graph perception v0 is wired and validated only in
`Assets/AGXUnity_Excavator/AGXUnity_Excavator.unity` — the simple scene with
excavator + sand pool + truck. The repo also keeps `Open-pit mine.unity` for
later multi-scene experiments, but no graph perception components are attached
there yet. Historical `AGXUnity_Excavator_measurements.unity` has been
removed.

## 1. Files and components

- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/GraphPerception/TerrainGraphProtocol.cs`
  Serializable data classes consumed by `JsonUtility`.
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/GraphPerception/TerrainGraphObservationProvider.cs`
  MonoBehaviour that samples the `DeformableTerrain` heightfield, synthesizes
  volumetric subsurface soil nodes, samples AGX dynamic soil particles, and
  (optionally) emits multi-point tool nodes and radius edges.
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/GraphPerception/BucketSamplePointProvider.cs`
  Companion MonoBehaviour. Holds an inspector list of bucket-anchored
  `Transform`s (cutting edge, back wall, side walls, base); the observation
  provider emits one `kind = tool` node per entry. Falls back to a single
  bucket-origin node when no points are configured.
- `Assets/AGXUnity_Excavator/AGXUnity_Excavator_Assets/Scripts/TerrainGraphSnapshotExporter.cs`
  Thin wrapper around the observation provider that handles key/context-menu
  triggers and writes one JSON file per export.

The exporter previously inlined sampling. It now delegates to the provider so
that offline exports and any future online channels share one schema.

## 2. Top-level JSON layout

```jsonc
{
  "schema_version": "terrain_graph_observation_v0",
  "frame": <int>,                  // Unity Time.frameCount when sampled
  "sim_time_sec": <float>,         // Unity Time.time
  "scenario_id": "<string>",       // optional, supplied via SetScenarioId or inspector
  "metadata": { ... },             // see TerrainGraphMetadata
  "nodes":   [ TerrainGraphNode, ... ],
  "edges":   [ TerrainGraphEdge, ... ],

  // Legacy compatibility projections so existing Tools/TerrainGraph scripts
  // (visualize_terrain_graph_snapshot.py, terrain_graph_snapshot_to_svg.py)
  // keep working without modification.
  "surface_nodes": [ { "i", "j", "x", "y", "z", "height" }, ... ],
  "particles":     [ { "index", "x", "y", "z", "radius", "mass" }, ... ]
}
```

The two legacy arrays carry the same surface samples / dynamic particles as
`nodes`, but in *world coordinates* and in the old key layout. New consumers
should prefer `nodes`/`edges` plus `metadata.world_from_graph_origin_*`.

## 3. Coordinate frame

All `TerrainGraphNode` positions and all `TerrainGraphEdge` deltas are
expressed in a **graph-local frame** centered on the resolved Region of
Interest. v0 uses translation only; rotation is identity.

```
position_world = (graph_x, graph_y, graph_z) + (world_from_graph_origin_x, _y, _z)
```

Metadata exposes both the origin and a quaternion `world_from_graph_rotation_*`
so future revisions can rotate the graph frame without breaking the protocol.

`metadata.coordinate_frame` will read `graph_local_translated` for v0.

The RoI center is chosen by `roi_center_mode`:

| mode             | source                                                                  |
|------------------|-------------------------------------------------------------------------|
| `bucket`         | `ExcavatorMachineController.BucketReference.position` (recommended)     |
| `dig_area`       | `DigAreaMeasurement.FindOrCreateInScene().transform.position`           |
| `terrain_center` | terrain center, computed from `TerrainData.size`                        |
| `world_origin`   | `Vector3.zero`                                                          |
| `custom`         | inspector-assigned `Transform`                                          |

If the requested mode cannot resolve a transform (e.g. bucket reference is
null), the provider falls back to `terrain_center`. Legacy `surface_nodes` /
`particles` are always emitted in *world* coordinates so the old visualizers
remain valid.

## 4. Node kinds

`TerrainGraphNode.kind` is a string. v0 emits:

| kind                | meaning                                                                                          |
|---------------------|--------------------------------------------------------------------------------------------------|
| `surface_soil`      | Sampled deformable-terrain heightfield cell (the top of a soil column). Was named `surface` in the very first v0; renamed 2026-05-25 for consistency with `subsurface_soil`. |
| `subsurface_soil`   | **Synthetic** volumetric soil node generated under a sampled surface cell. Optional; gated by `include_subsurface_soil`. AGX does not expose per-particle handles for un-failed sub-surface granular material, so we voxelize from the observed surface downward (à la Liu et al. 2026). |
| `dynamic_soil`      | AGX dynamic soil particle (granular body) currently exposed by `DeformableTerrain.GetParticles()`. |
| `tool`              | Bucket-anchored sample point. If a `BucketSamplePointProvider` is attached, one node per configured sample point; otherwise a single fallback node at the bucket reference origin. |
| `dynamic_rigidbody` | Reserved for future dynamic-rock / dynamic-body sampling.                                        |

Per-node fields, units, and intent:

| field                    | units / type | notes                                                                                                                                       |
|--------------------------|--------------|---------------------------------------------------------------------------------------------------------------------------------------------|
| `id`                     | int          | dense, frame-local; not stable across frames                                                                                                |
| `kind`                   | string       | see table above                                                                                                                             |
| `x`, `y`, `z`            | meters       | graph-local position (RoI-centered)                                                                                                         |
| `vx`, `vy`, `vz`         | m/s          | reserved; v0 always emits zero (no velocity history yet)                                                                                    |
| `radius`                 | meters       | `surface_soil`: `element_size / 2`; `subsurface_soil`: `subsurface_spacing_m / 2`; `dynamic_soil`: AGX particle radius; `tool`: 0           |
| `mass`                   | kg           | `dynamic_soil`: AGX particle mass; otherwise 0 (subsurface mass requires a density assumption — left to Python-side preprocessing for v0)   |
| `height`                 | meters       | raw heightfield value for `surface_soil`; `world_y - depth` for `subsurface_soil`; world Y for `dynamic_soil` / `tool`                      |
| `depth_below_surface`    | meters       | 0 for `surface_soil` and `tool`; positive for `subsurface_soil` (`= k * subsurface_spacing_m`); reserved (0) for `dynamic_soil` — future work may populate it from a terrain-height lookup |
| `surface_i`, `surface_j` | int          | heightfield grid index for `surface_soil` and the parent cell for `subsurface_soil`; -1 otherwise                                          |
| `source_index`           | int          | `dynamic_soil`: AGX `at(index)`; `tool`: index into the bucket sample point list; -1 otherwise. Not stable across frames                    |
| `is_dynamic`             | 0/1          | 1 for `dynamic_soil` / dynamic bodies                                                                                                       |
| `is_tool`                | 0/1          | 1 for `tool` nodes                                                                                                                          |
| `is_in_roi`              | 0/1          | 1 for active nodes inside the RoI radius; 0 for "context" background nodes inside the RoI context annulus (only when `include_context_nodes` is true). Out-of-context nodes are never emitted. Tool nodes are always 1. |
| `boundary_normal_*`      | unit vector  | reserved; v0 emits zero. Future RoI work will populate these                                                                                |

## 5. Edges

The provider builds two independent edge sets and concatenates them into the
single `edges` array. Both sets share the same row schema below; consumers
distinguish them by `kind_pair`.

### 5.1 Radius edges (soil ↔ soil)

When `include_edges` is enabled and `edge_radius_m > 0` and `max_edges > 0`,
the provider performs an O(N²) radius search over **active soil candidates
only**:

- tool nodes are excluded (handled separately, see §5.2)
- context nodes (`is_in_roi == 0`) are excluded — they're background-only
- candidate count is bounded to `max_edge_builder_nodes` (default 1500) to
  keep the cost predictable; a uniform subsample is taken if the limit is hit

Edges are appended in pair order, terminating when `max_edges` is reached.
`kind_pair` will be one of `surface_soil|surface_soil`,
`surface_soil|subsurface_soil`, `subsurface_soil|subsurface_soil`,
`surface_soil|dynamic_soil`, `subsurface_soil|dynamic_soil`,
`dynamic_soil|dynamic_soil`.

### 5.2 Tool–soil KNN edges

When `include_tool_soil_edges` is enabled, the provider connects every tool
node to its **K nearest active soil nodes** (default `K = 4`). Soil here
means the same three soil kinds listed above; context soil is excluded so
edges only describe the localized graph the GNN sees. An optional
`tool_soil_edge_max_distance_m` (default 1.5 m) caps how far a tool will
"reach" — set to `0` to disable the cutoff. Total tool-soil edges are
bounded by `max_tool_soil_edges` (default 200).

`kind_pair` will be one of `tool|surface_soil`, `tool|subsurface_soil`,
`tool|dynamic_soil`.

### 5.3 Per-edge fields

| field        | units / type | notes                                                                            |
|--------------|--------------|----------------------------------------------------------------------------------|
| `src`, `dst` | int          | references `nodes[i].id`                                                         |
| `dx`, `dy`, `dz` | meters   | `nodes[dst] - nodes[src]` in graph-local frame                                   |
| `distance`   | meters       | Euclidean length of `(dx, dy, dz)`                                               |
| `kind_pair`  | string       | "`src.kind` \| `dst.kind`", see tables above                                    |

### 5.4 Counts

`metadata.edge_count` is the total. `metadata.radius_edge_count` and
`metadata.tool_soil_edge_count` give the per-set breakdown for sanity checks.

Edges are undirected by convention (only one direction is emitted). Production
graph construction should happen Python-side once schema and sampling
stabilize.

## 6. Metadata

`TerrainGraphMetadata` records the sampling configuration *and* the resolved
RoI/coordinate frame, so a JSON consumer never needs to inspect Unity to
interpret the export.

Configuration / inputs:
- `terrain_name`, `terrain_resolution`, `terrain_width`, `terrain_length`,
  `terrain_height`, `element_size`, `maximum_depth` — `DeformableTerrain`
  geometry.
- `surface_stride`, `max_surface_nodes`, `max_dynamic_particles`,
  `include_surface_nodes`, `include_dynamic_particles`, `include_tool_nodes`,
  `include_edges`, `edge_radius_m`, `max_edges` — sampling controls.

Resolved RoI / coordinate frame:
- `roi_center_mode` (string), `roi_radius_m`, `roi_height_margin_m`,
  `roi_enabled` (`1` when `roi_radius_m > 0`).
- `roi_center_world_x/y/z` — world coordinates of the RoI center.
- `coordinate_frame` (`graph_local_translated` in v0).
- `world_from_graph_origin_x/y/z` and `world_from_graph_rotation_x/y/z/w` —
  rigid transform from graph frame to world.

Output telemetry (filled after sampling):
- `surface_node_count`, `dynamic_particle_count`, `tool_node_count`,
  `edge_count`.
- `dynamic_particles_available` — total `agx.GranularBodyPtrArray.size()`.
- `dynamic_particles_skipped_outside_roi`.
- `bucket_reference_valid`, `bucket_reference_name`, `bucket_world_x/y/z`.

## 7. Sampling controls

`TerrainGraphObservationProvider` inspector fields:

| field                       | default       | meaning                                                                 |
|-----------------------------|---------------|-------------------------------------------------------------------------|
| `m_terrain`                 | auto-resolved | `DeformableTerrain` to sample                                           |
| `m_machineController`       | auto-resolved | bucket reference source                                                 |
| `m_bucketReferenceOverride` | null          | manual override for the bucket transform                                |
| `m_bucketSampleProvider`    | auto-resolved | companion `BucketSamplePointProvider` for multi-point tool emission     |
| `m_customRoiCenter`         | null          | transform used when `roi_center_mode == Custom`                         |
| `m_roiCenterMode`           | `Bucket`      | see table in section 3                                                  |
| `m_roiRadiusMeters`         | 0             | XZ filter radius; <=0 disables the radius filter                        |
| `m_roiHeightMarginMeters`   | 0             | vertical filter half-extent for particles; <=0 disables the height filter |
| `m_surfaceStride`           | 4             | heightfield decimation                                                  |
| `m_maxSurfaceNodes`         | 0             | hard cap on emitted surface nodes; 0 disables                           |
| `m_maxDynamicParticles`     | 20000         | upper bound; provider uses ceil(total/maxCount) as stride               |
| `m_includeSurfaceNodes`     | true          |                                                                         |
| `m_includeDynamicParticles` | true          |                                                                         |
| `m_includeToolNodes`        | false         | emits one node per bucket sample point (or a fallback bucket origin)    |
| `m_includeSubsurfaceSoil`   | false         | enables synthetic volumetric column under each sampled surface cell     |
| `m_subsurfaceSpacingMeters` | 0.15          | vertical spacing of subsurface_soil nodes (meters)                      |
| `m_subsurfaceDepthMeters`   | 1.5           | how far below each surface cell to emit subsurface_soil nodes (meters)  |
| `m_maxSubsurfaceNodes`      | 0             | hard cap on emitted subsurface_soil nodes; 0 disables                   |
| `m_includeContextNodes`     | false         | emit background `is_in_roi=0` nodes in the annulus `(roi_radius, context_radius]` |
| `m_roiContextRadiusMeters`  | 0.0           | XZ outer radius for context emission; only effective when `> m_roiRadiusMeters` |
| `m_includeEdges`            | false         | enables radius-based soil↔soil edges                                   |
| `m_edgeRadiusMeters`        | 0.5           |                                                                         |
| `m_maxEdges`                | 5000          | hard cap on radius edges                                                |
| `m_maxEdgeBuilderNodes`     | 1500          | subsamples active soil candidates before O(N²) radius pairing           |
| `m_includeToolSoilEdges`    | false         | enables tool→soil KNN edges                                             |
| `m_toolSoilEdgeK`           | 4             | K nearest active soil nodes each tool connects to                       |
| `m_toolSoilEdgeMaxDistance` | 1.5           | max tool-to-soil distance (meters); 0 disables the cutoff               |
| `m_maxToolSoilEdges`        | 200           | hard cap on tool-soil edges                                             |
| `m_scenarioId`              | ""            | echoed into `scenario_id`                                               |

`BucketSamplePointProvider` inspector fields:

| field                       | default | meaning                                                                                              |
|-----------------------------|---------|------------------------------------------------------------------------------------------------------|
| `m_points`                  | empty   | list of `{ transform, label }`; the provider emits the world position of each non-null `transform`   |
| `m_includeOriginFallback`   | true    | when `m_points` is empty, emit a single node at `m_originFallback` (or the provider's bucket reference) |
| `m_originFallback`          | null    | fallback transform used when `m_points` is empty; null falls back to the bucket reference            |

Recommended starting set for `m_points` (5–8 anchors on the CAT 365 bucket):
cutting edge left, cutting edge mid, cutting edge right, back wall mid, left
side wall mid, right side wall mid, base mid. These give the GNN enough
geometry to learn bucket↔soil contact without exploding node counts.

## 8. Edge / RoI semantics

- When `include_context_nodes` is enabled and `roi_context_radius_m >
  roi_radius_m`, nodes in the annulus `(roi_radius, roi_context_radius]` are
  emitted with `is_in_roi = 0`. They are intended as the half-transparent
  background visible in Liu et al. 2026 Figure 1 and are not used by the
  edge builders (so they don't pollute the localized graph that the GNN
  consumes). They also do **not** appear in the legacy `surface_nodes[]` /
  `particles[]` projections, so existing `Tools/TerrainGraph/*.py`
  visualizations keep their previous look.
- When the user later trains a learned RoI proposer, `is_in_roi` is the
  natural prediction target: in-RoI = 1 (active particle), context = 0
  (background needed for supervision). The geometric RoI used today is the
  trivial baseline.
- `boundary_normal_*` is still zero in v0. The recommended first
  approximation is the XZ unit vector from the RoI center to the node, with
  vertical clipping normals when a height window is in use. Now that
  context nodes are exported, Python can also compute boundary normals from
  the in-RoI / context label transitions.
- Radius edges and tool-soil edges are both deterministic for a given
  (frame, node ordering, radius, K, caps) tuple. Edges from the two sets
  never overlap by construction (radius edges exclude tools; tool-soil
  edges only connect tool→soil).

## 9. Known limitations

- No node velocity history. `vx/vy/vz` are reserved fields that always read
  zero. Adding history requires the provider to cache the previous frame's
  node positions and re-identify them; the current `id` field is *not* stable
  across frames.
- Dynamic particles do not carry stable identifiers across frames. AGX's
  `at(index)` index is reused as the underlying particle pool changes, so
  consumers should match particles by position/time, not by `source_index`.
- **Subsurface_soil nodes are synthetic, not real particles.** They sit on a
  regular voxel column under each sampled surface cell with no awareness of
  AGX's internal terrain compaction, voids, or layered materials. They are
  intended as an input representation for GNN-style dynamics models — not as a
  faithful physics readout. When AGX has failed real soil into granular
  particles, prefer the `dynamic_soil` nodes for that volume.
- **Subsurface_soil mass is 0** in v0. Adding mass requires a density
  assumption per soil layer; do that on the Python side until terrain
  material metadata is plumbed through.
- **Tool sampling fidelity is operator-defined.** Without a
  `BucketSamplePointProvider`, the export contains a single bucket-origin
  node. With the provider, sample density and placement depend on how many
  child Transforms the operator wired up. Bucket mesh raycast / uniform
  surface sampling is future work.
- **`depth_below_surface` is not yet computed for `dynamic_soil`.** It stays
  0 there; future work can look up the local heightfield at the particle's
  XZ position.
- The radius edge builder is still O(N²) over the active (in-RoI, non-tool)
  candidate set, capped at `max_edge_builder_nodes` (default 1500). For
  larger graphs, a Python-side kd-tree pass remains the recommended path.
- The tool-soil edge builder is O(tools × soil) with a per-tool sort.
  Total cost is light for the typical 4–8 tool sample points but scales
  linearly with soil count, so very dense subsurface columns will slow it
  down. Keep `max_tool_soil_edges` set to bound the worst case.
- Surface sampling mirrors the legacy exporter, which reads `GetHeights(0, 0,
  resolution-1, resolution-1)`. The terminal row/column is therefore
  dropped, matching the previous behavior.
- The provider does not modify AGX state but does pay one
  `GranularBodyPtr.ReturnToPool()` per inspected particle, as required by AGX.
- v0 does not include any tool/action impulse, action vector, or task-state
  metrics. Those are still served via `ActObservationCollector` and the
  binary step-ack `env_state`.

## 10. Python side

Existing offline tools continue to read the legacy projections:

```bash
python3 Tools/TerrainGraph/visualize_terrain_graph_snapshot.py \
    TerrainGraphSnapshots/<file>.json --out /tmp/terrain_graph.png

python3 Tools/TerrainGraph/terrain_graph_snapshot_to_svg.py \
    TerrainGraphSnapshots/<file>.json --out /tmp/terrain_graph.svg
```

New consumers should read `nodes` and `edges`, apply
`world_from_graph_origin_*` to recover world coordinates, and ignore the
legacy `surface_nodes` / `particles` arrays.

## 11. Future protocol integration

This schema is intentionally Unity-only for v0. The following sequencing is
recommended before it touches the binary step-ack protocol:

1. **Sidecar JSONL.** Add a scripted-rollout sidecar that calls
   `TerrainGraphObservationProvider.Collect()` once per step and writes the
   resulting `TerrainGraphObservation` to a per-step JSONL stream tagged with
   the binary `step_id`. No protocol change needed.
2. **`GET_INFO` capability flags.** Once the schema is stable, advertise the
   following in `AgxSimResponsePayload`:
   - `supports_terrain_graph: bool`
   - `terrain_graph_schema_version: string`
   - `terrain_graph_node_fields: string[]`
   - `terrain_graph_edge_fields: string[]`
3. **Online graph channel.** Either upgrade the step-ack protocol version and
   append graph bytes to `STEP_RESP`, or expose a dedicated `GET_GRAPH`
   message, or open a parallel TCP stream aligned by `step_id`. The lowest-
   risk option is the parallel stream because it leaves the image/action
   client untouched.

Until step 3 is agreed with Repo A / Repo C, **do not** insert graph payloads
into the existing `STEP_RESP` or change `env_state` ordering. Doing so
would silently break clients that hard-code the V0 layout.
