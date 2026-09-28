# Garmin Achievements

A watch app that turns daily-life events (finished a run, hit a step goal, sat too long...) into
sarcastic Dungeon-Crawler-Carl-style RPG achievements, with a pixel-art animation and fanfare on the watch.

| Folder | What |
|---|---|
| [`server/`](server/README.md) | ASP.NET Core LLM layer: turns an event into achievement text (Claude Haiku 4.5, DeepSeek for comparison) |
| `watch/` | Connect IQ (Monkey C) watch app: background event detection, system notification, pixel-wave popup animation |

See [`server/README.md`](server/README.md) for the server (Hebrew) and the plan file for the watch app design/build phases.
