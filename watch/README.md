# Achievements watch app (Connect IQ)

Detects events in the background, asks the server (`../server/`) for a sarcastic achievement, and
shows a system notification (pops up on the watch) that opens a pixel-art animation with
sound/vibration. Falls back to local text if the server is unreachable.

## What triggers an achievement

Checked from a background job (requested every 5 min; Garmin schedules it loosely, so 5-30 min):

| Event | When |
|---|---|
| Completed activity (any sport) | A new activity appears in the watch history. Distance sports (>= 0.5 km) are judged on km against your own last 14 of that sport; everything else (strength, HIIT, yoga...) on minutes. The watch API only exposes the main sport, so all gym-type sessions arrive as "TRAINING". The first activity the app ever sees is recorded silently. |
| Steps goal / floors goal reached | Once a day (rare tier) |
| Resting heart rate | Once a morning, only when notably better/worse than your 14-day baseline |
| Body Battery low | Under 15%, 09:00-22:00, once a day (cursed) |
| Sedentary | Move bar maxed, 09:00-22:00, once a day (cursed) |
| Step goal missed | After 21:00 (cursed) |
| Nothing achieved today | After 22:00, if no real achievement was announced that day (cursed) |

**Exact workout kind:** while a recording is running, each background tick saves the recording's profile
(name, sport, sub-sport; `source/ProfileCapture.mc`) and stays completely silent: no notifications, no
network. When the finished activity appears in history it is matched by start time, so a strength/HIIT/yoga
session is announced as that instead of "TRAINING". A recording shorter than the tick interval is never
seen and gets the generic label. Whether the background process may read the profile is unverified on the
real watch: the diagnostics screen shows a `prof:` line with what was captured (or the error).

Day events (everything except activities) respect **quiet hours** (default 23:00-06:59) and a
**daily cap** (default 10 announcements, counting activities). Those numbers, plus the idle/goal hours,
are tuned on the *server* (`DAY_*` env vars in `../server/fly.toml`) and picked up on the watch's next
poll: no reinstall. Defaults live in `source/DayEvents.mc`. Sleep/wake is not offered: Connect IQ
doesn't reliably expose sleep data.

## Building

Needs the Connect IQ SDK + a JDK on `PATH`, and a developer key at `keys/developer_key.der`
(`openssl genrsa -out keys/developer_key.pem 4096 && openssl pkcs8 -topk8 -inform PEM -outform DER
-in keys/developer_key.pem -out keys/developer_key.der -nocrypt` — gitignored, generate your own).

```bash
monkeyc -f monkey.jungle -d fenix847mm -o bin/achievements.prg -y keys/developer_key.der -w
```

Swap `fenix847mm` for your device id (`ls "$APPDATA/Garmin/ConnectIQ/Devices"` on Windows).

## Running in the simulator

```bash
simulator.exe &            # from the SDK's bin/ directory
monkeydo bin/achievements.prg fenix847mm
```

The default home screen is the **Hall of Shame** (your achievement history — empty on first run).
**MENU** from there opens a diagnostic screen (capability probes); **MENU**/tap on *that* screen
cycles through fake achievements per tier via `DebugInjector` (including a `Token Wasted
Successfully` sanity check that skips the server entirely), **SELECT** goes back.

## Configuring the server

Garmin Connect Mobile's app-settings screen only exists for apps published through the Connect IQ
Store; a sideloaded/private app (this one) shows **"No settings"**, confirmed by testing. Also, on the
real watch `Application.Properties` was not reliably readable from the background process and
kept an empty value from an older install. So the server URL/key are read in this order
(`source/Config.mc`): Properties, then a Storage copy the foreground app makes at launch, then
**compile-time string resources** (`CfgServerUrl`/`CfgSharedKey`), which is what actually works on
a sideloaded watch:

- **Simulator:** its own Settings editor works for local dev/testing (note it keeps old saved
  values across runs).
- **Real watch:** use `tools/build_personal.sh` — bakes your real values into a gitignored copy of
  the project without ever touching the tracked `resources/strings/strings.xml` or `properties.xml`
  (which stay empty/safe):
  ```bash
  export WATCHCRAWLER_SERVER_URL=https://watchcrawler.fly.dev
  export WATCHCRAWLER_SHARED_KEY=<your WATCH_SHARED_KEY>
  tools/build_personal.sh fenix847mm
  # copy .personal-build/bin/achievements.prg to GARMIN/Apps/achievements.prg
  ```

Empty `serverUrl` = fully offline (local `TextBank` only, no network call at all).

**Note:** Connect IQ refuses plain HTTP (`SECURE_CONNECTION_REQUIRED`, response code -1001) — the
server URL must be HTTPS. Fly.io (see `../server/fly.toml`) provides this automatically.

## Remote test trigger

No physical button needed: visit the server's `/` page (e.g. `https://watchcrawler.fly.dev/`),
enter the shared key, **pick the event kind** (run tiers, strength, HIIT, yoga, swim, nothing today,
goal missed, sedentary, goals reached, Body Battery, resting HR, canned joke) and click
**Trigger test achievement**. The watch's background poll picks it up (5-30 min) and runs a real
request through the LLM server, the same code path as a genuine event. See `server/Program.cs`
(`/trigger-test*`) and `source/TriggerChecker.mc`.

It is **off by default** on the server (`ENABLE_TEST_TRIGGER=false` in `fly.toml`). While off, the
watch stops polling for it for 6 hours (so it doesn't wake the server) and re-checks whenever you
open the app. To use it: set `ENABLE_TEST_TRIGGER = "true"`, `fly deploy`, open the app once on the
watch.

On the watch, MENU (from the Hall of Shame) opens diagnostics: it shows the last background run
(`BG#n ... stage`) and the last trigger poll (`trig: ...`); MENU/tap there cycles through fake events.

## Screens and art

- **Hall of Shame** (home): your history, newest first, with the tier icon; SELECT replays, MENU = diagnostics.
- **Achievement popup:** three scenes (unlock with a star/diamond/skull tier icon, record, reward).
  Tier icons are 9x9 pixel grids in `source/anim/TierIcon.mc`.
- **Launcher icon:** `resources/drawables/launcher_icon.png` (65x65 for the fenix 8 47mm; the
  notification shows the same icon). Original art in `design/`.

## Releasing

Personal use, sideloaded (decision: not publishing to the Connect IQ Store for now). To install a new
build: `tools/build_personal.sh fenix847mm`, copy `.personal-build/bin/achievements.prg` to the
watch's `GARMIN/Apps/`, open the app once. A Store release would need the multi-user rework
(per-user keys and pairing, a privacy policy, Garmin review); see the project plan.

Before each push (no CI for the watch): build for `fenix847mm` with `-w` and check no new
warnings/errors, run it in the simulator, `git status` shows nothing from `.personal-build/`, no
key in any tracked file.

## Fonts

This SDK needs the old AngelCode `.fnt`+`.png` bitmap-font format, not a raw `.ttf` (confirmed
empirically — no BMFont GUI was available in this dev environment either, hence
`tools/make_bmfont.py`, a from-scratch Python+Pillow generator). Regenerate after changing the
character set:

```bash
python tools/make_bmfont.py <font.ttf> resources/fonts/<name><size> <size> "<chars>"
```

## Known limitations (see the project plan for full detail)

- No CI build for this app — the Connect IQ SDK needs an interactive EULA click-through, so there's
  no scriptable download for a hosted CI runner. Verify locally in the simulator before pushing.
- `UserProfile.getUserActivityHistory()` with zero activity FIT files is an **uncatchable VM crash**
  in this simulator build (not the documented "returns null" behaviour) — isolated to the
  background process so it doesn't affect the foreground app, but means the real activity-detection
  path has only been tested via `DebugInjector`, not a genuine completed walk/run yet.
- The pixel-wave animation is a simplified port of the original design spec (no cross-fade between
  scenes, binary ring-dot lighting).
- Strength/HIIT/yoga can't be told apart from the watch history (all "TRAINING"). A Strava-backed
  lookup is planned (Phase 5).
