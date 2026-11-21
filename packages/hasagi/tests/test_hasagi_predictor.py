from unittest.mock import patch

import numpy as np
import pytest
from PIL import Image

from hasagi import HASAGIPrediction, HASAGIPredictor


@pytest.fixture(scope="module")
def predictor():
    return HASAGIPredictor()


@pytest.fixture(scope="module")
def rgb():
    return np.random.default_rng(42).integers(0, 256, (420, 460, 3), dtype=np.uint8)


def test_independent_predictions(predictor, rgb):
    both = predictor.predict(rgb)
    assert isinstance(both, HASAGIPrediction)
    assert set(both.layer_probabilities) == {"sbnp", "epithelium", "stroma", "other"}
    assert all(0 <= p <= 1 for p in both.layer_probabilities.values())
    assert set(both.grade_probabilities) == {"poor", "fair", "good", "excellent"}
    assert sum(both.grade_probabilities.values()) == pytest.approx(1)
    assert both.grade == max(both.grade_probabilities, key=both.grade_probabilities.get)
    p = both.grade_probabilities
    assert both.score == pytest.approx(100 * (p["fair"] + 2*p["good"] + 3*p["excellent"]) / 3)

    with patch.object(predictor._grades, "run", side_effect=AssertionError("grade model ran")):
        layers = predictor.predict(rgb, predict_score=False)
    assert layers == HASAGIPrediction(both.labels, both.layer_probabilities, None, None, None)
    with patch.object(predictor._layers, "run", side_effect=AssertionError("layer model ran")):
        score = predictor.predict(rgb, predict_layers=False)
    assert score == HASAGIPrediction(None, None, both.score, both.grade, both.grade_probabilities)
    with pytest.raises(ValueError, match="at least one"):
        predictor.predict(rgb, predict_layers=False, predict_score=False)


@pytest.mark.parametrize(("logits", "labels"), [
    ([-2, -2, -2, -2], ("other",)),
    ([0, -2, 2, -2], ("sbnp", "stroma")),
])
def test_score_also_runs_for_other_and_mixed_layers(predictor, rgb, logits, labels):
    output = np.array([logits], dtype=np.float32)
    with patch.object(predictor._layers, "run", return_value=[output]):
        result = predictor.predict(rgb)
    assert result.labels == labels
    assert 0 <= result.score <= 100


def test_crop_uses_top_left(predictor, rgb):
    assert predictor.predict(rgb) == predictor.predict(Image.fromarray(rgb[:384, :384]))


def test_resize_uses_whole_image(predictor, rgb):
    image = Image.fromarray(rgb[:160])
    expected = image.convert("L").resize((384, 384), Image.Resampling.BILINEAR)
    assert predictor.predict(image, resize_mode="resize") == predictor.predict(expected)


def test_grayscale_array_formats(predictor, rgb):
    gray = rgb[:384, :384, 0]
    expected = predictor.predict(gray)
    assert predictor.predict(gray[..., None]) == expected
    assert predictor.predict(gray.astype(np.float32) / 255) == expected
    wide = gray.astype(np.uint16)
    wide[gray == 255] = 1024
    assert predictor.predict(wide) == expected


@pytest.mark.parametrize(("image", "mode", "error"), [
    (np.zeros((383, 400), np.uint8), "crop", "at least 384"),
    (np.zeros((400, 383), np.uint8), "crop", "at least 384"),
    (np.zeros((384, 384), np.uint8), "pad", "resize_mode"),
    (np.empty((0, 400), np.uint8), "resize", "nonempty"),
    (np.zeros((3, 384, 384), np.uint8), "resize", "layout"),
    (np.array([[np.nan]]), "resize", "finite"),
])
def test_invalid_inputs(predictor, image, mode, error):
    with pytest.raises(ValueError, match=error):
        predictor.predict(image, resize_mode=mode)


@pytest.mark.parametrize("predict_layers,predict_score", [(True, True), (True, False), (False, True)])
def test_batch_matches_single_and_runs_once_per_chunk(predictor, predict_layers, predict_score):
    images = [np.full((200 + i, 384), i * 50, dtype=np.uint8) for i in range(5)]
    options = dict(predict_layers=predict_layers, predict_score=predict_score, resize_mode="resize")
    expected = [predictor.predict(im, **options) for im in images]
    with (
        patch.object(predictor._layers, "run", wraps=predictor._layers.run) as layers,
        patch.object(predictor._grades, "run", wraps=predictor._grades.run) as grades,
    ):
        actual = predictor.predict_batch(iter(images), batch_size=2, **options)
    for model, enabled in ((layers, predict_layers), (grades, predict_score)):
        shapes = [call.args[1]["images"].shape[0] for call in model.call_args_list]
        assert shapes == ([2, 2, 1] if enabled else [])
    assert len(actual) == len(expected)
    for a, e in zip(actual, expected):
        assert (a.labels, a.grade) == (e.labels, e.grade)
        if predict_layers:
            assert a.layer_probabilities == pytest.approx(e.layer_probabilities, abs=1e-5)
        else:
            assert a.layer_probabilities is None
        if predict_score:
            assert a.score == pytest.approx(e.score, abs=1e-3)
            assert a.grade_probabilities == pytest.approx(e.grade_probabilities, abs=1e-5)
        else:
            assert a.score is a.grade_probabilities is None


def test_threads_and_empty_batch():
    predictor = HASAGIPredictor(num_threads=2)
    for session in (predictor._layers, predictor._grades):
        assert session.get_session_options().intra_op_num_threads == 2
    assert predictor.predict_batch([]) == []
    with pytest.raises(ValueError, match="at least one"):
        predictor.predict_batch([], predict_layers=False, predict_score=False)
    with pytest.raises(ValueError, match="resize_mode"):
        predictor.predict_batch([], resize_mode="pad")


@pytest.mark.parametrize("value", [False, 0, -1, 1.5])
def test_positive_integer_parameters(predictor, value):
    with pytest.raises(ValueError, match="num_threads"):
        HASAGIPredictor(num_threads=value)
    with pytest.raises(ValueError, match="batch_size"):
        predictor.predict_batch([], batch_size=value)


def test_batch_error_index(predictor, rgb):
    with pytest.raises(ValueError, match=r"images\[3\].*at least 384"):
        predictor.predict_batch([rgb, rgb, rgb, rgb[:10]], batch_size=2)
