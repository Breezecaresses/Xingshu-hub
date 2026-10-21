$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskOutput = Join-Path $taskRoot 'XingShu-Dev/build/preference-tests'
$null = New-Item -ItemType Directory -Force -Path $taskOutput
$taskExe = Join-Path $taskOutput 'PreferenceMigrationTests.exe'
& 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe' /nologo "/out:$taskExe" (Join-Path $PSScriptRoot 'MatchLifecycleTests/PreferenceMigrationTests.cs') (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/Core/LegacyNames.cs')
if ($LASTEXITCODE -ne 0) { throw 'Preference migration test compilation failed' }
& $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Preference migration tests failed' }
