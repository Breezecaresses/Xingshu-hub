$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskOutput = Join-Path $taskRoot 'XingShu-Dev/build/assist-ball-tests'
$null = New-Item -ItemType Directory -Force -Path $taskOutput
$taskExe = Join-Path $taskOutput 'AssistBallPolicyTests.exe'
& 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo "/out:$taskExe" (Join-Path $PSScriptRoot 'MatchLifecycleTests/AssistBallPolicyTests.cs') (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/Utilities/AssistBallPolicy.cs')
if ($LASTEXITCODE -ne 0) { throw 'Assist ball test compilation failed' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Assist ball tests failed' }
