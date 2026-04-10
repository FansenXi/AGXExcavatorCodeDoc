from __future__ import annotations

import argparse
import json
import time
from pathlib import Path

from roi_pipeline.core import (
    compute_hdf5_dataset_stats,
    discover_raw_samples,
    ensure_dataset_yaml,
    export_hdf5_dataset,
    group_samples_by_episode,
    ingest_raw_dataset,
    load_pipeline_config,
    resolve_repo_path,
    write_json_report,
)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="roi-dataset-tool",
        description="ROI dataset ingest/export/stats/watch utilities.",
    )
    parser.add_argument(
        "--config",
        type=Path,
        default=Path("tools/roi_pipeline_config.yaml"),
        help="Pipeline YAML config.",
    )

    subparsers = parser.add_subparsers(dest="command", required=True)

    ingest = subparsers.add_parser("ingest", help="Scan raw YOLO files and pack them into per-episode HDF5.")
    ingest.add_argument("--raw-dir", type=Path, default=None, help="Override dataset.raw_dir.")
    ingest.add_argument("--hdf5-dir", type=Path, default=None, help="Override dataset.hdf5_dir.")
    ingest.add_argument("--dedup-threshold", type=int, default=None, help="Override dedup.threshold.")
    ingest.add_argument("--lookback-frames", type=int, default=None, help="Override dedup.lookback_frames.")
    ingest.add_argument("--hash-size", type=int, default=None, help="Override dedup.hash_size.")
    ingest.add_argument("--overwrite", action="store_true", help="Overwrite existing HDF5 episodes.")
    ingest.add_argument(
        "--settle-seconds",
        type=float,
        default=0.0,
        help="Skip episodes whose latest raw file was modified more recently than this.",
    )

    export = subparsers.add_parser("export", help="Export HDF5 episodes back to a YOLO directory.")
    export.add_argument("--hdf5-dir", type=Path, default=None, help="Override dataset.hdf5_dir.")
    export.add_argument("--yolo-dir", type=Path, default=None, help="Override dataset.export_dir.")
    export.add_argument("--include-duplicates", action="store_true", help="Export all frames, not only kept frames.")
    export.add_argument("--no-overwrite", action="store_true", help="Do not clear the export directory first.")

    stats = subparsers.add_parser("stats", help="Print dataset statistics from HDF5 episodes.")
    stats.add_argument("--hdf5-dir", type=Path, default=None, help="Override dataset.hdf5_dir.")
    stats.add_argument("--output-json", type=Path, default=None, help="Optional JSON output path.")

    watch = subparsers.add_parser("watch", help="Poll raw dataset and ingest completed episodes.")
    watch.add_argument("--raw-dir", type=Path, default=None, help="Override dataset.raw_dir.")
    watch.add_argument("--hdf5-dir", type=Path, default=None, help="Override dataset.hdf5_dir.")
    watch.add_argument("--poll-seconds", type=float, default=5.0, help="Polling interval.")
    watch.add_argument("--settle-seconds", type=float, default=5.0, help="Episode quiet period before ingest.")
    watch.add_argument("--dedup-threshold", type=int, default=None, help="Override dedup.threshold.")
    watch.add_argument("--lookback-frames", type=int, default=None, help="Override dedup.lookback_frames.")
    watch.add_argument("--hash-size", type=int, default=None, help="Override dedup.hash_size.")

    return parser


def resolve_settings(args: argparse.Namespace) -> dict:
    config = load_pipeline_config(args.config)
    dataset_cfg = config["dataset"]
    dedup_cfg = config["dedup"]
    settings = {
        "raw_dir": resolve_repo_path(getattr(args, "raw_dir", None) or dataset_cfg["raw_dir"]),
        "hdf5_dir": resolve_repo_path(getattr(args, "hdf5_dir", None) or dataset_cfg["hdf5_dir"]),
        "export_dir": resolve_repo_path(getattr(args, "yolo_dir", None) or dataset_cfg["export_dir"]),
        "class_labels": list(dataset_cfg["class_labels"]),
        "dedup_threshold": int(getattr(args, "dedup_threshold", None) or dedup_cfg["threshold"]),
        "lookback_frames": int(getattr(args, "lookback_frames", None) or dedup_cfg["lookback_frames"]),
        "hash_size": int(getattr(args, "hash_size", None) or dedup_cfg["hash_size"]),
    }
    return settings


def cmd_ingest(args: argparse.Namespace) -> int:
    settings = resolve_settings(args)
    ingested = ingest_raw_dataset(
        raw_dir=settings["raw_dir"],
        hdf5_dir=settings["hdf5_dir"],
        class_labels=settings["class_labels"],
        dedup_threshold=settings["dedup_threshold"],
        lookback_frames=settings["lookback_frames"],
        hash_size=settings["hash_size"],
        overwrite=args.overwrite,
        settle_seconds=args.settle_seconds,
    )
    print(f"ingested {len(ingested)} episode(s) into {settings['hdf5_dir']}")
    for path in ingested:
        print(path)
    return 0


def cmd_export(args: argparse.Namespace) -> int:
    settings = resolve_settings(args)
    dataset_yaml = export_hdf5_dataset(
        hdf5_dir=settings["hdf5_dir"],
        export_dir=settings["export_dir"],
        class_labels=settings["class_labels"],
        skip_duplicates=not args.include_duplicates,
        overwrite=not args.no_overwrite,
    )
    print(f"exported YOLO dataset to {settings['export_dir']}")
    print(f"dataset yaml: {dataset_yaml}")
    return 0


def cmd_stats(args: argparse.Namespace) -> int:
    settings = resolve_settings(args)
    stats = compute_hdf5_dataset_stats(settings["hdf5_dir"])
    print(json.dumps(stats, indent=2, ensure_ascii=False))
    if args.output_json is not None:
        report_path = write_json_report(stats, resolve_repo_path(args.output_json))
        print(f"wrote stats json to {report_path}")
    return 0


def cmd_watch(args: argparse.Namespace) -> int:
    settings = resolve_settings(args)
    raw_dir = settings["raw_dir"]
    seen_episode_count = 0
    print(f"watching {raw_dir} -> {settings['hdf5_dir']}")
    while True:
        try:
            discovered = group_samples_by_episode(discover_raw_samples(raw_dir))
            if len(discovered) != seen_episode_count:
                print(f"detected {len(discovered)} episode group(s) in raw dataset")
                seen_episode_count = len(discovered)
            ingested = ingest_raw_dataset(
                raw_dir=settings["raw_dir"],
                hdf5_dir=settings["hdf5_dir"],
                class_labels=settings["class_labels"],
                dedup_threshold=settings["dedup_threshold"],
                lookback_frames=settings["lookback_frames"],
                hash_size=settings["hash_size"],
                overwrite=False,
                settle_seconds=args.settle_seconds,
            )
            for path in ingested:
                print(f"ingested {path}")
            time.sleep(args.poll_seconds)
        except KeyboardInterrupt:
            print("watch stopped")
            return 0


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    if args.command == "ingest":
        return cmd_ingest(args)
    if args.command == "export":
        return cmd_export(args)
    if args.command == "stats":
        return cmd_stats(args)
    if args.command == "watch":
        return cmd_watch(args)
    parser.error(f"Unknown command: {args.command}")
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
