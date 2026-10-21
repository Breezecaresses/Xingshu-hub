param (
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$baseDir = Split-Path -Parent $scriptDir
$projPath = Join-Path $baseDir "src\XingShu.Runtime\XingShu.Runtime.csproj"
$msbuild = "D:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"

if (-not (Test-Path $msbuild)) {
    # Fallback to vswhere
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
        if ($vsPath) {
            $msbuild = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
        }
    }
}

if (-not (Test-Path $msbuild)) {
    Write-Error "MSBuild.exe could not be located."
}

Write-Host "Starting build for XingShu.Runtime [$Configuration] using MSBuild..." -ForegroundColor Cyan
& $msbuild $projPath /t:Rebuild /p:Configuration=$Configuration /v:m /nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE"
}

$outDll = Join-Path $baseDir "build\bin\$Configuration\XingShu.Runtime.dll"
if (Test-Path $outDll) {
    $item = Get-Item $outDll
    Write-Host "Build succeeded: $($item.FullName) ($($item.Length) bytes)" -ForegroundColor Green
} else {
    Write-Error "Output DLL not found: $outDll"
}
