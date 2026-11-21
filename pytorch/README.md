# Native PyTorch models

The two CPU-loadable checkpoints in [weights](weights/) contain state
dictionaries for the four-stage classifier in [model.py](model.py). Each
state dictionary contains the trained parameters and buffers for one task.
Loading a model uses the bundled weights and requires no network access.

## Run inference

From the repository root, install the native dependencies and pass one or
more single-frame 384 × 384 image files. The command writes one JSON record
per image with layer labels, grade, score and both sets of probabilities.

```bash
python -m pip install -r pytorch/requirements.txt
python pytorch/infer.py frame.png
```

For tensor input, use `load_model("layers")` or `load_model("discernibility")`
from `pytorch.model`. Both accept float32 tensors of shape `(N, 3, 384, 384)`
and return four logits per image. The native image reader converts pixels to
grayscale, scales them to [0, 1], and normalizes the three channels with means
(0.485, 0.456, 0.406) and standard deviations (0.229, 0.224, 0.225).

Layer logits use independent sigmoid probabilities in the order SBNP,
epithelium, stroma and other. Grading logits use softmax in the order poor,
fair, good and excellent. The continuous score is the expected grade index
on a 0–100 scale.

## Export ONNX

```bash
python pytorch/export_onnx.py --output-dir build/onnx
```

The exporter checks each graph and writes `layers_fp32.onnx` and
`discernibility_fp32.onnx`. Both use opset 17 and a dynamic batch dimension,
with inputs named `images` and outputs named `logits`. The Python package
and Windows build use the reference exports in
[the package model directory](../packages/hasagi/src/hasagi/models/).

## Reference weights for 0.1.0b1

| File | SHA-256 |
| --- | --- |
| `weights/layers.pt` | `622b2a36776ab9929c652879740b0021b50e17a37702f728622d65b89b9bc491` |
| `weights/discernibility.pt` | `9e55f0a505396782550a7de9e950ac5ad1a740f100207613f9e28a8dfed778de` |
| `layers_fp32.onnx` (package) | `3c8980dc120b97abe1ae1b3efe2c97cadcb7e5037b8d746653d021cb9637f380` |
| `discernibility_fp32.onnx` (package) | `feff147d93b983c57aff84dc34c77b0b9972ad10907ee5a937f6c80d8bbf805b` |
