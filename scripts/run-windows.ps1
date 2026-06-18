$ErrorActionPreference = "Stop"

$RootDir = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $RootDir

if (!(Test-Path ".venv")) {
    py -3 -m venv .venv
}

$Python = Join-Path $RootDir ".venv\Scripts\python.exe"
if (!(Test-Path $Python)) {
    throw "Cannot find virtualenv Python at $Python"
}

& $Python -c "import fastapi, httpx, uvicorn"
if ($LASTEXITCODE -ne 0) {
    & $Python -m pip install -r services\requirements.txt
}

dotnet run --project src\AskShot.Client\AskShot.Client.csproj -c Release
