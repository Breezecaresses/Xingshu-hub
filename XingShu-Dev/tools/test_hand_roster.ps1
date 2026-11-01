param([string]$Compiler = 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskOutput = Join-Path $taskRoot 'XingShu-Dev/build/hand-roster-tests'
$null = New-Item -ItemType Directory -Force -Path $taskOutput
$taskExe = Join-Path $taskOutput 'HandRosterTests.exe'
& $Compiler /nologo /langversion:latest "/out:$taskExe" (Join-Path $PSScriptRoot 'HandRosterTests/HandRosterTests.cs') (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/Utilities/HandRosterLayout.cs') (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/Utilities/HandCardVisibility.cs')
if ($LASTEXITCODE -ne 0) { throw 'Hand roster test compilation failed' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Hand roster layout/state tests failed' }
