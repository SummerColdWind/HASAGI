# HASAGI

HASAGI identifies tissue layers and grades nerve discernibility in single-frame corneal in vivo confocal microscopy (IVCM) images. Both ONNX models run offline on the CPU.

## License

The HASAGI Noncommercial License permits use and adaptation of the Python
code and both ONNX models for noncommercial research or teaching. Commercial
use and any redistribution, including sharing copies for free with collaborators
or students, require prior written authorization from the applicable HASAGI
rights holders or their authorized representative. You may share a link to an
authorized download. See `LICENSE` for the full terms and `THIRD_PARTY_NOTICES`
for upstream licenses and attribution.

## Installation

Requires Python 3.10 or later.

```bash
# From packages/hasagi
python -m pip install .
```

Install the published beta from PyPI with `python -m pip install hasagi==0.1.0b1`.

## Usage

Create one predictor and reuse it across images. The constructor loads both models. The optional `num_threads` argument sets the number of CPU threads available to each ONNX session for computing an operator (default 4).

```python
from PIL import Image
from hasagi import HASAGIPredictor

predictor = HASAGIPredictor(num_threads=4)
with Image.open("frame.png") as image:
    result = predictor.predict(image, resize_mode="resize")

print(result.labels, result.grade, result.score)
```

For a 384 × 384 frame, either task can also run on its own.

```python
with Image.open("frame.png") as image:
    layers = predictor.predict(image, predict_score=False)
    score = predictor.predict(image, predict_layers=False)
```

## Batch inference

`predict_batch()` accepts a list, tuple, or generator of images. Each enabled model runs once per batch. The final batch can be smaller. To process a directory one batch at a time, reuse the predictor above.

```python
from pathlib import Path

def read_images(paths):
    for path in paths:
        with Image.open(path) as image:
            yield image

paths = sorted(Path("frames").glob("*.png"))
results = predictor.predict_batch(read_images(paths), batch_size=8, resize_mode="resize")
for path, result in zip(paths, results):
    print(path.name, result.labels, result.grade, result.score)
```

## Prediction parameters

| Parameter | Default | Behavior |
| --- | --- | --- |
| `image` / `images` | Required | Pass one PIL image or NumPy array to `predict()`, or an iterable of images to `predict_batch()`. |
| `batch_size` | `8` | Maximum images per model call in `predict_batch()`. Larger batches use more memory. |
| `predict_layers` | `True` | Identify all matching layers. |
| `predict_score` | `True` | Grade nerve discernibility regardless of the predicted layers. |
| `resize_mode` | `"crop"` | `"crop"` takes the top-left 384 × 384 pixels and requires both dimensions to be at least 384. `"resize"` scales the whole image to 384 × 384 using bilinear interpolation. |

Pass options by keyword. Both `num_threads` and `batch_size` must be positive integers. Disabling both tasks raises `ValueError`. For an invalid image, the batch error identifies its zero-based index.

NumPy arrays use `(H, W)` or channels-last grayscale, RGB, or RGBA layout. Supply `uint8` pixels, finite floats in `[0, 1]`, or `uint16` grayscale (values above 255 are clipped).

## Return value

`predict()` returns a `HASAGIPrediction` dataclass. `predict_batch()` returns a list of these results in input order, or `[]` for empty input. Fields for a disabled task are `None`.

| Field | Type when enabled | Meaning |
| --- | --- | --- |
| `labels` | `tuple[str, ...]` | Layers with probability ≥ 0.5, or `("other",)` if none reach the threshold. |
| `layer_probabilities` | `dict[str, float]` | Independent probabilities for `sbnp` (subbasal nerve plexus), `epithelium`, `stroma`, and `other`. |
| `score` | `float` | Nerve discernibility on a 0–100 scale. Higher scores indicate clearer nerves. |
| `grade` | `str` | One of `poor`, `fair`, `good`, or `excellent`. |
| `grade_probabilities` | `dict[str, float]` | Probability of each of the four grades. |
