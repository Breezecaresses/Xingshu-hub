param([string]$Compiler = 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$taskOutput = Join-Path $taskRoot 'XingShu-Dev/build/monster-cooldown-tests'
$null = New-Item -ItemType Directory -Force -Path $taskOutput
$taskRunner = Join-Path $taskOutput 'TestMonsterCooldownRunner.exe'
& $Compiler /nologo /langversion:latest "/out:$taskRunner" (Join-Path $PSScriptRoot 'TestMonsterCooldownRunner.cs') (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/Utilities/MonsterCooldownLedger.cs')
if ($LASTEXITCODE -ne 0) { throw 'Cooldown test compilation failed' }
& $taskRunner
if ($LASTEXITCODE -ne 0) { throw 'Cooldown tests failed' }
