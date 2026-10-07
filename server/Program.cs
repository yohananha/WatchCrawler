using System.Globalization;
using GarminAchievements;

// Usage:
//   dotnet run                                   -> API on http://localhost:5080
//   dotnet run -- compare [sample-events.json] [--blind]
//       -> comparison between providers; --blind hides the models and shuffles them into compare-key.json
//   dotnet run -- coupon add CODE PERCENT [--uses N] [--days D] [--note TEXT] | list | del CODE
//       -> hosted mode coupons, straight on the database (DATA_DIR/licenses.db)

var compareMode = args.Length > 0 && args[0].Equals("compare", StringComparison.OrdinalIgnoreCase);
var blind = compareMode && args.Contains("--blind", StringComparer.OrdinalIgnoreCase);
var eventsPath = compareMode ? args.Skip(1).FirstOrDefault(a => !a.StartsWith("--")) ?? "sample-events.json" : "sample-events.json";

if (args.Length > 0 && args[0].Equals("coupon", StringComparison.OrdinalIgnoreCase))
{
    Environment.Exit(CouponCli.Run(args.Skip(1).ToArray(), new LicenseStore()));
    return;
}

var builder = WebApplication.CreateBuilder(compareMode ? [] : args);

// PORT env var (set by Fly.io/Render/most cloud hosts) overrides the
// appsettings.json Urls binding, which is meant for local dev.
var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!compareMode && !string.IsNullOrEmpty(portEnv))
    builder.WebHost.UseUrls($"http://0.0.0.0:{portEnv}");

var settings = builder.Configuration.GetSection("Llm").Get<LlmSettings>() ?? new LlmSettings();
var reporting = builder.Configuration.GetSection("Reporting").Get<ReportingSettings>() ?? new ReportingSettings();
var hosted = HostedSettings.FromEnvironment(builder.Configuration.GetSection("Hosted").Get<HostedSettings>() ?? new HostedSettings(),
    Environment.GetEnvironmentVariable);

if (!compareMode)
{
    List<string> configErrors;
    try
    {
        StartupConfig.ApplyProvider(settings, Environment.GetEnvironmentVariable);
        configErrors = StartupConfig.Validate(settings, Environment.GetEnvironmentVariable);
        configErrors.AddRange(StartupConfig.ValidateHosted(hosted, Environment.GetEnvironmentVariable));
    }
    catch (InvalidOperationException ex)
    {
        configErrors = [ex.Message];
    }
    if (configErrors.Count > 0)
    {
        foreach (var err in configErrors) Console.Error.WriteLine($"CONFIG ERROR: {err}");
        await ErrorReporter.ReportStartupFailureAsync(reporting, string.Join("\n", configErrors));
        Environment.Exit(1);
    }
}

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(reporting);
builder.Services.AddSingleton(hosted);
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ProviderFactory>();
builder.Services.AddSingleton<RecentHistory>();
builder.Services.AddSingleton<AchievementGenerator>();
builder.Services.AddSingleton<InstallInfo>();
builder.Services.AddSingleton<ErrorReporter>();
builder.Services.AddSingleton<UsageTracker>();
if (hosted.Enabled)
{
    builder.Services.AddSingleton<LicenseStore>();
    builder.Services.AddSingleton(sp => PayPalClient.FromEnvironment(sp.GetRequiredService<IHttpClientFactory>(), Environment.GetEnvironmentVariable)!);
}

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
    requestLog.LogInformation("{Method} {Path} -> {Status} in {Ms}ms (key {Key}, device {Device}, client {Client}, ua {Ua})",
        context.Request.Method, context.Request.Path, context.Response.StatusCode, sw.ElapsedMilliseconds,
        context.Request.Headers.ContainsKey("X-Watch-Key") ? "sent" : "absent",
        context.Request.Headers["X-Device-Id"].FirstOrDefault() is { Length: > 0 } dev ? dev[..Math.Min(8, dev.Length)] : "-",
        client, context.Request.Headers.UserAgent.ToString());
});

var reporter = app.Services.GetRequiredService<ErrorReporter>();
var install = app.Services.GetRequiredService<InstallInfo>();
var usage = app.Services.GetRequiredService<UsageTracker>();
var store = hosted.Enabled ? app.Services.GetRequiredService<LicenseStore>() : null;
var paypal = hosted.Enabled ? app.Services.GetService<PayPalClient>() : null;
var lemon = hosted.Enabled ? LemonSqueezy.Settings.FromEnvironment(Environment.GetEnvironmentVariable) : null;
if (install.IsNew)
    reporter.Report("new install", $"A new WatchCrawler server started ({settings.ActiveProvider}{(hosted.Enabled ? ", hosted" : "")}).", priority: 2, tags: "tada");

// Hosted mode: the daily digest carries the business numbers, so a quiet day still reports.
if (store is not null)
    reporter.ExtraDigest = () =>
    {
        var st = store.Stats(DateTimeOffset.UtcNow, hosted);
        return $"Hosted: {st.Devices} devices ({st.Trials} on trial, {st.Licensed} licensed, {st.Expired} expired), " +
               $"{st.Orders} paid + {st.Redeemed} free unlocks, {st.AiToday} AI calls today, " +
               $"${st.SpendMonthUsd:0.00} this month / ${st.SpendTotalUsd:0.00} total.";
    };

// Unhandled exceptions: report, then answer JSON (the watch can't parse an HTML error page).
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested)
    {
        requestLog.LogError(ex, "Unhandled error on {Path}", context.Request.Path);
        reporter.Report($"crash {ex.GetType().Name}", $"{context.Request.Path}: {ex.Message}", priority: 4, tags: "rotating_light");
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = "Server error." });
        }
    }
});

// Shared-key auth: the watch sends X-Watch-Key, checked against the
// WATCH_SHARED_KEY env var. Startup refuses to run without one unless
// ALLOW_NO_WATCH_KEY=true (local testing), so randoms can't burn the API credit.
// In hosted mode the key is baked into a public store build, so it only keeps
// out casual scripts; the per-device trial/licence is what limits spend there,
// and /usage moves behind the admin key.
var watchKey = Environment.GetEnvironmentVariable("WATCH_SHARED_KEY");
var adminKey = Environment.GetEnvironmentVariable("ADMIN_KEY");
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isAdmin = path.StartsWithSegments("/admin") || (hosted.Enabled && path.StartsWithSegments("/usage"));
    if (isAdmin)
    {
        if (string.IsNullOrEmpty(adminKey) || context.Request.Headers["X-Admin-Key"] != adminKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid X-Admin-Key." });
            return;
        }
        await next();
        return;
    }
    var isProtected = path.StartsWithSegments("/achievement")
        || path.StartsWithSegments("/trigger-test")
        || path.StartsWithSegments("/day-settings")
        || path.StartsWithSegments("/usage");
    if (isProtected && !string.IsNullOrEmpty(watchKey) && context.Request.Headers["X-Watch-Key"] != watchKey)
    {
        // JSON, not plain text: the watch requests
        // HTTP_RESPONSE_CONTENT_TYPE_JSON and can't parse a text body,
        // which surfaced as a confusing -400 (INVALID_HTTP_BODY_IN_
        // NETWORK_RESPONSE) instead of a clear 401 - found by testing
        // an intentionally-wrong key against the real deployment.
        // A real watch (it sends its device id) with the wrong key falls back to its built-in lines
        // and can never deliver its own X-Watch-Error, so this is the only place it gets noticed.
        if (context.Request.Headers["X-Device-Id"].FirstOrDefault() is { Length: > 0 } dev)
            reporter.Report("watch bad key",
                $"Watch {dev[..Math.Min(8, dev.Length)]} was refused on {path} (key {(context.Request.Headers.ContainsKey("X-Watch-Key") ? "wrong" : "missing")}). It only gets built-in lines until it is rebuilt with WATCH_SHARED_KEY.",
                priority: 4, tags: "key");
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid X-Watch-Key." });
        return;
    }
    // Hosted mode: every watch call must say which watch it is (System.getDeviceSettings().uniqueIdentifier).
    if (hosted.Enabled && (path.StartsWithSegments("/achievement") || path.StartsWithSegments("/day-settings")))
    {
        var id = context.Request.Headers["X-Device-Id"].FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(id) || id.Length > 64 || id.Any(c => c < ' ' || c > '~'))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid X-Device-Id." });
            return;
        }
        context.Items["deviceId"] = id;
    }
    await next();
});

// The watch reports its own last failure (network code, background error) in X-Watch-Error on its next
// successful request - it has no other way to tell anyone. Only after auth, so strangers can't spam reports.
app.Use(async (context, next) =>
{
    var watchError = context.Request.Headers["X-Watch-Error"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(watchError))
    {
        var code = watchError.Split(' ', 2)[0];
        reporter.Report($"watch {code}", watchError, priority: 3, tags: "watch");
    }
    await next();
});

// Day-event tuning, changeable from Fly (env vars) without reinstalling the watch app: the watch reads
// these from every settings/trigger poll (see DayEvents.mc). Out-of-range values fall back to the defaults.
var dayEventSettings = DayEventSettings.FromEnvironment();

app.MapGet("/health", () => Results.Ok("ok"));

// The public pages: privacy policy (the store listing links to it), terms and refunds (Lemon Squeezy wants them),
// and in hosted mode the product page at /. Images for them are served from wwwroot/img.
app.UseStaticFiles();
var contact = Site.ContactFromEnvironment();
app.MapGet("/privacy", () => Results.Content(PrivacyPage.Html(hosted, contact), "text/html"));
app.MapGet("/terms", () => Results.Content(LegalPages.Terms(hosted, contact), "text/html"));
app.MapGet("/refunds", () => Results.Content(LegalPages.Refunds(hosted, contact), "text/html"));

// Spend so far and credit left, for `setup --usage` / --topup and curious users (admin-only when hosted).
app.MapGet("/usage", () => Results.Ok(usage.Snapshot()));

// Release watch builds poll this (a few times a day) for day-event tuning and credit warnings;
// dev builds get the same from /trigger-test/consume.
app.MapGet("/day-settings", async (HttpContext ctx, IHttpClientFactory http) =>
{
    await RefreshCreditAsync(http);
    if (store is null) return Results.Ok(new { settings = dayEventSettings, notice = usage.Notice() });
    var (device, ent) = Entitle(ctx);
    return Results.Ok(new { settings = dayEventSettings, notice = Licensing.Notice(device, ent, hosted), license = Licensing.Info(device, ent, hosted) });
});

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

app.MapPost("/trigger-test/consume", async (ILogger<Program> log, IHttpClientFactory http) =>
{
    await RefreshCreditAsync(http);
    var notice = usage.Notice();
    if (!testTriggerEnabled)
    {
        log.LogInformation("Watch polled trigger: feature disabled");
        return Results.Ok(new { wasArmed = false, enabled = false, settings = dayEventSettings, notice });
    }
    var kind = TestTrigger.ConsumeKind();
    var wasArmed = kind != null;
    log.LogInformation("Watch polled trigger: wasArmed={WasArmed} kind={Kind}", wasArmed, kind);
    return Results.Ok(new { wasArmed, enabled = true, kind, settings = dayEventSettings, notice });
});

static IResult TriggerDisabled() =>
    Results.Json(new { error = "Test trigger disabled (set ENABLE_TEST_TRIGGER=true)." }, statusCode: StatusCodes.Status403Forbidden);

// Unauthenticated on purpose (the key goes in the page's own field, sent as
// a header via fetch() below) - this is just the HTML shell. Hosted servers
// show the product page instead.
var kindOptions = string.Join("", TestTrigger.Kinds.Select(k => $"<option value=\"{k.Key}\">{k.Value}</option>"));
var storeUrl = Environment.GetEnvironmentVariable("CONNECTIQ_URL");
app.MapGet("/", () => hosted.Enabled ? Results.Content(LandingPage.Html(hosted, contact, storeUrl), "text/html") : Results.Content(("""
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
    HttpContext ctx,
    GameEvent e,
    AchievementGenerator generator,
    ProviderFactory factory,
    LlmSettings s,
    IHttpClientFactory http,
    ILogger<Program> log,
    CancellationToken ct) =>
{
    var provider = factory.Create(s.ActiveProvider);

    GenerationResult result;
    Device? device = null;
    Entitlement? ent = null;
    if (store is null)
    {
        result = await generator.GenerateAsync(e, provider, recordHistory: true, ct);
    }
    else
    {
        // Hosted: the watch only gets an LLM line while on trial / licensed, under its daily cap,
        // and while the month's budget (and the API account) hold. Otherwise it writes its own.
        (device, ent) = Entitle(ctx);
        if (!ent.AiAllowed)
        {
            log.LogInformation("No AI for device {Device}: {State}", device.Code, ent.State);
            await RefreshCreditAsync(http);
            return Results.Ok(new LocalReply(true, ent.State, Licensing.Notice(device, ent, hosted), Licensing.Info(device, ent, hosted)));
        }
        var id = device.Id;
        result = await generator.GenerateAsync(e, provider, store.History(id, s.RecentHistorySize),
            t => store.AddHistory(id, t, s.RecentHistorySize), ct);
        // Only a real line counts against the watch's daily cap; a failed call is free for the user.
        if (!result.Achievement.IsFallback)
            store.RecordAi(id, DateTimeOffset.UtcNow, s.Providers[s.ActiveProvider].CostUsd(result.InputTokens, result.OutputTokens));
    }

    var a = result.Achievement;
    log.LogInformation(
        "Achievement for {Type} value={Value}: tier={Tier} via {Provider}/{Model} fallback={Fallback} attempts={Attempts} tokens={In}+{Out} in {Ms}ms",
        e.Type, e.Value, a.Tier, a.Provider, a.Model, a.IsFallback, result.Attempts,
        result.InputTokens, result.OutputTokens, (long)result.Latency.TotalMilliseconds);

    if (result.Error is not null)
        log.LogWarning("Generation issue ({Provider}): {Error}", provider.Name, result.Error);

    usage.Record(result.InputTokens, result.OutputTokens);
    switch (result.ErrorKind)
    {
        case LlmErrorKind.OutOfCredit:
            usage.MarkOutOfCredit();
            break;
        case LlmErrorKind.BadKey:
            reporter.Report("bad API key", $"{provider.Name} rejected the API key: {result.Error}", priority: 4, tags: "key");
            break;
        case LlmErrorKind.None:
            usage.MarkCallSucceeded();
            break;
        default:
            // Only worth a report when the watch got the canned fallback, not a trimmed/retried success.
            if (a.IsFallback)
                reporter.Report($"llm {result.ErrorKind}", $"{provider.Name}/{provider.Model}: {result.Error}");
            break;
    }
    await RefreshCreditAsync(http);

    if (device is null || ent is null)
        return Results.Ok(result.Achievement with { Notice = usage.Notice() });
    // Hosted: the watch's own text bank beats the server's three "satellite is down" lines.
    if (a.IsFallback)
        return Results.Ok(new LocalReply(true, "llm-failed", Licensing.Notice(device, ent, hosted), Licensing.Info(device, ent, hosted)));
    return Results.Ok(result.Achievement with { Notice = Licensing.Notice(device, ent, hosted), License = Licensing.Info(device, ent, hosted) });
});

// ---- hosted mode: unlock page, payments, coupons, admin ----------------------------

if (store is not null)
{
    app.MapGet("/unlock", () => Results.Content(UnlockPage.Html(hosted, paypal?.ClientId, lemon is not null), "text/html"));

    // Lemon Squeezy (merchant of record) tells us an order was paid. Signed with the webhook secret; the watch
    // code rides along as custom data from the checkout link. Idempotent per order id.
    app.MapPost("/webhook/lemonsqueezy", async (HttpContext ctx, ILogger<Program> log) =>
    {
        if (lemon is null) return Results.StatusCode(404);
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();
        if (!LemonSqueezy.VerifySignature(body, ctx.Request.Headers["X-Signature"].FirstOrDefault(), lemon.WebhookSecret))
        {
            reporter.Report("lemonsqueezy bad signature", $"{body.Length} bytes from {ctx.Connection.RemoteIpAddress}", priority: 4, tags: "moneybag");
            return Results.Json(new { error = "Bad signature." }, statusCode: 401);
        }
        var order = LemonSqueezy.Parse(body);
        if (order is null) return Results.Json(new { error = "Unreadable payload." }, statusCode: 400);
        if (order.EventName == "order_refunded")
        {
            // Full refund: the years that order bought are taken back. A partial refund (a goodwill discount)
            // keeps them, but you get told.
            if (order.Status != "refunded")
            {
                reporter.Report("partial refund", $"Lemon Squeezy order {order.OrderId} is {order.Status}; licence kept.", priority: 3, tags: "moneybag");
                return Results.Ok(new { ignored = order.Status });
            }
            var refunded = store.RefundOrder($"ls:{order.OrderId}", DateTimeOffset.UtcNow, hosted.LicenseYears);
            reporter.Report("refund", refunded is null
                ? $"Lemon Squeezy order {order.OrderId} refunded; nothing to take back (unknown, unpaid or already refunded)."
                : $"Lemon Squeezy order {order.OrderId} refunded{(order.TestMode ? " (TEST MODE)" : "")}; {refunded.Code} is now " +
                  (refunded.LicensedUntil is { } left ? $"licensed until {left:yyyy-MM-dd}." : "unlicensed."),
                priority: 3, tags: "moneybag");
            return Results.Ok(new { code = refunded?.Code, licensedUntil = refunded?.LicensedUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        }
        if (order.EventName != "order_created") return Results.Ok(new { ignored = order.EventName });
        if (order.Status != "paid")
        {
            log.LogInformation("Lemon Squeezy order {Order} is {Status}; waiting", order.OrderId, order.Status);
            return Results.Ok(new { ignored = order.Status });
        }
        var device = order.Code is null ? null : store.ByCode(order.Code);
        if (device is null)
        {
            // Paid, but we can't tell which watch: a report so it can be fixed by hand (POST /admin/license).
            reporter.Report("sale without code", $"Lemon Squeezy order {order.OrderId} (${order.TotalUsd:0.00}, {order.Email}) has no known watch code '{order.Code}'.", priority: 4, tags: "moneybag");
            return Results.Ok(new { warning = "unknown code" });
        }
        var orderId = $"ls:{order.OrderId}";
        var now = DateTimeOffset.UtcNow;
        store.CreateOrder(orderId, device.Id, device.Code, order.TotalUsd, null, now);
        var licensed = store.Fulfil(orderId, now, hosted.LicenseYears);
        reporter.Report("sale", $"${order.TotalUsd:0.00} unlock{(order.TestMode ? " (TEST MODE)" : "")} for {device.Code}, licensed until {licensed.LicensedUntil:yyyy-MM-dd}.", priority: 2, tags: "moneybag");
        return Results.Ok(new { licensedUntil = licensed.LicensedUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
    });

    // What would this code pay? Also tells a user who already paid that they are fine.
    app.MapGet("/unlock/price", (string? code, string? coupon) =>
    {
        var device = store.ByCode(code ?? "");
        if (device is null) return Results.Json(new { error = "Unknown code. Check the code on your watch (open WatchCrawler, press MENU)." }, statusCode: 404);
        var now = DateTimeOffset.UtcNow;
        var c = store.UsableCoupon(coupon, now);
        // With Lemon Squeezy, our coupons table only does free (100%) unlocks; any other code is one of
        // THEIR discount codes and goes through to the checkout, where they validate it.
        var lsDiscount = lemon is not null && !string.IsNullOrWhiteSpace(coupon) && (c is null || c.PercentOff < 100) ? coupon!.Trim() : null;
        if (!string.IsNullOrWhiteSpace(coupon) && c is null && lemon is null)
            return Results.Json(new { error = "That coupon is not valid (unknown, expired or used up)." }, statusCode: 400);
        var ent = Licensing.Check(device, hosted, now);
        var licensed = device.LicensedUntil is { } u && u > now;
        var status = licensed ? $"Already unlocked until {device.LicensedUntil:yyyy-MM-dd}. Paying again adds {hosted.LicenseYears} more years."
            : ent.State == Entitlement.Expired ? "Trial over: unlock to restore AI narration."
            : $"On trial, {ent.DaysLeft} day(s) left. Unlock now and the {hosted.LicenseYears} years start today.";
        var price = lsDiscount is not null ? Licensing.Price(hosted, null) : Licensing.Price(hosted, c);
        return Results.Ok(new
        {
            code = device.Code, listPrice = Licensing.Price(hosted, null), price, coupon = lsDiscount is null ? c?.Code : null, licensed, status,
            checkoutUrl = lemon is null || price <= 0 ? null : LemonSqueezy.CheckoutUrl(lemon.CheckoutUrl, device.Code, lsDiscount),
            discountAtCheckout = lsDiscount,
        });
    });

    app.MapPost("/unlock/order", async (UnlockRequest req, CancellationToken ct) =>
    {
        if (paypal is null) return Results.Json(new { error = "Payments are not set up on this server." }, statusCode: 503);
        var device = store.ByCode(req.Code ?? "");
        if (device is null) return Results.Json(new { error = "Unknown code." }, statusCode: 404);
        var now = DateTimeOffset.UtcNow;
        var c = store.UsableCoupon(req.Coupon, now);
        if (!string.IsNullOrWhiteSpace(req.Coupon) && c is null) return Results.Json(new { error = "That coupon is not valid." }, statusCode: 400);
        var price = Licensing.Price(hosted, c);
        if (price <= 0) return Results.Json(new { error = "This coupon makes it free: use the redeem button." }, statusCode: 400);
        var orderId = await paypal.CreateOrderAsync(price, device.Code, $"WatchCrawler unlock ({hosted.LicenseYears} years), code {device.Code}", ct);
        store.CreateOrder(orderId, device.Id, device.Code, price, c?.Code, now);
        return Results.Ok(new { orderId, price });
    });

    app.MapPost("/unlock/capture", async (CaptureRequest req, ILogger<Program> log, CancellationToken ct) =>
    {
        if (paypal is null) return Results.Json(new { error = "Payments are not set up on this server." }, statusCode: 503);
        var order = string.IsNullOrWhiteSpace(req.OrderId) ? null : store.GetOrder(req.OrderId);
        if (order is null) return Results.Json(new { error = "Unknown order." }, statusCode: 404);
        var now = DateTimeOffset.UtcNow;
        if (order.Captured is null)
        {
            PayPalClient.Capture cap;
            try { cap = await paypal.CaptureAsync(order.OrderId, ct); }
            catch (HttpRequestException ex)
            {
                log.LogWarning("PayPal capture failed for {Order}: {Error}", order.OrderId, ex.Message);
                reporter.Report("paypal capture", ex.Message, priority: 3, tags: "moneybag");
                return Results.Json(new { error = "PayPal did not confirm the payment. If you were charged, reply to your receipt." }, statusCode: 502);
            }
            if (cap.Status != "COMPLETED" || cap.AmountUsd + 0.005 < order.AmountUsd)
            {
                reporter.Report("paypal mismatch", $"order {order.OrderId}: status {cap.Status}, got ${cap.AmountUsd:0.00} for ${order.AmountUsd:0.00}", priority: 4, tags: "moneybag");
                return Results.Json(new { error = $"Payment not completed (status {cap.Status})." }, statusCode: 402);
            }
        }
        var device = store.Fulfil(order.OrderId, now, hosted.LicenseYears);
        reporter.Report("sale", $"${order.AmountUsd:0.00} unlock{(order.Coupon is null ? "" : $" (coupon {order.Coupon})")} for {device.Code}, licensed until {device.LicensedUntil:yyyy-MM-dd}.", priority: 2, tags: "moneybag");
        return Results.Ok(new { licensedUntil = device.LicensedUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
    });

    // A 100% coupon (friends, testers): no PayPal round trip at all.
    app.MapPost("/unlock/redeem", (UnlockRequest req) =>
    {
        var device = store.ByCode(req.Code ?? "");
        if (device is null) return Results.Json(new { error = "Unknown code." }, statusCode: 404);
        var now = DateTimeOffset.UtcNow;
        var c = store.UsableCoupon(req.Coupon, now);
        if (c is null || Licensing.Price(hosted, c) > 0) return Results.Json(new { error = "That coupon does not give a free unlock." }, statusCode: 400);
        var orderId = $"coupon:{c.Code}:{device.Id}:{Guid.NewGuid():N}";
        store.CreateOrder(orderId, device.Id, device.Code, 0, c.Code, now);
        var licensed = store.Fulfil(orderId, now, hosted.LicenseYears);
        reporter.Report("free unlock", $"coupon {c.Code} ({c.Uses + 1}/{c.MaxUses}) for {device.Code}, licensed until {licensed.LicensedUntil:yyyy-MM-dd}.", priority: 2, tags: "gift");
        return Results.Ok(new { licensedUntil = licensed.LicensedUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
    });

    // ---- admin (X-Admin-Key) ----
    app.MapGet("/admin/stats", () => Results.Ok(new { stats = store.Stats(DateTimeOffset.UtcNow, hosted), usage = usage.Snapshot(), topSpenders = store.TopSpenders(10) }));
    app.MapGet("/admin/coupons", () => Results.Ok(store.Coupons()));
    app.MapPost("/admin/coupons", (CouponRequest req) =>
    {
        if (string.IsNullOrWhiteSpace(req.Code)) return Results.Json(new { error = "code is required." }, statusCode: 400);
        try
        {
            return Results.Ok(store.UpsertCoupon(req.Code, req.PercentOff ?? 100, req.MaxUses ?? 1,
                req.ExpiresInDays is { } d ? DateTimeOffset.UtcNow.AddDays(d) : null, req.Note));
        }
        catch (ArgumentException ex) { return Results.Json(new { error = ex.Message }, statusCode: 400); }
    });
    app.MapDelete("/admin/coupons/{code}", (string code) => store.DeleteCoupon(code) ? Results.Ok() : Results.NotFound());
    app.MapGet("/admin/devices/{code}", (string code) => store.ByCode(code) is { } d
        ? Results.Ok(new { device = d, entitlement = Licensing.Check(d, hosted, DateTimeOffset.UtcNow) }) : Results.NotFound());
    // Manual licence: a refund (revoke), a gift, or moving a paid licence to a new watch.
    app.MapPost("/admin/license", (LicenseRequest req) =>
    {
        var now = DateTimeOffset.UtcNow;
        if (req.Revoke == true && store.ByCode(req.Code ?? "") is { } rd) { store.Revoke(rd.Id); return Results.Ok(store.ById(rd.Id)); }
        if (req.TransferOrderId is { Length: > 0 } order && store.ByCode(req.Code ?? "") is { } td)
            return store.Transfer(order, td.Id, now) is { } moved ? Results.Ok(moved) : Results.Json(new { error = "No licensed device has that order id." }, statusCode: 404);
        if (store.ByCode(req.Code ?? "") is { } d)
            return Results.Ok(store.License(d.Id, $"admin:{now:yyyyMMddHHmmss}", now, req.Years ?? hosted.LicenseYears));
        return Results.Json(new { error = "Unknown code." }, statusCode: 404);
    });
    app.MapDelete("/admin/devices/{code}", (string code) =>
    {
        if (store.ByCode(code) is not { } d) return Results.NotFound();
        store.Forget(d.Id);
        return Results.Ok();
    });
}

app.Run();

// Hosted mode: the device behind this request (created on first contact) and what it is entitled to now.
(Device Device, Entitlement Entitlement) Entitle(HttpContext ctx)
{
    var now = DateTimeOffset.UtcNow;
    var device = store!.Touch((string)ctx.Items["deviceId"]!, now);
    var overBudget = usage.IsOutOfCredit || (hosted.MonthlyBudgetUsd > 0 && store.SpendThisMonth(now) >= hosted.MonthlyBudgetUsd);
    if (overBudget)
        reporter.Report("budget exhausted", $"Hosted: ${store.SpendThisMonth(now):0.00} spent this month (budget ${hosted.MonthlyBudgetUsd:0.00}, out of credit: {usage.IsOutOfCredit}). Watches get built-in lines.", priority: 4, tags: "moneybag");
    return (device, Licensing.Check(device, hosted, now, overBudget));
}

// DeepSeek balance (at most every 12h; Anthropic keys can't read theirs), then tell the owner and the
// user's own ntfy topic (USER_NTFY_URL, optional) once whenever the credit state changes.
async Task RefreshCreditAsync(IHttpClientFactory http)
{
    try
    {
        if (settings.Providers.TryGetValue(settings.ActiveProvider, out var p)
            && p.BaseUrl.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
            && usage.ProviderBalanceStale)
        {
            var client = http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{p.BaseUrl.TrimEnd('/')}/user/balance");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                Environment.GetEnvironmentVariable(p.ApiKeyEnv));
            using var res = await client.SendAsync(req);
            if (res.IsSuccessStatusCode && UsageTracker.ParseDeepSeekBalance(await res.Content.ReadAsStringAsync()) is { } bal)
                usage.SetProviderBalance(bal.Usd, bal.IsAvailable);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning("Balance check failed: {Error}", ex.Message);
    }

    if (usage.TakeStateChange() is { } state)
    {
        var snap = usage.Snapshot();
        var text = $"API credit is now '{state}' (remaining ~${snap.RemainingUsd?.ToString("0.00") ?? "?"}, " +
                   $"~{snap.DaysLeft?.ToString("0") ?? "?"} days, ~${snap.ProjectedMonthUsd:0.00}/month).";
        if (state != "ok") reporter.Report($"credit {state}", text, priority: 3, tags: "moneybag");
        if (Environment.GetEnvironmentVariable("USER_NTFY_URL") is { Length: > 0 } userNtfy)
        {
            try
            {
                using var userReq = new HttpRequestMessage(HttpMethod.Post, userNtfy) { Content = new StringContent(text) };
                userReq.Headers.Add("Title", state == "ok" ? "WatchCrawler: credit OK again" : "WatchCrawler: top up your API credit");
                userReq.Headers.Add("Tags", "moneybag");
                await http.CreateClient().SendAsync(userReq);
            }
            catch (Exception) { /* best effort */ }
        }
    }
    await reporter.MaybeSendDigestAsync();
}

sealed record UnlockRequest(string? Code, string? Coupon);
sealed record CaptureRequest(string? OrderId);
sealed record CouponRequest(string? Code, int? PercentOff, int? MaxUses, int? ExpiresInDays, string? Note);
sealed record LicenseRequest(string? Code, int? Years, bool? Revoke, string? TransferOrderId);
