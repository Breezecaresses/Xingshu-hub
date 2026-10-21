$ErrorActionPreference = 'Stop'
$taskRoot = 'E:\工作流\星引擎插件'
$updateDir = Join-Path $taskRoot 'XingShu-Dev\build\runtime-truth-fix'
$source = Join-Path $taskRoot 'AstralParty-Hub\src-tauri\target\release\xingshu-hub.exe'
$target = Join-Path $taskRoot 'AstralParty-Hub\release\XingShu\Hub\xingshu-hub.exe'
if ((Get-Item -LiteralPath $source).LastWriteTime -lt (Get-Item -LiteralPath (Join-Path $taskRoot 'AstralParty-Hub\dist\index.html')).LastWriteTime) { throw 'Hub build is not finished' }
$current = @(Get-Process xingshu-hub -ErrorAction SilentlyContinue | Where-Object Path -eq $target)
foreach ($process in $current) {
    Stop-Process -Id $process.Id -Force
    $process.WaitForExit()
}
Copy-Item -LiteralPath $source -Destination $target -Force
if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $source).Hash) { throw 'Hub hash mismatch' }
$env:RUST_LOG = 'info'
$started = Start-Process -FilePath $target -WorkingDirectory (Split-Path -LiteralPath $target) -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $updateDir 'hub.stdout.log') -RedirectStandardError (Join-Path $updateDir 'hub.stderr.log')
Write-Output ('Hub updated and restarted: PID=' + $started.Id + '; game processes untouched')
