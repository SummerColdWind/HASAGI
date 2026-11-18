# HASAGI

*High-throughput Automated Screening And Grading of IVCM Images*

**Windows x64 pre-release** · [v0.1.0-beta.1 release notes](https://github.com/SummerColdWind/HASAGI/releases/tag/v0.1.0-beta.1)

HASAGI analyzes folders of 384 × 384 single-frame corneal in vivo confocal microscopy (IVCM) images. It identifies the subbasal nerve plexus (SBNP), epithelium, stroma, and other tissue. An image can receive multiple labels. For images labeled only as SBNP, HASAGI also grades nerve discernibility and reports a score from 0 to 100.

## Downloads

Choose one package, extract the ZIP, and run the executable listed below. Each package includes both models and runs offline on the CPU.

| Windows x64 download | Executable | Use it to |
| --- | --- | --- |
| [Download GUI (ZIP)](https://github.com/SummerColdWind/HASAGI/releases/download/v0.1.0-beta.1/HASAGI-gui-v0.1.0-beta.1-Windows-x64.zip) | `HASAGI.exe` | Select a folder or drag it into the window. |
| [Download TUI (ZIP)](https://github.com/SummerColdWind/HASAGI/releases/download/v0.1.0-beta.1/HASAGI-tui-v0.1.0-beta.1-Windows-x64.zip) | `HASAGI-tui.exe` | Browse folders and follow progress using the keyboard. |
| [Download CLI (ZIP)](https://github.com/SummerColdWind/HASAGI/releases/download/v0.1.0-beta.1/HASAGI-cli-v0.1.0-beta.1-Windows-x64.zip) | `HASAGI-cli.exe` | Run batch analyses from a script. |

All three interfaces export CSV and Excel files with one row per image. Each row includes the absolute and relative file paths, predictions, model probabilities, and processing status.
