# Build NSIS Installer
# 依赖：NSIS 已安装（choco install nsis 或 winget install NSIS.NSIS）
# 先运行 package-windows.ps1 生成产物，再调用 makensis 打包

$ErrorActionPreference = "Stop"

param(
    [switch] $SkipPackage = $false
)

$RootDir = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $RootDir

$Version = $env:VERSION
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "0.0.1"
}

$AppDir = Join-Path $RootDir "out\package\windows\app"

# ── 1. 生成 app 目录（如果未跳过且产物不存在） ───────────
if (-not $SkipPackage) {
    if ((Test-Path (Join-Path $AppDir "AskShot.Client.exe")) -and
        (Test-Path (Join-Path $AppDir "service\askshot-service.exe"))) {
        Write-Host "[1/3] 跳过构建，产物已存在" -ForegroundColor Gray
    } else {
        Write-Host "[1/3] 构建应用产物..." -ForegroundColor Cyan
        & "$PSScriptRoot\..\scripts\package-windows.ps1"
    }
} else {
    Write-Host "[1/3] 跳过构建 (--SkipPackage)" -ForegroundColor Gray
}

if (!(Test-Path (Join-Path $AppDir "AskShot.Client.exe"))) {
    throw "打包失败：未找到 AskShot.Client.exe"
}
if (!(Test-Path (Join-Path $AppDir "service\askshot-service.exe"))) {
    throw "打包失败：未找到 service/askshot-service.exe"
}
Write-Host "  产物目录: $AppDir" -ForegroundColor Green

# ── 2. 查找 NSIS ──────────────────────────────────────────
$Makensis = $null
$Candidates = @(
    "makensis",
    "C:\Program Files (x86)\NSIS\makensis.exe",
    "C:\Program Files\NSIS\makensis.exe"
)
foreach ($c in $Candidates) {
    try {
        $null = Get-Command $c -ErrorAction Stop
        $Makensis = $c
        break
    } catch { }
}
if (!$Makensis) {
    throw @"
未找到 NSIS (makensis.exe)。
请安装 NSIS：
  winget install NSIS.NSIS
  或 choco install nsis
"@
}
Write-Host "[2/3] NSIS 路径: $Makensis" -ForegroundColor Green

# ── 3. 运行 makensis ──────────────────────────────────────
$NsiFile = Join-Path $RootDir "installer\askshot.nsi"
Write-Host "[3/3] 打包安装程序..." -ForegroundColor Cyan
& $Makensis "/DVERSION=$Version" $NsiFile

if ($LASTEXITCODE -ne 0) {
    throw "makensis 返回错误码 $LASTEXITCODE"
}

$InstallerFile = Join-Path $RootDir "out\AskShot-$Version-windows-x64-installer.exe"
if (Test-Path $InstallerFile) {
    $Size = [math]::Round((Get-Item $InstallerFile).Length / 1MB, 1)
    Write-Host ""
    Write-Host "✓ 安装程序已生成: $InstallerFile ($Size MB)" -ForegroundColor Green
} else {
    Write-Host "⚠ 未找到安装程序输出文件" -ForegroundColor Yellow
}
