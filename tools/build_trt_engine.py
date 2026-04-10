"""Build a TensorRT FP16 engine from an ONNX model.

Usage:
    python tools/build_trt_engine.py \
        --onnx ../../_model_archive/roi_detector_640.onnx \
        --output ../../_model_archive/roi_detector.engine \
        --fp16

Requires: tensorrt (pip install tensorrt) or trtexec on PATH.
"""
from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path


def build_with_python_api(onnx_path: Path, output_path: Path, *, fp16: bool, int8: bool) -> None:
    try:
        import tensorrt as trt
    except ImportError:
        print("ERROR: tensorrt Python package not found. Install via pip or use --use-trtexec.", file=sys.stderr)
        raise SystemExit(1)

    logger = trt.Logger(trt.Logger.INFO)
    builder = trt.Builder(logger)
    network = builder.create_network(1 << int(trt.NetworkDefinitionCreationFlag.EXPLICIT_BATCH))
    parser = trt.OnnxParser(network, logger)

    with open(onnx_path, "rb") as f:
        if not parser.parse(f.read()):
            for i in range(parser.num_errors):
                print(f"ONNX parse error: {parser.get_error(i)}", file=sys.stderr)
            raise SystemExit(1)

    config = builder.create_builder_config()
    config.set_memory_pool_limit(trt.MemoryPoolType.WORKSPACE, 1 << 30)

    if fp16:
        if not builder.platform_has_fast_fp16:
            print("WARNING: Platform does not have fast FP16 support.", file=sys.stderr)
        config.set_flag(trt.BuilderFlag.FP16)

    if int8:
        if not builder.platform_has_fast_int8:
            print("WARNING: Platform does not have fast INT8 support.", file=sys.stderr)
        config.set_flag(trt.BuilderFlag.INT8)

    print(f"Building TensorRT engine from {onnx_path} ...")
    print(f"  FP16={fp16}, INT8={int8}")

    serialized = builder.build_serialized_network(network, config)
    if serialized is None:
        print("ERROR: Engine build failed.", file=sys.stderr)
        raise SystemExit(1)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "wb") as f:
        f.write(serialized)

    size_mb = output_path.stat().st_size / (1024 * 1024)
    print(f"Engine written to {output_path} ({size_mb:.1f} MB)")


def build_with_trtexec(onnx_path: Path, output_path: Path, *, fp16: bool, int8: bool) -> None:
    trtexec = shutil.which("trtexec")
    if trtexec is None:
        trt_home = os.environ.get("TENSORRT_DIR", "")
        candidate = Path(trt_home) / "bin" / "trtexec.exe" if os.name == "nt" else Path(trt_home) / "bin" / "trtexec"
        if candidate.is_file():
            trtexec = str(candidate)
        else:
            print("ERROR: trtexec not found on PATH or in TENSORRT_DIR/bin.", file=sys.stderr)
            raise SystemExit(1)

    output_path.parent.mkdir(parents=True, exist_ok=True)

    cmd = [
        trtexec,
        f"--onnx={onnx_path}",
        f"--saveEngine={output_path}",
        "--workspace=1024",
    ]
    if fp16:
        cmd.append("--fp16")
    if int8:
        cmd.append("--int8")

    print(f"Running: {' '.join(cmd)}")
    result = subprocess.run(cmd, check=False)
    if result.returncode != 0:
        print(f"ERROR: trtexec failed with exit code {result.returncode}", file=sys.stderr)
        raise SystemExit(result.returncode)

    size_mb = output_path.stat().st_size / (1024 * 1024)
    print(f"Engine written to {output_path} ({size_mb:.1f} MB)")


def main() -> int:
    parser = argparse.ArgumentParser(description="Build TensorRT engine from ONNX model.")
    parser.add_argument("--onnx", type=Path, required=True, help="Path to input ONNX model.")
    parser.add_argument("--output", type=Path, required=True, help="Path to output .engine file.")
    parser.add_argument("--fp16", action="store_true", help="Enable FP16 precision.")
    parser.add_argument("--int8", action="store_true", help="Enable INT8 precision (requires calibration).")
    parser.add_argument("--use-trtexec", action="store_true",
                        help="Use trtexec CLI instead of Python API.")
    args = parser.parse_args()

    if not args.onnx.is_file():
        print(f"ERROR: ONNX file not found: {args.onnx}", file=sys.stderr)
        return 1

    if args.use_trtexec:
        build_with_trtexec(args.onnx, args.output, fp16=args.fp16, int8=args.int8)
    else:
        build_with_python_api(args.onnx, args.output, fp16=args.fp16, int8=args.int8)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
