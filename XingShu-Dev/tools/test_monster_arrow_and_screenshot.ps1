param([string]$Compiler = 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$testOutput = Join-Path $taskRoot 'XingShu-Dev/build/monster-arrow-screenshot-tests'
$null = New-Item -ItemType Directory -Force -Path $testOutput
if (!(Test-Path -LiteralPath $Compiler)) { throw 'C# compiler not found; pass -Compiler with a Roslyn csc.exe path' }

$arrowExe = Join-Path $testOutput 'TestMonsterArrowBoostRunner.exe'
& $Compiler /nologo /langversion:latest "/out:$arrowExe" `
    (Join-Path $PSScriptRoot 'TestMonsterArrowBoostRunner.cs') `
    (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/Targeting/MonsterWalkArrowBoost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Monster arrow boost test compilation failed' }
& $arrowExe
if ($LASTEXITCODE -ne 0) { throw 'Monster arrow boost tests failed' }

$shotExe = Join-Path $testOutput 'TestSettlementScreenshotRunner.exe'
& $Compiler /nologo /langversion:latest "/out:$shotExe" `
    (Join-Path $PSScriptRoot 'TestSettlementScreenshotRunner.cs') `
    (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShuRuntime/MatchPort/SettlementScreenshotPolicy.cs')
if ($LASTEXITCODE -ne 0) { throw 'Settlement screenshot test compilation failed' }
& $shotExe
if ($LASTEXITCODE -ne 0) { throw 'Settlement screenshot tests failed' }
