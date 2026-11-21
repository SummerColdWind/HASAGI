$ErrorActionPreference = 'Stop'
$shared = Join-Path $PSScriptRoot 'shared'
$bin = Join-Path $PSScriptRoot 'bin'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$models = Join-Path $root 'packages/hasagi/src/hasagi/models'
$vendor = Join-Path $shared 'vendor/ort'
$notices = Join-Path $bin 'licenses/onnxruntime'
New-Item -ItemType Directory -Force $bin, "$bin\models", $notices | Out-Null

$vswhere = "${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Install the Visual Studio C++ x64 build tools.' }
$fx = "$env:WINDIR/Microsoft.NET\Framework64\v4.0.30319"
Push-Location $shared
try {
    $compile = 'call "' + $vs + '\VC\Auxiliary\Build\vcvars64.bat" >nul && cl /nologo /std:c++17 /EHsc /O2 /MT /LD /utf-8 Inference.cpp /Fo..\bin\Inference.obj /link vendor\ort\onnxruntime.lib /OUT:..\bin\HasagiInference.dll /IMPLIB:..\bin\HasagiInference.lib'
    & cmd.exe /d /c $compile
    if ($LASTEXITCODE) { throw 'Native inference build failed.' }
} finally { Pop-Location }

$common = @('/nologo','/platform:x64','/codepage:65001','/optimize+',
    "/reference:$fx\WPF\PresentationCore.dll", "/reference:$fx\WPF\WindowsBase.dll",
    '/reference:System.Xaml.dll','/reference:System.Xml.Linq.dll',
    '/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll',
    "/resource:$shared\ReportTemplate.xlsx,report.xlsx")
$gui = Join-Path $PSScriptRoot 'gui'
$tui = Join-Path $PSScriptRoot 'tui'
& "$fx\csc.exe" @common /target:winexe "/out:$bin\HASAGI.exe" "/reference:$fx\WPF\PresentationFramework.dll" /reference:System.Windows.Forms.dll "/resource:$gui\MainWindow.xaml,window.xaml" "/resource:$gui\assets\hasagi-yasuo-ink.png,ink.png" "/resource:$gui\assets\hasagi-brush-wordmark.png,wordmark.png" "$shared\Analysis.cs" "$shared\Report.cs" "$gui\Hasagi.cs"
if ($LASTEXITCODE) { throw 'GUI build failed.' }
& "$fx\csc.exe" @common /target:exe "/out:$bin\HASAGI-tui.exe" /reference:System.Windows.Forms.dll "/resource:$tui\design\Idle.ans,design.ans" "$tui\Program.cs" "$tui\Screen.cs" "$tui\FolderPicker.cs" "$shared\Analysis.cs" "$shared\Report.cs" "$shared\BatchJob.cs"
if ($LASTEXITCODE) { throw 'TUI build failed.' }
& "$fx\csc.exe" @common /target:exe "/out:$bin\HASAGI-cli.exe" "$PSScriptRoot\cli\Program.cs" "$shared\Analysis.cs" "$shared\Report.cs" "$shared\BatchJob.cs"
if ($LASTEXITCODE) { throw 'CLI build failed.' }

Copy-Item -LiteralPath "$models/layers_fp32.onnx", "$models/discernibility_fp32.onnx" -Destination "$bin\models"
Copy-Item -LiteralPath "$vendor/onnxruntime.dll", "$root/LICENSE", "$root/THIRD_PARTY_NOTICES" -Destination $bin
Copy-Item -LiteralPath "$vendor/LICENSE", "$vendor/ThirdPartyNotices.txt" -Destination $notices
$crt = Get-Item "$vs/VC/Redist/MSVC/*/x64/Microsoft.VC*.CRT" | Sort-Object FullName -Descending | Select-Object -First 1
foreach ($dll in 'msvcp140.dll','msvcp140_1.dll','vcruntime140_1.dll','vcruntime140.dll') {
    Copy-Item -LiteralPath (Join-Path $crt.FullName $dll) -Destination $bin
}
Write-Output "Built GUI, TUI and CLI in $bin"
