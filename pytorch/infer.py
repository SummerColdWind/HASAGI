"""Run both native HASAGI models on single-frame 384 x 384 images."""

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image
import torch

from model import load_model

LAYERS = ("sbnp", "epithelium", "stroma", "other")
GRADES = ("poor", "fair", "good", "excellent")


def read_image(path):
    with Image.open(path) as image:
        if image.size != (384, 384) or getattr(image, "n_frames", 1) != 1:
            raise ValueError(f"{path}: expected one 384 x 384 frame")
        if image.mode == "F":
            values = np.asarray(image)
            if not np.isfinite(values).all() or values.min() < 0 or values.max() > 1:
                raise ValueError(f"{path}: expected finite floating-point intensities in [0, 1]")
            pixels = np.rint(values * 255).astype(np.float32)
        else:
            pixels = np.array(image.convert("L"), dtype=np.float32)
    gray = torch.from_numpy(pixels) / 255
    mean = torch.tensor([0.485, 0.456, 0.406])[:, None, None]
    std = torch.tensor([0.229, 0.224, 0.225])[:, None, None]
    return ((gray - mean) / std).unsqueeze(0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("images", type=Path, nargs="+")
    args = parser.parse_args()
    torch.set_num_threads(4)
    layers = load_model("layers")
    grades = load_model("discernibility")
    with torch.inference_mode():
        for path in args.images:
            x = read_image(path)
            p = layers(x)[0].double().sigmoid()
            g = grades(x)[0].double().softmax(0)
            print(json.dumps({
                "image": str(path),
                "labels": [name for name, value in zip(LAYERS, p) if value >= 0.5] or ["other"],
                "layer_probabilities": dict(zip(LAYERS, p.tolist())),
                "grade": GRADES[g.argmax().item()],
                "grade_probabilities": dict(zip(GRADES, g.tolist())),
                "score": (g[1] + 2 * g[2] + 3 * g[3]).item() * 100 / 3,
            }))


if __name__ == "__main__":
    main()
