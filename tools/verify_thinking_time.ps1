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
$taskAdded = @($taskNewMethods.Keys | Where-Object { !$taskOldMethods.ContainsKey($_) })
foreach ($taskName in $taskAdded) {
    if ($taskName -notmatch 'XingShuDecisionTimer::Register[012]\(') { throw "Unexpected new method: $taskName" }
}
$taskChanged = @()
foreach($taskName in $taskOldMethods.Keys) {
    if (!$taskNewMethods.ContainsKey($taskName)) { throw "Missing method $taskName" }
    if ((Get-TaskBody $taskOldMethods[$taskName]) -ne (Get-TaskBody $taskNewMethods[$taskName])) {
        $taskChanged += $taskName
        if ($taskName -notmatch 'StaticGlobalData::get_SELECT_ROLE(SKIN)?_TIMELIMIT\(' -and
            $taskName -notmatch 'GameLogic.OperationTimer::ActionDownTime\(' -and
            $taskName -notmatch 'UI.RoomHeroPanel::(Refresh|RefreshSelectSkin)\(' -and
            $taskName -notmatch 'Core.Unit.BattleProperty::OnGoldChanged\(' -and
            $taskName -notmatch 'GameLogic.BattleResultLogic::OnGameFinishS2CServerCallBack\(' -and
            $taskName -notmatch 'GameLogic.OperationTimer/[^:]+::<ActionDownTime>b__[012]\(' -and
            $taskName -notmatch 'GameLogic.OperationTimer::Dispose\(' -and
            $taskName -notmatch 'UI.OperateTimeWindow::UpdateShowProgress\(') { throw "Unexpected method change: $taskName" }
    }
}
foreach ($taskTarget in @(
    @('StaticGlobalData::get_SELECT_ROLE_TIMELIMIT(', 'ThinkingTime::ScaleSeconds'),
    @('StaticGlobalData::get_SELECT_ROLESKIN_TIMELIMIT(', 'ThinkingTime::ScaleSeconds'),
    @('GameLogic.OperationTimer::ActionDownTime(', 'ThinkingTime::ScaleSeconds', 4),
    @('GameLogic.OperationTimer::ActionDownTime(', 'XingShuDecisionTimer::Register'),
    @('GameLogic.OperationTimer::ActionDownTime(', 'ThinkingTime::BeginProgress'),
    @('GameLogic.OperationTimer::Dispose(', 'ThinkingTime::ClearProgress'),
    @('UI.OperateTimeWindow::UpdateShowProgress(', 'ThinkingTime::AllowRemoteProgress'),
    @('UI.RoomHeroPanel::Refresh(', 'XingShuDecisionTimer::Register'),
    @('UI.RoomHeroPanel::RefreshSelectSkin(', 'XingShuDecisionTimer::Register'),
    @('Core.Unit.BattleProperty::OnGoldChanged(', 'BattleStatsOverlay::ObserveGold'),
    @('GameLogic.BattleResultLogic::OnGameFinishS2CServerCallBack(', 'BattleStatsOverlay::ObserveSettlement')
)) {
    $taskMethod = @($taskNewMethods.Values | Where-Object { $_.FullName.Contains($taskTarget[0]) })
    if ($taskMethod.Count -ne 1) { throw "Ambiguous method: $($taskTarget[0])" }
    $taskCalls = @($taskMethod[0].Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains($taskTarget[1]) })
    $taskExpected = if ($taskTarget.Count -gt 2) { $taskTarget[2] } else { 1 }
    if ($taskCalls.Count -ne $taskExpected) { throw "Expected $taskExpected hook(s): $($taskTarget -join ' -> '), found $($taskCalls.Count)" }
}
$taskProgressCallbacks = @($taskNewMethods.Values | Where-Object { $_.FullName -match 'GameLogic.OperationTimer/[^:]+::<ActionDownTime>b__[012]\(' })
if ($taskProgressCallbacks.Count -ne 3) { throw 'Expected exactly 3 operation progress callbacks' }
foreach ($taskMethod in $taskProgressCallbacks) {
    $taskCalls = @($taskMethod.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains('ThinkingTime::UpdateProgress') })
    if ($taskCalls.Count -ne 1) { throw "Missing progress ownership guard: $taskMethod" }
}
$taskWrappers = @($taskNewMethods.Values | Where-Object { $_.DeclaringType.Name -eq 'XingShuDecisionTimer' })
if ($taskWrappers.Count -ne 3) { throw 'Expected exactly 3 decision-clock wrappers' }
foreach ($taskMethod in $taskWrappers) {
    $taskCalls = @($taskMethod.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains('ThinkingClock::.ctor') })
    if ($taskCalls.Count -ne 1) { throw "Missing real clock: $taskMethod" }
}
Write-Output "PASS: 3 real-clock wrappers, 3 duration hooks, 2 statistics observers, operation progress ownership guards; $($taskOldMethods.Count - $taskChanged.Count) existing method bodies unchanged ($($taskChanged.Count) changed)"
$taskOld.Dispose(); $taskNew.Dispose()
