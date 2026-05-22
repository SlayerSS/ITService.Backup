# ITBackup — publish single-file exe
#   .\publish.ps1              → dist\ITBackup-net8.exe (~10 MB, needs .NET 8 Desktop Runtime)
#   .\publish.ps1 -SelfContained → dist-full\ITBackup-standalone.exe (~71 MB, runtime included)

param(
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "src\ITService.Backup\ITService.Backup.csproj"
$profile = if ($SelfContained) { "win-x64-singlefile" } else { "win-x64-compact" }
$outName = if ($SelfContained) { "dist-full" } else { "dist" }
$out = Join-Path $root $outName

Write-Host "=== ITBackup publish ($profile) ===" -ForegroundColor Cyan
if (-not $SelfContained) {
    Write-Host "Requires .NET 8 Desktop Runtime on the PC" -ForegroundColor DarkGray
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish $project -c Release -p:PublishProfile=$profile

$exe = Join-Path $out "ITBackup.exe"
if (-not (Test-Path $exe)) {
    Write-Error "ITBackup.exe not found in $out"
}

# Убираем случайные native DLL из publish (VSS только из встроенных ресурсов → ProgramData)
Get-ChildItem $out -Filter "*.dll" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

$releaseName = if ($SelfContained) { "ITBackup-standalone.exe" } else { "ITBackup-net8.exe" }
$releaseExe = Join-Path $out $releaseName
Copy-Item $exe $releaseExe -Force

$sizeMb = [math]::Round((Get-Item $releaseExe).Length / 1MB, 2)
Write-Host ""
Write-Host "Done: $releaseExe" -ForegroundColor Green
Write-Host "Size: $sizeMb MB" -ForegroundColor Green
if (-not $SelfContained) {
    Write-Host ".NET 8 Desktop Runtime: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
}
