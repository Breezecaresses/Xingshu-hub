param([Parameter(Mandatory)][string]$HooksDll)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $taskRoot 'XingShu-Dev/tools/lib/Mono.Cecil.dll')
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
$taskVersionSource = Get-Content -LiteralPath (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShu/Runtime/XingShuRuntimeHost.cs') -Raw
$taskExpectedVersion = [regex]::Match($taskVersionSource, '#if XINGSHU_ANDROID_PREVIEW\s+public const string RuntimeVersion = "([^"]+)"').Groups[1].Value
if (!$taskExpectedVersion -or $taskVersion -ne $taskExpectedVersion) { throw "Unexpected preview version: $taskVersion; expected $taskExpectedVersion" }
$taskClock = $taskAssembly.MainModule.Types | Where-Object FullName -eq 'XingShuRuntime.Utilities.ThinkingClock'
$taskClockSource = $taskClock.Methods | Where-Object Name -eq 'RealSeconds'
if (!($taskClockSource.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains('System.DateTime::get_UtcNow') })) {
    throw 'Android thinking clock is missing its real-time source'
}
$taskClient = $taskAssembly.MainModule.Types | Where-Object FullName -eq 'XingShu.Bridge.XingShuBridgeClient'
$taskStart = $taskClient.Methods | Where-Object Name -eq 'Start'
if ($taskStart.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains('System.Threading.Thread::') }) {
    throw 'Android preview still starts the desktop bridge connection thread'
}
Write-Output 'PASS: Android preview identity; real-time clock; desktop bridge paused; no Process/Console references or native imports'
$taskAssembly.Dispose()
