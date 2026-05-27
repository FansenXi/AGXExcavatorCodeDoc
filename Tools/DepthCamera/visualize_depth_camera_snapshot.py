#!/usr/bin/env python3
"""Visualize a depth_camera_snapshot_v0 JSON export."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

try:
    import matplotlib.pyplot as plt
    import numpy as np
except ModuleNotFoundError as exc:
    raise SystemExit(
        f"Missing Python dependency: {exc.name}. Install numpy/matplotlib first."
    ) from exc


def _load(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def visualize(snapshot_path: Path, output_path: Path) -> None:
    snapshot = _load(snapshot_path)
    metadata = snapshot.get("metadata", {})
    width = int(metadata.get("width", 0))
    height = int(metadata.get("height", 0))
    if width <= 0 or height <= 0:
        raise SystemExit(f"{snapshot_path} has invalid width/height metadata")

    depth = np.asarray(snapshot.get("depth_m", []), dtype=np.float32)
    mask = np.asarray(snapshot.get("hit_mask", []), dtype=np.int8)
    points = np.asarray(snapshot.get("world_xyz", []), dtype=np.float32)

    expected = width * height
    if depth.size != expected or mask.size != expected:
        raise SystemExit(
            f"{snapshot_path} has {depth.size} depth values and {mask.size} mask values, "
            f"expected {expected}"
        )

    depth_img = depth.reshape((height, width))
    mask_img = mask.reshape((height, width))
    hit_depth = np.ma.masked_where(mask_img == 0, depth_img)

    fig, axes = plt.subplots(2, 2, figsize=(13, 10), constrained_layout=True)
    fig.suptitle(
        f"Depth Camera Snapshot — {snapshot_path.name} "
        f"(frame={snapshot.get('frame', '?')}, t={snapshot.get('sim_time_sec', 0):.3f}s)",
        fontsize=12,
    )

    ax = axes[0, 0]
    im = ax.imshow(hit_depth, origin="lower", cmap="viridis")
    ax.set_title("Depth [m], camera pixels")
    ax.set_xlabel("u")
    ax.set_ylabel("v")
    fig.colorbar(im, ax=ax, fraction=0.046, pad=0.04)

    ax = axes[0, 1]
    ax.imshow(mask_img, origin="lower", cmap="gray", vmin=0, vmax=1)
    ax.set_title("Hit mask")
    ax.set_xlabel("u")
    ax.set_ylabel("v")

    ax = axes[1, 0]
    if points.size == expected * 3:
        pts = points.reshape((expected, 3))
        valid = (mask.reshape(expected) == 1) & np.isfinite(pts).all(axis=1)
        if valid.any():
            scat = ax.scatter(
                pts[valid, 0],
                pts[valid, 2],
                c=pts[valid, 1],
                s=5,
                cmap="terrain",
                edgecolors="none",
            )
            fig.colorbar(scat, ax=ax, fraction=0.046, pad=0.04, label="world y [m]")
        ax.set_aspect("equal", adjustable="box")
    ax.set_title("World hit points, top view (XZ)")
    ax.set_xlabel("world x [m]")
    ax.set_ylabel("world z [m]")
    ax.grid(True, linestyle=":", linewidth=0.4, alpha=0.4)

    ax = axes[1, 1]
    ax.axis("off")
    hit_count = int(metadata.get("hit_count", int(mask.sum())))
    summary = [
        f"schema={snapshot.get('schema_version', '?')}",
        f"camera={metadata.get('camera_name', '?')}",
        f"resolution={width}x{height}",
        f"hits={hit_count}/{expected}",
        f"near={metadata.get('near_m', 0):.3f} m  far={metadata.get('far_m', 0):.3f} m",
        f"orthographic={metadata.get('orthographic', 0)}",
        f"ortho_size={metadata.get('orthographic_size', 0):.3f}",
        f"fov={metadata.get('field_of_view', 0):.3f}",
        f"camera_pos=({metadata.get('camera_world_x', 0):.3f}, "
        f"{metadata.get('camera_world_y', 0):.3f}, "
        f"{metadata.get('camera_world_z', 0):.3f})",
        f"depth_semantics={metadata.get('depth_semantics', '?')}",
    ]
    ax.set_title("Metadata summary")
    ax.text(
        0.02,
        0.98,
        "\n".join(summary),
        family="monospace",
        fontsize=9,
        ha="left",
        va="top",
        transform=ax.transAxes,
    )

    output_path.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output_path, dpi=160)
    plt.close(fig)


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Visualize a depth_camera_snapshot_v0 JSON export."
    )
    parser.add_argument("snapshot", type=Path)
    parser.add_argument("--out", type=Path, default=None)
    args = parser.parse_args()

    out = args.out or args.snapshot.with_suffix(".depth.png")
    visualize(args.snapshot, out)
    print(f"saved {out}")


if __name__ == "__main__":
    main()
