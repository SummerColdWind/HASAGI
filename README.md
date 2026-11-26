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
| [Download GUI (ZIP)](https://github.com/SummerColdWind/HASAGI/releases/download/v0.1.0-beta.2/HASAGI-gui-v0.1.0-beta.2-Windows-x64.zip) | `HASAGI.exe` | Select a folder or drag it into the window. |
| [Download TUI (ZIP)](https://github.com/SummerColdWind/HASAGI/releases/download/v0.1.0-beta.2/HASAGI-tui-v0.1.0-beta.2-Windows-x64.zip) | `HASAGI-tui.exe` | Browse folders and follow progress using the keyboard. |
| [Download CLI (ZIP)](https://github.com/SummerColdWind/HASAGI/releases/download/v0.1.0-beta.2/HASAGI-cli-v0.1.0-beta.2-Windows-x64.zip) | `HASAGI-cli.exe` | Run batch analyses from a script. |

All three interfaces export CSV and Excel files with one row per image. Each row includes the absolute and relative file paths, predictions, model probabilities, and processing status.

## License

The HASAGI rights holders license the project code, documentation, tests,
build scripts and models in this checkout under the
[GNU Affero General Public License, version 3 only](LICENSE)
(`AGPL-3.0-only`). This includes the PyTorch checkpoints, both ONNX models,
the Python package and the Windows applications. Third-party materials are
subject to the licenses listed in the [third-party notices](THIRD_PARTY_NOTICES).

The previously published PyPI `0.1.0b1` distributions and Windows
`v0.1.0-beta.1` downloads carry the HASAGI Noncommercial License.
