param([string]$Compiler = 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$testOutput = Join-Path $taskRoot 'XingShu-Dev/build/monster-target-tests'
$sourceRoot = Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/Targeting'
$null = New-Item -ItemType Directory -Force -Path $testOutput
if (!(Test-Path -LiteralPath $Compiler)) { throw 'C# compiler not found; pass -Compiler with a Roslyn csc.exe path' }
$models = Join-Path $sourceRoot 'MonsterTargetModels.cs'
$mathRunner = Join-Path $testOutput 'TestMonsterTargetRunner.exe'
& $Compiler /nologo "/out:$mathRunner" (Join-Path $PSScriptRoot 'TestMonsterTargetRunner.cs') $models (Join-Path $sourceRoot 'MonsterTargetMath.cs') (Join-Path $sourceRoot 'MonsterTargetPreview.cs')
if ($LASTEXITCODE -ne 0) { throw 'Math test compilation failed' }
& $mathRunner
if ($LASTEXITCODE -ne 0) { throw 'Math tests failed' }
$contextRunner = Join-Path $testOutput 'TestMonsterTargetContextRunner.exe'
& $Compiler /nologo "/out:$contextRunner" (Join-Path $PSScriptRoot 'TestMonsterTargetContextRunner.cs') $models (Join-Path $sourceRoot 'MonsterTargetContext.cs')
if ($LASTEXITCODE -ne 0) { throw 'Context test compilation failed' }
& $contextRunner
if ($LASTEXITCODE -ne 0) { throw 'Context tests failed' }
