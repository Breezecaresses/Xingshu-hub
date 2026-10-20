param([Parameter(Mandatory)][string]$HooksDll)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $taskRoot 'APX-Dev/tools/lib/Mono.Cecil.dll')
$taskAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $HooksDll).Path)
foreach ($taskType in $taskAssembly.MainModule.GetTypeReferences()) {
    if ($taskType.FullName -in @('System.Diagnostics.Process', 'System.Diagnostics.ProcessStartInfo', 'System.Console')) {
        throw "Android stripped desktop API still referenced: $($taskType.FullName)"
    }
}
function Get-TaskTypes($types) {
    foreach ($taskType in $types) { $taskType; Get-TaskTypes $taskType.NestedTypes }
}
foreach ($taskType in (Get-TaskTypes $taskAssembly.MainModule.Types)) {
    foreach ($taskMethod in $taskType.Methods) {
        if ($taskMethod.IsPInvokeImpl) { throw "Native import in managed Android preview: $($taskMethod.FullName)" }
    }
}
$taskHost = $taskAssembly.MainModule.Types | Where-Object FullName -eq 'XingShu.Runtime.XingShuRuntimeHost'
$taskVersion = ($taskHost.Fields | Where-Object Name -eq 'RuntimeVersion').Constant
if ($taskVersion -ne '1.0.0-android-preview1') { throw "Unexpected preview version: $taskVersion" }
Write-Output "PASS: Android preview identity; no Process/Console references or native imports"
