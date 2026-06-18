$ErrorActionPreference = "Stop"

$RootDir = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $RootDir

$Version = $env:VERSION
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "0.0.1"
}

$OutDir = Join-Path $RootDir "out\package\windows"
$ServiceDist = Join-Path $OutDir "service-dist"
$AppDir = Join-Path $OutDir "app"
$Artifact = Join-Path $RootDir "out\AskShot-$Version-windows-x64.zip"

Remove-Item -Recurse -Force $OutDir -ErrorAction SilentlyContinue
Remove-Item -Force $Artifact -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $OutDir | Out-Null

$PackageVenv = Join-Path $RootDir ".venv-package-windows"
if (!(Test-Path $PackageVenv)) {
    python -m venv $PackageVenv
}
$Python = Join-Path $PackageVenv "Scripts\python.exe"

& $Python -m pip install --upgrade pip
& $Python -m pip install pyinstaller -r services\requirements.txt

& $Python -m PyInstaller `
    --noconfirm `
    --clean `
    --onefile `
    --name askshot-service `
    --distpath $ServiceDist `
    --workpath (Join-Path $OutDir "build-service") `
    services\main.py

dotnet publish src\AskShot.Client\AskShot.Client.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $AppDir

$ServiceDir = Join-Path $AppDir "service"
New-Item -ItemType Directory -Force $ServiceDir | Out-Null
Copy-Item (Join-Path $ServiceDist "askshot-service.exe") (Join-Path $ServiceDir "askshot-service.exe")

@"
AskShot Windows portable build.

First run:
1. Run AskShot.Client.exe.
2. Configure your VLM API in the tray Console.
3. Use Ctrl+Shift+A to capture and explain.
"@ | Set-Content -Encoding UTF8 (Join-Path $AppDir "README.txt")

New-Item -ItemType Directory -Force (Join-Path $RootDir "out") | Out-Null
Compress-Archive -Path (Join-Path $AppDir "*") -DestinationPath $Artifact -Force
Write-Output $Artifact
