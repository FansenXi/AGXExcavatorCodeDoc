#!/usr/bin/env python3
"""Visualize a lidar_snapshot_v0 JSON export."""

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
    rings = int(metadata.get("rings", 0))
    azimuth_steps = int(metadata.get("azimuth_steps", 0))
    if rings <= 0 or azimuth_steps <= 0:
        raise SystemExit(f"{snapshot_path} has invalid rings/azimuth_steps metadata")

    expected = rings * azimuth_steps
    ranges = np.asarray(snapshot.get("ranges_m", []), dtype=np.float32)
    mask = np.asarray(snapshot.get("hit_mask", []), dtype=np.int8)
    points = np.asarray(snapshot.get("world_xyz", []), dtype=np.float32)
    if ranges.size != expected or mask.size != expected:
        raise SystemExit(
            f"{snapshot_path} has {ranges.size} ranges and {mask.size} mask values, "
            f"expected {expected}"
        )

    range_img = ranges.reshape((rings, azimuth_steps))
    mask_img = mask.reshape((rings, azimuth_steps))
    hit_ranges = np.ma.masked_where(mask_img == 0, range_img)

    fig, axes = plt.subplots(2, 2, figsize=(14, 10), constrained_layout=True)
    fig.suptitle(
        f"LiDAR Snapshot — {snapshot_path.name} "
        f"(frame={snapshot.get('frame', '?')}, t={snapshot.get('sim_time_sec', 0):.3f}s)",
        fontsize=12,
    )

    ax = axes[0, 0]
    im = ax.imshow(hit_ranges, origin="lower", aspect="auto", cmap="viridis")
    ax.set_title("Range image [m]")
    ax.set_xlabel("azimuth step")
    ax.set_ylabel("ring")
    fig.colorbar(im, ax=ax, fraction=0.046, pad=0.04)

    ax = axes[0, 1]
    ax.imshow(mask_img, origin="lower", aspect="auto", cmap="gray", vmin=0, vmax=1)
    ax.set_title("Hit mask")
    ax.set_xlabel("azimuth step")
    ax.set_ylabel("ring")

    valid_points = None
    valid_mask = mask.reshape(expected) == 1
    if points.size == expected * 3:
        pts = points.reshape((expected, 3))
        valid = valid_mask & np.isfinite(pts).all(axis=1)
        valid_points = pts[valid]

    ax = axes[1, 0]
    if valid_points is not None and valid_points.size:
        scat = ax.scatter(
            valid_points[:, 0],
            valid_points[:, 2],
            c=valid_points[:, 1],
            s=3,
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
    if valid_points is not None and valid_points.size:
        ax.scatter(
            valid_points[:, 0],
            valid_points[:, 1],
            c=valid_points[:, 2],
            s=3,
            cmap="coolwarm",
            edgecolors="none",
        )
    ax.set_title("World hit points, side view (XY)")
    ax.set_xlabel("world x [m]")
    ax.set_ylabel("world y [m]")
    ax.grid(True, linestyle=":", linewidth=0.4, alpha=0.4)

    summary = (
        f"hits={int(metadata.get('hit_count', int(mask.sum())))}/{expected}  "
        f"rings={rings} azimuth={azimuth_steps}  "
        f"v=[{metadata.get('vertical_min_deg', 0):.1f}, {metadata.get('vertical_max_deg', 0):.1f}] deg  "
        f"max={metadata.get('max_distance_m', 0):.1f} m"
    )
    fig.text(0.5, 0.01, summary, ha="center", va="bottom", family="monospace", fontsize=9)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output_path, dpi=160)
    plt.close(fig)


def main() -> None:
    parser = argparse.ArgumentParser(description="Visualize a lidar_snapshot_v0 JSON export.")
    parser.add_argument("snapshot", type=Path)
    parser.add_argument("--out", type=Path, default=None)
    args = parser.parse_args()

    out = args.out or args.snapshot.with_suffix(".lidar.png")
    visualize(args.snapshot, out)
    print(f"saved {out}")


if __name__ == "__main__":
    main()
