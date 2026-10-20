param([string]$Compiler = 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskOutput = Join-Path $taskRoot 'APX-Dev/build/decision-stats-tests'
$null = New-Item -ItemType Directory -Force -Path $taskOutput
$taskMatch = Join-Path $taskRoot 'APX-Dev/src/APX.hooks/APXRuntime/MatchPort'
$taskRunner = Join-Path $taskOutput 'DecisionAndStatsTests.exe'
& $Compiler /nologo /langversion:latest "/out:$taskRunner" (Join-Path $PSScriptRoot 'MatchLifecycleTests/DecisionAndStatsTests.cs') (Join-Path $taskRoot 'APX-Dev/src/APX.hooks/APXRuntime/Utilities/ThinkingClock.cs') (Join-Path $taskRoot 'APX-Dev/src/APX.hooks/APXRuntime/Utilities/DecisionProgressOwner.cs') (Join-Path $taskRoot 'APX-Dev/src/APX.hooks/APXRuntime/Utilities/HandFanLayout.cs') (Join-Path $taskMatch 'MatchRating.cs') (Join-Path $taskMatch 'MatchLedger.cs') (Join-Path $taskMatch 'StatsWidgetFrame.cs') (Join-Path $taskMatch 'StatsWidgetPresenter.cs')
if ($LASTEXITCODE -ne 0) { throw 'Decision/statistics test compilation failed' }
& $taskRunner
if ($LASTEXITCODE -ne 0) { throw 'Decision/statistics tests failed' }
