$ErrorActionPreference = 'Stop'
$taskRoot = 'E:\工作流\星引擎插件'
$updateDir = Join-Path $taskRoot 'APX-Dev\build\runtime-truth-fix'
$hubSource = Join-Path $taskRoot 'AstralParty-Hub\src-tauri\target\release\xingshu-hub.exe'
$hubTarget = Join-Path $taskRoot 'AstralParty-Hub\release\XingShu\Hub\xingshu-hub.exe'
$steamExe = 'E:\Steam\steamapps\common\Astral Party\8vJXn6CN\AstralParty_CN.exe'
$tapExe = 'F:\TapTap\PC Games\524022\CN_TAPTAP_V3.3.0\吉星派对.exe'
$profiles = @(Get-Content -LiteralPath (Join-Path $taskRoot 'AstralParty-Hub/src-tauri/src/xing_shu/platform/platform_profiles.json') -Raw | ConvertFrom-Json)
$allowedTargets = @($profiles | Where-Object platformId -In @('STEAM_CN', 'TAPTAP') | ForEach-Object {
    $resolvedRoot = [IO.Path]::GetFullPath($_.installRoot).TrimEnd('\') + '\'
    $resolvedTarget = [IO.Path]::GetFullPath((Join-Path $_.installRoot $_.bundleRelativePath))
    if (!$resolvedTarget.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolvedTarget) -ne '__data') { throw 'Unexpected profile target' }
    $resolvedTarget
})
$gameNames = @([IO.Path]::GetFileNameWithoutExtension($steamExe), [IO.Path]::GetFileNameWithoutExtension($tapExe))
$unverifiableGames = @(Get-Process | Where-Object { $_.ProcessName -in $gameNames -and -not $_.Path })
if ($unverifiableGames.Count) {
    throw '游戏仍在运行，当前权限无法核对进程路径；请手动退出双端游戏后再安装。尚未修改安装文件。'
}
$updates = @(Get-Content -LiteralPath (Join-Path $updateDir 'deployment.json') -Raw | ConvertFrom-Json)
if ($updates.Count -ne 2) { throw 'Expected exactly two runtime updates' }
foreach ($update in $updates) {
    if ($allowedTargets -notcontains $update.target) { throw 'Unexpected install path' }
    foreach ($path in @($update.candidate, $update.backup)) {
        $resolved = (Resolve-Path -LiteralPath $path).Path
        if (-not $resolved.StartsWith($updateDir + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Update source escaped workspace' }
    }
    if ((Get-FileHash -LiteralPath $update.target -Algorithm SHA256).Hash -ne $update.beforeSha256) { throw 'Installed runtime changed since preparation' }
    if ((Get-FileHash -LiteralPath $update.backup -Algorithm SHA256).Hash -ne $update.beforeSha256) { throw 'Backup verification failed' }
    if ((Get-FileHash -LiteralPath $update.candidate -Algorithm SHA256).Hash -ne $update.candidateSha256) { throw 'Candidate verification failed' }
}
if ((Get-Item -LiteralPath $hubSource).LastWriteTime -lt (Get-Item -LiteralPath (Join-Path $taskRoot 'AstralParty-Hub\dist\index.html')).LastWriteTime) { throw 'Hub build is not finished' }
$hubBeforeHash = (Get-FileHash -LiteralPath $hubTarget -Algorithm SHA256).Hash
$hubBackup = Join-Path $updateDir ('xingshu-hub.before.' + $hubBeforeHash.Substring(0,12) + '.exe')
if (-not (Test-Path -LiteralPath $hubBackup)) { Copy-Item -LiteralPath $hubTarget -Destination $hubBackup }
if ((Get-FileHash -LiteralPath $hubBackup -Algorithm SHA256).Hash -ne $hubBeforeHash) { throw 'Hub backup verification failed' }
$exactProcesses = @(Get-Process | Where-Object { $_.Path -in @($steamExe, $tapExe, $hubTarget) })
foreach ($process in $exactProcesses) {
    Write-Output ('Stopping exact application: ' + $process.Path)
    $null = $process.CloseMainWindow()
    if (-not $process.WaitForExit(8000)) {
        if ((Get-Process -Id $process.Id -ErrorAction SilentlyContinue).Path -eq $process.Path) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }
    }
}
$installed = @()
try {
    foreach ($update in $updates) {
        $staging = $update.target + '.xingshu-update'
        Copy-Item -LiteralPath $update.candidate -Destination $staging -Force
        [IO.File]::SetAttributes($update.target, ([IO.File]::GetAttributes($update.target) -band (-bnot [IO.FileAttributes]::ReadOnly)))
        Move-Item -LiteralPath $staging -Destination $update.target -Force
        $installed += $update
        if ((Get-FileHash -LiteralPath $update.target -Algorithm SHA256).Hash -ne $update.candidateSha256) { throw 'Installed hash mismatch' }
        Write-Output ($update.platform + ': updated')
    }
    Copy-Item -LiteralPath $hubSource -Destination $hubTarget -Force
    if ((Get-FileHash -LiteralPath $hubSource).Hash -ne (Get-FileHash -LiteralPath $hubTarget).Hash) { throw 'Hub verification failed' }
} catch {
    foreach ($update in $installed) { Copy-Item -LiteralPath $update.backup -Destination $update.target -Force }
    Copy-Item -LiteralPath $hubBackup -Destination $hubTarget -Force
    throw
}
$hub = Start-Process -FilePath $hubTarget -WorkingDirectory (Split-Path -LiteralPath $hubTarget) -PassThru -RedirectStandardOutput (Join-Path $updateDir 'hub.stdout.log') -RedirectStandardError (Join-Path $updateDir 'hub.stderr.log')
Write-Output ('Hub started: pid=' + $hub.Id)
Start-Process -FilePath 'steam://rungameid/2622000'
Write-Output 'Steam game launch requested through the Steam client'
$tapLauncher = 'E:\TapTap\TapTap.exe'
if (Test-Path -LiteralPath $tapLauncher) {
    Start-Process -FilePath $tapLauncher -WorkingDirectory (Split-Path -LiteralPath $tapLauncher)
    Write-Output 'TapTap launcher opened; start the spectator game through the official launcher'
} else {
    Write-Output 'Start the spectator game through the official TapTap launcher; direct exe launch may fail platform validation'
}
