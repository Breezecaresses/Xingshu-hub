$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskOutput = Join-Path $taskRoot 'XingShu-Dev/build/mobile-slider-tests'
$null = New-Item -ItemType Directory -Force -Path $taskOutput
$taskExe = Join-Path $taskOutput 'MobileSliderInputTests.exe'
& 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo "/out:$taskExe" (Join-Path $PSScriptRoot 'MatchLifecycleTests/MobileSliderInputTests.cs') (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/UI/XingShuMobileSliderInput.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mobile slider test compilation failed' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Mobile slider tests failed' }
