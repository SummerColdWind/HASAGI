from collections.abc import Iterable, Iterator
from dataclasses import dataclass
from itertools import islice
from numbers import Integral
from pathlib import Path
from typing import Literal

import numpy as np
import onnxruntime as ort
from PIL import Image

_SIDE = 384
_LAYERS = ("sbnp", "epithelium", "stroma", "other")
_GRADES = ("poor", "fair", "good", "excellent")
_MEAN = np.array([0.485, 0.456, 0.406], dtype=np.float32)[:, None, None]
_STD = np.array([0.229, 0.224, 0.225], dtype=np.float32)[:, None, None]


@dataclass(frozen=True)
class HASAGIPrediction:
    """Selected layers, a 0–100 discernibility score, and named probabilities.

    Fields for a disabled prediction task are None.
    """

    labels: tuple[str, ...] | None
    layer_probabilities: dict[str, float] | None
    score: float | None
    grade: str | None
    grade_probabilities: dict[str, float] | None


def _from_array(array: np.ndarray) -> Image.Image:
    if array.ndim == 3 and array.shape[2] == 1:
        array = array[:, :, 0]
    if array.ndim != 2 and not (array.ndim == 3 and array.shape[2] in (3, 4)):
        raise ValueError("Expected a grayscale, RGB or RGBA array in H×W or H×W×C layout.")
    if array.size == 0:
        raise ValueError("Expected a nonempty image.")
    if np.issubdtype(array.dtype, np.floating):
        if not np.isfinite(array).all() or array.min() < 0 or array.max() > 1:
            raise ValueError("Floating-point images must contain finite values in [0, 1].")
        array = np.rint(array * 255).astype(np.uint8)
    elif array.ndim == 2 and array.dtype.kind == "u" and array.dtype.itemsize == 2:
        array = array.clip(0, 255).astype(np.uint8)
    elif array.dtype != np.uint8:
        raise ValueError("Expected uint8, uint16 grayscale, or floating-point pixels in [0, 1].")
    return Image.fromarray(array)


def _model_input(image: Image.Image | np.ndarray, resize_mode: str) -> np.ndarray:
    if isinstance(image, np.ndarray):
        image = _from_array(image)
    elif not isinstance(image, Image.Image):
        raise TypeError("image must be a PIL.Image.Image or a numpy.ndarray.")
    if getattr(image, "n_frames", 1) != 1 or min(image.size) < 1:
        raise ValueError("Expected a nonempty, single-frame image.")
    if image.mode == "F":
        image = _from_array(np.asarray(image))
    if resize_mode == "crop":
        if min(image.size) < _SIDE:
            raise ValueError("crop requires both image dimensions to be at least 384 pixels.")
        if image.size != (_SIDE, _SIDE):
            image = image.crop((0, 0, _SIDE, _SIDE))
    gray = image.convert("L")
    if gray.size != (_SIDE, _SIDE):
        gray = gray.resize((_SIDE, _SIDE), Image.Resampling.BILINEAR)
    pixels = np.asarray(gray, dtype=np.float32) / 255.0
    return (pixels - _MEAN) / _STD


def _inputs(images: Iterable[Image.Image | np.ndarray], resize_mode: str) -> Iterator[np.ndarray]:
    iterator = iter(images)
    index = 0
    while True:
        try:
            image = next(iterator)
            pixels = _model_input(image, resize_mode)
        except StopIteration:
            return
        except (TypeError, ValueError, OSError) as exc:
            raise type(exc)(f"images[{index}]: {exc}") from exc
        yield pixels
        index += 1


def _positive_integer(value: int, name: str) -> int:
    if isinstance(value, bool) or not isinstance(value, Integral) or value < 1:
        raise ValueError(f"{name} must be a positive integer.")
    return int(value)


def _logits(session: ort.InferenceSession, pixels: np.ndarray) -> np.ndarray:
    output = session.run(["logits"], {"images": pixels})[0]
    if output.shape != (len(pixels), 4) or not np.isfinite(output).all():
        raise ValueError("Expected four finite logits per image from the HASAGI model.")
    return output.astype(np.float64)


def _prediction(layer_logits: np.ndarray | None, grade_logits: np.ndarray | None) -> HASAGIPrediction:
    labels = layer_probabilities = None
    if layer_logits is not None:
        probabilities = np.exp(-np.logaddexp(0, -layer_logits))
        layer_probabilities = dict(zip(_LAYERS, probabilities.tolist()))
        labels = tuple(name for name, p in layer_probabilities.items() if p >= 0.5) or ("other",)

    score = grade = grade_probabilities = None
    if grade_logits is not None:
        weights = np.exp(grade_logits - grade_logits.max())
        probabilities = weights / weights.sum()
        grade_probabilities = dict(zip(_GRADES, probabilities.tolist()))
        grade = _GRADES[grade_logits.argmax()]
        score = float(np.dot(probabilities, (0, 1, 2, 3)) * 100 / 3)
    return HASAGIPrediction(labels, layer_probabilities, score, grade, grade_probabilities)


class HASAGIPredictor:
    """Load both bundled ONNX models once and reuse their CPU sessions."""

    def __init__(self, *, num_threads: int = 4) -> None:
        models = Path(__file__).parent / "models"
        options = ort.SessionOptions()
        options.intra_op_num_threads = _positive_integer(num_threads, "num_threads")
        options.inter_op_num_threads = 1
        self._layers = ort.InferenceSession(
            str(models / "layers_fp32.onnx"), options, providers=["CPUExecutionProvider"]
        )
        self._grades = ort.InferenceSession(
            str(models / "discernibility_fp32.onnx"), options, providers=["CPUExecutionProvider"]
        )

    def predict(
        self,
        image: Image.Image | np.ndarray,
        *,
        predict_layers: bool = True,
        predict_score: bool = True,
        resize_mode: Literal["crop", "resize"] = "crop",
    ) -> HASAGIPrediction:
        """Run each enabled model independently on a single image.

        Arrays use grayscale, RGB or RGBA order. Supply uint8 pixels, uint16
        grayscale (clipped to 255), or floating-point pixels in [0, 1].
        Crop takes the upper-left 384×384 pixels. Resize uses bilinear
        interpolation over the whole image. Enable at least one task.
        """
        return self.predict_batch(
            (image,), batch_size=1, predict_layers=predict_layers,
            predict_score=predict_score, resize_mode=resize_mode,
        )[0]

    def predict_batch(
        self,
        images: Iterable[Image.Image | np.ndarray],
        *,
        batch_size: int = 8,
        predict_layers: bool = True,
        predict_score: bool = True,
        resize_mode: Literal["crop", "resize"] = "crop",
    ) -> list[HASAGIPrediction]:
        """Process successive batches and return one result per image in input order."""
        batch_size = _positive_integer(batch_size, "batch_size")
        if not (predict_layers or predict_score):
            raise ValueError("Enable at least one of predict_layers or predict_score.")
        if resize_mode not in ("crop", "resize"):
            raise ValueError("resize_mode must be 'crop' or 'resize'.")
        inputs = _inputs(images, resize_mode)
        results = []
        while batch := list(islice(inputs, batch_size)):
            pixels = np.stack(batch)
            layers = _logits(self._layers, pixels) if predict_layers else [None] * len(batch)
            grades = _logits(self._grades, pixels) if predict_score else [None] * len(batch)
            results.extend(_prediction(layer, grade) for layer, grade in zip(layers, grades))
        return results
