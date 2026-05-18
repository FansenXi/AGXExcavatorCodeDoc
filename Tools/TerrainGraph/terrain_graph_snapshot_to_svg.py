#!/usr/bin/env python3
"""Render a Unity AGX terrain graph snapshot to a dependency-free SVG."""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path


def _points(items: list[dict]) -> list[tuple[float, float, float]]:
    return [(float(p["x"]), float(p["y"]), float(p["z"])) for p in items]


def _extent(point_sets: list[list[tuple[float, float, float]]]) -> tuple[float, float, float, float, float, float]:
    pts = [p for points in point_sets for p in points]
    if not pts:
        return -1.0, 1.0, -1.0, 1.0, 0.0, 1.0
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    zs = [p[2] for p in pts]
    return min(xs), max(xs), min(zs), max(zs), min(ys), max(ys)


def _color(value: float, vmin: float, vmax: float, *, red: bool = False) -> str:
    t = 0.5 if vmax <= vmin else max(0.0, min(1.0, (value - vmin) / (vmax - vmin)))
    if red:
        r = int(120 + 120 * t)
        g = int(50 + 90 * (1.0 - t))
        b = int(70 + 60 * (1.0 - t))
    else:
        r = int(40 + 150 * t)
        g = int(95 + 110 * t)
        b = int(150 - 90 * t)
    return f"#{r:02x}{g:02x}{b:02x}"


class Panel:
    def __init__(
        self,
        x: float,
        y: float,
        w: float,
        h: float,
        xmin: float,
        xmax: float,
        zmin: float,
        zmax: float,
    ) -> None:
        self.x = x
        self.y = y
        self.w = w
        self.h = h
        pad_x = max(1e-6, (xmax - xmin) * 0.05)
        pad_z = max(1e-6, (zmax - zmin) * 0.05)
        self.xmin = xmin - pad_x
        self.xmax = xmax + pad_x
        self.zmin = zmin - pad_z
        self.zmax = zmax + pad_z

    def map(self, p: tuple[float, float, float]) -> tuple[float, float]:
        sx = self.x + (p[0] - self.xmin) / (self.xmax - self.xmin) * self.w
        sy = self.y + self.h - (p[2] - self.zmin) / (self.zmax - self.zmin) * self.h
        return sx, sy


def _sample(points: list[tuple[float, float, float]], limit: int) -> list[tuple[float, float, float]]:
    if len(points) <= limit:
        return points
    step = len(points) / float(limit)
    return [points[int(i * step)] for i in range(limit)]


def _radius_edges(points: list[tuple[float, float, float]], radius: float, max_edges: int) -> list[tuple[int, int]]:
    edges: list[tuple[int, int]] = []
    r2 = radius * radius
    for i, a in enumerate(points):
        for j in range(i + 1, len(points)):
            b = points[j]
            dx = a[0] - b[0]
            dz = a[2] - b[2]
            if dx * dx + dz * dz <= r2:
                edges.append((i, j))
                if len(edges) >= max_edges:
                    return edges
    return edges


def _panel_header(svg: list[str], panel: Panel, title: str) -> None:
    svg.append(f'<rect x="{panel.x}" y="{panel.y}" width="{panel.w}" height="{panel.h}" fill="#ffffff" stroke="#1f2937" stroke-width="1"/>')
    svg.append(f'<text x="{panel.x + 10}" y="{panel.y + 22}" font-size="16" font-family="sans-serif" fill="#111827">{title}</text>')


def _draw_points(
    svg: list[str],
    panel: Panel,
    points: list[tuple[float, float, float]],
    ymin: float,
    ymax: float,
    *,
    radius: float,
    opacity: float,
    red: bool = False,
) -> None:
    for p in points:
        x, y = panel.map(p)
        svg.append(
            f'<circle cx="{x:.2f}" cy="{y:.2f}" r="{radius}" '
            f'fill="{_color(p[1], ymin, ymax, red=red)}" fill-opacity="{opacity}"/>'
        )


def render(snapshot_path: Path, output_path: Path, *, max_graph_nodes: int, graph_radius: float, max_edges: int) -> None:
    with snapshot_path.open("r", encoding="utf-8") as f:
        snapshot = json.load(f)

    surface = _points(snapshot.get("surface_nodes", []))
    particles = _points(snapshot.get("particles", []))
    hybrid = surface + particles
    xmin, xmax, zmin, zmax, ymin, ymax = _extent([surface, particles])

    width, height = 1400, 950
    margin, gap = 45, 30
    panel_w = (width - 2 * margin - gap) / 2
    panel_h = (height - 2 * margin - gap) / 2
    panels = [
        Panel(margin, margin, panel_w, panel_h, xmin, xmax, zmin, zmax),
        Panel(margin + panel_w + gap, margin, panel_w, panel_h, xmin, xmax, zmin, zmax),
        Panel(margin, margin + panel_h + gap, panel_w, panel_h, xmin, xmax, zmin, zmax),
        Panel(margin + panel_w + gap, margin + panel_h + gap, panel_w, panel_h, xmin, xmax, zmin, zmax),
    ]

    svg = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">',
        '<rect width="100%" height="100%" fill="#f8fafc"/>',
    ]

    _panel_header(svg, panels[0], f"Static surface heightmap nodes: {len(surface)}")
    _draw_points(svg, panels[0], surface, ymin, ymax, radius=2.2, opacity=0.82)

    _panel_header(svg, panels[1], f"Dynamic AGX soil particles: {len(particles)}")
    _draw_points(svg, panels[1], particles, ymin, ymax, radius=4.0, opacity=0.85, red=True)

    _panel_header(svg, panels[2], f"Hybrid nodes: {len(hybrid)}")
    _draw_points(svg, panels[2], surface, ymin, ymax, radius=1.7, opacity=0.25)
    _draw_points(svg, panels[2], particles, ymin, ymax, radius=4.0, opacity=0.9, red=True)

    graph_points = _sample(hybrid, max_graph_nodes)
    edges = _radius_edges(graph_points, graph_radius, max_edges)
    _panel_header(svg, panels[3], f"Radius graph: nodes={len(graph_points)} edges={len(edges)} r={graph_radius:g}m")
    for i, j in edges:
        x1, y1 = panels[3].map(graph_points[i])
        x2, y2 = panels[3].map(graph_points[j])
        svg.append(f'<line x1="{x1:.2f}" y1="{y1:.2f}" x2="{x2:.2f}" y2="{y2:.2f}" stroke="#334155" stroke-opacity="0.35" stroke-width="0.5"/>')
    _draw_points(svg, panels[3], graph_points, ymin, ymax, radius=2.4, opacity=0.9)

    svg.append("</svg>")
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text("\n".join(svg), encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description="Render a terrain graph snapshot JSON to SVG without external dependencies.")
    parser.add_argument("snapshot", type=Path)
    parser.add_argument("--out", type=Path, default=None)
    parser.add_argument("--max-graph-nodes", type=int, default=900)
    parser.add_argument("--graph-radius", type=float, default=0.45)
    parser.add_argument("--max-edges", type=int, default=5000)
    args = parser.parse_args()

    out = args.out or args.snapshot.with_suffix(".svg")
    render(args.snapshot, out, max_graph_nodes=args.max_graph_nodes, graph_radius=args.graph_radius, max_edges=args.max_edges)
    print(f"saved {out}")


if __name__ == "__main__":
    main()
