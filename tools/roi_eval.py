from __future__ import annotations

import argparse
import json
from pathlib import Path

from roi_pipeline.core import (
    append_eval_history,
    compute_hdf5_dataset_stats,
    dataset_quality_report,
    export_hdf5_dataset,
    iso_now,
    load_pipeline_config,
    resolve_repo_path,
    run_ultralytics_val,
    write_json_report,
)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="roi-eval",
        description="ROI dataset and model evaluation utilities.",
    )
    parser.add_argument(
        "--config",
        type=Path,
        default=Path("tools/roi_pipeline_config.yaml"),
        help="Pipeline YAML config.",
    )

    subparsers = parser.add_subparsers(dest="command", required=True)

    dataset_quality = subparsers.add_parser("dataset-quality", help="Evaluate HDF5 dataset quality.")
    dataset_quality.add_argument("--hdf5-dir", type=Path, default=None, help="Override dataset.hdf5_dir.")
    dataset_quality.add_argument("--report", type=Path, default=None, help="Optional report output path.")

    model_eval = subparsers.add_parser("model-eval", help="Run Ultralytics validation against the exported val split.")
    model_eval.add_argument("--model", type=Path, required=True, help="Model path (.pt or .onnx).")
    model_eval.add_argument("--hdf5-dir", type=Path, default=None, help="Override dataset.hdf5_dir.")
    model_eval.add_argument("--export-dir", type=Path, default=None, help="Override dataset.export_dir.")
    model_eval.add_argument("--report", type=Path, default=None, help="Optional report output path.")
    model_eval.add_argument("--split", type=str, default="val", choices=["train", "val"], help="YOLO split to validate.")
    model_eval.add_argument("--imgsz", type=int, default=None, help="Override training.imgsz.")
    model_eval.add_argument("--notes", type=str, default="", help="Free-form notes for eval_history.csv.")

    return parser


def resolve_paths(args: argparse.Namespace) -> dict:
    config = load_pipeline_config(args.config)
    dataset_cfg = config["dataset"]
    training_cfg = config["training"]
    eval_cfg = config["eval"]
    return {
        "config": config,
        "hdf5_dir": resolve_repo_path(args.hdf5_dir or dataset_cfg["hdf5_dir"]),
        "export_dir": resolve_repo_path(getattr(args, "export_dir", None) or dataset_cfg["export_dir"]),
        "class_labels": list(dataset_cfg["class_labels"]),
        "imgsz": int(getattr(args, "imgsz", None) or training_cfg["imgsz"]),
        "history_csv": resolve_repo_path(eval_cfg["history_csv"]),
    }


def cmd_dataset_quality(args: argparse.Namespace) -> int:
    settings = resolve_paths(args)
    report = dataset_quality_report(settings["hdf5_dir"])
    report_path = resolve_repo_path(args.report) if args.report else settings["hdf5_dir"] / "quality_report.json"
    write_json_report(report, report_path)
    print(json.dumps(report, indent=2, ensure_ascii=False))
    print(f"wrote quality report to {report_path}")
    return 0


def cmd_model_eval(args: argparse.Namespace) -> int:
    settings = resolve_paths(args)
    dataset_yaml = export_hdf5_dataset(
        hdf5_dir=settings["hdf5_dir"],
        export_dir=settings["export_dir"],
        class_labels=settings["class_labels"],
        skip_duplicates=True,
        overwrite=True,
    )
    stats = compute_hdf5_dataset_stats(settings["hdf5_dir"])
    metrics = run_ultralytics_val(
        model_path=resolve_repo_path(args.model),
        dataset_yaml=dataset_yaml,
        split=args.split,
        imgsz=settings["imgsz"],
    )
    per_class = {
        f"AP_{label}": float(metrics["per_class_AP"][index]) if index < len(metrics["per_class_AP"]) else 0.0
        for index, label in enumerate(settings["class_labels"])
    }
    report = {
        "timestamp": iso_now(),
        "model_path": str(resolve_repo_path(args.model)),
        "dataset_yaml": str(dataset_yaml),
        "split": args.split,
        "n_train_frames": int(stats["split_kept_counts"].get("train", 0)),
        "n_val_frames": int(stats["split_kept_counts"].get("val", 0)),
        "dedup_rate": float(stats["dedup_rate"]),
        "metrics": {
            "mAP50": float(metrics["mAP50"]),
            "mAP50_95": float(metrics["mAP50_95"]),
            "per_class_AP": per_class,
            "speed_ms": metrics["speed_ms"],
            "inference_fps": float(metrics["inference_fps"]),
            "results_dict": metrics["results_dict"],
        },
    }
    report_path = resolve_repo_path(args.report) if args.report else settings["hdf5_dir"] / "eval_report.json"
    write_json_report(report, report_path)

    history_row = {
        "timestamp": report["timestamp"],
        "model_name": Path(args.model).name,
        "n_train_frames": report["n_train_frames"],
        "n_val_frames": report["n_val_frames"],
        "dedup_rate": report["dedup_rate"],
        "mAP50": report["metrics"]["mAP50"],
        "mAP50_95": report["metrics"]["mAP50_95"],
        "inference_fps": report["metrics"]["inference_fps"],
        "notes": args.notes,
        **per_class,
    }
    append_eval_history(history_row, settings["history_csv"])

    print(json.dumps(report, indent=2, ensure_ascii=False))
    print(f"wrote eval report to {report_path}")
    print(f"updated eval history at {settings['history_csv']}")
    return 0


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    if args.command == "dataset-quality":
        return cmd_dataset_quality(args)
    if args.command == "model-eval":
        return cmd_model_eval(args)
    parser.error(f"Unknown command: {args.command}")
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
