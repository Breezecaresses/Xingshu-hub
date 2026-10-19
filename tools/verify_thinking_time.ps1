param([Parameter(Mandatory)][string]$Before, [Parameter(Mandatory)][string]$After)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $taskRoot 'APX-Dev/tools/lib/Mono.Cecil.dll')
function Get-TaskMethods($types) {
    foreach($type in $types) {
        foreach($method in $type.Methods) { if ($method.HasBody) { $method } }
        Get-TaskMethods $type.NestedTypes
    }
}
function Get-TaskBody($method) {
    (@($method.Body.Instructions | ForEach-Object { $_.ToString() }) +
     @($method.Body.Variables | ForEach-Object { $_.VariableType.FullName }) +
     @($method.Body.ExceptionHandlers | ForEach-Object {
         "$($_.HandlerType):$($_.TryStart):$($_.TryEnd):$($_.HandlerStart):$($_.HandlerEnd):$($_.FilterStart):$($_.CatchType)"
     })) -join "`n"
}
$taskOld = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $Before).Path)
$taskNew = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $After).Path)
$taskOldMethods = @{}; $taskNewMethods = @{}
Get-TaskMethods $taskOld.MainModule.Types | ForEach-Object { $taskOldMethods[$_.FullName] = $_ }
Get-TaskMethods $taskNew.MainModule.Types | ForEach-Object { $taskNewMethods[$_.FullName] = $_ }
if ($taskOldMethods.Count -ne $taskNewMethods.Count) { throw 'Method count changed' }
$taskChanged = @()
foreach($taskName in $taskOldMethods.Keys) {
    if (!$taskNewMethods.ContainsKey($taskName)) { throw "Missing method $taskName" }
    if ((Get-TaskBody $taskOldMethods[$taskName]) -ne (Get-TaskBody $taskNewMethods[$taskName])) {
        $taskChanged += $taskName
        if ($taskName -notmatch 'StaticGlobalData::get_SELECT_ROLE(SKIN)?_TIMELIMIT\(' -and
            $taskName -notmatch 'GameLogic.OperationTimer::ActionDownTime\(') { throw "Unexpected method change: $taskName" }
        $taskCalls = @($taskNewMethods[$taskName].Body.Instructions | Where-Object {
            $_.Operand -and $_.Operand.ToString().Contains('APXRuntime.Utilities.ThinkingTime::ScaleSeconds')
        })
        if (!$taskCalls.Count) { throw "Missing scale call: $taskName" }
    }
}
if ($taskChanged.Count -ne 3) { throw "Expected three changed timer methods, found $($taskChanged.Count)" }
Write-Output "PASS: exactly 3 timer methods changed; $($taskOldMethods.Count - 3) other method bodies unchanged"
