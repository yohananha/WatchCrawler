# WatchCrawler watch app (Connect IQ)

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

Users never do this by hand: `../setup.sh` calls `tools/build_personal.sh`, which finds the SDK,
creates a signing key if there isn't one, and bakes in the server URL and key.

By hand, you need the Connect IQ SDK plus a JDK, and a developer key at `keys/developer_key.der`
(`openssl genrsa -out keys/developer_key.pem 4096 && openssl pkcs8 -topk8 -inform PEM -outform DER
-in keys/developer_key.pem -out keys/developer_key.der -nocrypt`; gitignored, generate your own).

There are two build flavours:

| Build | Jungle | What's in it |
|---|---|---|
| **User** (default, what setup makes) | `monkey.jungle` | Everything except `(:dev)` code. MENU does nothing, there's no settings entry, no test-trigger polling. Server settings and credit notices are polled every 3 h via `GET /day-settings`. |
| **Developer** | `"monkey.jungle;dev.jungle"` | Adds the diagnostics screen (MENU), the fake-achievement injector, remote test-trigger polling every cycle, and the settings entries in `resources-dev/`. Drops the `(:user)` stand-ins. |

```bash
monkeyc -f monkey.jungle -d <device> -o bin/WatchCrawler.prg -y keys/developer_key.der -w
monkeyc -f "monkey.jungle;dev.jungle" -d <device> -o bin/WatchCrawler.prg -y keys/developer_key.der -w
```

To keep something out of user builds, annotate it `(:dev)`. If callers need a replacement, give
the user build a same-named `(:user)` version (see `HallOfShameDelegate.onMenu`,
`TriggerChecker.endpoint`). Device ids: `ls "$APPDATA/Garmin/ConnectIQ/Devices"` on Windows.

## Running in the simulator

```bash
simulator.exe &            # from the SDK's bin/ directory
monkeydo bin/WatchCrawler.prg <device>
```

The default home screen is the **Hall of Shame** (your achievement history — empty on first run).
In a **developer build**, **MENU** from there opens a diagnostic screen (capability probes); **MENU**/tap on *that* screen
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
- **Real watch:** use `tools/build_personal.sh` (or `../setup.sh --build-only`, which remembers
  the values). It bakes your real values into a gitignored copy of the project without touching the
  tracked `resources/strings/strings.xml` or `properties.xml`, which stay empty and safe:
  ```bash
  export WATCHCRAWLER_SERVER_URL=https://your-watchcrawler.fly.dev
  export WATCHCRAWLER_SHARED_KEY=<your WATCH_SHARED_KEY>
  tools/build_personal.sh <device>          # user build
  tools/build_personal.sh --dev <device>    # developer build
  # copy .personal-build/bin/WatchCrawler.prg to GARMIN/Apps/
  ```

Empty `serverUrl` means fully offline: local `TextBank` only, no network calls, and the Hall of
Shame shows "NOT SET UP: RUN SETUP".

## Credit notices and error reports

- **Credit notices:** every server answer carries `notice`, a "Mana Reserves Low" / "Out of Mana"
  message when the user's API credit is low or empty. `SystemNotice.mc` keeps the latest one. The
  background service shows it as a "SYSTEM MESSAGE" at most once a day, outside quiet hours, on a
  tick with nothing else to announce.
- **Error reports:** `WatchErr.mc` remembers the last failed request (response code, where, how
  many times, build stamp). It sends that as `X-Watch-Error` on the next request that gets through,
  and the server forwards it to the developer's reports. Phone-not-connected codes (-104, -2) are
  ignored.

**Note:** Connect IQ refuses plain HTTP (`SECURE_CONNECTION_REQUIRED`, response code -1001) — the
server URL must be HTTPS. Fly.io (see `../server/fly.toml`) provides this automatically.

## Remote test trigger

**Developer builds only.** No physical button needed: visit the server's `/` page (e.g. `https://your-watchcrawler.fly.dev/`),
enter the shared key, **pick the event kind** (run tiers, strength, HIIT, yoga, swim, nothing today,
goal missed, sedentary, goals reached, Body Battery, resting HR, canned joke) and click
**Trigger test achievement**. The watch's background poll picks it up (5-30 min) and runs a real
request through the LLM server, the same code path as a genuine event. See `server/Program.cs`
(`/trigger-test*`) and `source/TriggerChecker.mc`.

It is **off by default** on the server (`ENABLE_TEST_TRIGGER=false` in `fly.toml`). While off, the
watch stops polling for it for 6 hours (so it doesn't wake the server) and re-checks whenever you
open the app. To use it: set `ENABLE_TEST_TRIGGER = "true"`, `fly deploy`, open the app once on the
watch.

On the watch (developer build), MENU (from the Hall of Shame) opens diagnostics: it shows the last background run
(`BG#n ... stage`) and the last trigger poll (`trig: ...`); MENU/tap there cycles through fake events.

## Screens and art

- **Hall of Shame** (home): your history, newest first, with the tier icon; SELECT replays, MENU = diagnostics (dev builds).
- **Achievement popup:** three scenes (unlock with a star/diamond/skull tier icon, record, reward).
  Tier icons are 9x9 pixel grids in `source/anim/TierIcon.mc`.
- **Launcher icon:** `resources/drawables/launcher_icon.png` (65x65 for the fenix 8 47mm; the
  notification shows the same icon). Original art in `design/`.

## Releasing

Sideloaded only for now: each user runs `../setup.sh`, which builds a user build with their own
server baked in. There's no Connect IQ Store listing yet. That would need a Store submission,
a privacy policy, and Garmin review.

To install a new build, copy `.personal-build/bin/WatchCrawler.prg` to the watch's `GARMIN/Apps/`
and open the app once. Builds made before the rename were called `achievements.prg`: delete that
file from the watch so you don't end up with two copies.

Before each push (no CI for the watch):
- Build both flavours with `-w` and check for new warnings or errors.
- Run it in the simulator.
- Check `git status` shows nothing from `.personal-build/`.
- Check no key is in any tracked file.

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
