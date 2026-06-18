#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

VERSION="${VERSION:-0.0.1}"
OUT_DIR="$ROOT_DIR/out/package/macos"
APP_DIST="$OUT_DIR/app-dist"
SERVICE_DIST="$OUT_DIR/service-dist"
PACKAGE_DIR="$OUT_DIR/AskShot-$VERSION-macos"
ARTIFACT="$ROOT_DIR/out/AskShot-$VERSION-macos-x64.zip"

rm -rf "$OUT_DIR" "$ARTIFACT"
mkdir -p "$OUT_DIR" "$PACKAGE_DIR"

PACKAGE_VENV="$ROOT_DIR/.venv-package-macos"
PYTHON_BIN="${PYTHON:-python3}"
if [[ ! -d "$PACKAGE_VENV" ]]; then
  "$PYTHON_BIN" -m venv "$PACKAGE_VENV"
fi
source "$PACKAGE_VENV/bin/activate"

python -m pip install --upgrade pip
python -m pip install pyinstaller -r src/AskShot.Mac/requirements.txt

python -m PyInstaller \
  --noconfirm \
  --clean \
  --onefile \
  --name askshot-service \
  --distpath "$SERVICE_DIST" \
  --workpath "$OUT_DIR/build-service" \
  services/main.py

python -m PyInstaller \
  --noconfirm \
  --clean \
  --windowed \
  --name AskShot \
  --distpath "$APP_DIST" \
  --workpath "$OUT_DIR/build-app" \
  src/AskShot.Mac/askshot_mac.py

mkdir -p "$APP_DIST/AskShot.app/Contents/Resources"
cp "$SERVICE_DIST/askshot-service" "$APP_DIST/AskShot.app/Contents/Resources/askshot-service"
chmod +x "$APP_DIST/AskShot.app/Contents/Resources/askshot-service"

codesign --force --deep --sign - "$APP_DIST/AskShot.app" || true

cp -R "$APP_DIST/AskShot.app" "$PACKAGE_DIR/AskShot.app"
cat > "$PACKAGE_DIR/README.txt" <<'EOF'
AskShot macOS portable build.

First run:
1. Move AskShot.app to Applications if desired.
2. Open AskShot.app.
3. Grant Accessibility and Screen Recording permissions when prompted.

This build is ad-hoc signed, not notarized.
EOF

mkdir -p "$ROOT_DIR/out"
ditto -c -k --sequesterRsrc --keepParent "$PACKAGE_DIR" "$ARTIFACT"
echo "$ARTIFACT"
