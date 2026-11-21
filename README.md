# HASAGI

*High-throughput Automated Screening And Grading of IVCM Images*

HASAGI identifies tissue layers in corneal in vivo confocal microscopy (IVCM)
images and grades nerve discernibility on a 0–100 scale. A frame can contain
more than one layer. Both tasks use four-stage EfficientNetV2-B0 models.

| Use HASAGI to | Start here |
| --- | --- |
| Load the original PyTorch weights, run a model, or export it to ONNX | [Native PyTorch](pytorch/README.md) |
| Process images or batches in Python with offline CPU inference | [Python package](packages/hasagi/README.md) |
| Use or build the Windows desktop, terminal, or command-line application | [Windows applications](apps/windows/README.md) |

## Python installation

Install the published beta with Python 3.10 or later.

```bash
python -m pip install hasagi==0.1.0b1
```

To build or install the package from a checkout, run
`python -m pip install ./packages/hasagi` from the repository root.
The Python API and native inference example can score any frame. The Windows
applications report a discernibility grade only for frames labeled solely as
subbasal nerve plexus (SBNP).

## Downloads

Choose one package, extract the ZIP, and run the executable listed below. Each package includes both models and runs offline on the CPU.

| Windows x64 download | Executable | Use it to |
| --- | --- | --- |
| [Download GUI (ZIP)](https://github.com/SummerColdWind/HASAGI/releases/download/v0.1.0-beta.1/HASAGI-gui-v0.1.0-beta.1-Windows-x64.zip) | `HASAGI.exe` | Select a folder or drag it into the window. |
| [Download TUI (ZIP)](https://github.com/SummerColdWind/HASAGI/releases/download/v0.1.0-beta.1/HASAGI-tui-v0.1.0-beta.1-Windows-x64.zip) | `HASAGI-tui.exe` | Browse folders and follow progress using the keyboard. |
| [Download CLI (ZIP)](https://github.com/SummerColdWind/HASAGI/releases/download/v0.1.0-beta.1/HASAGI-cli-v0.1.0-beta.1-Windows-x64.zip) | `HASAGI-cli.exe` | Run batch analyses from a script. |

All three interfaces export CSV and Excel files with one row per image. Each row includes the absolute and relative file paths, predictions, model probabilities, and processing status.

## License

The [HASAGI Noncommercial License](LICENSE) covers the project code and models,
including the native checkpoints, ONNX exports, Python package and Windows
applications. Noncommercial research and teaching are permitted. Commercial
use and any redistribution require prior written authorization. See the
[third-party notices](THIRD_PARTY_NOTICES) for upstream licenses and attribution.
