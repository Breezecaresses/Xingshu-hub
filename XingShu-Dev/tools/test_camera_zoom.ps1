param([string]$Compiler = 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskOutput = Join-Path $taskRoot 'XingShu-Dev/build/camera-zoom-tests'
$null = New-Item -ItemType Directory -Force -Path $taskOutput
$taskExe = Join-Path $taskOutput 'CameraZoomTests.exe'
$taskCamera = Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/CameraPort'
$taskSources = @('MapCamera.cs','CameraControlGate.cs','CameraControlMode.cs','CameraControlChange.cs','CameraSelectionPolicy.cs','MapDragMath.cs','PortMath.cs') | ForEach-Object { Join-Path $taskCamera $_ }
& $Compiler /nologo /langversion:latest "/out:$taskExe" (Join-Path $PSScriptRoot 'CameraZoomTests/CameraZoomTests.cs') $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Camera regression test compilation failed' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Camera regression tests failed' }
