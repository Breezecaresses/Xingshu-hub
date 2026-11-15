$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskOutput = Join-Path $taskRoot 'XingShu-Dev/build/hotkey-tests'
$null = New-Item -ItemType Directory -Force -Path $taskOutput
$taskExe = Join-Path $taskOutput 'HotkeySettingsTests.exe'
& 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo "/out:$taskExe" (Join-Path $PSScriptRoot 'MatchLifecycleTests/HotkeySettingsTests.cs') (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/Core/XingShuHotkeys.cs')
if ($LASTEXITCODE -ne 0) { throw 'Hotkey test compilation failed' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Hotkey behavior tests failed' }
