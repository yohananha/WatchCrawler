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

// Remote test trigger: arm here (browser page below, or curl), the watch's
// background check consumes it on its next 5-min tick and runs it through
// the real AchievementGenerator, same as a genuine detected activity - see
// watch/source/TriggerChecker.mc and BackgroundService.mc.
app.MapPost("/trigger-test", () =>
{
    TestTrigger.Arm();
    return Results.Ok(new { armed = true });
});

app.MapGet("/trigger-test", () => Results.Ok(new { armed = TestTrigger.IsArmed() }));

app.MapPost("/trigger-test/consume", () => Results.Ok(new { wasArmed = TestTrigger.ConsumeIfArmed() }));

// Unauthenticated on purpose (the key goes in the page's own field, sent as
// a header via fetch() below) - this is just the HTML shell.
app.MapGet("/", () => Results.Content("""
    <!doctype html>
    <html>
    <head><meta charset="utf-8"><title>WatchCrawler</title></head>
    <body style="font-family:system-ui,sans-serif;max-width:480px;margin:48px auto;padding:0 16px">
        <h2>WatchCrawler test trigger</h2>
        <p>Arms a real LLM-generated test achievement. Your watch picks it up on its next
        background check - usually within 5 minutes.</p>
        <input type="password" id="key" placeholder="Shared key" autocomplete="off"
               style="width:100%;padding:8px;box-sizing:border-box;font-size:16px">
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
                    var res = await fetch('/trigger-test', { method: 'POST', headers: { 'X-Watch-Key': key } });
                    result.textContent = res.ok
                        ? 'Armed! Check your watch in a few minutes.'
                        : 'Failed (' + res.status + '). Check the shared key.';
                } catch (e) {
                    result.textContent = 'Request failed: ' + e;
                }
            }
        </script>
    </body>
    </html>
    """, "text/html"));

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

    if (result.Error is not null)
        log.LogWarning("Generation issue ({Provider}): {Error}", provider.Name, result.Error);

    return Results.Ok(result.Achievement);
});

app.Run();
