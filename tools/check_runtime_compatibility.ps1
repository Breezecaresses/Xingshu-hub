param([Parameter(Mandatory)][string]$RuntimeDll, [string]$HooksDll)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $taskRoot 'XingShu-Dev/tools/lib/Mono.Cecil.dll')
$taskResolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$taskResolver.AddSearchDirectory((Split-Path -Parent (Resolve-Path -LiteralPath $RuntimeDll).Path))
foreach ($taskDir in @('references/unity','references/framework','references/thirdparty','build/bin/Release')) {
    $taskResolver.AddSearchDirectory((Join-Path $taskRoot ('XingShu-Dev/' + $taskDir)))
}
$taskParameters = [Mono.Cecil.ReaderParameters]::new()
$taskParameters.AssemblyResolver = $taskResolver
$taskRuntime = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $RuntimeDll).Path, $taskParameters)
# The input may be named AstralParty.Runtime.before.dll beside an older candidate.
# Resolve hooks against this exact input rather than whichever same-name DLL is found first.
$taskRegister = [Mono.Cecil.DefaultAssemblyResolver].GetMethod('RegisterAssembly', [Reflection.BindingFlags]'Instance,NonPublic')
$null = $taskRegister.Invoke($taskResolver, @($taskRuntime))
if (!$HooksDll) { $HooksDll = Join-Path $taskRoot 'XingShu-Dev/build/bin/Release/XingShu.Runtime.dll' }
$taskHooks = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $HooksDll).Path, $taskParameters)
$taskCount = 0
foreach ($taskMember in $taskHooks.MainModule.GetMemberReferences()) {
    if ($taskMember.DeclaringType.Scope.Name -ne 'AstralParty.Runtime') { continue }
    $taskResolved = $taskMember.Resolve()
    if (!$taskResolved) { throw "Unresolved game member: $taskMember" }
    if (![object]::ReferenceEquals($taskResolved.Module, $taskRuntime.MainModule)) { throw "Game member resolved outside the selected runtime: $taskMember" }
    $taskCount++
}
foreach ($taskRequirement in @(
    @('UI.ExpressionWindow', '_btn_QuickReply'),
    @('UI.ExpressionWindow', 'quickReplyCts'),
    @('UI.ExpressionWindow', '_showQuickReply'),
    @('FairyGUI.EventListener', '_bridge'),
    @('FairyGUI.EventBridge', '_isLocking'),
    @('GameLogic.AccountLogic', 'playerCareer'),
    @('GameLogic.ReplayLogic', '_cdnBaseUrl'),
    @('Core.Unit.CharacterMove', '_walkDirections'),
    @('Core.Unit.Character', 'move')
)) {
    $taskType = $taskRuntime.MainModule.Types | Where-Object FullName -eq $taskRequirement[0]
    if (!$taskType -or !($taskType.Fields | Where-Object Name -eq $taskRequirement[1])) {
        throw "Missing reflected field: $($taskRequirement -join '::')"
    }
}
$taskSettingType = $taskRuntime.MainModule.Types | Where-Object FullName -eq 'UI.SettingWindow'
$taskSettingChanged = @($taskSettingType.Methods | Where-Object {
    $_.Name -eq 'OnAdultSwitch' -and !$_.IsStatic -and $_.ReturnType.FullName -eq 'System.Void' -and
    $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'FairyGUI.EventContext'
})
if ($taskSettingChanged.Count -ne 1) { throw 'Missing or ambiguous native artwork setting callback' }
Write-Output "PASS: $taskCount game member references resolve; 9 reflected fields and native artwork callback exist"
