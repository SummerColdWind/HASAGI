"""HASAGI's four-stage EfficientNetV2-B0 classifier and release weights."""

from pathlib import Path

import timm
import torch
from torch import nn

TASKS = ("layers", "discernibility")
STAGES = (("stage6", 192), ("stage5", 112), ("stage4", 96), ("stage3", 48))


class ScalePool(nn.Module):
    def __init__(self, channels):
        super().__init__()
        self.project = nn.Linear(channels, 256)
        self.norm = nn.LayerNorm(256, eps=1e-6)
        self.tanh = nn.Linear(256, 128)
        self.gate = nn.Linear(256, 128)
        self.score = nn.Linear(128, 4)

    def forward(self, feature):
        tokens = self.norm(self.project(feature.flatten(2).transpose(1, 2)))
        scores = self.score(self.tanh(tokens).tanh() * self.gate(tokens).sigmoid())
        with torch.autocast(device_type=feature.device.type, enabled=False):
            weights = scores.float().transpose(1, 2).softmax(-1)
            return weights @ tokens.float()


class HASAGIModel(nn.Module):
    def __init__(self):
        super().__init__()
        self.backbone = timm.create_model("tf_efficientnetv2_b0.in1k", pretrained=False,
                                          num_classes=4, drop_path_rate=0.1, drop_rate=0.0)
        self.backbone.classifier = nn.Identity()
        self.backbone.conv_head = nn.Identity()
        self.backbone.bn2 = nn.Identity()
        self.pools = nn.ModuleDict({name: ScalePool(channels) for name, channels in STAGES})
        self.classifier = nn.Linear(256 * len(STAGES), 4)

    def forward(self, images):
        x = self.backbone.bn1(self.backbone.conv_stem(images))
        pooled = {}
        for index, block in enumerate(self.backbone.blocks, start=1):
            x = block(x)
            name = f"stage{index}"
            if name in self.pools:
                pooled[name] = self.pools[name](x)
        with torch.autocast(device_type=images.device.type, enabled=False):
            features = torch.cat([pooled[name] for name, _ in STAGES], dim=-1)
            return (features * self.classifier.weight.float()[None]).sum(-1) + self.classifier.bias.float()


def load_model(task, device="cpu"):
    """Load an offline model in evaluation mode. Inputs are normalized NCHW tensors."""
    if task not in TASKS:
        raise ValueError(f"Expected one of {TASKS}, got {task!r}")
    path = Path(__file__).parent / "weights" / f"{task}.pt"
    model = HASAGIModel()
    model.load_state_dict(torch.load(path, map_location="cpu", weights_only=True))
    return model.to(device).eval()
