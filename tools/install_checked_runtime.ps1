param([Parameter(Mandatory)][string]$CandidateDirectory, [switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskBuild = (Resolve-Path -LiteralPath $CandidateDirectory).Path
if (!$taskBuild.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Candidate directory escaped workspace' }
$taskProfiles = @(Get-Content -LiteralPath (Join-Path $taskRoot 'AstralParty-Hub/src-tauri/src/xing_shu/platform/platform_profiles.json') -Raw | ConvertFrom-Json)
$taskItems = @(Get-Content -LiteralPath (Join-Path $taskBuild 'deployment.json') -Raw | ConvertFrom-Json)
if ($taskItems.Count -lt 1 -or $taskItems.Count -gt 4 -or @($taskItems.platform | Sort-Object -Unique).Count -ne $taskItems.Count) { throw 'Invalid channel manifest' }
function Assert-GameClosed {
    if (Get-Process -Name AstralParty_CN,吉星派对 -ErrorAction SilentlyContinue) {
        throw 'Game is running; exit normally before installing. No process will be stopped.'
    }
}
function Task-Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
Assert-GameClosed
foreach ($taskItem in $taskItems) {
    if ($taskItem.platform -notin @('STEAM_CN','TAPTAP','BILIBILI','WEGAME')) { throw 'Unsupported platform' }
    $taskProfile = @($taskProfiles | Where-Object platformId -eq $taskItem.platform)
    if ($taskProfile.Count -ne 1) { throw 'Missing or ambiguous platform profile' }
    $taskPrefix = [IO.Path]::GetFullPath($taskProfile[0].installRoot).TrimEnd('\') + '\'
    $taskTarget = [IO.Path]::GetFullPath((Join-Path $taskPrefix $taskProfile[0].bundleRelativePath))
    if (!$taskTarget.StartsWith($taskPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($taskTarget) -ne '__data' -or $taskTarget -ne [IO.Path]::GetFullPath($taskItem.target)) { throw 'Unexpected game target' }
    foreach ($taskSource in @($taskItem.candidate, $taskItem.backup)) {
        if (!(Resolve-Path -LiteralPath $taskSource).Path.StartsWith($taskBuild + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Source escaped candidate directory' }
    }
    if ((Task-Hash $taskItem.candidate) -ne $taskItem.candidateSha256 -or
        (Task-Hash $taskItem.backup) -ne $taskItem.beforeSha256) { throw 'Candidate/backup hash mismatch' }
    if ((Task-Hash (Join-Path $taskPrefix $taskProfile[0].gameRuntimeDllPath)) -ne $taskProfile[0].gameRuntimeDllSha256.ToLowerInvariant()) { throw 'Native game binary changed since audit' }
    if ($taskItem.resourceVersion -ne $taskProfile[0].resourceVersion) { throw 'Candidate/profile mismatch' }
    $taskCurrent = Task-Hash $taskItem.target
    if ($taskCurrent -ne $taskItem.beforeSha256 -and $taskCurrent -ne $taskItem.candidateSha256) { throw 'Installed runtime changed since preparation' }
}
if ($CheckOnly) {
    $taskItems | Select-Object platform, resourceVersion, target, candidateSha256
    return
}
$taskChanged = [Collections.Generic.List[object]]::new()
try {
    foreach ($taskItem in $taskItems) {
        Assert-GameClosed
        $taskCurrent = Task-Hash $taskItem.target
        if ($taskCurrent -eq $taskItem.candidateSha256) { continue }
        if ($taskCurrent -ne $taskItem.beforeSha256) { throw 'Game files changed immediately before installation' }
        $taskChanged.Add($taskItem)
        Copy-Item -LiteralPath $taskItem.candidate -Destination $taskItem.target -Force
        if ((Task-Hash $taskItem.target) -ne $taskItem.candidateSha256) { throw 'Installed hash mismatch' }
        Write-Output ($taskItem.platform + ': installed and SHA256 verified')
    }
    [pscustomobject]@{InstalledAt=(Get-Date).ToString('o');Files=@($taskItems)} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskBuild 'installed.json') -Encoding utf8
} catch {
    for ($taskIndex = $taskChanged.Count - 1; $taskIndex -ge 0; $taskIndex--) {
        $taskItem = $taskChanged[$taskIndex]
        if ((Task-Hash $taskItem.backup) -ne $taskItem.beforeSha256) { throw 'Rollback backup mismatch; preserve files for recovery' }
        Copy-Item -LiteralPath $taskItem.backup -Destination $taskItem.target -Force
        if ((Task-Hash $taskItem.target) -ne $taskItem.beforeSha256) { throw 'Rollback verification failed' }
    }
    throw
}
