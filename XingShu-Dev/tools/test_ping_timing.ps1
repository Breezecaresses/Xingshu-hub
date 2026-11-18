param([string]$Compiler = 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskOutput = Join-Path $taskRoot 'XingShu-Dev/build/ping-timing-tests'
$taskSource = Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime'
$null = New-Item -ItemType Directory -Force -Path $taskOutput
$taskExe = Join-Path $taskOutput 'PingDisplayTimingTests.exe'
& $Compiler /nologo /langversion:latest "/out:$taskExe" `
    (Join-Path $PSScriptRoot 'MatchLifecycleTests/PingDisplayTimingTests.cs') `
    (Join-Path $taskSource 'XingShuRuntime/Utilities/PingClockState.cs') `
    (Join-Path $taskSource 'XingShuRuntime/Utilities/ThinkingClock.cs')
if ($LASTEXITCODE -ne 0) { throw 'Ping timing test compilation failed' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Ping timing tests failed' }
