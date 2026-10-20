param([Parameter(Mandatory)][string]$RuntimeDll, [string]$HooksDll)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $taskRoot 'APX-Dev/tools/lib/Mono.Cecil.dll')
$taskResolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$taskResolver.AddSearchDirectory((Split-Path -Parent (Resolve-Path -LiteralPath $RuntimeDll).Path))
foreach ($taskDir in @('references/unity','references/framework','references/thirdparty','build/bin/Release')) {
    $taskResolver.AddSearchDirectory((Join-Path $taskRoot ('APX-Dev/' + $taskDir)))
}
$taskParameters = [Mono.Cecil.ReaderParameters]::new()
$taskParameters.AssemblyResolver = $taskResolver
$taskRuntime = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $RuntimeDll).Path, $taskParameters)
# The input may be named AstralParty.Runtime.before.dll beside an older candidate.
# Resolve hooks against this exact input rather than whichever same-name DLL is found first.
$taskRegister = [Mono.Cecil.DefaultAssemblyResolver].GetMethod('RegisterAssembly', [Reflection.BindingFlags]'Instance,NonPublic')
$null = $taskRegister.Invoke($taskResolver, @($taskRuntime))
if (!$HooksDll) { $HooksDll = Join-Path $taskRoot 'APX-Dev/build/bin/Release/APX.hooks.dll' }
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
    @('GameLogic.ReplayLogic', '_cdnBaseUrl')
)) {
    $taskType = $taskRuntime.MainModule.Types | Where-Object FullName -eq $taskRequirement[0]
    if (!$taskType -or !($taskType.Fields | Where-Object Name -eq $taskRequirement[1])) {
        throw "Missing reflected field: $($taskRequirement -join '::')"
    }
}
Write-Output "PASS: $taskCount game member references resolve; 7 reflected quick-reply, career and replay fields exist"
