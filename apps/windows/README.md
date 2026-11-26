# Windows applications

The GUI, TUI and CLI analyze folders of single-frame 384 × 384 PNG, BMP,
JPEG and TIFF images on a Windows x64 CPU. Each writes a CSV and an Excel
workbook with one row per image. The three interfaces share the image reader,
ONNX inference bridge and report writer in [shared](shared/).

## Windows v0.1.0-beta.2

On first use, Windows display language selects Simplified Chinese, Traditional Chinese or English. Other languages use English. The GUI language menu, TUI F4 menu and CLI option `--lang zh-CN|zh-TW|en` remember the choice for all three applications under the current Windows account. Each batch uses the language selected when analysis starts for its Excel workbook. CSV uses stable field names and status codes. CLI help and progress messages are in English.

## Build and run

Use x64 Windows with PowerShell, the .NET Framework 4.x compiler and WPF
assemblies, and Visual Studio C++ x64 build tools with a Windows SDK.
Run these commands in PowerShell from the repository root.

```powershell
.\apps\windows\build.ps1
.\apps\windows\bin\HASAGI-cli.exe C:\images -o C:\reports
```

Launch `HASAGI.exe` from the same output directory to choose a folder in
the GUI. Run `HASAGI-tui.exe` in Windows Terminal for keyboard controls
(F2 selects an input folder, F5 starts analysis, Q quits).

All three executables share the DLLs and models in `bin`. The build compiles
`HasagiInference.dll` and copies both ONNX files from the
[Python package](../../packages/hasagi/src/hasagi/models/). Windows
processing classifies each frame and scores nerve discernibility only
when SBNP is its sole predicted layer.
