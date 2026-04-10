"""Re-export an existing YOLO checkpoint to ONNX at a specified resolution.

Usage:
    python tools/export_roi_onnx.py \
        --model ../../ROI_Runs/roi_yolo11n/weights/best.pt \
        --imgsz 640 \
        --output ../../_model_archive/roi_detector_640.onnx
"""
from __future__ import annotations

import argparse
import shutil
from pathlib import Path

from ultralytics import YOLO


def main() -> int:
    parser = argparse.ArgumentParser(description="Export YOLO checkpoint to ONNX.")
    parser.add_argument("--model", type=Path, required=True, help="Path to .pt checkpoint.")
    parser.add_argument("--imgsz", type=int, default=640, help="Export image size (square).")
    parser.add_argument("--opset", type=int, default=17, help="ONNX opset version.")
    parser.add_argument("--output", type=Path, default=None,
                        help="Destination path for .onnx file. If omitted, uses Ultralytics default location.")
    args = parser.parse_args()

    if not args.model.is_file():
        print(f"ERROR: Model not found: {args.model}")
        return 1

    model = YOLO(str(args.model))
    exported = model.export(format="onnx", opset=args.opset, imgsz=args.imgsz)
    exported_path = Path(exported)
    print(f"Exported ONNX: {exported_path}")

    if args.output is not None:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(exported_path, args.output)
        print(f"Copied to: {args.output}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
