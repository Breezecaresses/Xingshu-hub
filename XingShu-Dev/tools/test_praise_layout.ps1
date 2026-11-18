$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskOutput = Join-Path $taskRoot 'XingShu-Dev/build/praise-layout-tests'
$null = New-Item -ItemType Directory -Force -Path $taskOutput
$taskExe = Join-Path $taskOutput 'PraiseIconLayoutTests.exe'
& 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo "/out:$taskExe" (Join-Path $PSScriptRoot 'MatchLifecycleTests/PraiseIconLayoutTests.cs') (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/Social/PraiseIconLayout.cs') (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/Social/PraiseSelection.cs')
if ($LASTEXITCODE -ne 0) { throw 'Praise layout test compilation failed' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Praise layout tests failed' }
