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

On first open you'll see a diagnostic screen (capability probes). **MENU**/tap cycles through fake
achievements per tier (including a `Token Wasted Successfully` sanity check that skips the server
entirely) via `DebugInjector`; **SELECT** opens the Hall of Shame once the queue is empty.

## Configuring the server

Server URL and shared key are read from Connect IQ app settings (`resources/properties`,
`resources/settings`) — set via Garmin Connect Mobile once installed, or the simulator's own
Settings editor. Empty `serverUrl` = fully offline (local `TextBank` only, no network call at all).

**Note:** Connect IQ refuses plain HTTP (`SECURE_CONNECTION_REQUIRED`, response code -1001) — the
server URL must be HTTPS. Fly.io (see `../server/fly.toml`) provides this automatically.

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
