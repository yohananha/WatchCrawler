using GarminAchievements;

// Usage:
//   dotnet run                                   -> API on http://localhost:5080
//   dotnet run -- compare [sample-events.json] [--blind]
//       -> comparison between providers; --blind hides the models and shuffles them into compare-key.json

var compareMode = args.Length > 0 && args[0].Equals("compare", StringComparison.OrdinalIgnoreCase);
var blind = compareMode && args.Contains("--blind", StringComparer.OrdinalIgnoreCase);
var eventsPath = compareMode ? args.Skip(1).FirstOrDefault(a => !a.StartsWith("--")) ?? "sample-events.json" : "sample-events.json";

var builder = WebApplication.CreateBuilder(compareMode ? [] : args);

// PORT env var (set by Fly.io/Render/most cloud hosts) overrides the
// appsettings.json Urls binding, which is meant for local dev.
var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!compareMode && !string.IsNullOrEmpty(portEnv))
    builder.WebHost.UseUrls($"http://0.0.0.0:{portEnv}");

var settings = builder.Configuration.GetSection("Llm").Get<LlmSettings>() ?? new LlmSettings();
builder.Services.AddSingleton(settings);
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ProviderFactory>();
builder.Services.AddSingleton<RecentHistory>();
builder.Services.AddSingleton<AchievementGenerator>();

var app = builder.Build();

if (compareMode)
{
    await Comparison.RunAsync(app.Services, eventsPath, blind);
    return;
}

// One line per request (before auth, so rejected ones show too). Success is
// otherwise silent, which made "did the watch ever check in?" unanswerable
// from the Fly logs. Never logs the key itself, only whether one was sent.
var requestLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Requests");
app.Use(async (context, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    await next();
    if (context.Request.Path.StartsWithSegments("/health")) return;
    var client = context.Request.Headers["Fly-Client-IP"].FirstOrDefault() ?? context.Connection.RemoteIpAddress?.ToString();
    requestLog.LogInformation("{Method} {Path} -> {Status} in {Ms}ms (key {Key}, client {Client}, ua {Ua})",
        context.Request.Method, context.Request.Path, context.Response.StatusCode, sw.ElapsedMilliseconds,
        context.Request.Headers.ContainsKey("X-Watch-Key") ? "sent" : "absent", client,
        context.Request.Headers.UserAgent.ToString());
});

// Shared-key auth: the watch sends X-Watch-Key, checked against the
// WATCH_SHARED_KEY env var. Unset -> auth is off (local dev convenience;
// always set it in production so randoms can't burn your Anthropic quota).
var watchKey = Environment.GetEnvironmentVariable("WATCH_SHARED_KEY");
if (!string.IsNullOrEmpty(watchKey))
{
    app.Use(async (context, next) =>
    {
        var isProtected = context.Request.Path.StartsWithSegments("/achievement")
            || context.Request.Path.StartsWithSegments("/trigger-test");
        if (isProtected && context.Request.Headers["X-Watch-Key"] != watchKey)
        {
            // JSON, not plain text: the watch requests
            // HTTP_RESPONSE_CONTENT_TYPE_JSON and can't parse a text body,
            // which surfaced as a confusing -400 (INVALID_HTTP_BODY_IN_
            // NETWORK_RESPONSE) instead of a clear 401 - found by testing
            // an intentionally-wrong key against the real deployment.
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid X-Watch-Key." });
            return;
        }
        await next();
    });
}

app.MapGet("/health", () => Results.Ok("ok"));

// ENABLE_TEST_TRIGGER=true turns the remote test trigger on. Off by default so
// a production deploy doesn't expose it (or get woken by the watch polling
// for it). When off, /trigger-test* refuses and /trigger-test/consume tells
// the watch {enabled:false} so it stops polling for a while.
var testTriggerEnabled = string.Equals(
    Environment.GetEnvironmentVariable("ENABLE_TEST_TRIGGER"), "true", StringComparison.OrdinalIgnoreCase);

// Remote test trigger: arm here (browser page below, or curl), the watch's
// background check consumes it on its next 5-min tick and runs it through
// the real AchievementGenerator, same as a genuine detected activity - see
// watch/source/TriggerChecker.mc and BackgroundService.mc.
app.MapPost("/trigger-test", (string? kind, ILogger<Program> log) =>
{
    if (!testTriggerEnabled) return TriggerDisabled();
    kind ??= TestTrigger.DefaultKind;
    if (!TestTrigger.Kinds.ContainsKey(kind))
        return Results.Json(new { error = $"Unknown kind '{kind}'." }, statusCode: StatusCodes.Status400BadRequest);
    TestTrigger.Arm(kind);
    log.LogInformation("Test trigger ARMED kind={Kind}", kind);
    return Results.Ok(new { armed = true, kind });
});

app.MapGet("/trigger-test", () =>
    testTriggerEnabled ? Results.Ok(new { armed = TestTrigger.IsArmed() }) : TriggerDisabled());

app.MapPost("/trigger-test/consume", (ILogger<Program> log) =>
{
    if (!testTriggerEnabled)
    {
        log.LogInformation("Watch polled trigger: feature disabled");
        return Results.Ok(new { wasArmed = false, enabled = false });
    }
    var kind = TestTrigger.ConsumeKind();
    var wasArmed = kind != null;
    log.LogInformation("Watch polled trigger: wasArmed={WasArmed} kind={Kind}", wasArmed, kind);
    return Results.Ok(new { wasArmed, enabled = true, kind });
});

static IResult TriggerDisabled() =>
    Results.Json(new { error = "Test trigger disabled (set ENABLE_TEST_TRIGGER=true)." }, statusCode: StatusCodes.Status403Forbidden);

// Unauthenticated on purpose (the key goes in the page's own field, sent as
// a header via fetch() below) - this is just the HTML shell.
var kindOptions = string.Join("", TestTrigger.Kinds.Select(k => $"<option value=\"{k.Key}\">{k.Value}</option>"));
app.MapGet("/", () => Results.Content(("""
    <!doctype html>
    <html>
    <head><meta charset="utf-8"><title>WatchCrawler</title></head>
    <body style="font-family:system-ui,sans-serif;max-width:480px;margin:48px auto;padding:0 16px">
        <h2>WatchCrawler test trigger</h2>
        <p>Arms a real LLM-generated test achievement. Your watch picks it up on its next
        background check. Garmin schedules these loosely, so expect anywhere from 5 to 30 minutes.</p>
        <input type="password" id="key" placeholder="Shared key" autocomplete="off"
               style="width:100%;padding:8px;box-sizing:border-box;font-size:16px">
        <select id="kind" style="width:100%;margin-top:10px;padding:8px;box-sizing:border-box;font-size:16px">
            {{KIND_OPTIONS}}
        </select>
        <button onclick="trigger()" style="margin-top:10px;padding:10px 18px;font-size:16px">
            Trigger test achievement
        </button>
        <p id="result" style="margin-top:12px"></p>
        <script>
            async function trigger() {
                var key = document.getElementById('key').value;
                var result = document.getElementById('result');
                result.textContent = 'Arming...';
                try {
                    var res = await fetch('/trigger-test?kind=' + encodeURIComponent(document.getElementById('kind').value), { method: 'POST', headers: { 'X-Watch-Key': key } });
                    result.textContent = res.ok
                        ? 'Armed! Check your watch in 5-30 minutes.'
                        : 'Failed (' + res.status + '). Check the shared key.';
                } catch (e) {
                    result.textContent = 'Request failed: ' + e;
                }
            }
        </script>
    </body>
    </html>
    """).Replace("{{KIND_OPTIONS}}", kindOptions), "text/html"));

app.MapPost("/achievement", async (
    GameEvent e,
    AchievementGenerator generator,
    ProviderFactory factory,
    LlmSettings s,
    ILogger<Program> log,
    CancellationToken ct) =>
{
    var provider = factory.Create(s.ActiveProvider);
    var result = await generator.GenerateAsync(e, provider, recordHistory: true, ct);

    var a = result.Achievement;
    log.LogInformation(
        "Achievement for {Type} value={Value}: tier={Tier} via {Provider}/{Model} fallback={Fallback} attempts={Attempts} tokens={In}+{Out} in {Ms}ms",
        e.Type, e.Value, a.Tier, a.Provider, a.Model, a.IsFallback, result.Attempts,
        result.InputTokens, result.OutputTokens, (long)result.Latency.TotalMilliseconds);

    if (result.Error is not null)
        log.LogWarning("Generation issue ({Provider}): {Error}", provider.Name, result.Error);

    return Results.Ok(result.Achievement);
});

app.Run();
