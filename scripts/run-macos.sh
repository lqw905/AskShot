#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

if [[ ! -d ".venv" ]]; then
  python3 -m venv .venv
fi

source .venv/bin/activate

if ! python - <<'PY'
import fastapi
import httpx
import pynput
import PySide6
PY
then
  python -m pip install -r src/AskShot.Mac/requirements.txt
fi

exec python src/AskShot.Mac/askshot_mac.py
