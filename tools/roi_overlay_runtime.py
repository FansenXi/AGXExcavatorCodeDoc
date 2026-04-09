from __future__ import annotations

import argparse
import csv
import io
import socket
import struct
import subprocess
import time
import zlib
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable, Sequence

import cv2
import imageio_ffmpeg
import numpy as np
import onnxruntime as ort
import yaml


MAGIC = 0xA6A6A6A6
HEADER_VERSION = 1
HEADER_STRUCT = struct.Struct("<IHHII")
GET_INFO_REQ = 1
GET_INFO_RESP = 2
RESET_REQ = 3
RESET_RESP = 4
STEP_REQ = 5
STEP_RESP = 6


@dataclass
class ProtocolConfig:
    host: str = "127.0.0.1"
    port: int = 5057
    connect_timeout_sec: float = 30.0
    reset_on_start: bool = True
    step_interval_sec: float = 0.0
    max_steps: int = 300
    action: list[float] = field(default_factory=lambda: [0.0, 0.0, 0.0, 0.0])


@dataclass
class DetectorConfig:
    model_path: str = "../../_model_archive/roi_smoke_detector.onnx"
    input_width: int = 576
    input_height: int = 576
    confidence_threshold: float = 0.35
    nms_iou_threshold: float = 0.5
    output_tensor_name: str = ""
    class_labels: list[str] = field(
        default_factory=lambda: ["bucket", "excavator_arm", "truck", "container", "dig_area"]
    )
    max_expected_joint_velocity: float = 1.5


@dataclass
class OutputConfig:
    preview: bool = True
    preview_window_name: str = "ROI Overlay Runtime"
    preview_scale: float = 1.0
    encode_video: bool = True
    output_path: str = "../../ExperimentLogs/roi_overlay_runtime/roi_overlay_runtime.h264"
    csv_log_path: str = "../../ExperimentLogs/roi_overlay_runtime/roi_overlay_runtime.csv"
    fps_override: float = 0.0
    h264_crf: int = 18
    h264_preset: str = "veryfast"


@dataclass
class RuntimeConfig:
    protocol: ProtocolConfig = field(default_factory=ProtocolConfig)
    detector: DetectorConfig = field(default_factory=DetectorConfig)
    output: OutputConfig = field(default_factory=OutputConfig)
    repo_root: Path = field(default_factory=Path)


@dataclass
class Detection:
    x: float
    y: float
    width: float
    height: float
    confidence: float
    class_id: int
    label: str


@dataclass
class CameraDescriptor:
    name: str
    width: int
    height: int
    fps: float
    pixel_format: str
    row_order: str


@dataclass
class InfoResponse:
    success: bool
    error: str
    protocol_version: str
    dt: float
    control_hz: float
    action_semantics: str
    warnings: list[str]
    cameras: list[CameraDescriptor]


@dataclass
class StepResponse:
    success: bool
    error: str
    step_id: int
    qpos: list[float]
    qvel: list[float]
    env_state: list[float]
    image_rgb: np.ndarray | None
    image_format: str
    reward: float
    sim_time_ns: int
    warnings: list[str]


class BufferReader:
    def __init__(self, data: bytes) -> None:
        self._buffer = memoryview(data)
        self._offset = 0

    def _take(self, size: int) -> memoryview:
        end = self._offset + size
        if end > len(self._buffer):
            raise ValueError("payload_truncated")
        chunk = self._buffer[self._offset:end]
        self._offset = end
        return chunk

    def read_int32(self) -> int:
        return struct.unpack("<i", self._take(4))[0]

    def read_int64(self) -> int:
        return struct.unpack("<q", self._take(8))[0]

    def read_float32(self) -> float:
        return struct.unpack("<f", self._take(4))[0]

    def read_bool(self) -> bool:
        return bool(self._take(1)[0])

    def read_bytes(self) -> bytes:
        length = self.read_int32()
        if length < 0:
            raise ValueError("negative_byte_array_length")
        return self._take(length).tobytes()

    def read_string(self) -> str:
        return self.read_bytes().decode("utf-8")

    def read_string_array(self) -> list[str]:
        length = self.read_int32()
        if length < 0:
            raise ValueError("negative_string_array_length")
        return [self.read_string() for _ in range(length)]

    def read_float_array(self) -> list[float]:
        length = self.read_int32()
        if length < 0:
            raise ValueError("negative_float_array_length")
        return [self.read_float32() for _ in range(length)]


class AgxSimClient:
    def __init__(self, config: ProtocolConfig) -> None:
        self._config = config
        self._socket: socket.socket | None = None

    def connect(self) -> None:
        deadline = time.time() + max(1.0, self._config.connect_timeout_sec)
        last_error: Exception | None = None
        while time.time() < deadline:
            try:
                candidate = socket.create_connection((self._config.host, self._config.port), timeout=2.0)
                candidate.settimeout(10.0)
                self._socket = candidate
                return
            except OSError as error:
                last_error = error
                time.sleep(0.5)

        raise RuntimeError(
            f"Unable to connect to AGX sim server at {self._config.host}:{self._config.port}: {last_error}"
        )

    def close(self) -> None:
        if self._socket is None:
            return
        try:
            self._socket.close()
        finally:
            self._socket = None

    def get_info(self) -> InfoResponse:
        response_type, payload = self._exchange(GET_INFO_REQ, b"")
        if response_type != GET_INFO_RESP:
            raise RuntimeError(f"Unexpected response type for GET_INFO: {response_type}")
        return parse_info_response(payload)

    def reset(self) -> None:
        response_type, payload = self._exchange(RESET_REQ, b"")
        if response_type != RESET_RESP:
            raise RuntimeError(f"Unexpected response type for RESET: {response_type}")
        success, error = parse_common_response_prefix(payload)
        if not success:
            raise RuntimeError(f"Reset failed: {error}")

    def step(self, step_id: int, action: Sequence[float]) -> StepResponse:
        payload = io.BytesIO()
        payload.write(struct.pack("<q", step_id))
        payload.write(struct.pack("<i", len(action)))
        for value in action:
            payload.write(struct.pack("<f", float(value)))
        payload.write(struct.pack("<q", time.time_ns()))
        response_type, response_payload = self._exchange(STEP_REQ, payload.getvalue())
        if response_type != STEP_RESP:
            raise RuntimeError(f"Unexpected response type for STEP: {response_type}")
        return parse_step_response(response_payload)

    def _exchange(self, message_type: int, payload: bytes) -> tuple[int, bytes]:
        if self._socket is None:
            raise RuntimeError("AGX client is not connected")

        frame = serialize_frame(message_type, payload)
        self._socket.sendall(frame)
        return read_frame(self._socket)


class OnnxRuntimeRoiDetector:
    def __init__(self, config: DetectorConfig, repo_root: Path) -> None:
        self._config = config
        self._model_path = resolve_path(repo_root, config.model_path)
        self._session = ort.InferenceSession(str(self._model_path), providers=["CPUExecutionProvider"])
        self._input_name = self._session.get_inputs()[0].name

        first_input_shape = self._session.get_inputs()[0].shape
        self._input_height = int(first_input_shape[-2]) if _is_positive_dimension(first_input_shape[-2]) else config.input_height
        self._input_width = int(first_input_shape[-1]) if _is_positive_dimension(first_input_shape[-1]) else config.input_width
        if self._input_width <= 0 or self._input_height <= 0:
            raise RuntimeError(f"Invalid model input size resolved from {self._model_path}")

    @property
    def input_width(self) -> int:
        return self._input_width

    @property
    def input_height(self) -> int:
        return self._input_height

    def detect(self, frame_rgb: np.ndarray) -> tuple[list[Detection], float]:
        preprocessed = self._prepare_input(frame_rgb)
        start_time = time.perf_counter()
        raw_outputs = self._session.run(None, {self._input_name: preprocessed})
        inference_ms = (time.perf_counter() - start_time) * 1000.0
        detections = self._parse_outputs(raw_outputs)
        detections = self._apply_nms(detections)
        return detections, inference_ms

    def _prepare_input(self, frame_rgb: np.ndarray) -> np.ndarray:
        resized = cv2.resize(frame_rgb, (self._input_width, self._input_height), interpolation=cv2.INTER_LINEAR)
        normalized = resized.astype(np.float32) / 255.0
        chw = np.transpose(normalized, (2, 0, 1))
        return np.expand_dims(chw, axis=0)

    def _parse_outputs(self, raw_outputs: list[np.ndarray]) -> list[Detection]:
        if not raw_outputs:
            return []

        if self._config.output_tensor_name:
            output_names = [meta.name for meta in self._session.get_outputs()]
            try:
                target_index = output_names.index(self._config.output_tensor_name)
                raw_tensor = np.asarray(raw_outputs[target_index], dtype=np.float32)
            except ValueError:
                raw_tensor = max((np.asarray(output, dtype=np.float32) for output in raw_outputs), key=lambda item: item.size)
        else:
            raw_tensor = max((np.asarray(output, dtype=np.float32) for output in raw_outputs), key=lambda item: item.size)

        tensor_data = raw_tensor.reshape(-1)
        tensor_shape = [int(dimension) for dimension in raw_tensor.shape]

        explicit = self._try_parse_explicit_detections(tensor_data)
        if explicit:
            return explicit

        shape_inferred = self._try_parse_yolo_tensor_from_shape(tensor_data, tensor_shape)
        if shape_inferred:
            return shape_inferred

        class_count = max(1, len(self._config.class_labels))
        feature_sizes = [(class_count + 5, True), (class_count + 4, False)]

        best: list[Detection] = []
        for feature_size, has_objectness in feature_sizes:
            for feature_major in (False, True):
                parsed = self._try_parse_yolo_tensor(tensor_data, feature_size, has_objectness, feature_major)
                if len(parsed) > len(best):
                    best = parsed

        return best

    def _try_parse_explicit_detections(self, tensor_data: np.ndarray) -> list[Detection]:
        if tensor_data.size < 6 or tensor_data.size % 6 != 0:
            return []

        detections: list[Detection] = []
        for index in range(tensor_data.size // 6):
            offset = index * 6
            score = float(tensor_data[offset + 4])
            if score < self._config.confidence_threshold:
                continue

            class_index = int(round(float(tensor_data[offset + 5])))
            rect = self._resolve_box_rect(
                float(tensor_data[offset + 0]),
                float(tensor_data[offset + 1]),
                float(tensor_data[offset + 2]),
                float(tensor_data[offset + 3]),
                assume_corners=True,
            )
            if rect is None:
                continue

            detections.append(self._build_detection(rect, score, class_index))
        return detections

    def _try_parse_yolo_tensor_from_shape(self, tensor_data: np.ndarray, tensor_shape: list[int]) -> list[Detection]:
        non_trivial = [dimension for dimension in tensor_shape if dimension > 1]
        if len(non_trivial) < 2:
            return []

        attempted: set[tuple[int, int, bool, bool]] = set()
        best: list[Detection] = []
        for first_index in range(len(non_trivial) - 1):
            for second_index in range(first_index + 1, len(non_trivial)):
                first_dimension = non_trivial[first_index]
                second_dimension = non_trivial[second_index]
                for feature_size, candidate_count, feature_major in (
                    (first_dimension, second_dimension, True),
                    (second_dimension, first_dimension, False),
                ):
                    if feature_size <= 5 or candidate_count <= 0:
                        continue
                    if feature_size * candidate_count != tensor_data.size:
                        continue
                    for has_objectness in (True, False):
                        key = (feature_size, candidate_count, feature_major, has_objectness)
                        if key in attempted:
                            continue
                        attempted.add(key)
                        parsed = self._try_parse_yolo_tensor(tensor_data, feature_size, has_objectness, feature_major)
                        if len(parsed) > len(best):
                            best = parsed
        return best

    def _try_parse_yolo_tensor(
        self,
        tensor_data: np.ndarray,
        feature_size: int,
        has_objectness: bool,
        feature_major: bool,
    ) -> list[Detection]:
        if feature_size <= 5 or tensor_data.size < feature_size or tensor_data.size % feature_size != 0:
            return []

        candidate_count = tensor_data.size // feature_size
        detections: list[Detection] = []
        for candidate_index in range(candidate_count):
            center_x = self._read_tensor_value(tensor_data, candidate_count, feature_size, candidate_index, 0, feature_major)
            center_y = self._read_tensor_value(tensor_data, candidate_count, feature_size, candidate_index, 1, feature_major)
            width = self._read_tensor_value(tensor_data, candidate_count, feature_size, candidate_index, 2, feature_major)
            height = self._read_tensor_value(tensor_data, candidate_count, feature_size, candidate_index, 3, feature_major)
            if width <= 0.0 or height <= 0.0:
                continue

            class_start_index = 5 if has_objectness else 4
            objectness = (
                max(0.0, self._read_tensor_value(tensor_data, candidate_count, feature_size, candidate_index, 4, feature_major))
                if has_objectness
                else 1.0
            )
            best_class_score = 0.0
            best_class_index = 0
            for class_index in range(feature_size - class_start_index):
                class_score = max(
                    0.0,
                    self._read_tensor_value(
                        tensor_data,
                        candidate_count,
                        feature_size,
                        candidate_index,
                        class_start_index + class_index,
                        feature_major,
                    ),
                )
                if class_score > best_class_score:
                    best_class_score = class_score
                    best_class_index = class_index

            score = objectness * best_class_score
            if score < self._config.confidence_threshold:
                continue

            rect = self._resolve_box_rect(center_x, center_y, width, height, assume_corners=False)
            if rect is None:
                continue

            detections.append(self._build_detection(rect, score, best_class_index))
        return detections

    @staticmethod
    def _read_tensor_value(
        tensor_data: np.ndarray,
        candidate_count: int,
        feature_size: int,
        candidate_index: int,
        feature_index: int,
        feature_major: bool,
    ) -> float:
        if feature_major:
            return float(tensor_data[feature_index * candidate_count + candidate_index])
        return float(tensor_data[candidate_index * feature_size + feature_index])

    def _resolve_box_rect(
        self, x0: float, y0: float, x1: float, y1: float, assume_corners: bool
    ) -> tuple[float, float, float, float] | None:
        if assume_corners:
            looks_like_pixels = max(abs(x0), abs(y0), abs(x1), abs(y1)) > 1.5
            x_min = x0 / self._input_width if looks_like_pixels else x0
            y_min = y0 / self._input_height if looks_like_pixels else y0
            x_max = x1 / self._input_width if looks_like_pixels else x1
            y_max = y1 / self._input_height if looks_like_pixels else y1
            return clamp_normalized_rect(x_min, y_min, x_max - x_min, y_max - y_min)

        looks_like_pixels = max(abs(x0), abs(y0), abs(x1), abs(y1)) > 1.5
        center_x = x0 / self._input_width if looks_like_pixels else x0
        center_y = y0 / self._input_height if looks_like_pixels else y0
        width = x1 / self._input_width if looks_like_pixels else x1
        height = y1 / self._input_height if looks_like_pixels else y1
        return clamp_normalized_rect(center_x - 0.5 * width, center_y - 0.5 * height, width, height)

    def _build_detection(
        self, rect: tuple[float, float, float, float], score: float, class_index: int
    ) -> Detection:
        label = self._config.class_labels[class_index] if 0 <= class_index < len(self._config.class_labels) else "unknown"
        return Detection(
            x=rect[0],
            y=rect[1],
            width=rect[2],
            height=rect[3],
            confidence=float(score),
            class_id=class_index,
            label=label,
        )

    def _apply_nms(self, detections: list[Detection]) -> list[Detection]:
        if len(detections) <= 1:
            return detections

        kept: list[Detection] = []
        ordered = sorted(detections, key=lambda item: item.confidence, reverse=True)
        for candidate in ordered:
            should_keep = True
            for accepted in kept:
                if accepted.label != candidate.label:
                    continue
                if compute_iou(accepted, candidate) >= self._config.nms_iou_threshold:
                    should_keep = False
                    break
            if should_keep:
                kept.append(candidate)
        return kept


class H264FrameWriter:
    def __init__(self, output_path: Path, width: int, height: int, fps: float, crf: int, preset: str) -> None:
        self._output_path = output_path
        self._output_path.parent.mkdir(parents=True, exist_ok=True)
        ffmpeg_executable = imageio_ffmpeg.get_ffmpeg_exe()
        command = [
            ffmpeg_executable,
            "-y",
            "-f",
            "rawvideo",
            "-pix_fmt",
            "bgr24",
            "-s",
            f"{width}x{height}",
            "-r",
            f"{fps:.6f}",
            "-i",
            "-",
            "-an",
            "-c:v",
            "libx264",
            "-preset",
            preset,
            "-crf",
            str(crf),
            "-pix_fmt",
            "yuv420p",
        ]
        if output_path.suffix.lower() == ".h264":
            command.extend(["-f", "h264"])
        command.append(str(output_path))
        self._process = subprocess.Popen(
            command,
            stdin=subprocess.PIPE,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.PIPE,
        )

    def write(self, frame_bgr: np.ndarray) -> None:
        if self._process.stdin is None:
            raise RuntimeError("ffmpeg stdin is not available")
        self._process.stdin.write(frame_bgr.tobytes())

    def close(self) -> None:
        stderr_output = ""
        try:
            if self._process.stdin is not None:
                self._process.stdin.close()
            if self._process.stderr is not None:
                stderr_output = self._process.stderr.read().decode("utf-8", errors="replace")
            return_code = self._process.wait(timeout=10.0)
            if return_code != 0:
                raise RuntimeError(f"ffmpeg exited with {return_code}: {stderr_output.strip()}")
        finally:
            if self._process.stderr is not None:
                self._process.stderr.close()


def serialize_frame(message_type: int, payload: bytes) -> bytes:
    payload = payload or b""
    header = HEADER_STRUCT.pack(
        MAGIC,
        HEADER_VERSION,
        message_type,
        len(payload),
        zlib.crc32(payload) & 0xFFFFFFFF,
    )
    return header + payload


def read_frame(sock: socket.socket) -> tuple[int, bytes]:
    header_bytes = read_exact(sock, HEADER_STRUCT.size)
    magic, version, message_type, payload_length, expected_crc = HEADER_STRUCT.unpack(header_bytes)
    if magic != MAGIC:
        raise RuntimeError("invalid_magic")
    if version != HEADER_VERSION:
        raise RuntimeError("unsupported_header_version")
    payload = read_exact(sock, payload_length)
    actual_crc = zlib.crc32(payload) & 0xFFFFFFFF
    if actual_crc != expected_crc:
        raise RuntimeError("crc_mismatch")
    return message_type, payload


def read_exact(sock: socket.socket, size: int) -> bytes:
    buffer = bytearray()
    while len(buffer) < size:
        chunk = sock.recv(size - len(buffer))
        if not chunk:
            raise RuntimeError("stream_closed")
        buffer.extend(chunk)
    return bytes(buffer)


def parse_common_response_prefix(payload: bytes) -> tuple[bool, str]:
    reader = BufferReader(payload)
    return reader.read_bool(), reader.read_string()


def parse_info_response(payload: bytes) -> InfoResponse:
    reader = BufferReader(payload)
    success = reader.read_bool()
    error = reader.read_string()
    protocol_version = reader.read_string()
    dt = reader.read_float32()
    control_hz = reader.read_float32()
    action_semantics = reader.read_string()
    _ = reader.read_string_array()
    _ = reader.read_string_array()
    _ = reader.read_string_array()
    _ = reader.read_string_array()
    _ = reader.read_string_array()
    _ = reader.read_bool()
    _ = reader.read_bool()
    camera_count = reader.read_int32()
    cameras: list[CameraDescriptor] = []
    for _index in range(camera_count):
        cameras.append(
            CameraDescriptor(
                name=reader.read_string(),
                width=reader.read_int32(),
                height=reader.read_int32(),
                fps=reader.read_float32(),
                pixel_format=reader.read_string(),
                row_order=reader.read_string(),
            )
        )
    warnings = reader.read_string_array()
    return InfoResponse(
        success=success,
        error=error,
        protocol_version=protocol_version,
        dt=dt,
        control_hz=control_hz,
        action_semantics=action_semantics,
        warnings=warnings,
        cameras=cameras,
    )


def parse_step_response(payload: bytes) -> StepResponse:
    reader = BufferReader(payload)
    success = reader.read_bool()
    error = reader.read_string()
    step_id = reader.read_int64()
    qpos = reader.read_float_array()
    qvel = reader.read_float_array()
    env_state = reader.read_float_array()
    image_format = reader.read_string()
    width = reader.read_int32()
    height = reader.read_int32()
    image_bytes = reader.read_bytes()
    reward = reader.read_float32()
    sim_time_ns = reader.read_int64()
    warnings = reader.read_string_array()

    image_rgb: np.ndarray | None = None
    if image_bytes and width > 0 and height > 0:
        image_rgb = np.frombuffer(image_bytes, dtype=np.uint8).reshape(height, width, 3).copy()

    return StepResponse(
        success=success,
        error=error,
        step_id=step_id,
        qpos=qpos,
        qvel=qvel,
        env_state=env_state,
        image_rgb=image_rgb,
        image_format=image_format,
        reward=reward,
        sim_time_ns=sim_time_ns,
        warnings=warnings,
    )


def clamp_normalized_rect(x: float, y: float, width: float, height: float) -> tuple[float, float, float, float] | None:
    x_min = max(0.0, min(1.0, x))
    y_min = max(0.0, min(1.0, y))
    x_max = max(0.0, min(1.0, x + width))
    y_max = max(0.0, min(1.0, y + height))
    clamped_width = x_max - x_min
    clamped_height = y_max - y_min
    if clamped_width <= 0.0 or clamped_height <= 0.0:
        return None
    return x_min, y_min, clamped_width, clamped_height


def compute_iou(first: Detection, second: Detection) -> float:
    left = max(first.x, second.x)
    top = max(first.y, second.y)
    right = min(first.x + first.width, second.x + second.width)
    bottom = min(first.y + first.height, second.y + second.height)
    intersection_width = max(0.0, right - left)
    intersection_height = max(0.0, bottom - top)
    intersection = intersection_width * intersection_height
    if intersection <= 0.0:
        return 0.0
    first_area = first.width * first.height
    second_area = second.width * second.height
    union = first_area + second_area - intersection
    if union <= 0.0:
        return 0.0
    return intersection / union


def compute_motion_intensity(qvel: Sequence[float], max_expected_joint_velocity: float) -> float:
    qvel_values = list(qvel) + [0.0] * max(0, 4 - len(qvel))
    weighted = (
        abs(qvel_values[0]) * 0.6
        + abs(qvel_values[1]) * 0.15
        + abs(qvel_values[2]) * 0.15
        + abs(qvel_values[3]) * 0.1
    )
    denominator = max(1.0e-5, float(max_expected_joint_velocity))
    return max(0.0, min(1.0, weighted / denominator))


def draw_overlay(
    frame_bgr: np.ndarray,
    detections: Sequence[Detection],
    step_response: StepResponse | None,
    inference_ms: float,
    motion_intensity: float,
    fps: float,
) -> None:
    for detection in detections:
        color = color_for_label(detection.label)
        x0 = int(round(detection.x * frame_bgr.shape[1]))
        y0 = int(round(detection.y * frame_bgr.shape[0]))
        x1 = int(round((detection.x + detection.width) * frame_bgr.shape[1]))
        y1 = int(round((detection.y + detection.height) * frame_bgr.shape[0]))
        cv2.rectangle(frame_bgr, (x0, y0), (x1, y1), color, 2)
        label_text = f"{detection.label} {detection.confidence:.2f}"
        draw_text_block(frame_bgr, label_text, (x0, max(18, y0 - 8)), color)

    qvel = step_response.qvel if step_response else []
    lines = [
        f"step={step_response.step_id if step_response else -1}  reward={step_response.reward if step_response else 0.0:.3f}",
        f"fps={fps:.2f}  infer_ms={inference_ms:.2f}  rois={len(detections)}",
        f"motion={motion_intensity:.3f}  qvel={[round(value, 3) for value in qvel[:4]]}",
        f"format={step_response.image_format if step_response else 'n/a'}",
    ]
    draw_hud_panel(frame_bgr, lines)


def draw_hud_panel(frame_bgr: np.ndarray, lines: Sequence[str]) -> None:
    padding = 10
    line_height = 22
    width = min(frame_bgr.shape[1] - 20, 520)
    height = padding * 2 + line_height * len(lines)
    cv2.rectangle(frame_bgr, (10, 10), (10 + width, 10 + height), (24, 24, 24), thickness=-1)
    cv2.rectangle(frame_bgr, (10, 10), (10 + width, 10 + height), (180, 180, 180), thickness=1)
    for index, line in enumerate(lines):
        cv2.putText(
            frame_bgr,
            line,
            (20, 10 + padding + line_height * (index + 1) - 6),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.55,
            (255, 255, 255),
            1,
            cv2.LINE_AA,
        )


def draw_text_block(frame_bgr: np.ndarray, text: str, origin: tuple[int, int], color: tuple[int, int, int]) -> None:
    (text_width, text_height), baseline = cv2.getTextSize(text, cv2.FONT_HERSHEY_SIMPLEX, 0.5, 1)
    x, y = origin
    top_left = (x, max(0, y - text_height - baseline - 4))
    bottom_right = (x + text_width + 8, y + 4)
    cv2.rectangle(frame_bgr, top_left, bottom_right, color, thickness=-1)
    cv2.putText(
        frame_bgr,
        text,
        (x + 4, y - 4),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.5,
        (0, 0, 0),
        1,
        cv2.LINE_AA,
    )


def color_for_label(label: str) -> tuple[int, int, int]:
    normalized = (label or "").strip().lower()
    if normalized == "bucket":
        return (40, 40, 220)
    if normalized == "excavator_arm":
        return (0, 140, 255)
    if normalized == "truck":
        return (220, 120, 0)
    if normalized == "container":
        return (220, 220, 0)
    if normalized == "dig_area":
        return (0, 180, 0)
    return (255, 255, 255)


def load_config(config_path: Path) -> RuntimeConfig:
    raw = yaml.safe_load(config_path.read_text(encoding="utf-8")) or {}
    repo_root = config_path.resolve().parents[1]
    return RuntimeConfig(
        protocol=ProtocolConfig(**(raw.get("protocol") or {})),
        detector=DetectorConfig(**(raw.get("detector") or {})),
        output=OutputConfig(**(raw.get("output") or {})),
        repo_root=repo_root,
    )


def resolve_path(repo_root: Path, configured_path: str) -> Path:
    path = Path(configured_path)
    if path.is_absolute():
        return path
    return (repo_root / path).resolve()


def create_csv_writer(csv_path: Path) -> tuple[csv.writer, Any]:
    csv_path.parent.mkdir(parents=True, exist_ok=True)
    handle = csv_path.open("w", newline="", encoding="utf-8")
    writer = csv.writer(handle)
    writer.writerow(
        [
            "frame_index",
            "step_id",
            "sim_time_ns",
            "num_detections",
            "motion_intensity",
            "swing_vel",
            "boom_vel",
            "stick_vel",
            "bucket_vel",
            "inference_time_ms",
            "detections",
        ]
    )
    return writer, handle


def run_self_test(config: RuntimeConfig) -> int:
    detector = OnnxRuntimeRoiDetector(config.detector, config.repo_root)
    base_frame = np.zeros((detector.input_height, detector.input_width, 3), dtype=np.uint8)
    cv2.putText(
        base_frame,
        "ROI external self-test",
        (24, 48),
        cv2.FONT_HERSHEY_SIMPLEX,
        1.0,
        (255, 255, 255),
        2,
        cv2.LINE_AA,
    )

    output_path = with_suffix_insert(resolve_path(config.repo_root, config.output.output_path), "_selftest")
    csv_path = with_suffix_insert(resolve_path(config.repo_root, config.output.csv_log_path), "_selftest")
    csv_writer, csv_handle = create_csv_writer(csv_path)
    frame_writer: H264FrameWriter | None = None

    try:
        for frame_index in range(30):
            frame_rgb = base_frame.copy()
            detections, inference_ms = detector.detect(frame_rgb)
            motion_intensity = 0.0
            frame_bgr = cv2.cvtColor(frame_rgb, cv2.COLOR_RGB2BGR)
            draw_overlay(
                frame_bgr,
                detections,
                StepResponse(
                    success=True,
                    error="",
                    step_id=frame_index,
                    qpos=[0.0, 0.0, 0.0, 0.0],
                    qvel=[0.0, 0.0, 0.0, 0.0],
                    env_state=[],
                    image_rgb=frame_rgb,
                    image_format="raw_rgb",
                    reward=0.0,
                    sim_time_ns=time.time_ns(),
                    warnings=[],
                ),
                inference_ms,
                motion_intensity,
                fps=30.0,
            )

            if frame_writer is None and config.output.encode_video:
                frame_writer = H264FrameWriter(
                    output_path=output_path,
                    width=frame_bgr.shape[1],
                    height=frame_bgr.shape[0],
                    fps=30.0,
                    crf=config.output.h264_crf,
                    preset=config.output.h264_preset,
                )

            if frame_writer is not None:
                frame_writer.write(frame_bgr)

            csv_writer.writerow(
                [
                    frame_index,
                    frame_index,
                    time.time_ns(),
                    len(detections),
                    f"{motion_intensity:.6f}",
                    0.0,
                    0.0,
                    0.0,
                    0.0,
                    f"{inference_ms:.6f}",
                    encode_detections(detections),
                ]
            )

        print(f"Self-test wrote video to {output_path}")
        print(f"Self-test wrote csv to {csv_path}")
        return 0
    finally:
        if frame_writer is not None:
            frame_writer.close()
        csv_handle.close()


def run_runtime(config: RuntimeConfig) -> int:
    detector = OnnxRuntimeRoiDetector(config.detector, config.repo_root)
    client = AgxSimClient(config.protocol)
    output_path = resolve_path(config.repo_root, config.output.output_path)
    csv_path = resolve_path(config.repo_root, config.output.csv_log_path)

    frame_writer: H264FrameWriter | None = None
    csv_writer: csv.writer | None = None
    csv_handle: Any = None

    try:
        client.connect()
        info = client.get_info()
        if not info.success:
            raise RuntimeError(f"GET_INFO failed: {info.error}")
        if config.protocol.reset_on_start:
            client.reset()

        fps = (
            config.output.fps_override
            if config.output.fps_override > 0
            else (info.control_hz if info.control_hz > 0 else 50.0)
        )
        max_steps = config.protocol.max_steps if config.protocol.max_steps > 0 else 2**31 - 1

        for frame_index in range(max_steps):
            response = client.step(frame_index, config.protocol.action)
            if not response.success:
                raise RuntimeError(f"STEP failed at step {frame_index}: {response.error}")
            if response.image_rgb is None:
                print(f"Step {frame_index} returned no image. warnings={response.warnings}")
                continue

            detections, inference_ms = detector.detect(response.image_rgb)
            motion_intensity = compute_motion_intensity(response.qvel, config.detector.max_expected_joint_velocity)
            frame_bgr = cv2.cvtColor(response.image_rgb, cv2.COLOR_RGB2BGR)
            draw_overlay(frame_bgr, detections, response, inference_ms, motion_intensity, fps)

            if csv_writer is None:
                csv_writer, csv_handle = create_csv_writer(csv_path)

            if frame_writer is None and config.output.encode_video:
                frame_writer = H264FrameWriter(
                    output_path=output_path,
                    width=frame_bgr.shape[1],
                    height=frame_bgr.shape[0],
                    fps=fps,
                    crf=config.output.h264_crf,
                    preset=config.output.h264_preset,
                )

            if frame_writer is not None:
                frame_writer.write(frame_bgr)

            csv_writer.writerow(
                [
                    frame_index,
                    response.step_id,
                    response.sim_time_ns,
                    len(detections),
                    f"{motion_intensity:.6f}",
                    float(response.qvel[0]) if len(response.qvel) > 0 else 0.0,
                    float(response.qvel[1]) if len(response.qvel) > 1 else 0.0,
                    float(response.qvel[2]) if len(response.qvel) > 2 else 0.0,
                    float(response.qvel[3]) if len(response.qvel) > 3 else 0.0,
                    f"{inference_ms:.6f}",
                    encode_detections(detections),
                ]
            )

            if config.output.preview:
                preview_frame = frame_bgr
                if abs(config.output.preview_scale - 1.0) > 1.0e-4:
                    preview_frame = cv2.resize(
                        frame_bgr,
                        (0, 0),
                        fx=config.output.preview_scale,
                        fy=config.output.preview_scale,
                        interpolation=cv2.INTER_LINEAR,
                    )
                cv2.imshow(config.output.preview_window_name, preview_frame)
                key = cv2.waitKey(1) & 0xFF
                if key in (27, ord("q")):
                    print("Preview closed by user.")
                    break

            if config.protocol.step_interval_sec > 0.0:
                time.sleep(config.protocol.step_interval_sec)

        print(f"Runtime wrote video to {output_path}")
        print(f"Runtime wrote csv to {csv_path}")
        return 0
    finally:
        client.close()
        if frame_writer is not None:
            frame_writer.close()
        if csv_handle is not None:
            csv_handle.close()
        if config.output.preview:
            cv2.destroyAllWindows()


def encode_detections(detections: Iterable[Detection]) -> str:
    return ";".join(
        f"{item.label}:{item.confidence:.3f}:{item.x:.3f}:{item.y:.3f}:{item.width:.3f}:{item.height:.3f}"
        for item in detections
    )


def with_suffix_insert(path: Path, suffix: str) -> Path:
    return path.with_name(f"{path.stem}{suffix}{path.suffix}")


def _is_positive_dimension(value: Any) -> bool:
    return isinstance(value, (int, np.integer)) and int(value) > 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run external ROI overlay runtime against AGX STEP_RESP raw_rgb frames.")
    parser.add_argument("--config", type=Path, default=Path("tools/roi_runtime_config.yaml"), help="YAML config path")
    parser.add_argument("--self-test", action="store_true", help="Run a synthetic smoke test without connecting to Unity")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    config_path = args.config.resolve()
    config = load_config(config_path)
    if args.self_test:
        return run_self_test(config)
    return run_runtime(config)


if __name__ == "__main__":
    raise SystemExit(main())
