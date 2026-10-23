param([switch]$CheckOnly, [string]$CandidateDirectory, [switch]$ManagedOnly)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskBuild = Join-Path $taskRoot 'XingShu-Dev/build/runtime-truth-fix'
if ($CandidateDirectory) {
    $taskBuild = [IO.Path]::GetFullPath($CandidateDirectory)
    if (!$taskBuild.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Candidate directory escaped workspace' }
}
$taskVersionSource = Get-Content -LiteralPath (Join-Path $taskRoot 'XingShu-Dev/src/XingShu.Runtime/XingShu/Runtime/XingShuRuntimeHost.cs') -Raw
$taskVersionMatch = [regex]::Match($taskVersionSource, '#else\s+public const string RuntimeVersion = "(1\.0\.0(?:-game-ui-(r[0-9]+))?)";')
if (!$taskVersionMatch.Success) { throw 'Unknown game UI candidate version' }
$taskVersion = $taskVersionMatch.Groups[1].Value
$taskRecordName = if ($taskVersionMatch.Groups[2].Success) { 'game-ui-' + $taskVersionMatch.Groups[2].Value + '-installed.json' } else { 'release-' + $taskVersion + '-installed.json' }
function Assert-GameClosed {
    if (Get-Process -Name AstralParty_CN,吉星派对 -ErrorAction SilentlyContinue) {
        throw 'Game is running; exit normally before installing. No process will be stopped.'
    }
}
function Task-Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Task-CurrentHash([string]$Path) { if (Test-Path -LiteralPath $Path -PathType Leaf) { Task-Hash $Path } else { $null } }
Assert-GameClosed
$taskExpected = @{
    STEAM_CN = 'E:\Steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN_Data\com.unity.addressables\AssetBundles\57a198703b66364d5420d39c0d251747\3486f99276eba77677d4880fd8d32918\__data'
    TAPTAP = 'F:\TapTap\PC Games\524022\CN_TAPTAP_V3.3.0\吉星派对_Data\com.unity.addressables\AssetBundles\57a198703b66364d5420d39c0d251747\71c9197f5dd65c9b30bdf1e716719991\__data'
}
$taskManifest = @(Get-Content -LiteralPath (Join-Path $taskBuild 'deployment.json') -Raw | ConvertFrom-Json)
if ($taskManifest.Count -ne 2 -or (@($taskManifest.platform | Sort-Object -Unique).Count -ne 2)) { throw 'Expected exactly Steam CN and TapTap candidates' }
$taskPlan = [Collections.Generic.List[object]]::new()
foreach ($taskItem in $taskManifest) {
    if (!$taskExpected.ContainsKey($taskItem.platform) -or
        [IO.Path]::GetFullPath($taskItem.target) -ne $taskExpected[$taskItem.platform]) { throw 'Unexpected game target' }
    foreach ($taskSource in @($taskItem.candidate, $taskItem.backup)) {
        if (![IO.Path]::GetFullPath($taskSource).StartsWith($taskBuild + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Source escaped candidate directory' }
    }
    if ((Task-Hash $taskItem.candidate) -ne $taskItem.candidateSha256 -or (Task-Hash $taskItem.backup) -ne $taskItem.beforeSha256) { throw 'Candidate or backup hash mismatch' }
    $taskCurrent = Task-Hash $taskItem.target
    if ($taskCurrent -ne $taskItem.beforeSha256 -and $taskCurrent -ne $taskItem.candidateSha256) { throw "Unknown installed runtime: $($taskItem.platform)" }
    $taskPlan.Add([pscustomobject]@{ Kind=$taskItem.platform; Target=$taskItem.target; Source=$taskItem.candidate; Backup=$taskItem.backup; Before=$taskCurrent; After=$taskItem.candidateSha256 })
}
if (!$ManagedOnly) {
$taskNative = Join-Path $taskRoot 'XingShu-Dev/build/native-speed-10x/release/version.dll'
$taskNativeHash = '36e7df0fce281c95dccd237760fdc982fba3d56d25883d55ded86b87a0588e20'
if ((Task-Hash $taskNative) -ne $taskNativeHash) { throw 'Unknown native 10x component' }
foreach ($taskTarget in @('E:\Steam\steamapps\common\Astral Party\8vJXn6CN\version.dll', 'F:\TapTap\PC Games\524022\CN_TAPTAP_V3.3.0\version.dll')) {
    if (!(Test-Path -LiteralPath (Split-Path -Parent $taskTarget) -PathType Container)) { throw 'Game root missing' }
    $taskBefore = Task-CurrentHash $taskTarget
    $taskBackup = $null
    if ($taskBefore) {
        $taskBackup = Join-Path $taskBuild ('native.before.' + $taskBefore + '.dll')
        if (!(Test-Path -LiteralPath $taskBackup)) { Copy-Item -LiteralPath $taskTarget -Destination $taskBackup }
        if ((Task-Hash $taskBackup) -ne $taskBefore) { throw 'Native backup hash mismatch' }
    }
    $taskPlan.Add([pscustomobject]@{ Kind='Native10x'; Target=$taskTarget; Source=$taskNative; Backup=$taskBackup; Before=$taskBefore; After=$taskNativeHash })
}
}
if ($CheckOnly) { $taskPlan | Select-Object Kind, Target, Before, After; return }
$taskChanged = [Collections.Generic.List[object]]::new()
try {
    foreach ($taskItem in $taskPlan) {
        Assert-GameClosed
        if ((Task-CurrentHash $taskItem.Target) -ne $taskItem.Before) { throw 'Installed files changed since validation' }
        if ($taskItem.Before -eq $taskItem.After) { continue }
        $taskChanged.Add($taskItem)
        Copy-Item -LiteralPath $taskItem.Source -Destination $taskItem.Target -Force
        if ((Task-Hash $taskItem.Target) -ne $taskItem.After) { throw 'Installed hash mismatch' }
        Write-Output "Installed $($taskItem.Kind): $($taskItem.After)"
    }
    [pscustomobject]@{ InstalledAt=(Get-Date).ToString('o'); Version=$taskVersion; Files=@($taskPlan.ToArray()) } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskBuild $taskRecordName) -Encoding utf8
} catch {
    for ($taskIndex = $taskChanged.Count - 1; $taskIndex -ge 0; $taskIndex--) {
        $taskItem = $taskChanged[$taskIndex]
        if ($taskItem.Before) {
            if ((Task-Hash $taskItem.Backup) -ne $taskItem.Before) { throw 'Rollback backup mismatch; preserve all files for manual recovery' }
            Copy-Item -LiteralPath $taskItem.Backup -Destination $taskItem.Target -Force
        } else {
            if (Test-Path -LiteralPath $taskItem.Target -PathType Leaf) { Remove-Item -LiteralPath $taskItem.Target -Force }
        }
        if ((Task-CurrentHash $taskItem.Target) -ne $taskItem.Before) { throw 'Rollback verification failed' }
    }
    throw
}
