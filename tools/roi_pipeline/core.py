from __future__ import annotations

import csv
import copy
import json
import re
import statistics
import shutil
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import cv2
import h5py
import numpy as np
import yaml
from PIL import Image

from roi_pipeline.hdf5_io import RoiEpisodeRecord, list_roi_episodes, read_roi_episode, write_roi_episode
from roi_pipeline.schema import (
    ATTR_CAPTURE_MODE,
    ATTR_CLASS_LABELS,
    ATTR_DEDUP_METHOD,
    ATTR_DEDUP_THRESHOLD,
    ATTR_JPEG_QUALITY,
    ATTR_LOOKBACK_FRAMES,
    ATTR_NOTES,
    ATTR_SOURCE_RAW_DIR,
    ATTR_SPLIT,
    ATTR_STEP_INTERVAL,
    ATTR_TIMESTAMP,
    DEFAULT_CLASS_LABELS,
    DS_CLASS_HISTOGRAM,
    DS_DHASH,
    DS_IMAGES,
    DS_IS_KEPT,
    DS_LABEL_AREA_RATIO,
    roi_episode_path,
)


SCRIPT_ROOT = Path(__file__).resolve().parents[1]
REPO_ROOT = SCRIPT_ROOT.parent
PROJECT_ROOT = REPO_ROOT.parent.parent

DEFAULT_PIPELINE_CONFIG: dict[str, Any] = {
    "dataset": {
        "raw_dir": "../../ROI_Dataset",
        "hdf5_dir": "../../ROI_HDF5",
        "export_dir": "../../ROI_Export",
        "class_labels": DEFAULT_CLASS_LABELS,
    },
    "dedup": {
        "method": "dhash",
        "hash_size": 8,
        "threshold": 10,
        "lookback_frames": 5,
    },
    "training": {
        "base_model": "../../_model_archive/yolo11n.pt",
        "epochs": 100,
        "imgsz": 640,
        "batch": 16,
        "workers": 0,
        "output_dir": "../../ROI_Runs",
        "onnx_opset": 17,
        "run_name": "roi_yolo11n_4class",
    },
    "eval": {
        "metrics": ["mAP50", "mAP50_95", "per_class_AP"],
        "history_csv": "../../ROI_Runs/eval_history.csv",
        "visualize_errors": False,
        "error_top_k": 20,
    },
}

RAW_SAMPLE_PATTERN = re.compile(
    r"^episode_(?P<episode>\d+)_step_(?P<step>\d+)_frame_(?P<frame>\d+)$"
)

SPLIT_BUCKET_MODULUS = 100
SPLIT_BUCKET_MULTIPLIER = 61
SPLIT_BUCKET_OFFSET = 17


@dataclass(slots=True)
class RawSample:
    split: str
    episode_index: int
    step_id: int
    frame_id: int
    image_path: Path
    label_path: Path


def resolve_repo_path(path_like: str | Path) -> Path:
    path = Path(path_like)
    if path.is_absolute():
        return path
    return (REPO_ROOT / path).resolve()


def deep_merge(base: dict[str, Any], override: dict[str, Any]) -> dict[str, Any]:
    merged = dict(base)
    for key, value in (override or {}).items():
        if isinstance(value, dict) and isinstance(merged.get(key), dict):
            merged[key] = deep_merge(merged[key], value)
        else:
            merged[key] = value
    return merged


def load_pipeline_config(config_path: str | Path | None = None) -> dict[str, Any]:
    config = copy.deepcopy(DEFAULT_PIPELINE_CONFIG)
    if config_path is None:
        return config
    with open(resolve_repo_path(config_path), "r", encoding="utf-8") as handle:
        loaded = yaml.safe_load(handle) or {}
    return deep_merge(config, loaded)


def ensure_dataset_yaml(yolo_dir: str | Path, class_labels: list[str]) -> Path:
    yolo_dir = Path(yolo_dir)
    yolo_dir.mkdir(parents=True, exist_ok=True)
    dataset_yaml = yolo_dir / "dataset.yaml"
    payload = {
        "path": str(yolo_dir),
        "train": "images/train",
        "val": "images/val",
        "names": {index: label for index, label in enumerate(class_labels)},
    }
    with open(dataset_yaml, "w", encoding="utf-8") as handle:
        yaml.safe_dump(payload, handle, sort_keys=False, allow_unicode=True)
    return dataset_yaml


def compute_episode_split_bucket(episode_index: int) -> int:
    bucket = ((int(episode_index) * SPLIT_BUCKET_MULTIPLIER) + SPLIT_BUCKET_OFFSET) % SPLIT_BUCKET_MODULUS
    return int(bucket)


def is_validation_episode(episode_index: int, validation_split: float) -> bool:
    validation_bucket_count = max(0, min(99, int(round(float(validation_split) * 100.0))))
    if validation_bucket_count <= 0:
        return False
    return compute_episode_split_bucket(episode_index) < validation_bucket_count


def resolve_episode_split(
    episode_index: int,
    *,
    manifest: dict[str, Any] | None,
    fallback_split: str,
) -> str:
    manifest = manifest or {}
    validation_split = manifest.get("validation_split")
    if validation_split is not None:
        return "val" if is_validation_episode(episode_index, float(validation_split)) else "train"
    if fallback_split in {"train", "val"}:
        return fallback_split
    return "train"


def parse_raw_sample(image_path: Path, raw_dir: Path) -> RawSample | None:
    stem_match = RAW_SAMPLE_PATTERN.match(image_path.stem)
    if stem_match is None:
        return None
    split = image_path.parent.name
    label_path = raw_dir / "labels" / split / f"{image_path.stem}.txt"
    if not label_path.exists():
        return None
    return RawSample(
        split=split,
        episode_index=int(stem_match.group("episode")),
        step_id=int(stem_match.group("step")),
        frame_id=int(stem_match.group("frame")),
        image_path=image_path,
        label_path=label_path,
    )


def discover_raw_samples(raw_dir: str | Path) -> list[RawSample]:
    raw_dir = Path(raw_dir)
    image_root = raw_dir / "images"
    if not image_root.exists():
        return []

    samples: list[RawSample] = []
    for image_path in sorted(image_root.rglob("*.jpg")):
        parsed = parse_raw_sample(image_path, raw_dir)
        if parsed is not None:
            samples.append(parsed)
    return sorted(samples, key=lambda s: (s.episode_index, s.step_id, s.frame_id))


def group_samples_by_episode(samples: list[RawSample]) -> dict[int, list[RawSample]]:
    grouped: dict[int, list[RawSample]] = {}
    for sample in samples:
        grouped.setdefault(sample.episode_index, []).append(sample)
    return grouped


def read_rgb_image(path: str | Path) -> np.ndarray:
    with Image.open(path) as image:
        return np.asarray(image.convert("RGB"), dtype=np.uint8)


def read_yolo_label_file(path: str | Path) -> tuple[np.ndarray, np.ndarray]:
    class_ids: list[int] = []
    boxes: list[list[float]] = []
    with open(path, "r", encoding="utf-8") as handle:
        for raw_line in handle:
            line = raw_line.strip()
            if not line:
                continue
            parts = line.split()
            if len(parts) != 5:
                raise ValueError(f"Invalid YOLO row in {path}: {line}")
            class_ids.append(int(parts[0]))
            boxes.append([float(parts[1]), float(parts[2]), float(parts[3]), float(parts[4])])
    if not boxes:
        return np.zeros((0, 4), dtype=np.float32), np.zeros((0,), dtype=np.int32)
    return np.asarray(boxes, dtype=np.float32), np.asarray(class_ids, dtype=np.int32)


def compute_dhash(image_rgb: np.ndarray, hash_size: int = 8) -> np.uint64:
    gray = cv2.cvtColor(image_rgb, cv2.COLOR_RGB2GRAY)
    resized = cv2.resize(gray, (hash_size + 1, hash_size), interpolation=cv2.INTER_AREA)
    diff = resized[:, 1:] > resized[:, :-1]
    value = np.uint64(0)
    for bit in diff.flatten():
        value = (value << np.uint64(1)) | np.uint64(int(bit))
    return value


def hamming_distance(left: np.uint64, right: np.uint64) -> int:
    return int((int(left) ^ int(right)).bit_count())


def compute_label_area_ratio(boxes: np.ndarray) -> float:
    if boxes.size == 0:
        return 0.0
    areas = boxes[:, 2] * boxes[:, 3]
    return float(np.clip(np.sum(areas), 0.0, 1.0))


def load_episode_manifest(raw_dir: str | Path, episode_index: int) -> dict[str, Any]:
    raw_dir = Path(raw_dir)
    manifest_candidates = [
        raw_dir / "manifests" / f"episode_{episode_index:04d}_manifest.json",
        raw_dir / f"episode_{episode_index:04d}_manifest.json",
    ]
    for manifest_path in manifest_candidates:
        if manifest_path.exists():
            with open(manifest_path, "r", encoding="utf-8") as handle:
                return json.load(handle)
    return {}


def infer_step_interval(step_ids: list[int]) -> int:
    if len(step_ids) < 2:
        return 0
    deltas = [curr - prev for prev, curr in zip(step_ids[:-1], step_ids[1:]) if curr > prev]
    if not deltas:
        return 0
    return int(statistics.median(deltas))


def build_episode_record(
    samples: list[RawSample],
    *,
    raw_dir: str | Path,
    class_labels: list[str],
    dedup_threshold: int,
    lookback_frames: int,
    hash_size: int,
) -> RoiEpisodeRecord:
    if not samples:
        raise ValueError("build_episode_record requires at least one sample")

    manifest = load_episode_manifest(raw_dir, samples[0].episode_index)
    images = [read_rgb_image(sample.image_path) for sample in samples]
    image_shapes = {image.shape for image in images}
    if len(image_shapes) != 1:
        raise ValueError(
            f"Episode {samples[0].episode_index} contains inconsistent image shapes: {sorted(image_shapes)}"
        )

    label_pairs = [read_yolo_label_file(sample.label_path) for sample in samples]
    n_objects = np.asarray([boxes.shape[0] for boxes, _ in label_pairs], dtype=np.int32)
    max_objects = max(1, int(np.max(n_objects)) if n_objects.size > 0 else 0)
    boxes_array = np.zeros((len(samples), max_objects, 4), dtype=np.float32)
    class_ids_array = np.full((len(samples), max_objects), fill_value=-1, dtype=np.int32)
    label_area_ratio = np.zeros((len(samples),), dtype=np.float32)
    class_histogram = np.zeros((len(class_labels),), dtype=np.int32)

    for index, (boxes, class_ids) in enumerate(label_pairs):
        count = boxes.shape[0]
        if count > 0:
            boxes_array[index, :count] = boxes
            class_ids_array[index, :count] = class_ids
            label_area_ratio[index] = compute_label_area_ratio(boxes)
            for class_id in class_ids:
                if 0 <= int(class_id) < len(class_histogram):
                    class_histogram[int(class_id)] += 1

    dhash = np.asarray([compute_dhash(image, hash_size=hash_size) for image in images], dtype=np.uint64)
    is_kept = np.ones((len(samples),), dtype=np.bool_)
    for index in range(len(samples)):
        min_distance: int | None = None
        lookback_start = max(0, index - max(1, lookback_frames))
        for previous in range(lookback_start, index):
            distance = hamming_distance(dhash[index], dhash[previous])
            if min_distance is None or distance < min_distance:
                min_distance = distance
        if min_distance is not None and min_distance < dedup_threshold:
            is_kept[index] = False

    capture_timestamp = manifest.get("timestamp") or datetime.fromtimestamp(
        min(sample.image_path.stat().st_mtime for sample in samples), tz=timezone.utc
    ).isoformat()
    split = resolve_episode_split(
        samples[0].episode_index,
        manifest=manifest,
        fallback_split=samples[0].split,
    )
    metadata = {
        ATTR_TIMESTAMP: capture_timestamp,
        ATTR_CLASS_LABELS: ",".join(class_labels),
        ATTR_DEDUP_METHOD: "dhash",
        ATTR_DEDUP_THRESHOLD: int(dedup_threshold),
        ATTR_CAPTURE_MODE: manifest.get("capture_mode", "unknown"),
        ATTR_STEP_INTERVAL: int(manifest.get("step_interval", infer_step_interval([sample.step_id for sample in samples]))),
        ATTR_NOTES: manifest.get("notes", ""),
        ATTR_JPEG_QUALITY: int(manifest.get("jpeg_quality", 90)),
        ATTR_SOURCE_RAW_DIR: str(Path(raw_dir).resolve()),
        ATTR_LOOKBACK_FRAMES: int(lookback_frames),
    }

    return RoiEpisodeRecord(
        episode_index=samples[0].episode_index,
        split=split,
        images=np.stack(images, axis=0).astype(np.uint8),
        step_ids=np.asarray([sample.step_id for sample in samples], dtype=np.int64),
        frame_ids=np.asarray([sample.frame_id for sample in samples], dtype=np.int64),
        boxes=boxes_array,
        class_ids=class_ids_array,
        n_objects=n_objects,
        dhash=dhash,
        is_kept=is_kept,
        label_area_ratio=label_area_ratio.astype(np.float32),
        class_histogram=class_histogram,
        metadata=metadata,
    )


def ingest_raw_dataset(
    *,
    raw_dir: str | Path,
    hdf5_dir: str | Path,
    class_labels: list[str],
    dedup_threshold: int,
    lookback_frames: int,
    hash_size: int,
    overwrite: bool = False,
    settle_seconds: float = 0.0,
) -> list[Path]:
    raw_dir = Path(raw_dir)
    hdf5_dir = Path(hdf5_dir)
    hdf5_dir.mkdir(parents=True, exist_ok=True)

    ingested_paths: list[Path] = []
    grouped = group_samples_by_episode(discover_raw_samples(raw_dir))
    now = time.time()
    for episode_index, episode_samples in sorted(grouped.items()):
        if settle_seconds > 0.0:
            latest_mtime = max(sample.image_path.stat().st_mtime for sample in episode_samples)
            if now - latest_mtime < settle_seconds:
                continue

        output_path = roi_episode_path(hdf5_dir, episode_index)
        if output_path.exists() and not overwrite:
            continue

        record = build_episode_record(
            episode_samples,
            raw_dir=raw_dir,
            class_labels=class_labels,
            dedup_threshold=dedup_threshold,
            lookback_frames=lookback_frames,
            hash_size=hash_size,
        )
        write_roi_episode(output_path, record)
        ingested_paths.append(output_path)
    return ingested_paths


def _active_frame_indices(record: RoiEpisodeRecord, skip_duplicates: bool) -> np.ndarray:
    if not skip_duplicates:
        return np.arange(record.images.shape[0], dtype=np.int32)
    return np.flatnonzero(record.is_kept)


def export_hdf5_dataset(
    *,
    hdf5_dir: str | Path,
    export_dir: str | Path,
    class_labels: list[str],
    skip_duplicates: bool = True,
    overwrite: bool = True,
) -> Path:
    hdf5_dir = Path(hdf5_dir)
    export_dir = Path(export_dir)
    if overwrite and export_dir.exists():
        shutil.rmtree(export_dir)
    (export_dir / "images" / "train").mkdir(parents=True, exist_ok=True)
    (export_dir / "images" / "val").mkdir(parents=True, exist_ok=True)
    (export_dir / "labels" / "train").mkdir(parents=True, exist_ok=True)
    (export_dir / "labels" / "val").mkdir(parents=True, exist_ok=True)

    for episode_path in list_roi_episodes(hdf5_dir):
        record = read_roi_episode(episode_path)
        active_indices = _active_frame_indices(record, skip_duplicates=skip_duplicates)
        split = str(record.metadata.get(ATTR_SPLIT, record.split))
        for frame_index in active_indices:
            stem = (
                f"episode_{record.episode_index:04d}_step_{int(record.step_ids[frame_index]):06d}"
                f"_frame_{int(record.frame_ids[frame_index]):06d}"
            )
            image_path = export_dir / "images" / split / f"{stem}.jpg"
            label_path = export_dir / "labels" / split / f"{stem}.txt"
            Image.fromarray(record.images[frame_index]).save(image_path, quality=int(record.metadata.get(ATTR_JPEG_QUALITY, 90)))
            count = int(record.n_objects[frame_index])
            with open(label_path, "w", encoding="utf-8") as handle:
                for object_index in range(count):
                    class_id = int(record.class_ids[frame_index, object_index])
                    cx, cy, width, height = record.boxes[frame_index, object_index]
                    handle.write(f"{class_id} {cx:.6f} {cy:.6f} {width:.6f} {height:.6f}\n")

    return ensure_dataset_yaml(export_dir, class_labels)


def compute_hdf5_dataset_stats(hdf5_dir: str | Path) -> dict[str, Any]:
    hdf5_dir = Path(hdf5_dir)
    episodes = list_roi_episodes(hdf5_dir)
    split_counts: dict[str, int] = {"train": 0, "val": 0}
    split_kept_counts: dict[str, int] = {"train": 0, "val": 0}
    class_histogram: np.ndarray | None = None
    class_labels: list[str] = list(DEFAULT_CLASS_LABELS)
    episode_rows: list[dict[str, Any]] = []
    label_area_values: list[float] = []
    diversity_values: list[int] = []

    for episode_path in episodes:
        with h5py.File(episode_path, "r") as handle:
            metadata = dict(handle["metadata"].attrs)
            split = str(metadata.get(ATTR_SPLIT, "train"))
            metadata_labels = str(metadata.get(ATTR_CLASS_LABELS, "")).strip()
            if metadata_labels:
                class_labels = [label.strip() for label in metadata_labels.split(",") if label.strip()]
            image_shape = handle[DS_IMAGES].shape
            raw_count = int(image_shape[0])
            is_kept = handle[DS_IS_KEPT][()].astype(np.bool_)
            kept_count = int(np.count_nonzero(is_kept))
            label_area_ratio = handle[DS_LABEL_AREA_RATIO][()].astype(np.float32)
            dhash_values = handle[DS_DHASH][()].astype(np.uint64)
            episode_class_histogram = handle[DS_CLASS_HISTOGRAM][()].astype(np.int64)

        split_counts[split] = split_counts.get(split, 0) + raw_count
        split_kept_counts[split] = split_kept_counts.get(split, 0) + kept_count
        label_area_values.extend(label_area_ratio.tolist())
        diversity_values.extend(
            hamming_distance(dhash_values[index], dhash_values[index - 1])
            for index in range(1, len(dhash_values))
        )
        if class_histogram is None:
            class_histogram = episode_class_histogram
        else:
            class_histogram += episode_class_histogram
        episode_rows.append(
            {
                "episode_index": int(metadata.get("episode_index", 0)),
                "split": split,
                "n_frames_raw": raw_count,
                "n_frames_kept": kept_count,
                "dedup_rate": float(1.0 - kept_count / raw_count) if raw_count > 0 else 0.0,
                "image_width": int(image_shape[2]),
                "image_height": int(image_shape[1]),
                "step_interval": int(metadata.get(ATTR_STEP_INTERVAL, 0)),
                "capture_mode": str(metadata.get(ATTR_CAPTURE_MODE, "unknown")),
            }
        )

    if class_histogram is not None and len(class_labels) != len(class_histogram):
        class_labels = DEFAULT_CLASS_LABELS[: len(class_histogram)]
    total_raw = sum(split_counts.values())
    total_kept = sum(split_kept_counts.values())
    dedup_rate = float(1.0 - total_kept / total_raw) if total_raw > 0 else 0.0

    def distribution(values: list[float | int]) -> dict[str, float]:
        if not values:
            return {"min": 0.0, "p50": 0.0, "p90": 0.0, "mean": 0.0, "max": 0.0}
        array = np.asarray(values, dtype=np.float64)
        return {
            "min": float(np.min(array)),
            "p50": float(np.percentile(array, 50)),
            "p90": float(np.percentile(array, 90)),
            "mean": float(np.mean(array)),
            "max": float(np.max(array)),
        }

    return {
        "schema_version": "roi/1.0",
        "episodes": episode_rows,
        "n_episodes": len(episode_rows),
        "n_frames_raw": total_raw,
        "n_frames_kept": total_kept,
        "dedup_rate": dedup_rate,
        "split_counts": split_counts,
        "split_kept_counts": split_kept_counts,
        "class_histogram": {
            label: int(count)
            for label, count in zip(class_labels, class_histogram.tolist() if class_histogram is not None else [])
        },
        "label_area_ratio": distribution(label_area_values),
        "dhash_distance": distribution(diversity_values),
    }


def dataset_quality_report(hdf5_dir: str | Path) -> dict[str, Any]:
    stats = compute_hdf5_dataset_stats(hdf5_dir)
    class_histogram = stats.get("class_histogram", {})
    readiness_issues: list[str] = []
    if stats["split_kept_counts"].get("train", 0) <= 0:
        readiness_issues.append("train split is empty")
    if stats["split_kept_counts"].get("val", 0) <= 0:
        readiness_issues.append("val split is empty")
    if stats["n_episodes"] < 8:
        readiness_issues.append("fewer than 8 episodes")
    if any(count <= 0 for count in class_histogram.values()):
        readiness_issues.append("at least one class has zero coverage")

    diversity_mean = float(stats["dhash_distance"]["mean"])
    coverage_score = 0.0 if not class_histogram else sum(1 for count in class_histogram.values() if count > 0) / len(class_histogram)
    dedup_score = max(0.0, min(1.0, 1.0 - abs(stats["dedup_rate"] - 0.35) / 0.35))
    diversity_score = max(0.0, min(1.0, diversity_mean / 16.0))
    quality_score = float((coverage_score * 0.4) + (dedup_score * 0.25) + (diversity_score * 0.35))

    stats["quality"] = {
        "coverage_score": coverage_score,
        "dedup_score": dedup_score,
        "diversity_score": diversity_score,
        "quality_score": quality_score,
        "status": "ready" if not readiness_issues else ("warning" if len(readiness_issues) <= 2 else "blocked"),
        "issues": readiness_issues,
    }
    return stats


def write_json_report(report: dict[str, Any], path: str | Path) -> Path:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2, ensure_ascii=False)
    return path


def append_eval_history(row: dict[str, Any], csv_path: str | Path) -> Path:
    csv_path = Path(csv_path)
    csv_path.parent.mkdir(parents=True, exist_ok=True)
    default_fieldnames = [
        "timestamp",
        "model_name",
        "n_train_frames",
        "n_val_frames",
        "dedup_rate",
        "mAP50",
        "mAP50_95",
        "inference_fps",
        "notes",
    ]
    ap_fieldnames = [f"AP_{label}" for label in DEFAULT_CLASS_LABELS]
    insert_index = default_fieldnames.index("inference_fps")
    default_fieldnames[insert_index:insert_index] = ap_fieldnames

    fieldnames = list(default_fieldnames)
    if csv_path.exists():
        with open(csv_path, "r", encoding="utf-8", newline="") as handle:
            reader = csv.reader(handle)
            existing_header = next(reader, [])
        if existing_header:
            fieldnames = list(existing_header)

    for column_name in default_fieldnames:
        if column_name not in fieldnames:
            fieldnames.append(column_name)

    for column_name in row.keys():
        if column_name not in fieldnames:
            fieldnames.append(column_name)

    needs_header = not csv_path.exists()
    with open(csv_path, "a", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fieldnames)
        if needs_header:
            writer.writeheader()
        writer.writerow({name: row.get(name, "") for name in fieldnames})
    return csv_path


def iso_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def run_ultralytics_val(
    *,
    model_path: str | Path,
    dataset_yaml: str | Path,
    split: str,
    imgsz: int,
    workers: int = 0,
) -> dict[str, Any]:
    from ultralytics import YOLO

    model = YOLO(str(model_path))
    metrics = model.val(
        data=str(dataset_yaml),
        split=split,
        imgsz=imgsz,
        workers=max(0, int(workers)),
        plots=False,
        save_json=False,
        verbose=False,
    )

    box_metrics = getattr(metrics, "box", metrics)
    maps_attr = getattr(box_metrics, "maps", None)
    if maps_attr is None:
        per_class_maps: list[float] = []
    else:
        per_class_maps = np.asarray(maps_attr, dtype=np.float64).tolist()
    speed = getattr(metrics, "speed", {}) or {}
    inference_ms = float(speed.get("inference", 0.0) or 0.0)

    return {
        "mAP50": float(getattr(box_metrics, "map50", 0.0) or 0.0),
        "mAP50_95": float(getattr(box_metrics, "map", 0.0) or 0.0),
        "per_class_AP": [float(value) for value in per_class_maps],
        "speed_ms": {
            key: float(value or 0.0)
            for key, value in speed.items()
        },
        "inference_fps": float(1000.0 / inference_ms) if inference_ms > 0.0 else 0.0,
        "results_dict": {
            str(key): float(value) if isinstance(value, (int, float, np.floating)) else value
            for key, value in getattr(metrics, "results_dict", {}).items()
        },
    }
