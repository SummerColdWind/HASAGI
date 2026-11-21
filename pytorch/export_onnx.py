"""Export the two native checkpoints as FP32 ONNX models with dynamic batches."""

import argparse
from pathlib import Path
from tempfile import TemporaryDirectory

import onnx
import torch

from model import TASKS, load_model


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", required=True, type=Path)
    output = parser.parse_args().output_dir
    output.mkdir(parents=True, exist_ok=True)
    torch.set_num_threads(4)
    with TemporaryDirectory(dir=output) as tmp, torch.inference_mode():
        for task in TASKS:
            path = Path(tmp) / f"{task}_fp32.onnx"
            torch.onnx.export(
                load_model(task), torch.zeros(2, 3, 384, 384), str(path),
                opset_version=17, dynamo=False, input_names=["images"], output_names=["logits"],
                dynamic_axes={"images": {0: "batch"}, "logits": {0: "batch"}},
            )
            onnx.checker.check_model(str(path))
        for path in Path(tmp).glob("*.onnx"):
            path.replace(output / path.name)


if __name__ == "__main__":
    main()
