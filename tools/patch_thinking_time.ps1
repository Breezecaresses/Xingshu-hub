param([Parameter(Mandatory)][string]$InputDll, [Parameter(Mandatory)][string]$OutputDll, [string]$HooksDll)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $taskRoot 'APX-Dev/tools/lib/Mono.Cecil.dll')
Add-Type -Path (Join-Path $taskRoot 'APX-Dev/tools/lib/Mono.Cecil.Rocks.dll')
$taskResolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
foreach($taskDir in @('references/updated_20261019_verified','references/unity','references/framework','build/bin/Release')) {
    $taskResolver.AddSearchDirectory((Join-Path $taskRoot ('APX-Dev/' + $taskDir)))
}
$taskReadParams = [Mono.Cecil.ReaderParameters]::new()
$taskReadParams.AssemblyResolver = $taskResolver
$taskAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $InputDll).Path, $taskReadParams)
if (!$HooksDll) { $HooksDll = Join-Path $taskRoot 'APX-Dev/build/bin/Release/APX.hooks.dll' }
$taskHooks = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $HooksDll).Path)
$taskScaler = $taskHooks.MainModule.Types | Where-Object FullName -eq 'APXRuntime.Utilities.ThinkingTime'
$taskIntMethod = $taskScaler.Methods | Where-Object { $_.Name -eq 'ScaleSeconds' -and $_.ReturnType.FullName -eq 'System.Int32' }
$taskFloatMethod = $taskScaler.Methods | Where-Object { $_.Name -eq 'ScaleSeconds' -and $_.ReturnType.FullName -eq 'System.Single' }
if (!$taskIntMethod -or !$taskFloatMethod) { throw 'Thinking-time hooks are missing' }
$taskIntRef = $taskAssembly.MainModule.ImportReference($taskIntMethod)
$taskFloatRef = $taskAssembly.MainModule.ImportReference($taskFloatMethod)
$taskPatched = 0
foreach($taskType in $taskAssembly.MainModule.Types) {
    foreach($taskMethod in $taskType.Methods) {
        if (!$taskMethod.HasBody) { continue }
        $taskHero = $taskType.FullName -eq 'StaticGlobalData' -and $taskMethod.Name -in @('get_SELECT_ROLE_TIMELIMIT','get_SELECT_ROLESKIN_TIMELIMIT')
        $taskTurn = $taskType.FullName -eq 'GameLogic.OperationTimer' -and $taskMethod.Name -eq 'ActionDownTime'
        if (!$taskHero -and !$taskTurn) { continue }
        # Already patched inputs are accepted without stacking multipliers.
        if ($taskMethod.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains('APXRuntime.Utilities.ThinkingTime::ScaleSeconds') }) { $taskPatched++; continue }
        [Mono.Cecil.Rocks.MethodBodyRocks]::SimplifyMacros($taskMethod.Body)
        $taskProcessor = $taskMethod.Body.GetILProcessor()
        foreach($taskInstruction in @($taskMethod.Body.Instructions)) {
            if (($taskHero -and $taskInstruction.OpCode.Code -eq 'Ret') -or
                ($taskTurn -and $taskInstruction.OpCode.Code -eq 'Stsfld' -and $taskInstruction.Operand.Name -eq 'operationTime')) {
                $taskRef = if ($taskHero) { $taskIntRef } else { $taskFloatRef }
                $taskCall = $taskProcessor.Create([Mono.Cecil.Cil.OpCodes]::Call, $taskRef)
                $taskProcessor.InsertBefore($taskInstruction, $taskCall)
                # Preserve branches that previously jumped straight to the assignment/return.
                foreach($taskBranch in $taskMethod.Body.Instructions) {
                    if ([Object]::ReferenceEquals($taskBranch.Operand, $taskInstruction)) { $taskBranch.Operand = $taskCall }
                }
            }
        }
        [Mono.Cecil.Rocks.MethodBodyRocks]::OptimizeMacros($taskMethod.Body)
        $taskPatched++
    }
}
if ($taskPatched -ne 3) { throw "Expected three verified timer methods, found $taskPatched" }
$taskAssembly.Write([IO.Path]::GetFullPath($OutputDll))
$taskAssembly.Dispose()
$taskHooks.Dispose()
$taskClockPatcher = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($OutputDll))) 'ThinkingClockPatcher.exe'
$taskCompiler = 'D:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/Roslyn/csc.exe'
& $taskCompiler /nologo "/out:$taskClockPatcher" "/reference:$(Join-Path $taskRoot 'APX-Dev/tools/lib/Mono.Cecil.dll')" "/reference:$(Join-Path $taskRoot 'APX-Dev/tools/lib/Mono.Cecil.Rocks.dll')" (Join-Path $PSScriptRoot 'patch_thinking_clock.cs')
if ($LASTEXITCODE -ne 0) { throw 'Real-clock patcher compilation failed' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'APX-Dev/tools/lib/Mono.Cecil.dll') -Destination (Join-Path (Split-Path -Parent $taskClockPatcher) 'Mono.Cecil.dll') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'APX-Dev/tools/lib/Mono.Cecil.Rocks.dll') -Destination (Join-Path (Split-Path -Parent $taskClockPatcher) 'Mono.Cecil.Rocks.dll') -Force
$taskClockOutput = [IO.Path]::GetFullPath($OutputDll) + '.realclock'
& $taskClockPatcher ([IO.Path]::GetFullPath($OutputDll)) $HooksDll $taskClockOutput $taskRoot
if ($LASTEXITCODE -ne 0) { throw 'Real-clock decision timer patch failed' }
Move-Item -LiteralPath $taskClockOutput -Destination ([IO.Path]::GetFullPath($OutputDll)) -Force
Write-Output 'Patched hero/skin selection and the shared operation/chip timer (3 methods)'
