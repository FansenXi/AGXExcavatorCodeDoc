from __future__ import annotations

from pathlib import Path


SCHEMA_VERSION = "roi/1.0"

GRP_METADATA = "metadata"
GRP_FRAMES = "frames"
GRP_LABELS = "labels"
GRP_QUALITY = "quality"

DS_IMAGES = "frames/images"
DS_STEP_IDS = "frames/step_ids"
DS_FRAME_IDS = "frames/frame_ids"
DS_BOXES = "labels/boxes"
DS_CLASS_IDS = "labels/class_ids"
DS_N_OBJECTS = "labels/n_objects"
DS_DHASH = "quality/dhash"
DS_IS_KEPT = "quality/is_kept"
DS_LABEL_AREA_RATIO = "quality/label_area_ratio"
DS_CLASS_HISTOGRAM = "quality/class_histogram"

ATTR_SCHEMA_VERSION = "schema_version"
ATTR_EPISODE_INDEX = "episode_index"
ATTR_TIMESTAMP = "timestamp"
ATTR_SPLIT = "split"
ATTR_IMAGE_WIDTH = "image_width"
ATTR_IMAGE_HEIGHT = "image_height"
ATTR_CLASS_LABELS = "class_labels"
ATTR_N_CLASSES = "n_classes"
ATTR_N_FRAMES_RAW = "n_frames_raw"
ATTR_N_FRAMES_KEPT = "n_frames_kept"
ATTR_DEDUP_METHOD = "dedup_method"
ATTR_DEDUP_THRESHOLD = "dedup_threshold"
ATTR_CAPTURE_MODE = "capture_mode"
ATTR_STEP_INTERVAL = "step_interval"
ATTR_NOTES = "notes"
ATTR_JPEG_QUALITY = "jpeg_quality"
ATTR_SOURCE_RAW_DIR = "source_raw_dir"
ATTR_LOOKBACK_FRAMES = "lookback_frames"

DEFAULT_CLASS_LABELS = [
    "bucket",
    "excavator_arm",
    "truck",
    "container",
    "dig_area",
]


def roi_episode_filename(episode_index: int) -> str:
    return f"roi_episode_{episode_index:04d}.hdf5"


def roi_episode_path(hdf5_dir: str | Path, episode_index: int) -> Path:
    return Path(hdf5_dir) / roi_episode_filename(episode_index)

