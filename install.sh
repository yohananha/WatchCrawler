#!/usr/bin/env bash
# One-line install (macOS / Linux / Git Bash on Windows):
#   curl -fsSL https://raw.githubusercontent.com/yohananha/WatchCrawler/main/install.sh | bash
# Downloads WatchCrawler into ~/WatchCrawler (or updates it) and starts setup.
set -euo pipefail

DIR="${WATCHCRAWLER_DIR:-$HOME/WatchCrawler}"
REPO="https://github.com/yohananha/WatchCrawler"

if command -v git >/dev/null 2>&1; then
    if [ -d "$DIR/.git" ]; then
        echo "Updating $DIR ..."
        git -C "$DIR" pull --ff-only
    else
        echo "Downloading WatchCrawler to $DIR ..."
        git clone --depth 1 "$REPO.git" "$DIR"
    fi
else
    echo "Downloading WatchCrawler to $DIR ..."
    tmp="$(mktemp -d)"
    curl -fsSL "$REPO/archive/refs/heads/main.tar.gz" | tar -xz -C "$tmp"
    mkdir -p "$DIR"
    cp -R "$tmp"/WatchCrawler-main/. "$DIR"/
    rm -rf "$tmp"
fi

cd "$DIR"
# stdin is the curl pipe here, so hand the terminal back to setup's questions.
exec bash ./setup.sh "$@" < /dev/tty
