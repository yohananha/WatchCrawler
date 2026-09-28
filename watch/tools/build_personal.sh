#!/usr/bin/env bash
# Builds achievements.prg with your real serverUrl/sharedKey baked in.
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
#   export WATCHCRAWLER_SERVER_URL=https://watchcrawler.fly.dev
#   export WATCHCRAWLER_SHARED_KEY=<your WATCH_SHARED_KEY>
#   watch/tools/build_personal.sh <device-id>       # e.g. fenix847mm
#
# Output: watch/.personal-build/bin/achievements.prg - copy that to
# GARMIN/Apps on your watch (rename to achievements.prg if needed).

set -euo pipefail

if [ -z "${WATCHCRAWLER_SERVER_URL:-}" ] || [ -z "${WATCHCRAWLER_SHARED_KEY:-}" ]; then
    echo "Set WATCHCRAWLER_SERVER_URL and WATCHCRAWLER_SHARED_KEY first." >&2
    exit 1
fi

DEVICE="${1:?Usage: build_personal.sh <device-id>, e.g. fenix847mm}"
WATCH_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_DIR="$WATCH_DIR/.personal-build"

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"
cp -r "$WATCH_DIR"/{manifest.xml,monkey.jungle,resources,source,keys} "$BUILD_DIR/"

PROPS="$BUILD_DIR/resources/properties/properties.xml"
python3 - "$PROPS" "$WATCHCRAWLER_SERVER_URL" "$WATCHCRAWLER_SHARED_KEY" << 'PYEOF'
import sys
path, url, key = sys.argv[1], sys.argv[2], sys.argv[3]
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

mkdir -p "$BUILD_DIR/bin"
cd "$BUILD_DIR"
monkeyc -f monkey.jungle -d "$DEVICE" -o bin/achievements.prg -y keys/developer_key.der -w

echo
echo "Built: $BUILD_DIR/bin/achievements.prg"
echo "Copy that to GARMIN/Apps/achievements.prg on your watch."
