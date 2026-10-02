#!/usr/bin/env bash
# Builds WatchCrawler.prg with your real serverUrl/sharedKey baked in.
# Normally run for you by ../../setup.sh; run it directly to rebuild.
#
# Why this exists: Garmin Connect Mobile's app-settings UI is generated from
# the Connect IQ Store's registered manifest for an app - a sideloaded/
# private app (like this one) shows "No settings" there, so there's no way
# to configure serverUrl/sharedKey after installing. This script bakes your
# real values into a build instead, by copying the whole watch/ project into
# a gitignored directory and editing THAT copy's properties.xml - the
# tracked resources/properties/properties.xml (committed to git) always
# stays empty/safe.
#
# Usage:
#   export WATCHCRAWLER_SERVER_URL=https://your-watchcrawler.fly.dev
#   export WATCHCRAWLER_SHARED_KEY=<your WATCH_SHARED_KEY>
#   watch/tools/build_personal.sh [--dev] <device-id>      # e.g. fenix7
#
#   --dev   developer build: keeps the diagnostics screen (MENU), the fake
#           achievement injector, remote test trigger polling and the settings
#           entries (see dev.jungle). Without it you get the user build.
#   --store store package: a release user build for EVERY device in the
#           manifest, as watch/.personal-build/bin/WatchCrawler.iq - what you
#           upload to the Connect IQ Store. No device id needed. Bake in the
#           hosted server's public URL and shared key (see server/README.md,
#           "Hosted mode").
#
# Output: watch/.personal-build/bin/WatchCrawler.prg - copy that to
# GARMIN/Apps on your watch.

set -euo pipefail

DEV=0
STORE=0
case "${1:-}" in
    --dev) DEV=1; shift ;;
    --store) STORE=1; shift ;;
esac

if [ -z "${WATCHCRAWLER_SERVER_URL:-}" ] || [ -z "${WATCHCRAWLER_SHARED_KEY:-}" ]; then
    echo "Set WATCHCRAWLER_SERVER_URL and WATCHCRAWLER_SHARED_KEY first (setup.sh does this for you)." >&2
    exit 1
fi

DEVICE="${1:-}"
if [ "$STORE" = 0 ] && [ -z "$DEVICE" ]; then
    echo "Usage: build_personal.sh [--dev] <device-id> (e.g. fenix7), or build_personal.sh --store" >&2
    exit 1
fi
WATCH_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_DIR="$WATCH_DIR/.personal-build"

# ---- find the Connect IQ compiler ------------------------------------------
find_monkeyc() {
    if command -v monkeyc >/dev/null 2>&1; then command -v monkeyc; return; fi
    if command -v monkeyc.bat >/dev/null 2>&1; then command -v monkeyc.bat; return; fi
    local roots=(
        "${APPDATA:-}/Garmin/ConnectIQ/Sdks"                         # Windows
        "$HOME/Library/Application Support/Garmin/ConnectIQ/Sdks"   # macOS
        "$HOME/.Garmin/ConnectIQ/Sdks"                              # Linux
    )
    local root sdk
    for root in "${roots[@]}"; do
        [ -d "$root" ] || continue
        # Newest SDK first (names embed the version and date).
        sdk="$(ls -1d "$root"/*/ 2>/dev/null | sort -r | head -1)"
        [ -n "$sdk" ] || continue
        for exe in monkeyc monkeyc.bat; do
            if [ -f "$sdk/bin/$exe" ]; then echo "$sdk/bin/$exe"; return; fi
        done
    done
}

MONKEYC="$(find_monkeyc || true)"
if [ -z "$MONKEYC" ]; then
    cat >&2 <<'EOF'
Garmin's Connect IQ SDK was not found. One-time install:
  1. Download the SDK Manager: https://developer.garmin.com/connect-iq/sdk/
  2. Open it, accept the license, install the latest SDK, and under "Devices"
     download your watch model.
  3. Install Java if you don't have it (https://adoptium.net - "Temurin 17 LTS").
  4. Run this again.
EOF
    exit 2
fi

if ! command -v java >/dev/null 2>&1; then
    echo "Java is needed by the Garmin compiler. Install it from https://adoptium.net (Temurin 17 LTS) and run this again." >&2
    exit 2
fi

PYTHON="$(command -v python3 || command -v python || true)"
if [ -z "$PYTHON" ]; then
    echo "Python 3 is needed for this build step. Install it from https://www.python.org/downloads/ and run this again." >&2
    exit 2
fi

# ---- signing key (every Connect IQ build must be signed; any key works for sideloading) ----
if [ ! -f "$WATCH_DIR/keys/developer_key.der" ]; then
    if ! command -v openssl >/dev/null 2>&1; then
        echo "openssl is needed once to create your signing key (it comes with Git for Windows / macOS)." >&2
        exit 2
    fi
    echo "Creating your personal signing key in watch/keys/ (one time, never shared)..."
    mkdir -p "$WATCH_DIR/keys"
    openssl genrsa -out "$WATCH_DIR/keys/developer_key.pem" 4096 2>/dev/null
    openssl pkcs8 -topk8 -inform PEM -outform DER -in "$WATCH_DIR/keys/developer_key.pem" \
        -out "$WATCH_DIR/keys/developer_key.der" -nocrypt
fi

# ---- copy the project and bake in the values ------------------------------
rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"
cp -r "$WATCH_DIR"/{manifest.xml,monkey.jungle,dev.jungle,resources,resources-dev,source,keys} "$BUILD_DIR/"

PROPS="$BUILD_DIR/resources/properties/properties.xml"
"$PYTHON" - "$PROPS" "$WATCHCRAWLER_SERVER_URL" "$WATCHCRAWLER_SHARED_KEY" << 'PYEOF'
import sys
from xml.sax.saxutils import escape
path, url, key = sys.argv[1], escape(sys.argv[2].rstrip("/")), escape(sys.argv[3])
text = open(path, encoding="utf-8").read()
text = text.replace(
    '<property id="serverUrl" type="string"></property>',
    '<property id="serverUrl" type="string">' + url + '</property>',
)
text = text.replace(
    '<property id="sharedKey" type="string"></property>',
    '<property id="sharedKey" type="string">' + key + '</property>',
)
open(path, "w", encoding="utf-8").write(text)
PYEOF

"$PYTHON" - "$BUILD_DIR/resources/strings/strings.xml" "$WATCHCRAWLER_SERVER_URL" "$WATCHCRAWLER_SHARED_KEY" << 'PYEOF'
import sys
from xml.sax.saxutils import escape
path, url, key = sys.argv[1], escape(sys.argv[2].rstrip("/")), escape(sys.argv[3])
t = open(path, encoding="utf-8").read()
t = t.replace('<string id="CfgServerUrl"></string>', '<string id="CfgServerUrl">' + url + '</string>')
t = t.replace('<string id="CfgSharedKey"></string>', '<string id="CfgSharedKey">' + key + '</string>')
open(path, "w", encoding="utf-8").write(t)
PYEOF

# Shown on the diagnostics screen and in error reports: git hash (+ if uncommitted changes), build time, -dev.
BUILD_STAMP="$(git -C "$WATCH_DIR" rev-parse --short HEAD 2>/dev/null || echo nogit)$(git -C "$WATCH_DIR" diff --quiet 2>/dev/null || echo '+')-$(date +%m%d-%H%M)"
[ "$DEV" = 1 ] && BUILD_STAMP="$BUILD_STAMP-dev"
sed -i.bak "s/STAMP = \"dev\"/STAMP = \"$BUILD_STAMP\"/" "$BUILD_DIR/source/BuildInfo.mc" && rm -f "$BUILD_DIR/source/BuildInfo.mc.bak"

JUNGLE="monkey.jungle"
[ "$DEV" = 1 ] && JUNGLE="monkey.jungle;dev.jungle"

mkdir -p "$BUILD_DIR/bin"
cd "$BUILD_DIR"
if [ "$STORE" = 1 ]; then
    # -e packages every manifest device into one .iq; -r strips (:debug) code (the simulator self-test).
    "$MONKEYC" -f "$JUNGLE" -e -r -o bin/WatchCrawler.iq -y keys/developer_key.der -w
    echo
    echo "Built: $BUILD_DIR/bin/WatchCrawler.iq (build $BUILD_STAMP, store package)"
    echo "Upload it at https://apps.garmin.com/developer/upload - every later upload must be signed with the same key."
    exit 0
fi
"$MONKEYC" -f "$JUNGLE" -d "$DEVICE" -o bin/WatchCrawler.prg -y keys/developer_key.der -w

echo
echo "Built: $BUILD_DIR/bin/WatchCrawler.prg (build $BUILD_STAMP$([ "$DEV" = 1 ] && echo ', developer build'))"
echo "Copy it to GARMIN/Apps/ on your watch."
