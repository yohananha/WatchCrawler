#!/usr/bin/env bash
# WatchCrawler setup: your own server, your own API key, your own watch build.
#
#   ./setup.sh                 full setup (asks a few questions)
#   ./setup.sh --build-only    rebuild the watch app only (after `git pull`, or for another watch)
#   ./setup.sh --usage         what you've spent so far and how much credit is left
#   ./setup.sh --topup 10      you added $10 of API credit: tell the server
#   ./setup.sh --dry-run       show what would happen, change nothing
#
# Windows: double-click setup.cmd (it runs this file with Git Bash).

set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG_DIR="$HOME/.watchcrawler"
CONFIG="$CONFIG_DIR/config"
IMAGE="ghcr.io/yohananha/watchcrawler-server:latest"

# Prices (USD per 1M tokens) and typical tokens per notification - keep in sync with server/appsettings.json.
ANTHROPIC_IN=1.0;  ANTHROPIC_OUT=5.0
DEEPSEEK_IN=0.30;  DEEPSEEK_OUT=1.20
AVG_IN=1200;       AVG_OUT=80

DRY_RUN=0
MODE=setup
TOPUP=""
while [ $# -gt 0 ]; do
    case "$1" in
        --dry-run) DRY_RUN=1 ;;
        --build-only) MODE=build ;;
        --usage) MODE=usage ;;
        --topup) MODE=topup; TOPUP="${2:-}"; shift ;;
        -h|--help) sed -n '2,10p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1 (try --help)"; exit 1 ;;
    esac
    shift
done

# ---- output helpers ---------------------------------------------------------
if [ -t 1 ]; then B=$'\e[1m'; G=$'\e[32m'; Y=$'\e[33m'; R=$'\e[31m'; C=$'\e[36m'; N=$'\e[0m'; else B=; G=; Y=; R=; C=; N=; fi
say()  { echo "$@"; }
step() { echo; echo "${B}${C}== $* ==${N}"; }
ok()   { echo "${G}✓${N} $*"; }
warn() { echo "${Y}!${N} $*"; }
die()  { echo "${R}✗ $*${N}" >&2; exit 1; }

ask() {  # ask "Question" default -> $REPLY
    local q="$1" def="${2:-}"
    if [ -n "$def" ]; then read -r -p "$q [$def]: " REPLY; REPLY="${REPLY:-$def}"
    else read -r -p "$q: " REPLY; fi
}
ask_secret() {
    read -r -s -p "$1: " REPLY; echo
}
yes_no() {  # yes_no "Question" Y|N
    local def="$2" hint="[Y/n]"; [ "$def" = N ] && hint="[y/N]"
    read -r -p "$1 $hint: " REPLY; REPLY="${REPLY:-$def}"
    [[ "$REPLY" =~ ^[Yy] ]]
}

run() {  # run a command, or just show it in --dry-run (secrets never appear in arguments)
    if [ "$DRY_RUN" = 1 ]; then echo "  (dry run) $*"; else "$@"; fi
}

random_hex() {
    if command -v openssl >/dev/null 2>&1; then openssl rand -hex "$1"
    else od -An -N"$1" -tx1 /dev/urandom | tr -d ' \n'; fi
}

# ---- error reports to the developer (only after the user agreed) -----------
REPORTS=""        # "yes" once agreed
CURRENT_STEP="start"
API_KEY=""
SHARED_KEY=""

# The developer's report topic isn't in the code: CI bakes it into the published server image's
# environment (Reporting__NtfyUrl, from a repository secret). Read it from the image config through
# the public registry API - only when a report is actually being sent.
report_url() {
    local repo="yohananha/watchcrawler-server" token manifest digest
    local accept="application/vnd.oci.image.index.v1+json,application/vnd.docker.distribution.manifest.list.v2+json,application/vnd.oci.image.manifest.v1+json,application/vnd.docker.distribution.manifest.v2+json"
    token="$(curl -fsS -m 10 "https://ghcr.io/token?scope=repository:$repo:pull" | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')"
    [ -n "$token" ] || return 0
    manifest="$(curl -fsS -m 10 -H "Authorization: Bearer $token" -H "Accept: $accept" "https://ghcr.io/v2/$repo/manifests/latest")"
    if [[ "$manifest" == *'"manifests"'* ]]; then  # multi-platform index: follow the first image
        digest="$(echo "$manifest" | grep -o '"digest": *"sha256:[0-9a-f]*"' | head -1 | grep -o 'sha256:[0-9a-f]*')"
        manifest="$(curl -fsS -m 10 -H "Authorization: Bearer $token" -H "Accept: $accept" "https://ghcr.io/v2/$repo/manifests/$digest")"
    fi
    digest="$(echo "$manifest" | tr -d '\n ' | grep -o '"config":{[^}]*}' | grep -o 'sha256:[0-9a-f]*')"
    [ -n "$digest" ] || return 0
    curl -fsSL -m 10 -H "Authorization: Bearer $token" "https://ghcr.io/v2/$repo/blobs/$digest" \
        | grep -o 'Reporting__NtfyUrl=https://[^"]*' | head -1 | cut -d= -f2-
}

scrub() {
    local s="$1"
    [ -n "$API_KEY" ] && s="${s//$API_KEY/[key]}"
    [ -n "$SHARED_KEY" ] && s="${s//$SHARED_KEY/[key]}"
    echo "$s" | sed -E 's/sk-[A-Za-z0-9_-]{6,}/[key]/g'
}

on_error() {
    local code=$? line="$1" cmd="$2"
    trap - ERR
    echo
    echo "${R}✗ Setup failed during: $CURRENT_STEP${N}"
    echo "  (command: $(scrub "$cmd"), exit $code)"
    local ntfy=""
    if [ "$REPORTS" = yes ] && [ "$DRY_RUN" = 0 ]; then ntfy="$(report_url 2>/dev/null || true)"; fi
    if [ -n "$ntfy" ]; then
        curl -s -m 10 -H "Title: WatchCrawler: setup failed" -H "Tags: hammer_and_wrench" \
            -d "$(scrub "step: $CURRENT_STEP | os: $(uname -s) | line $line: $cmd (exit $code)")" \
            "$ntfy" >/dev/null 2>&1 || true
        echo "  A short error report was sent to the WatchCrawler developer."
    fi
    echo "  Fix the problem above and run ./setup.sh again - it picks up where it can."
    exit "$code"
}
trap 'on_error $LINENO "$BASH_COMMAND"' ERR

# ---- saved settings -------------------------------------------------------------
load_config() { [ -f "$CONFIG" ] && . "$CONFIG" || true; }
save_config() {
    [ "$DRY_RUN" = 1 ] && { echo "  (dry run) would save settings to $CONFIG"; return; }
    mkdir -p "$CONFIG_DIR"
    umask 077
    cat > "$CONFIG" <<EOF
# WatchCrawler settings (written by setup.sh). The LLM API key is NOT stored here.
SERVER_URL='${SERVER_URL:-}'
SHARED_KEY='${SHARED_KEY:-}'
PROVIDER='${PROVIDER:-}'
HOST='${HOST:-}'
FLY_APP='${FLY_APP:-}'
DEVICE='${DEVICE:-}'
REPORTS='${REPORTS:-}'
EOF
    chmod 600 "$CONFIG" 2>/dev/null || true
}

server_get() {  # server_get /path -> body (needs SERVER_URL, SHARED_KEY)
    curl -fsS -m 30 -H "X-Watch-Key: $SHARED_KEY" "$SERVER_URL$1"
}

json_num() {  # json_num '{"a":1.5}' a -> 1.5 (flat JSON only)
    echo "$1" | grep -o "\"$2\": *[-0-9.]*" | head -1 | sed 's/.*: *//'
}

banner() {
    cat <<'EOF'

 __        __    _       _      ____                    _
 \ \      / /_ _| |_ ___| |__  / ___|_ __ __ ___      _| | ___ _ __
  \ \ /\ / / _` | __/ __| '_ \| |   | '__/ _` \ \ /\ / / |/ _ \ '__|
   \ V  V / (_| | || (__| | | | |___| | | (_| |\ V  V /| |  __/ |
    \_/\_/ \__,_|\__\___|_| |_|\____|_|  \__,_| \_/\_/ |_|\___|_|

   Your watch, narrated by a sarcastic dungeon System. Setup.
EOF
    [ "$DRY_RUN" = 1 ] && warn "Dry run: nothing will be created, deployed or saved."
    return 0
}

# ============================================================================
# Watch build
# ============================================================================
build_watch() {
    CURRENT_STEP="building the watch app"
    step "Build the watch app"
    local devices
    devices="$(grep -o 'product id="[^"]*"' "$ROOT/watch/manifest.xml" | sed 's/product id="//; s/"//' | tr '\n' ' ')"
    say "Supported watches: ${B}$devices${N}"
    say "(fenix847mm = fenix 8 47mm, epix2pro51mm = epix Pro 51mm, and so on. Yours isn't listed? See the README.)"
    ask "Which watch do you have?" "${DEVICE:-fenix7}"
    DEVICE="$REPLY"
    save_config

    if [ "$DRY_RUN" = 1 ]; then
        echo "  (dry run) watch/tools/build_personal.sh $DEVICE"
        return
    fi
    local rc=0
    WATCHCRAWLER_SERVER_URL="$SERVER_URL" WATCHCRAWLER_SHARED_KEY="$SHARED_KEY" \
        bash "$ROOT/watch/tools/build_personal.sh" "$DEVICE" || rc=$?
    if [ "$rc" = 2 ]; then
        warn "Your server is ready. Once the tools above are installed, finish with:  ./setup.sh --build-only"
        exit 0
    elif [ "$rc" != 0 ]; then
        false  # -> error report / message
    fi

    cp "$ROOT/watch/.personal-build/bin/WatchCrawler.prg" "$ROOT/WatchCrawler.prg"
    cat <<EOF

${B}${G}Done! Your watch app: $ROOT/WatchCrawler.prg${N}

Install it:
  1. Plug the watch into your computer with its USB cable.
     (Mac: install OpenMTP from https://openmtp.org to see the watch's files.)
  2. Copy ${B}WatchCrawler.prg${N} into the ${B}GARMIN/Apps${N} folder on the watch.
  3. Unplug, open ${B}WatchCrawler${N} from the watch's app list once. That's it:
     it now listens in the background and narrates your achievements.
EOF
}

# ============================================================================
# Full setup
# ============================================================================
choose_provider() {
    CURRENT_STEP="choosing the AI provider"
    step "1. Your AI provider"
    say "WatchCrawler uses YOUR OWN AI account, so you control (and pay) the cost."
    say "  1) Anthropic Claude (recommended, funniest)  - key from https://console.anthropic.com/settings/keys"
    say "  2) DeepSeek (cheapest)                       - key from https://platform.deepseek.com/api_keys"
    ask "Choose 1 or 2" "1"
    case "$REPLY" in
        2) PROVIDER=deepseek; KEY_VAR=DEEPSEEK_API_KEY; P_IN=$DEEPSEEK_IN; P_OUT=$DEEPSEEK_OUT; TOPUP_URL="https://platform.deepseek.com/top_up" ;;
        *) PROVIDER=anthropic; KEY_VAR=ANTHROPIC_API_KEY; P_IN=$ANTHROPIC_IN; P_OUT=$ANTHROPIC_OUT; TOPUP_URL="https://console.anthropic.com/settings/billing" ;;
    esac

    CURRENT_STEP="checking the API key"
    while :; do
        ask_secret "Paste your $PROVIDER API key (hidden while you type)"
        API_KEY="$(echo "$REPLY" | tr -d '[:space:]')"
        [ -n "$API_KEY" ] || { warn "Empty key, try again."; continue; }
        [ "$DRY_RUN" = 1 ] && break
        local status
        if [ "$PROVIDER" = anthropic ]; then
            status="$(curl -s -o /dev/null -w '%{http_code}' -m 20 https://api.anthropic.com/v1/models \
                -H "x-api-key: $API_KEY" -H "anthropic-version: 2023-06-01" || true)"
        else
            DS_BALANCE_JSON="$(curl -s -m 20 https://api.deepseek.com/user/balance -H "Authorization: Bearer $API_KEY" || true)"
            status=$([[ "$DS_BALANCE_JSON" == *balance_infos* ]] && echo 200 || echo 401)
        fi
        if [ "$status" = 200 ]; then ok "Key works."; break; fi
        warn "That key was rejected (HTTP $status). Check you copied all of it, then paste again."
    done
}

estimate_cost() {
    CURRENT_STEP="estimating cost"
    step "2. What will it cost?"
    ask "About how many notifications a day do you expect? (activities + daily events)" "10"
    EVENTS="$REPLY"
    [[ "$EVENTS" =~ ^[0-9]+$ ]] || EVENTS=10
    local monthly months
    monthly="$(awk -v i="$AVG_IN" -v o="$AVG_OUT" -v pi="$P_IN" -v po="$P_OUT" -v e="$EVENTS" \
        'BEGIN { printf "%.2f", (i*pi + o*po) / 1000000 * e * 30 }')"
    months="$(awk -v m="$monthly" 'BEGIN { if (m > 0) printf "%d", 5 / m; else print "many" }')"
    say "Estimated cost: ${B}~\$$monthly per month${N} at $EVENTS notifications a day."
    say "Recommended: buy ${B}\$5${N} of credit (the minimum) - that lasts about ${B}$months months${N}."
    say "Buy credit here: $TOPUP_URL"

    CREDIT=""
    if [ "$PROVIDER" = deepseek ] && [ -n "${DS_BALANCE_JSON:-}" ]; then
        local bal
        bal="$(echo "$DS_BALANCE_JSON" | grep -o '"total_balance": *"[0-9.]*"' | head -1 | grep -o '[0-9.]*' || true)"
        [ -n "$bal" ] && ok "Your DeepSeek balance: $bal (the server checks it by itself, twice a day)."
    else
        say
        say "Anthropic doesn't let a key read its own balance, so tell WatchCrawler how much credit"
        say "you have; it will warn you on the watch about 2 weeks before it runs out."
        ask "Credit on your account now, in USD (Enter to skip)" ""
        [[ "$REPLY" =~ ^[0-9]+([.][0-9]+)?$ ]] && CREDIT="$REPLY"
    fi
}

ask_reports() {
    CURRENT_STEP="error report consent"
    step "3. Help fix problems"
    say "If something breaks, a short error report (no API keys, no activity or health data -"
    say "just what failed) can be sent to the WatchCrawler developer so it can be fixed."
    if yes_no "Allow error reports?" Y; then REPORTS=yes; else REPORTS=no; fi

    USER_NTFY=""
    say
    say "Optional: get 'top up your credit' alerts on your phone too (free ntfy app, no account)."
    if yes_no "Set up phone alerts?" N; then
        USER_NTFY="https://ntfy.sh/watchcrawler-me-$(random_hex 8)"
        say "Install the ntfy app (https://ntfy.sh) and subscribe to: ${B}$USER_NTFY${N}"
    fi
}

secrets_file() {  # KEY=VALUE lines for the server
    echo "LLM_PROVIDER=$PROVIDER"
    echo "$KEY_VAR=$API_KEY"
    echo "WATCH_SHARED_KEY=$SHARED_KEY"
    echo "Llm__EstimatedEventsPerDay=$EVENTS"
    [ -n "$CREDIT" ] && echo "CREDIT_BALANCE_USD=$CREDIT"
    [ -n "$USER_NTFY" ] && echo "USER_NTFY_URL=$USER_NTFY"
    [ "$REPORTS" = no ] && echo "REPORT_ERRORS=false"
    return 0
}

deploy_fly() {
    CURRENT_STEP="deploying to Fly.io"
    local FLY
    FLY="$(command -v flyctl || command -v fly || true)"
    if [ -z "$FLY" ] && [ -x "$HOME/.fly/bin/flyctl" ]; then FLY="$HOME/.fly/bin/flyctl"; fi
    if [ -z "$FLY" ]; then
        say "Fly.io's command-line tool is needed. Install it:"
        say "  Mac/Linux:  curl -L https://fly.io/install.sh | sh"
        say "  Windows:    powershell -Command \"iwr https://fly.io/install.ps1 -useb | iex\""
        die "Then open a new terminal and run ./setup.sh again."
    fi

    if ! "$FLY" auth whoami >/dev/null 2>&1; then
        say "Log in (or sign up) to Fly.io in the browser window that opens."
        say "Fly asks new accounts for a card; a server this small usually stays within the free allowance."
        run "$FLY" auth login
    fi

    FLY_APP="${FLY_APP:-watchcrawler-$(random_hex 3)}"
    ask "Server name (becomes https://NAME.fly.dev)" "$FLY_APP"
    FLY_APP="$REPLY"
    say "Regions: iad (US East), lax (US West), lhr (London), fra (Frankfurt), ams, mad, syd, nrt, sin, gru..."
    ask "Region closest to you" "iad"
    local region="$REPLY"

    if "$FLY" status -a "$FLY_APP" >/dev/null 2>&1; then
        ok "Fly app $FLY_APP already exists - updating it."
    else
        run "$FLY" apps create "$FLY_APP"
        run "$FLY" volumes create trigger_data --region "$region" --size 1 -a "$FLY_APP" -y
    fi

    say "Storing your secrets on Fly (encrypted there; never on this computer)..."
    if [ "$DRY_RUN" = 1 ]; then
        echo "  (dry run) $FLY secrets import -a $FLY_APP --stage   <- $(secrets_file | cut -d= -f1 | tr '\n' ' ')"
    else
        secrets_file | "$FLY" secrets import -a "$FLY_APP" --stage >/dev/null
    fi
    run "$FLY" deploy -a "$FLY_APP" -c "$ROOT/server/fly.toml" --image "$IMAGE" \
        --primary-region "$region" --ha=false --yes
    SERVER_URL="https://$FLY_APP.fly.dev"
}

deploy_docker() {
    CURRENT_STEP="starting the Docker server"
    command -v docker >/dev/null 2>&1 || die "Install Docker Desktop (https://www.docker.com/products/docker-desktop/) and run ./setup.sh again."
    if [ "$DRY_RUN" = 1 ]; then echo "  (dry run) write $ROOT/.env and: docker compose up -d"
    else
        umask 077
        { secrets_file; echo "PORT=8080"; } > "$ROOT/.env"
        (cd "$ROOT" && docker compose up -d)
    fi
    say
    say "Your server runs on this computer (http://localhost:8080), but the watch only talks HTTPS."
    say "Give it a free HTTPS address with Cloudflare Tunnel, in another terminal:"
    say "  1. Install: https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/"
    say "  2. Run:     cloudflared tunnel --url http://localhost:8080"
    say "  3. Copy the https://....trycloudflare.com address it prints."
    say "  (This computer must stay on, and a quick tunnel's address changes when restarted - then run"
    say "   ./setup.sh --build-only with the new address. For always-on, prefer Fly.io.)"
    ask "Paste the https:// address" ""
    SERVER_URL="$REPLY"
}

deploy_render() {
    CURRENT_STEP="Render setup"
    say "1. Fork https://github.com/yohananha/WatchCrawler to your GitHub account."
    say "2. In https://dashboard.render.com: New > Blueprint > pick your fork > it finds server/render.yaml."
    say "3. When it asks for values, use:"
    secrets_file | sed -E "s/^([A-Z_]+_API_KEY)=.*/\1=<the API key you pasted above>/; s/^/     /"
    say "   (the WATCH_SHARED_KEY above was generated for you - copy it exactly)"
    say "4. Wait for the deploy, then copy the service address (https://....onrender.com)."
    warn "Render's free plan sleeps and has no disk: credit tracking resets on restarts."
    ask "Paste the https:// address" ""
    SERVER_URL="$REPLY"
}

choose_host() {
    CURRENT_STEP="choosing where the server runs"
    step "4. Where your server runs"
    say "The server holds your API key and talks to the AI; the watch talks only to it."
    say "  1) Fly.io (recommended: always on, HTTPS included, setup does everything)"
    say "  2) This computer with Docker (free, but it must stay on)"
    say "  3) Render.com (free tier, a few manual clicks)"
    say "  4) I already have a WatchCrawler server"
    ask "Choose 1-4" "1"
    case "$REPLY" in
        2) HOST=docker; deploy_docker ;;
        3) HOST=render; deploy_render ;;
        4) HOST=existing
           ask "Server address (https://...)" "${SERVER_URL:-}"; SERVER_URL="$REPLY"
           ask "Its WATCH_SHARED_KEY" "${PREV_SHARED_KEY:-}"; SHARED_KEY="$REPLY" ;;
        *) HOST=fly; deploy_fly ;;
    esac
    SERVER_URL="${SERVER_URL%/}"
    [[ "$SERVER_URL" == https://* ]] || [ "$DRY_RUN" = 1 ] || die "The address must start with https:// (the watch refuses plain http)."
}

wait_for_server() {
    CURRENT_STEP="checking the server answers"
    [ "$DRY_RUN" = 1 ] && return
    say "Waiting for $SERVER_URL to answer..."
    local i
    for i in $(seq 1 30); do
        if server_get /day-settings >/dev/null 2>&1; then ok "Server is up and your key works."; return; fi
        sleep 4
    done
    die "The server didn't answer at $SERVER_URL/day-settings. Check its logs (Fly: fly logs -a $FLY_APP)."
}

full_setup() {
    banner
    load_config
    choose_provider
    estimate_cost
    ask_reports
    PREV_SHARED_KEY="${SHARED_KEY:-}"
    SHARED_KEY="$(random_hex 16)"
    choose_host
    save_config
    wait_for_server
    build_watch
}

# ============================================================================
# Maintenance commands
# ============================================================================
need_config() {
    load_config
    [ -n "${SERVER_URL:-}" ] && [ -n "${SHARED_KEY:-}" ] || die "No saved setup found ($CONFIG). Run ./setup.sh first."
}

show_usage() {
    need_config
    CURRENT_STEP="reading usage"
    local u
    u="$(server_get /usage)"
    step "API usage ($PROVIDER)"
    say "This month:        \$$(json_num "$u" monthToDateUsd) over $(json_num "$u" monthEvents) notifications"
    say "At this pace:      ~\$$(json_num "$u" projectedMonthUsd) per month"
    local rem days
    rem="$(json_num "$u" remainingUsd)"; days="$(json_num "$u" daysLeft)"
    if [ -n "$rem" ]; then say "Credit left:       ~\$$rem${days:+ (about $days days)}"
    else say "Credit left:       unknown - run ./setup.sh --topup <amount> after buying credit"; fi
    say "State:             $(echo "$u" | grep -o '"state": *"[a-z]*"' | grep -o '[a-z]*"$' | tr -d '"')"
}

do_topup() {
    need_config
    [[ "$TOPUP" =~ ^[0-9]+([.][0-9]+)?$ ]] || die "Usage: ./setup.sh --topup 10   (the dollars you just added)"
    CURRENT_STEP="recording a top-up"
    local u rem new
    u="$(server_get /usage)"
    rem="$(json_num "$u" remainingUsd)"
    new="$(awk -v r="${rem:-0}" -v t="$TOPUP" 'BEGIN { printf "%.2f", r + t }')"
    say "Credit was ~\$${rem:-0}, now ~\$$new."
    case "${HOST:-}" in
        fly)
            local FLY; FLY="$(command -v flyctl || command -v fly || echo "$HOME/.fly/bin/flyctl")"
            if [ "$DRY_RUN" = 1 ]; then echo "  (dry run) $FLY secrets set CREDIT_BALANCE_USD=$new -a $FLY_APP"
            else echo "CREDIT_BALANCE_USD=$new" | "$FLY" secrets import -a "$FLY_APP"; fi ;;
        docker)
            run sed -i.bak "/^CREDIT_BALANCE_USD=/d" "$ROOT/.env"
            [ "$DRY_RUN" = 1 ] || echo "CREDIT_BALANCE_USD=$new" >> "$ROOT/.env"
            (cd "$ROOT" && run docker compose up -d) ;;
        *)  say "Set CREDIT_BALANCE_USD=$new in your server's environment settings, then restart it." ;;
    esac
    ok "Recorded."
}

case "$MODE" in
    setup) full_setup ;;
    build) need_config; build_watch ;;
    usage) show_usage ;;
    topup) do_topup ;;
esac
