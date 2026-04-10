from __future__ import annotations

import argparse
import json
from pathlib import Path

from ultralytics import YOLO

from roi_pipeline.core import (
    compute_hdf5_dataset_stats,
    export_hdf5_dataset,
    iso_now,
    load_pipeline_config,
    resolve_repo_path,
    run_ultralytics_val,
    write_json_report,
)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="roi-train",
        description="Export HDF5 -> YOLO, train Ultralytics, export ONNX, and run eval.",
    )
    parser.add_argument(
        "--config",
        type=Path,
        default=Path("tools/roi_pipeline_config.yaml"),
        help="Pipeline YAML config.",
    )
    parser.add_argument("--hdf5-dir", type=Path, default=None, help="Override dataset.hdf5_dir.")
    parser.add_argument("--export-dir", type=Path, default=None, help="Override dataset.export_dir.")
    parser.add_argument("--output-dir", type=Path, default=None, help="Override training.output_dir.")
    parser.add_argument("--model", type=Path, default=None, help="Override training.base_model.")
    parser.add_argument("--epochs", type=int, default=None, help="Override training.epochs.")
    parser.add_argument("--imgsz", type=int, default=None, help="Override training.imgsz.")
    parser.add_argument("--batch", type=int, default=None, help="Override training.batch.")
    parser.add_argument("--workers", type=int, default=None, help="Override training.workers.")
    parser.add_argument("--run-name", type=str, default=None, help="Override training.run_name.")
    parser.add_argument("--notes", type=str, default="", help="Eval history notes.")
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    config = load_pipeline_config(args.config)
    dataset_cfg = config["dataset"]
    training_cfg = config["training"]
    eval_cfg = config["eval"]

    hdf5_dir = resolve_repo_path(args.hdf5_dir or dataset_cfg["hdf5_dir"])
    export_dir = resolve_repo_path(args.export_dir or dataset_cfg["export_dir"])
    output_dir = resolve_repo_path(args.output_dir or training_cfg["output_dir"])
    base_model = resolve_repo_path(args.model or training_cfg["base_model"])
    epochs = int(args.epochs or training_cfg["epochs"])
    imgsz = int(args.imgsz or training_cfg["imgsz"])
    batch = int(args.batch or training_cfg["batch"])
    workers = int(args.workers if args.workers is not None else training_cfg.get("workers", 0))
    run_name = args.run_name or training_cfg["run_name"]
    class_labels = list(dataset_cfg["class_labels"])

    dataset_yaml = export_hdf5_dataset(
        hdf5_dir=hdf5_dir,
        export_dir=export_dir,
        class_labels=class_labels,
        skip_duplicates=True,
        overwrite=True,
    )

    model = YOLO(str(base_model))
    model.train(
        data=str(dataset_yaml),
        epochs=epochs,
        imgsz=imgsz,
        batch=batch,
        workers=max(0, workers),
        project=str(output_dir),
        name=run_name,
        exist_ok=True,
    )
    save_dir = Path(model.trainer.save_dir)
    best_checkpoint = Path(model.trainer.best)

    exported_onnx = YOLO(str(best_checkpoint)).export(format="onnx", opset=int(training_cfg["onnx_opset"]), imgsz=imgsz)
    val_metrics = run_ultralytics_val(
        model_path=best_checkpoint,
        dataset_yaml=dataset_yaml,
        split="val",
        imgsz=imgsz,
        workers=workers,
    )
    dataset_stats = compute_hdf5_dataset_stats(hdf5_dir)
    report = {
        "timestamp": iso_now(),
        "save_dir": str(save_dir),
        "best_checkpoint": str(best_checkpoint),
        "onnx_path": str(exported_onnx),
        "dataset_yaml": str(dataset_yaml),
        "metrics": val_metrics,
        "dataset_stats": dataset_stats,
    }
    report_path = save_dir / "train_report.json"
    write_json_report(report, report_path)

    history_csv = resolve_repo_path(eval_cfg["history_csv"])
    from roi_pipeline.core import append_eval_history

    history_row = {
        "timestamp": report["timestamp"],
        "model_name": best_checkpoint.name,
        "n_train_frames": int(dataset_stats["split_kept_counts"].get("train", 0)),
        "n_val_frames": int(dataset_stats["split_kept_counts"].get("val", 0)),
        "dedup_rate": float(dataset_stats["dedup_rate"]),
        "mAP50": float(val_metrics["mAP50"]),
        "mAP50_95": float(val_metrics["mAP50_95"]),
        "inference_fps": float(val_metrics["inference_fps"]),
        "notes": args.notes,
    }
    for index, label in enumerate(class_labels):
        history_row[f"AP_{label}"] = float(val_metrics["per_class_AP"][index]) if index < len(val_metrics["per_class_AP"]) else 0.0
    append_eval_history(history_row, history_csv)

    print(json.dumps(report, indent=2, ensure_ascii=False))
    print(f"wrote train report to {report_path}")
    print(f"updated eval history at {history_csv}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
