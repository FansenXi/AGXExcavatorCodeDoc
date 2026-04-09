from pathlib import Path

import onnx
from onnx import TensorProto, helper


def build_model(output_path: Path) -> None:
    input_tensor = helper.make_tensor_value_info(
        "images",
        TensorProto.FLOAT,
        [1, 3, 576, 576],
    )
    output_tensor = helper.make_tensor_value_info(
        "detections",
        TensorProto.FLOAT,
        [1, 6, 1, 1],
    )

    conv_weights = helper.make_tensor(
        name="smoke_conv_w",
        data_type=TensorProto.FLOAT,
        dims=[6, 3, 1, 1],
        vals=[0.0] * (6 * 3),
    )
    conv_bias = helper.make_tensor(
        name="smoke_conv_b",
        data_type=TensorProto.FLOAT,
        dims=[6],
        vals=[
            0.18,  # x_min
            0.22,  # y_min
            0.72,  # x_max
            0.78,  # y_max
            0.99,  # confidence
            0.0,   # class index (bucket)
        ],
    )

    nodes = [
        helper.make_node(
            "GlobalAveragePool",
            inputs=["images"],
            outputs=["pooled"],
            name="SmokePool",
        ),
        helper.make_node(
            "Conv",
            inputs=["pooled", "smoke_conv_w", "smoke_conv_b"],
            outputs=["detections"],
            name="SmokeConv",
            kernel_shape=[1, 1],
            strides=[1, 1],
        ),
    ]

    graph = helper.make_graph(
        nodes=nodes,
        name="RoiSmokeDetector",
        inputs=[input_tensor],
        outputs=[output_tensor],
        initializer=[conv_weights, conv_bias],
    )

    model = helper.make_model(
        graph,
        producer_name="roi_smoke_generator",
        opset_imports=[helper.make_operatorsetid("", 11)],
    )
    model.ir_version = 7
    onnx.checker.check_model(model)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    onnx.save(model, output_path)


if __name__ == "__main__":
    repo_root = Path(__file__).resolve().parents[1]
    project_root = repo_root.parents[1]
    output_file = project_root / "_model_archive" / "roi_smoke_detector.onnx"
    build_model(output_file)
    print(output_file)
