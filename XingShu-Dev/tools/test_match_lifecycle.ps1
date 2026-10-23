param([string]$Compiler = 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskOutput = Join-Path $taskRoot 'XingShu-Dev/build/lifecycle-tests'
$taskSource = Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime'
$null = New-Item -ItemType Directory -Force -Path $taskOutput
$taskExe = Join-Path $taskOutput 'HandSessionLifecycleTests.exe'
& $Compiler /nologo /langversion:latest "/out:$taskExe" (Join-Path $PSScriptRoot 'MatchLifecycleTests/Program.cs') (Join-Path $taskSource 'XingShu/Truth/HandSessionLifecycle.cs')
if ($LASTEXITCODE -ne 0) { throw 'Lifecycle test compilation failed' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Lifecycle tests failed' }
$taskExe = Join-Path $taskOutput 'ThinkingTimeTests.exe'
& $Compiler /nologo /langversion:latest "/out:$taskExe" (Join-Path $PSScriptRoot 'MatchLifecycleTests/ThinkingTimeTests.cs') (Join-Path $taskSource 'XingShuRuntime/Utilities/ThinkingTime.cs') (Join-Path $taskSource 'XingShuRuntime/Utilities/DecisionProgressOwner.cs')
if ($LASTEXITCODE -ne 0) { throw 'Thinking-time integration test compilation failed' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Thinking-time integration tests failed' }
