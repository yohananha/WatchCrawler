# Achievements watch app (Connect IQ)

Detects a completed activity in the background, asks the server (`../server/`) for a sarcastic
achievement, and shows a pixel-art popup with sound/vibration. Falls back to local text if the
server is unreachable.

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

Server URL and shared key are `Application.Properties`, normally set via Garmin Connect Mobile's
app-settings screen — **except that only works for apps published through the Connect IQ Store.**
A sideloaded/private app (this one) shows **"No settings"** in GCM, confirmed by testing. Two ways
around it:

- **Simulator:** its own Settings editor works fine for local dev/testing.
- **Real watch:** use `tools/build_personal.sh` — bakes your real values into a build without ever
  writing them into the tracked `resources/properties/properties.xml` (which stays empty/safe):
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
enter the shared key, click **Trigger test achievement**. The watch's background check (requested every 5 min, but Garmin schedules these loosely - expect 5-30 min)
polls for this and, if armed, runs a real request through the LLM server — same code path
as a genuine detected activity. See `server/Program.cs` (`/trigger-test*`) and
`watch/source/TriggerChecker.mc`.

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
  scenes, binary ring-dot lighting, fixed-width text wrap instead of per-line chord width).
