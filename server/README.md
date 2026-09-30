# WatchCrawler server

A small ASP.NET Core service (.NET 10, no external packages). It receives an event from the watch
and returns a sarcastic achievement written by an LLM. Each user runs their own copy with their own
API key: `../setup.sh` deploys it to Fly.io from the prebuilt image
`ghcr.io/yohananha/watchcrawler-server`. It also has a blind-comparison mode for picking an LLM.

## Files

| File | Role |
|---|---|
| `Program.cs` | API endpoints, shared-key auth, startup checks, compare mode |
| `StartupConfig.cs` | `LLM_PROVIDER` mapping; refuses to start without an API key or `WATCH_SHARED_KEY` |
| `Models.cs` | Event, achievement, settings |
| `Providers.cs` | `ILlmProvider`: Anthropic plus OpenAI-compatible (DeepSeek, OpenRouter); structured HTTP errors (out of credit, bad key) |
| `Achievements.cs` | Tier and sound (deterministic), prompt, recent history, generator with retry/fallback |
| `UsageTracker.cs` | Spend per month, credit left, low/out-of-credit notice for the watch |
| `ErrorReporter.cs` | Scrubbed failure reports and a daily digest to the developer's ntfy topic |
| `Comparison.cs` | Runs every provider on the same events and writes a blind report |
| `TestTrigger.cs` | Remote test trigger flag (a file on the volume) |
| `DayEventSettings.cs` | Quiet hours, daily cap and end-of-day event hours from env vars, sent to the watch |
| `Dockerfile`, `fly.toml`, `render.yaml` | Deploy configs |
| `.env.example` | Every setting, with an explanation |
| `Tests/` | xUnit tests |

## Endpoints

All except `/health` and `/` need the `X-Watch-Key` header.

| Endpoint | Use |
|---|---|
| `POST /achievement` | Event in, achievement out. The response also carries `notice` (credit warning, or null) |
| `GET /day-settings` | Polled by user watch builds a few times a day: day-event `settings` plus `notice` |
| `GET /usage` | Spend this month, projected monthly cost, credit left (`setup.sh --usage`) |
| `POST/GET /trigger-test?kind=…`, `POST /trigger-test/consume` | Remote test trigger (dev watch builds; `ENABLE_TEST_TRIGGER=true`) |
| `GET /health` | Liveness |

The watch may also send `X-Watch-Error` (its last failure) on any request. It is forwarded to the
error reports.

## Settings (env vars)

See [`.env.example`](.env.example) for the full list. Put secrets in `fly secrets` (setup does
this), never in `fly.toml`.

| Variable | Role |
|---|---|
| `LLM_PROVIDER` | `anthropic` (default) or `deepseek` |
| `ANTHROPIC_API_KEY` / `DEEPSEEK_API_KEY` | **Secret.** The key for the chosen provider |
| `WATCH_SHARED_KEY` | **Secret.** The watch sends it on every request. Required, unless `ALLOW_NO_WATCH_KEY=true` for local testing |
| `CREDIT_BALANCE_USD` | Credit the user bought. Anthropic keys can't read their own balance, so the server subtracts tracked spend from this. Changing the value (a top-up) resets the count |
| `USER_NTFY_URL` | Optional: the user's own ntfy topic for credit alerts |
| `REPORT_ERRORS` | `false` turns off error reports to the developer |
| `Llm__EstimatedEventsPerDay` | Used for the cost estimate before there is real usage |
| `DATA_DIR` | Where state files live (`/data` on the volume): install id, usage, report counters |
| `HISTORY_STATE_PATH`, `TRIGGER_STATE_PATH` | Joke history and test-trigger flag files |
| `ENABLE_TEST_TRIGGER` | `true` enables the test trigger page and polling. Default `false` |
| `DAY_QUIET_FROM` / `DAY_QUIET_TO` | Quiet hours (default 23–7): no day events |
| `DAY_MAX_PER_DAY` | Daily cap on announcements (10) |
| `DAY_IDLE_HOUR` / `DAY_GOAL_HOUR` | After which hour "nothing achieved today" (22) and "step goal missed" (21) may fire |
| `BUILD_STAMP` | Set by CI at image build time; shown in error reports |

`DAY_*` changes reach the watch on its next poll, with no reinstall. Invalid values fall back to
the defaults.

## Cost and credit

Every LLM call's tokens are priced with `InputPricePerM` / `OutputPricePerM` from
`appsettings.json`. `AvgInputTokens` / `AvgOutputTokens` (about 1,200 in and 80 out) drive the
estimate before any real usage exists.

The remaining credit comes from one of these:
- DeepSeek's `GET /user/balance`, checked at most every 12 hours.
- Otherwise `CREDIT_BALANCE_USD` minus the tracked spend.

An "out of credit" API error always wins: Anthropic returns 400 "credit balance is too low" and
DeepSeek returns 402. States:

- `ok`.
- `low`: less than 14 days left at the recent rate.
- `out`.

When the state is low or out, the watch shows a "Mana Reserves Low" / "Out of Mana" System message
once a day. The developer's reports and the user's `USER_NTFY_URL` are told once, when the state
changes.

## Error reports

`ErrorReporter` posts to `Reporting:NtfyUrl` in `appsettings.json`. It reports:

- Unhandled exceptions.
- LLM failures that fell back to canned text.
- A rejected API key.
- Credit state changes.
- Watch-side errors.
- A one-time "new install" message.
- Startup config errors.

Before anything is sent:
- API keys, the shared key, bearer tokens, long tokens and URL query strings are stripped.
- No activity data or profile text is included.

Each error kind is sent at most once an hour. All of them are counted into a daily digest, which
goes out with ntfy's `Email` header to `Reporting:DigestEmail`. The server sleeps when idle, so the
digest is sent on the first request after 24 hours rather than from a timer. State is kept in
`DATA_DIR`.

## Running locally

```bash
export ANTHROPIC_API_KEY=sk-ant-...
export ALLOW_NO_WATCH_KEY=true        # or set WATCH_SHARED_KEY and send it as X-Watch-Key
export REPORT_ERRORS=false            # don't send your local experiments to the developer
dotnet run
curl -X POST http://localhost:5080/achievement -H "Content-Type: application/json" \
  -d '{"type":"activity_completed","value":10.2,"unit":"km","baselineMean":8,"baselineStd":1.5}'
```

Or with Docker, from the repo root: `docker compose up` (reads `.env`).

### Blind comparison between providers

Set a key for each provider you have. Providers without a key are skipped. Then run:

```bash
dotnet run -- compare sample-events.json           # the report shows the provider/model next to each answer
dotnet run -- compare sample-events.json --blind   # hidden and shuffled; the key goes to compare-key.json
```

The console prints average latency, tokens, fallbacks and the estimated monthly cost.

### Tests

```bash
dotnet test Tests/GarminAchievements.Tests.csproj
```

CI runs them on every push and PR, then builds the Docker image. On `main` and on `v*` tags it
publishes the image to GHCR.

## Design decisions

- **Tier and sound are decided in code, not by the model.** They come from a z-score against the
  user's own baseline:
  - ≤ -1 → cursed.
  - ≥ 0.6 → rare.
  - ≥ 1.3 → epic.
  - ≥ 2 → legendary.

  This keeps the watch animation consistent.
- **The model only writes the title, text and reward.**
  - Invalid or too-long JSON gets one retry. If the retry is still too long, it's trimmed.
  - If it fails completely, the server uses a small fallback bank.
  - It doesn't retry when the account is out of credit or the key is bad.
- **The last 10 achievements** go into the prompt to avoid repeats. They're kept as a JSON file on
  the volume.
- **One server per user**, so files are enough and there's no database.
