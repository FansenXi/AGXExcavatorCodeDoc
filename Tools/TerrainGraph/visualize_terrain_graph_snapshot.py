#!/usr/bin/env python3
"""Visualize Unity AGX terrain graph snapshots.

The Unity side exports a JSON snapshot with:
  - surface_nodes: sampled deformable-terrain heightfield cells
  - particles: dynamic soil particles currently exposed by AGX

This script draws four views so the representation tradeoffs are visible:
  1. surface height samples
  2. dynamic particles
  3. hybrid nodes
  4. a sparse radius graph over the hybrid nodes
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
from typing import Iterable

try:
    import matplotlib.pyplot as plt
    import numpy as np
except ModuleNotFoundError as exc:
    missing = exc.name
    raise SystemExit(
        f"Missing Python dependency: {missing}. "
        "Run this from an environment with numpy/matplotlib, or install them first."
    ) from exc


def _as_points(items: list[dict], *, height_key: str = "y") -> np.ndarray:
    if not items:
        return np.zeros((0, 3), dtype=np.float32)
    return np.array(
        [[float(item["x"]), float(item[height_key]), float(item["z"])] for item in items],
        dtype=np.float32,
    )


def _sample_indices(n: int, max_nodes: int) -> np.ndarray:
    if n <= max_nodes:
        return np.arange(n)
    return np.linspace(0, n - 1, max_nodes).astype(np.int64)


def _radius_edges(points_xz: np.ndarray, radius: float, max_edges: int) -> list[tuple[int, int]]:
    edges: list[tuple[int, int]] = []
    if len(points_xz) < 2:
        return edges

    radius_sq = radius * radius
    for i in range(len(points_xz)):
        delta = points_xz[i + 1 :] - points_xz[i]
        dist_sq = np.einsum("ij,ij->i", delta, delta)
        neighbors = np.flatnonzero(dist_sq <= radius_sq)
        for neighbor in neighbors:
            edges.append((i, i + 1 + int(neighbor)))
            if len(edges) >= max_edges:
                return edges
    return edges


def _scatter_xz(ax, points: np.ndarray, title: str, *, size: float, alpha: float, cmap: str):
    ax.set_title(title)
    if len(points) == 0:
        ax.text(0.5, 0.5, "no nodes", ha="center", va="center", transform=ax.transAxes)
        ax.set_aspect("equal", adjustable="box")
        return None
    scatter = ax.scatter(points[:, 0], points[:, 2], c=points[:, 1], s=size, alpha=alpha, cmap=cmap)
    ax.set_xlabel("world x")
    ax.set_ylabel("world z")
    ax.set_aspect("equal", adjustable="box")
    return scatter


def visualize(
    snapshot_path: Path,
    output_path: Path,
    *,
    max_graph_nodes: int,
    graph_radius: float,
    max_edges: int,
) -> None:
    with snapshot_path.open("r", encoding="utf-8") as f:
        snapshot = json.load(f)

    surface = _as_points(snapshot.get("surface_nodes", []), height_key="y")
    particles = _as_points(snapshot.get("particles", []), height_key="y")
    hybrid = np.concatenate([surface, particles], axis=0)

    fig, axes = plt.subplots(2, 2, figsize=(13, 10), constrained_layout=True)
    fig.suptitle(f"Terrain Graph Snapshot: {snapshot_path.name}")

    surface_scatter = _scatter_xz(
        axes[0, 0],
        surface,
        f"Static surface heightmap samples ({len(surface)})",
        size=8,
        alpha=0.85,
        cmap="terrain",
    )
    if surface_scatter is not None:
        fig.colorbar(surface_scatter, ax=axes[0, 0], label="height y")

    particle_scatter = _scatter_xz(
        axes[0, 1],
        particles,
        f"Dynamic AGX soil particles ({len(particles)})",
        size=18,
        alpha=0.8,
        cmap="viridis",
    )
    if particle_scatter is not None:
        fig.colorbar(particle_scatter, ax=axes[0, 1], label="height y")

    axes[1, 0].set_title(f"Hybrid nodes: surface + particles ({len(hybrid)})")
    if len(surface):
        axes[1, 0].scatter(surface[:, 0], surface[:, 2], c="#9aa4b2", s=5, alpha=0.35, label="surface")
    if len(particles):
        axes[1, 0].scatter(particles[:, 0], particles[:, 2], c="#d1495b", s=16, alpha=0.85, label="particles")
    axes[1, 0].set_xlabel("world x")
    axes[1, 0].set_ylabel("world z")
    axes[1, 0].set_aspect("equal", adjustable="box")
    axes[1, 0].legend(loc="best")

    axes[1, 1].set_title(f"Sparse radius graph, r={graph_radius:g} m")
    if len(hybrid):
        idx = _sample_indices(len(hybrid), max_graph_nodes)
        graph_points = hybrid[idx]
        graph_xz = graph_points[:, [0, 2]]
        edges = _radius_edges(graph_xz, graph_radius, max_edges)
        for i, j in edges:
            axes[1, 1].plot(
                [graph_points[i, 0], graph_points[j, 0]],
                [graph_points[i, 2], graph_points[j, 2]],
                color="#334155",
                linewidth=0.35,
                alpha=0.45,
            )
        axes[1, 1].scatter(graph_points[:, 0], graph_points[:, 2], c=graph_points[:, 1], s=8, cmap="plasma")
        axes[1, 1].text(
            0.02,
            0.98,
            f"nodes={len(graph_points)} edges={len(edges)}",
            ha="left",
            va="top",
            transform=axes[1, 1].transAxes,
        )
    axes[1, 1].set_xlabel("world x")
    axes[1, 1].set_ylabel("world z")
    axes[1, 1].set_aspect("equal", adjustable="box")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output_path, dpi=180)
    plt.close(fig)


def main() -> None:
    parser = argparse.ArgumentParser(description="Visualize a Unity AGX terrain graph snapshot JSON.")
    parser.add_argument("snapshot", type=Path, help="Path to terrain_graph_*.json exported by Unity.")
    parser.add_argument("--out", type=Path, default=None, help="Output PNG path.")
    parser.add_argument("--max-graph-nodes", type=int, default=900)
    parser.add_argument("--graph-radius", type=float, default=0.45)
    parser.add_argument("--max-edges", type=int, default=5000)
    args = parser.parse_args()

    out = args.out or args.snapshot.with_suffix(".png")
    visualize(
        args.snapshot,
        out,
        max_graph_nodes=args.max_graph_nodes,
        graph_radius=args.graph_radius,
        max_edges=args.max_edges,
    )
    print(f"saved {out}")


if __name__ == "__main__":
    main()
