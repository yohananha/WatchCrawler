using System.Net;

namespace GarminAchievements.Tests;

/// <summary>Controllable clock (UTC == local, so day boundaries are predictable).</summary>
public sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Records every request instead of sending it.</summary>
public sealed class RecordingHandler : HttpMessageHandler
{
    public List<(HttpRequestMessage Req, string Body)> Sent { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Sent.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}

public class StartupConfigTests
{
    private static LlmSettings Settings() => new()
    {
        ActiveProvider = "Anthropic",
        Providers = new()
        {
            ["Anthropic"] = new() { Kind = "anthropic", ApiKeyEnv = "ANTHROPIC_API_KEY" },
            ["DeepSeek"] = new() { Kind = "openai", ApiKeyEnv = "DEEPSEEK_API_KEY" },
        }
    };

    private static Func<string, string?> Env(params (string, string)[] vars) =>
        name => vars.FirstOrDefault(v => v.Item1 == name).Item2;

    [Fact]
    public void LlmProvider_IsMatchedCaseInsensitively()
    {
        var s = Settings();
        StartupConfig.ApplyProvider(s, Env(("LLM_PROVIDER", "deepseek")));
        Assert.Equal("DeepSeek", s.ActiveProvider);
    }

    [Fact]
    public void UnknownLlmProvider_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => StartupConfig.ApplyProvider(Settings(), Env(("LLM_PROVIDER", "gpt"))));
    }

    [Fact]
    public void MissingApiKey_IsAnError()
    {
        var errors = StartupConfig.Validate(Settings(), Env(("WATCH_SHARED_KEY", "k")));
        Assert.Contains(errors, e => e.Contains("ANTHROPIC_API_KEY"));
    }

    [Fact]
    public void MissingSharedKey_IsAnError_UnlessExplicitlyAllowed()
    {
        Assert.Contains(StartupConfig.Validate(Settings(), Env(("ANTHROPIC_API_KEY", "sk-ant-x"))),
            e => e.Contains("WATCH_SHARED_KEY"));
        Assert.Empty(StartupConfig.Validate(Settings(),
            Env(("ANTHROPIC_API_KEY", "sk-ant-x"), ("ALLOW_NO_WATCH_KEY", "true"))));
    }

    [Fact]
    public void FullyConfigured_HasNoErrors()
    {
        Assert.Empty(StartupConfig.Validate(Settings(), Env(("ANTHROPIC_API_KEY", "sk-ant-x"), ("WATCH_SHARED_KEY", "k"))));
    }
}

public class ErrorReporterTests
{
    private const string ApiKey = "sk-ant-api03-SECRETSECRET";
    private const string SharedKey = "my-watch-shared-key";

    private static (ErrorReporter reporter, RecordingHandler handler, ManualTime time) Create(string? reportErrors = null, string email = "")
    {
        var handler = new RecordingHandler();
        var time = new ManualTime(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        var llm = new LlmSettings { Providers = new() { ["Anthropic"] = new() { Model = "m", ApiKeyEnv = "ANTHROPIC_API_KEY" } } };
        var env = new Dictionary<string, string?>
        {
            ["ANTHROPIC_API_KEY"] = ApiKey, ["WATCH_SHARED_KEY"] = SharedKey, ["REPORT_ERRORS"] = reportErrors
        };
        var reporter = new ErrorReporter(new ReportingSettings { NtfyUrl = "https://ntfy.example/topic", DigestEmail = email },
            llm, new InstallInfo(path: null), new HttpClient(handler), time, n => env.GetValueOrDefault(n), statePath: null);
        return (reporter, handler, time);
    }

    [Fact]
    public async Task Report_ScrubsKeysAndQueryStrings()
    {
        var (reporter, handler, _) = Create();
        await reporter.ReportAsync("llm Http", $"HTTP 500 key={ApiKey} shared={SharedKey} url=https://x.dev/a?token=abc Bearer abc.def");

        var body = Assert.Single(handler.Sent).Body;
        Assert.DoesNotContain("SECRETSECRET", body);
        Assert.DoesNotContain(SharedKey, body);
        Assert.DoesNotContain("token=abc", body);
        Assert.DoesNotContain("abc.def", body);
        Assert.Contains("llm Http", body);
    }

    [Fact]
    public async Task SameKind_IsSentOncePerHour()
    {
        var (reporter, handler, time) = Create();
        await reporter.ReportAsync("watch -104", "no phone");
        await reporter.ReportAsync("watch -104", "no phone");
        Assert.Single(handler.Sent);

        time.Advance(TimeSpan.FromMinutes(61));
        await reporter.ReportAsync("watch -104", "no phone");
        Assert.Equal(2, handler.Sent.Count);
    }

    [Fact]
    public async Task OptOut_SendsNothing()
    {
        var (reporter, handler, _) = Create(reportErrors: "false");
        Assert.False(reporter.Enabled);
        await reporter.ReportAsync("crash", "boom");
        Assert.Empty(handler.Sent);
    }

    [Fact]
    public async Task Digest_GoesOutAfter24h_WithEmailHeader()
    {
        var (reporter, handler, time) = Create(email: "dev@example.com");
        await reporter.ReportAsync("llm Timeout", "slow");  // starts the digest clock
        time.Advance(TimeSpan.FromHours(25));
        await reporter.ReportAsync("llm Timeout", "slow");  // sends the report, then the due digest

        var digest = handler.Sent.Last();
        Assert.Contains("digest", digest.Req.Headers.GetValues("Title").Single());
        Assert.Equal("dev@example.com", digest.Req.Headers.GetValues("Email").Single());
        Assert.Contains("llm Timeout: 2x", digest.Body);
    }
}

public class UsageTrackerTests
{
    private static LlmSettings Llm(string kind = "anthropic") => new()
    {
        ActiveProvider = "P",
        EstimatedEventsPerDay = 10,
        Providers = new() { ["P"] = new() { Kind = kind, InputPricePerM = 1.0, OutputPricePerM = 5.0, AvgInputTokens = 1200, AvgOutputTokens = 80 } }
    };

    private static ManualTime Time() => new(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Estimate_UsesTypicalTokensAndPrices()
    {
        // (1200 * $1 + 80 * $5) / 1M = $0.0016 per event -> x10/day x30 = $0.48/month
        Assert.Equal(0.48, UsageTracker.Estimate(Llm().Providers["P"], 10), 3);
    }

    [Fact]
    public void Record_AddsCostToMonth()
    {
        var t = new UsageTracker(Llm(), Time(), creditBalanceUsd: null, path: null);
        t.Record(1_000_000, 0);
        t.Record(0, 200_000);
        var r = t.Snapshot();
        Assert.Equal(2.0, r.MonthToDateUsd, 3);
        Assert.Equal(2, r.MonthEvents);
        Assert.Null(r.RemainingUsd); // no credit amount known
        Assert.Equal("ok", r.State);
        Assert.Null(t.Notice());
    }

    [Fact]
    public void CreditBalance_MinusSpend_GivesRemainingAndLowState()
    {
        var time = Time();
        var t = new UsageTracker(Llm(), time, creditBalanceUsd: "5", path: null);
        // Spend $0.30/day for 3 days -> $4.10 left at $0.30/day ≈ 13.7 days -> low
        for (var d = 0; d < 3; d++)
        {
            t.Record(300_000, 0);
            time.Advance(TimeSpan.FromDays(1));
        }
        time.Advance(TimeSpan.FromDays(-1));
        var r = t.Snapshot();
        Assert.Equal(4.1, r.RemainingUsd!.Value, 2);
        Assert.Equal("low", r.State);
        Assert.Equal("low", t.Notice()!.State);
    }

    [Fact]
    public void PlentyOfCredit_IsOk()
    {
        var t = new UsageTracker(Llm(), Time(), creditBalanceUsd: "5", path: null);
        Assert.Equal("ok", t.Snapshot().State); // estimate: $0.016/day -> ~300 days
    }

    [Fact]
    public void OutOfCredit_UntilASuccessfulCall()
    {
        var t = new UsageTracker(Llm(), Time(), creditBalanceUsd: null, path: null);
        t.MarkOutOfCredit();
        Assert.Equal("out", t.Snapshot().State);
        Assert.Equal("out", t.Notice()!.State);
        t.MarkCallSucceeded();
        Assert.Equal("ok", t.Snapshot().State);
    }

    [Fact]
    public void TopUp_ResetsSpendSinceBaseline()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"usage-{Guid.NewGuid():N}.json");
        try
        {
            var t1 = new UsageTracker(Llm(), Time(), "5", path);
            t1.Record(3_000_000, 0); // $3 spent
            Assert.Equal(2.0, t1.Snapshot().RemainingUsd!.Value, 3);

            var t2 = new UsageTracker(Llm(), Time(), "12", path); // topped up: new amount
            Assert.Equal(12.0, t2.Snapshot().RemainingUsd!.Value, 3);
            Assert.Equal(3.0, t2.Snapshot().MonthToDateUsd, 3); // month total survives restarts
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void StateChange_IsReportedOnce()
    {
        var t = new UsageTracker(Llm(), Time(), null, null);
        Assert.Null(t.TakeStateChange());
        t.MarkOutOfCredit();
        Assert.Equal("out", t.TakeStateChange());
        Assert.Null(t.TakeStateChange());
    }

    [Fact]
    public void DeepSeekBalance_Parses()
    {
        var parsed = UsageTracker.ParseDeepSeekBalance("""
            {"is_available":true,"balance_infos":[{"currency":"USD","total_balance":"3.50","granted_balance":"0.00","topped_up_balance":"3.50"}]}
            """);
        Assert.Equal((3.5, true), parsed);
        Assert.Null(UsageTracker.ParseDeepSeekBalance("not json"));
    }
}

public class LlmErrorTests
{
    private static GameEvent Event() => new() { Type = "activity_completed", Value = 5, Unit = "km" };

    [Theory]
    [InlineData(400, """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low to access the Anthropic API."}}""", LlmErrorKind.OutOfCredit)]
    [InlineData(402, """{"error":{"message":"Insufficient Balance"}}""", LlmErrorKind.OutOfCredit)]
    [InlineData(401, "{}", LlmErrorKind.BadKey)]
    [InlineData(429, "{}", LlmErrorKind.RateLimited)]
    [InlineData(500, "{}", LlmErrorKind.Http)]
    public void Classify(int status, string body, LlmErrorKind expected)
    {
        Assert.Equal(expected, LlmErrors.Classify(new LlmHttpException("P", status, body)));
    }

    [Fact]
    public async Task OutOfCredit_DoesNotRetry_AndIsFlagged()
    {
        var settings = new LlmSettings();
        var gen = new AchievementGenerator(settings, new RecentHistory(settings));
        var provider = new FakeProvider()
            .Throws(new LlmHttpException("Fake", 402, "Insufficient Balance"))
            .Returns("""{"title":"x","text":"y","reward":"z"}""");

        var result = await gen.GenerateAsync(Event(), provider, recordHistory: false, CancellationToken.None);

        Assert.True(result.Achievement.IsFallback);
        Assert.Equal(LlmErrorKind.OutOfCredit, result.ErrorKind);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task Timeout_IsCaught_AndFallsBack()
    {
        var settings = new LlmSettings();
        var gen = new AchievementGenerator(settings, new RecentHistory(settings));
        var provider = new FakeProvider()
            .Throws(new TaskCanceledException("HttpClient.Timeout"))
            .Throws(new TaskCanceledException("HttpClient.Timeout"));

        var result = await gen.GenerateAsync(Event(), provider, recordHistory: false, CancellationToken.None);

        Assert.True(result.Achievement.IsFallback);
        Assert.Equal(LlmErrorKind.Timeout, result.ErrorKind);
    }
}
