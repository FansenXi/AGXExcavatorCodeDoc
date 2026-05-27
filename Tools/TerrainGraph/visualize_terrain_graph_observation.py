#!/usr/bin/env python3
"""Visualize a terrain_graph_observation_v0 JSON export.

Unlike ``visualize_terrain_graph_snapshot.py``, which only reads the legacy
``surface_nodes[]`` / ``particles[]`` arrays, this viewer consumes the
v0 ``nodes[]`` + ``edges[]`` schema documented in
``Assets/AGXUnity_Excavator/Docs/terrain_graph_observation.md``.

It surfaces the visual cues the Liu et al. 2026 L-GBND figure relies on:

* node kinds ``surface_soil`` / ``subsurface_soil`` / ``dynamic_soil`` /
  ``tool`` are drawn with distinct colors and sizes;
* ``is_in_roi == 0`` context background is drawn translucent;
* ``edges[].kind_pair`` starting with ``tool|`` is drawn over radius
  soil-soil edges in a hotter color.

If the input file predates the v0 schema (no ``nodes`` array), the viewer
falls back to the legacy arrays so the script remains useful on old
snapshots.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

try:
    import matplotlib.pyplot as plt
    import numpy as np
    from matplotlib.patches import Circle
except ModuleNotFoundError as exc:
    missing = exc.name
    raise SystemExit(
        f"Missing Python dependency: {missing}. "
        "Run this from an environment with numpy/matplotlib, or install them first."
    ) from exc


# ---------------------------------------------------------------------------
# Style constants
# ---------------------------------------------------------------------------
KIND_STYLE: dict[str, dict] = {
    "surface_soil":      {"color": "#3b82f6", "size": 14, "marker": "o", "zorder": 3},
    "subsurface_soil":   {"color": "#a16207", "size": 6,  "marker": "s", "zorder": 2},
    "dynamic_soil":      {"color": "#dc2626", "size": 22, "marker": "o", "zorder": 4},
    "tool":              {"color": "#16a34a", "size": 70, "marker": "^", "zorder": 6},
    "dynamic_rigidbody": {"color": "#9333ea", "size": 50, "marker": "D", "zorder": 5},
}

EDGE_STYLE_RADIUS = {"color": "#475569", "linewidth": 0.35, "alpha": 0.45}
EDGE_STYLE_TOOL = {"color": "#f97316", "linewidth": 0.8, "alpha": 0.85}

ROI_CIRCLE_STYLE = {"edgecolor": "#0ea5e9", "facecolor": "none",
                    "linewidth": 1.2, "linestyle": "--", "alpha": 0.8}
CONTEXT_CIRCLE_STYLE = {"edgecolor": "#0ea5e9", "facecolor": "none",
                        "linewidth": 0.8, "linestyle": ":", "alpha": 0.5}


# ---------------------------------------------------------------------------
# Data loading
# ---------------------------------------------------------------------------
def _load(snapshot_path: Path) -> dict:
    with snapshot_path.open("r", encoding="utf-8") as f:
        return json.load(f)


def _nodes_to_array(nodes: list[dict]) -> dict[str, np.ndarray]:
    """Pack node dicts into parallel numpy arrays keyed by field name."""
    n = len(nodes)
    out = {
        "x": np.zeros(n, dtype=np.float32),
        "y": np.zeros(n, dtype=np.float32),
        "z": np.zeros(n, dtype=np.float32),
        "kind": np.array([node.get("kind", "surface_soil") for node in nodes]),
        "is_in_roi": np.zeros(n, dtype=np.int8),
        "is_tool": np.zeros(n, dtype=np.int8),
        "is_dynamic": np.zeros(n, dtype=np.int8),
        "depth": np.zeros(n, dtype=np.float32),
    }
    for i, node in enumerate(nodes):
        out["x"][i] = float(node.get("x", 0.0))
        out["y"][i] = float(node.get("y", 0.0))
        out["z"][i] = float(node.get("z", 0.0))
        out["is_in_roi"][i] = int(node.get("is_in_roi", 0))
        out["is_tool"][i] = int(node.get("is_tool", 0))
        out["is_dynamic"][i] = int(node.get("is_dynamic", 0))
        out["depth"][i] = float(node.get("depth_below_surface", 0.0))
    return out


def _legacy_to_v0(snapshot: dict) -> list[dict]:
    """Synthesize a minimal v0 ``nodes`` array from legacy fields.

    Used when a pre-v0 snapshot is loaded so the viewer still works.
    """
    nodes: list[dict] = []
    for s in snapshot.get("surface_nodes", []):
        nodes.append({
            "kind": "surface_soil",
            "x": s.get("x", 0.0), "y": s.get("y", 0.0), "z": s.get("z", 0.0),
            "is_in_roi": 1, "is_tool": 0, "is_dynamic": 0,
        })
    for p in snapshot.get("particles", []):
        nodes.append({
            "kind": "dynamic_soil",
            "x": p.get("x", 0.0), "y": p.get("y", 0.0), "z": p.get("z", 0.0),
            "is_in_roi": 1, "is_tool": 0, "is_dynamic": 1,
        })
    return nodes


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
def _select(packed: dict[str, np.ndarray], mask: np.ndarray) -> dict[str, np.ndarray]:
    return {k: v[mask] for k, v in packed.items()}


def _scatter_kind(ax, packed: dict[str, np.ndarray], kind: str, *,
                  xaxis: str = "x", yaxis: str = "z",
                  context_alpha: float = 0.18, active_alpha: float = 0.95) -> None:
    style = KIND_STYLE.get(kind)
    if style is None:
        return
    kind_mask = packed["kind"] == kind
    if not kind_mask.any():
        return
    for in_roi, alpha in ((1, active_alpha), (0, context_alpha)):
        sub_mask = kind_mask & (packed["is_in_roi"] == in_roi)
        if not sub_mask.any():
            continue
        sel = _select(packed, sub_mask)
        ax.scatter(
            sel[xaxis], sel[yaxis],
            c=style["color"],
            s=style["size"],
            marker=style["marker"],
            alpha=alpha,
            zorder=style["zorder"],
            edgecolors="none",
            label=f"{kind} ({'roi' if in_roi else 'ctx'})",
        )


def _draw_roi_circles(ax, metadata: dict) -> None:
    if int(metadata.get("roi_enabled", 0)) != 1:
        return
    # Coordinates are graph_local_translated => RoI center is (0, 0).
    cx, cz = 0.0, 0.0
    r_roi = float(metadata.get("roi_radius_m", 0.0))
    r_ctx = float(metadata.get("roi_context_radius_m", 0.0))
    if r_roi > 0:
        ax.add_patch(Circle((cx, cz), r_roi, **ROI_CIRCLE_STYLE))
    if r_ctx > r_roi > 0 and int(metadata.get("include_context_nodes", 0)) == 1:
        ax.add_patch(Circle((cx, cz), r_ctx, **CONTEXT_CIRCLE_STYLE))


def _edges_segments(edges: list[dict], packed: dict[str, np.ndarray],
                    *, xaxis: str, yaxis: str,
                    tool: bool) -> tuple[np.ndarray, np.ndarray]:
    """Return arrays of shape (N,) suitable for plt.plot with NaN segment breaks."""
    if not edges:
        return np.empty(0), np.empty(0)
    xs: list[float] = []
    ys: list[float] = []
    n = len(packed["x"])
    for edge in edges:
        kp = edge.get("kind_pair", "")
        is_tool = kp.startswith("tool|") or kp.endswith("|tool")
        if is_tool != tool:
            continue
        src = int(edge.get("src", -1))
        dst = int(edge.get("dst", -1))
        if not (0 <= src < n and 0 <= dst < n):
            continue
        xs.extend([float(packed[xaxis][src]), float(packed[xaxis][dst]), np.nan])
        ys.extend([float(packed[yaxis][src]), float(packed[yaxis][dst]), np.nan])
    return np.array(xs, dtype=np.float32), np.array(ys, dtype=np.float32)


def _format_meta_text(metadata: dict, n_nodes: int, n_edges: int) -> str:
    parts = [
        f"terrain={metadata.get('terrain_name', '?')} stride={metadata.get('surface_stride', '?')}",
        f"nodes={n_nodes}  edges={n_edges}",
        f"  surface={metadata.get('surface_node_count', 0)} (+{metadata.get('surface_context_node_count', 0)} ctx)",
        f"  subsurface={metadata.get('subsurface_node_count', 0)} (+{metadata.get('subsurface_context_node_count', 0)} ctx)",
        f"  dynamic={metadata.get('dynamic_particle_count', 0)} (+{metadata.get('dynamic_particle_context_count', 0)} ctx)",
        f"  tool={metadata.get('tool_node_count', 0)}",
        f"  radius_edges={metadata.get('radius_edge_count', 0)}",
        f"  tool_soil_edges={metadata.get('tool_soil_edge_count', 0)}",
        f"roi r={metadata.get('roi_radius_m', 0):.2f} m  "
        f"ctx r={metadata.get('roi_context_radius_m', 0):.2f} m  "
        f"mode={metadata.get('roi_center_mode', '?')}",
        f"coord_frame={metadata.get('coordinate_frame', '?')}",
    ]
    return "\n".join(parts)


# ---------------------------------------------------------------------------
# Plot
# ---------------------------------------------------------------------------
def visualize(snapshot_path: Path, output_path: Path) -> None:
    snapshot = _load(snapshot_path)
    metadata = snapshot.get("metadata", {})
    nodes_raw = snapshot.get("nodes") or _legacy_to_v0(snapshot)
    edges = snapshot.get("edges", [])

    if not nodes_raw:
        raise SystemExit(f"snapshot {snapshot_path} has no nodes to plot")

    packed = _nodes_to_array(nodes_raw)

    fig, axes = plt.subplots(2, 2, figsize=(14, 12), constrained_layout=True)
    fig.suptitle(
        f"Terrain Graph Observation v0 — {snapshot_path.name}  "
        f"(frame={snapshot.get('frame', '?')}, t={snapshot.get('sim_time_sec', 0):.3f}s)",
        fontsize=12,
    )

    # Draw subsurface first so surface dots sit on top.
    kinds_order = ["subsurface_soil", "surface_soil", "dynamic_soil", "tool"]

    # ---- Panel (0,0): top-down plan view of nodes ---------------------------
    ax = axes[0, 0]
    ax.set_title("Plan view (XZ) — nodes by kind, context = translucent")
    for kind in kinds_order:
        _scatter_kind(ax, packed, kind, xaxis="x", yaxis="z")
    _draw_roi_circles(ax, metadata)
    ax.set_xlabel("graph_local x [m]")
    ax.set_ylabel("graph_local z [m]")
    ax.set_aspect("equal", adjustable="box")
    ax.legend(loc="upper right", fontsize=7, framealpha=0.8)
    ax.grid(True, linestyle=":", linewidth=0.4, alpha=0.4)

    # ---- Panel (0,1): plan view with edges ---------------------------------
    ax = axes[0, 1]
    ax.set_title("Plan view (XZ) — edges: grey=radius, orange=tool|soil")
    rx, rz = _edges_segments(edges, packed, xaxis="x", yaxis="z", tool=False)
    if rx.size:
        ax.plot(rx, rz, **EDGE_STYLE_RADIUS, zorder=1)
    tx, tz = _edges_segments(edges, packed, xaxis="x", yaxis="z", tool=True)
    if tx.size:
        ax.plot(tx, tz, **EDGE_STYLE_TOOL, zorder=5)
    for kind in kinds_order:
        _scatter_kind(ax, packed, kind, xaxis="x", yaxis="z",
                      context_alpha=0.10, active_alpha=0.9)
    _draw_roi_circles(ax, metadata)
    ax.set_xlabel("graph_local x [m]")
    ax.set_ylabel("graph_local z [m]")
    ax.set_aspect("equal", adjustable="box")
    ax.grid(True, linestyle=":", linewidth=0.4, alpha=0.4)

    # ---- Panel (1,0): side view (XY) showing depth columns ------------------
    ax = axes[1, 0]
    ax.set_title("Side view (XY) — surface stack + tool + dynamic particles")
    for kind in kinds_order:
        _scatter_kind(ax, packed, kind, xaxis="x", yaxis="y",
                      context_alpha=0.18, active_alpha=0.95)
    tx, ty = _edges_segments(edges, packed, xaxis="x", yaxis="y", tool=True)
    if tx.size:
        ax.plot(tx, ty, **EDGE_STYLE_TOOL, zorder=5)
    ax.set_xlabel("graph_local x [m]")
    ax.set_ylabel("graph_local y [m] (height)")
    ax.grid(True, linestyle=":", linewidth=0.4, alpha=0.4)

    # ---- Panel (1,1): textual summary of metadata ---------------------------
    ax = axes[1, 1]
    ax.axis("off")
    ax.set_title("Metadata summary")
    ax.text(
        0.02, 0.98,
        _format_meta_text(metadata, len(nodes_raw), len(edges)),
        family="monospace",
        fontsize=9,
        ha="left",
        va="top",
        transform=ax.transAxes,
    )

    output_path.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output_path, dpi=160)
    plt.close(fig)


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------
def main() -> None:
    parser = argparse.ArgumentParser(
        description="Visualize a terrain_graph_observation_v0 JSON export."
    )
    parser.add_argument("snapshot", type=Path,
                        help="Path to terrain_graph_*.json exported by Unity (v0 schema).")
    parser.add_argument("--out", type=Path, default=None,
                        help="Output PNG path (default: <snapshot>.obs.png).")
    args = parser.parse_args()

    out = args.out or args.snapshot.with_suffix(".obs.png")
    visualize(args.snapshot, out)
    print(f"saved {out}")


if __name__ == "__main__":
    main()
