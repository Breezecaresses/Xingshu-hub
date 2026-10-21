param([string]$Compiler = 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe', [switch]$Android)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskOutput = Join-Path $taskRoot 'XingShu-Dev/build/decision-stats-tests'
if ($Android) { $taskOutput = Join-Path $taskOutput 'android' }
$null = New-Item -ItemType Directory -Force -Path $taskOutput
$taskMatch = Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/MatchPort'
$taskRunner = Join-Path $taskOutput 'DecisionAndStatsTests.exe'
$taskDefines = if ($Android) { '/define:XINGSHU_ANDROID_PREVIEW' } else { '/define:TRACE' }
& $Compiler /nologo /langversion:latest $taskDefines "/out:$taskRunner" (Join-Path $PSScriptRoot 'MatchLifecycleTests/DecisionAndStatsTests.cs') (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/Utilities/ThinkingClock.cs') (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/Utilities/DecisionProgressOwner.cs') (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/Utilities/HandFanLayout.cs') (Join-Path $taskMatch 'MatchRating.cs') (Join-Path $taskMatch 'MatchLedger.cs') (Join-Path $taskMatch 'StatsWidgetFrame.cs') (Join-Path $taskMatch 'StatsWidgetPresenter.cs')
if ($LASTEXITCODE -ne 0) { throw 'Decision/statistics test compilation failed' }
& $taskRunner
if ($LASTEXITCODE -ne 0) { throw 'Decision/statistics tests failed' }
