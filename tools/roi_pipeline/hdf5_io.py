from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import h5py
import numpy as np

from roi_pipeline.schema import (
    ATTR_CAPTURE_MODE,
    ATTR_CLASS_LABELS,
    ATTR_DEDUP_METHOD,
    ATTR_DEDUP_THRESHOLD,
    ATTR_EPISODE_INDEX,
    ATTR_IMAGE_HEIGHT,
    ATTR_IMAGE_WIDTH,
    ATTR_JPEG_QUALITY,
    ATTR_LOOKBACK_FRAMES,
    ATTR_N_CLASSES,
    ATTR_N_FRAMES_KEPT,
    ATTR_N_FRAMES_RAW,
    ATTR_NOTES,
    ATTR_SCHEMA_VERSION,
    ATTR_SOURCE_RAW_DIR,
    ATTR_SPLIT,
    ATTR_STEP_INTERVAL,
    ATTR_TIMESTAMP,
    DS_BOXES,
    DS_CLASS_HISTOGRAM,
    DS_CLASS_IDS,
    DS_DHASH,
    DS_FRAME_IDS,
    DS_IMAGES,
    DS_IS_KEPT,
    DS_LABEL_AREA_RATIO,
    DS_N_OBJECTS,
    DS_STEP_IDS,
    GRP_METADATA,
    SCHEMA_VERSION,
)


@dataclass(slots=True)
class RoiEpisodeRecord:
    episode_index: int
    split: str
    images: np.ndarray
    step_ids: np.ndarray
    frame_ids: np.ndarray
    boxes: np.ndarray
    class_ids: np.ndarray
    n_objects: np.ndarray
    dhash: np.ndarray
    is_kept: np.ndarray
    label_area_ratio: np.ndarray
    class_histogram: np.ndarray
    metadata: dict[str, Any] = field(default_factory=dict)


def write_roi_episode(path: str | Path, record: RoiEpisodeRecord, *, compress: bool = True) -> None:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    dataset_kwargs = {"compression": "lzf"} if compress else {}

    with h5py.File(path, "w") as handle:
        metadata_group = handle.create_group(GRP_METADATA)
        metadata_group.attrs[ATTR_SCHEMA_VERSION] = SCHEMA_VERSION
        metadata_group.attrs[ATTR_EPISODE_INDEX] = int(record.episode_index)
        metadata_group.attrs[ATTR_SPLIT] = str(record.split)
        metadata_group.attrs[ATTR_IMAGE_WIDTH] = int(record.images.shape[2])
        metadata_group.attrs[ATTR_IMAGE_HEIGHT] = int(record.images.shape[1])
        metadata_group.attrs[ATTR_N_CLASSES] = int(record.class_histogram.shape[0])
        metadata_group.attrs[ATTR_N_FRAMES_RAW] = int(record.images.shape[0])
        metadata_group.attrs[ATTR_N_FRAMES_KEPT] = int(np.count_nonzero(record.is_kept))
        metadata_group.attrs[ATTR_SCHEMA_VERSION] = SCHEMA_VERSION

        default_metadata = {
            ATTR_TIMESTAMP: "",
            ATTR_CLASS_LABELS: "",
            ATTR_DEDUP_METHOD: "dhash",
            ATTR_DEDUP_THRESHOLD: 0,
            ATTR_CAPTURE_MODE: "unknown",
            ATTR_STEP_INTERVAL: 0,
            ATTR_NOTES: "",
            ATTR_JPEG_QUALITY: 90,
            ATTR_SOURCE_RAW_DIR: "",
            ATTR_LOOKBACK_FRAMES: 0,
        }
        default_metadata.update(record.metadata or {})
        for key, value in default_metadata.items():
            metadata_group.attrs[key] = value

        handle.create_dataset(DS_IMAGES, data=record.images.astype(np.uint8), **dataset_kwargs)
        handle.create_dataset(DS_STEP_IDS, data=record.step_ids.astype(np.int64))
        handle.create_dataset(DS_FRAME_IDS, data=record.frame_ids.astype(np.int64))
        handle.create_dataset(DS_BOXES, data=record.boxes.astype(np.float32))
        handle.create_dataset(DS_CLASS_IDS, data=record.class_ids.astype(np.int32))
        handle.create_dataset(DS_N_OBJECTS, data=record.n_objects.astype(np.int32))
        handle.create_dataset(DS_DHASH, data=record.dhash.astype(np.uint64))
        handle.create_dataset(DS_IS_KEPT, data=record.is_kept.astype(np.bool_))
        handle.create_dataset(DS_LABEL_AREA_RATIO, data=record.label_area_ratio.astype(np.float32))
        handle.create_dataset(DS_CLASS_HISTOGRAM, data=record.class_histogram.astype(np.int32))


def read_roi_episode(path: str | Path) -> RoiEpisodeRecord:
    path = Path(path)
    with h5py.File(path, "r") as handle:
        metadata = dict(handle[GRP_METADATA].attrs)
        return RoiEpisodeRecord(
            episode_index=int(metadata.get(ATTR_EPISODE_INDEX, 0)),
            split=str(metadata.get(ATTR_SPLIT, "train")),
            images=handle[DS_IMAGES][()].astype(np.uint8),
            step_ids=handle[DS_STEP_IDS][()].astype(np.int64),
            frame_ids=handle[DS_FRAME_IDS][()].astype(np.int64),
            boxes=handle[DS_BOXES][()].astype(np.float32),
            class_ids=handle[DS_CLASS_IDS][()].astype(np.int32),
            n_objects=handle[DS_N_OBJECTS][()].astype(np.int32),
            dhash=handle[DS_DHASH][()].astype(np.uint64),
            is_kept=handle[DS_IS_KEPT][()].astype(np.bool_),
            label_area_ratio=handle[DS_LABEL_AREA_RATIO][()].astype(np.float32),
            class_histogram=handle[DS_CLASS_HISTOGRAM][()].astype(np.int32),
            metadata=metadata,
        )


def list_roi_episodes(hdf5_dir: str | Path) -> list[Path]:
    hdf5_dir = Path(hdf5_dir)
    episodes: list[Path] = []
    for path in hdf5_dir.glob("roi_episode_*.hdf5"):
        try:
            int(path.stem.split("_")[-1])
        except ValueError:
            continue
        episodes.append(path)
    return sorted(episodes, key=lambda p: int(p.stem.split("_")[-1]))
