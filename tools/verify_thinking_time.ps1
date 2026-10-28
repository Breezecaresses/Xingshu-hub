param([Parameter(Mandatory)][string]$Before, [Parameter(Mandatory)][string]$After)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $taskRoot 'XingShu-Dev/tools/lib/Mono.Cecil.dll')
$taskLegacy = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'legacy_runtime_names.json') -Raw | ConvertFrom-Json
function Get-TaskMethods($types) {
    foreach($type in $types) {
        foreach($method in $type.Methods) { if ($method.HasBody) { $method } }
        Get-TaskMethods $type.NestedTypes
    }
}
function Get-TaskBody($method) {
    $taskBody = (@($method.Body.Instructions | ForEach-Object { $_.ToString() }) +
     @($method.Body.Variables | ForEach-Object { $_.VariableType.FullName }) +
     @($method.Body.ExceptionHandlers | ForEach-Object {
         "$($_.HandlerType):$($_.TryStart):$($_.TryEnd):$($_.HandlerStart):$($_.HandlerEnd):$($_.FilterStart):$($_.CatchType)"
     })) -join "`n"
    # A verified upgrade changes old hook type references without changing instructions.
    $taskBody.Replace($taskLegacy.runtimeNamespace + '.', 'XingShuRuntime.')
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
            $taskName -notmatch 'GameLogic.BattleLogic::OnGetHeroInfoS2CServerCallBackAsync\(' -and
            $taskName -notmatch 'GameLogic.BattleLogic::On(RoundStart|ActionStartNotify)S2CServerCallBack\(' -and
            $taskName -notmatch 'GameLogic.CardLogic::OnUseEffectCardS2CServerCallBack\(' -and
            $taskName -notmatch 'GameLogic.ActionLogic::OnUpdateHeroAttrS2CServerCallBack\(' -and
            $taskName -notmatch 'GameLogic.AccountLogic::OnGet(ShowPlayer|PlayerFightRecord)S2CServerCallBack\(' -and
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
    @('GameLogic.BattleResultLogic::OnGameFinishS2CServerCallBack(', 'BattleStatsOverlay::ObserveSettlement'),
    @('GameLogic.BattleLogic::OnGetHeroInfoS2CServerCallBackAsync(', 'BattleStatsOverlay::ObserveHeroInfo'),
    @('GameLogic.ActionLogic::OnUpdateHeroAttrS2CServerCallBack(', 'BattleStatsOverlay::ObserveHealingSources'),
    @('GameLogic.BattleLogic::OnGetHeroInfoS2CServerCallBackAsync(', 'MonsterCooldownRuntime::ObserveHeroInfo'),
    @('GameLogic.ActionLogic::OnUpdateHeroAttrS2CServerCallBack(', 'MonsterCooldownRuntime::ObserveAttributes'),
    @('GameLogic.CardLogic::OnUseEffectCardS2CServerCallBack(', 'MonsterCooldownRuntime::ObserveUse'),
    @('GameLogic.BattleLogic::OnRoundStartS2CServerCallBack(', 'MonsterCooldownRuntime::ObserveRound'),
    @('GameLogic.BattleLogic::OnActionStartNotifyS2CServerCallBack(', 'MonsterCooldownRuntime::ObserveAction'),
    @('GameLogic.AccountLogic::OnGetShowPlayerS2CServerCallBack(', 'GameLibraryReplies::ObserveCareer'),
    @('GameLogic.AccountLogic::OnGetPlayerFightRecordS2CServerCallBack(', 'GameLibraryReplies::ObserveDetail')
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
function Get-TaskInstructions($method, $skip) {
    $taskInstructions = $method.Body.Instructions
    $taskPositions = @{}
    for ($taskIndex = $skip; $taskIndex -lt $taskInstructions.Count; $taskIndex++) {
        $taskPositions[$taskInstructions[$taskIndex]] = $taskIndex - $skip
    }
    @($taskInstructions | Select-Object -Skip $skip | ForEach-Object {
        $taskOperand = $_.Operand
        if ($taskOperand -is [Mono.Cecil.Cil.Instruction]) {
            $taskOperand = 'target:' + $taskPositions[$taskOperand]
        } elseif ($taskOperand -is [Mono.Cecil.Cil.Instruction[]]) {
            $taskOperand = 'targets:' + (($taskOperand | ForEach-Object { $taskPositions[$_] }) -join ',')
        }
        $_.OpCode.Code.ToString() + ':' + $taskOperand
    }) -join "`n"
}
function Get-TaskObserverPrefix($method, $allowed) {
    $taskInstructions = $method.Body.Instructions
    $taskSkip = 0
    while ($taskInstructions.Count -gt $taskSkip + 4 -and
        $taskInstructions[$taskSkip + 3].OpCode.Code.ToString() -eq 'Call' -and
        @($allowed | Where-Object { $taskInstructions[$taskSkip + 3].Operand.FullName.Contains($_) }).Count -gt 0) {
        if ($taskInstructions[$taskSkip].OpCode.Code.ToString() -notin @('Ldarg', 'Ldarg_S') -or
            $taskInstructions[$taskSkip].Operand.Index -ne 1 -or
            $taskInstructions[$taskSkip + 1].OpCode.Code.ToString() -notin @('Brtrue', 'Brtrue_S') -or
            ![object]::ReferenceEquals($taskInstructions[$taskSkip + 1].Operand, $taskInstructions[$taskSkip + 4]) -or
            $taskInstructions[$taskSkip + 2].OpCode.Code.ToString() -notin @('Ldarg', 'Ldarg_S') -or
            $taskInstructions[$taskSkip + 2].Operand.Index -ne 0) {
            throw "Observer must skip errors and pass only the decoded message: $($method.FullName)"
        }
        $taskSkip += 4
    }
    return $taskSkip
}
foreach ($taskReplyTarget in @(
    @('GameLogic.BattleLogic::OnGetHeroInfoS2CServerCallBackAsync(', @('BattleStatsOverlay::ObserveHeroInfo', 'MonsterCooldownRuntime::ObserveHeroInfo')),
    @('GameLogic.ActionLogic::OnUpdateHeroAttrS2CServerCallBack(', @('BattleStatsOverlay::ObserveHealingSources', 'MonsterCooldownRuntime::ObserveAttributes')),
    @('GameLogic.CardLogic::OnUseEffectCardS2CServerCallBack(', @('MonsterCooldownRuntime::ObserveUse')),
    @('GameLogic.BattleLogic::OnRoundStartS2CServerCallBack(', @('MonsterCooldownRuntime::ObserveRound')),
    @('GameLogic.BattleLogic::OnActionStartNotifyS2CServerCallBack(', @('MonsterCooldownRuntime::ObserveAction'))
)) {
    $taskReplyNew = @($taskNewMethods.Values | Where-Object { $_.FullName.Contains($taskReplyTarget[0]) })[0]
    $taskReplyOld = $taskOldMethods[$taskReplyNew.FullName]
    if (!$taskReplyOld) { throw 'Missing original protocol handler' }
    $taskNewSkip = Get-TaskObserverPrefix $taskReplyNew $taskReplyTarget[1]
    if ($taskNewSkip -ne 4 * $taskReplyTarget[1].Count) { throw "Missing or duplicated observer prefix: $($taskReplyTarget[0])" }
    $taskOldSkip = Get-TaskObserverPrefix $taskReplyOld $taskReplyTarget[1]
    if ((Get-TaskInstructions $taskReplyOld $taskOldSkip) -ne (Get-TaskInstructions $taskReplyNew $taskNewSkip)) {
        throw "Observer changed the original handler body: $($taskReplyTarget[0])"
    }
}
Write-Output "PASS: 3 real-clock wrappers, 3 duration hooks, 4 statistics and 5 cooldown observers (error guards and original handlers preserved), 2 account reply observers, operation progress ownership guards; $($taskOldMethods.Count - $taskChanged.Count) existing method bodies unchanged ($($taskChanged.Count) changed)"
$taskOld.Dispose(); $taskNew.Dispose()
