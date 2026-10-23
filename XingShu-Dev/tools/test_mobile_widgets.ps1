$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskOutput = Join-Path $taskRoot 'XingShu-Dev/build/mobile-widget-tests'
$null = New-Item -ItemType Directory -Force -Path $taskOutput
$taskExe = Join-Path $taskOutput 'MobileWidgetScaleTests.exe'
$taskSource = Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime'
& 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo /langversion:latest "/out:$taskExe" (Join-Path $PSScriptRoot 'MatchLifecycleTests/MobileWidgetScaleTests.cs') (Join-Path $taskSource 'Utilities/XingShuMobileUIHelper.cs') (Join-Path $taskSource 'MatchPort/StatsWidgetPreferences.cs') (Join-Path $taskSource 'MatchPort/StatsWidgetBounds.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mobile widget test compilation failed' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Mobile widget tests failed' }
