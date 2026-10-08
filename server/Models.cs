namespace GarminAchievements;

/// <summary>An event reported by the watch (or by a server-side poller).</summary>
public sealed record GameEvent
{
    /// <summary>e.g. activity_completed, steps_goal, wake, sleep_summary, sedentary, resting_hr, body_battery_low</summary>
    public required string Type { get; init; }
    public double? Value { get; init; }
    public string? Unit { get; init; }

    /// <summary>Personal baseline (e.g. 14-day rolling). Later computed on the server; for now it can be sent in.</summary>
    public double? BaselineMean { get; init; }
    public double? BaselineStd { get; init; }

    /// <summary>false for metrics where lower is better (resting HR, 5K time).</summary>
    public bool HigherIsBetter { get; init; } = true;

    public Dictionary<string, string>? Details { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}

public enum Tier { Cursed, Common, Rare, Epic, Legendary }

/// <summary>What the LLM writes. Everything else is decided deterministically.</summary>
public sealed record AchievementText(string Title, string Text, string Reward);

/// <summary>What the watch receives.</summary>
public sealed record Achievement(
    string Title,
    string Text,
    string Reward,
    string Tier,
    string Sound,
    bool IsFailure,
    string Provider,
    string Model,
    bool IsFallback)
{
    /// <summary>Low/out-of-credit warning for the watch to show once a day (null when credit is fine).</summary>
    public CreditNotice? Notice { get; init; }
    /// <summary>Hosted mode only: the watch's trial/licence state and unlock code.</summary>
    public LicenseInfo? License { get; init; }
}

/// <summary>Hosted mode's answer when the LLM is not called for this watch (trial over, daily cap, budget): no
/// title, so the watch writes the line itself from its built-in bank, but it still gets the notice and licence.</summary>
public sealed record LocalReply(bool Local, string Reason, CreditNotice? Notice, LicenseInfo? License);

/// <summary>A "System" message about the API credit, shaped like an achievement so the watch can reuse its view.</summary>
public sealed record CreditNotice(string State, string Title, string Text, string Reward);

public sealed record LlmResult(string Content, int InputTokens, int OutputTokens, TimeSpan Latency);

public sealed record GenerationResult(
    Achievement Achievement,
    int InputTokens,
    int OutputTokens,
    TimeSpan Latency,
    int Attempts,
    string? Error)
{
    public LlmErrorKind ErrorKind { get; init; } = LlmErrorKind.None;
}

public enum LlmErrorKind { None, OutOfCredit, BadKey, RateLimited, Http, Timeout, BadOutput, Other }

public sealed class LlmSettings
{
    public string ActiveProvider { get; set; } = "Anthropic";
    public string Language { get; set; } = "English";
    public bool AllowProfanity { get; set; }
    public int MaxTitleChars { get; set; } = 40;
    public int MaxTextChars { get; set; } = 120;
    public int MaxRewardChars { get; set; } = 70;
    public double Temperature { get; set; } = 1.0;
    public int RecentHistorySize { get; set; } = 10;
    public int EstimatedEventsPerDay { get; set; } = 20;
    public Dictionary<string, ProviderSettings> Providers { get; set; } = new();
}

public sealed class ProviderSettings
{
    /// <summary>"anthropic" (Messages API) or "openai" (OpenAI-compatible chat/completions).</summary>
    public string Kind { get; set; } = "openai";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";

    /// <summary>Anthropic only, with a Model like "claude-haiku-latest" (see ModelResolver): the model used when
    /// the newest one can't be looked up or rejects a request.</summary>
    public string FallbackModel { get; set; } = "";

    /// <summary>Anthropic only: output_config.effort ("low", "medium"...) for models with adaptive thinking.
    /// Never sent to FallbackModel, so keep that a model that takes no effort (e.g. claude-haiku-4-5).</summary>
    public string Effort { get; set; } = "";

    public string ApiKeyEnv { get; set; } = "";

    /// <summary>OpenAI-compatible reasoning models only: sends thinking={type:disabled} so the token budget goes to the answer.</summary>
    public bool DisableThinking { get; set; }

    /// <summary>USD per 1M tokens: compare-mode estimates, spend tracking (UsageTracker) and the setup script's monthly estimate.</summary>
    public double InputPricePerM { get; set; }
    public double OutputPricePerM { get; set; }

    /// <summary>Typical tokens per achievement, for the monthly estimate before any real usage exists.</summary>
    public int AvgInputTokens { get; set; } = 1200;
    public int AvgOutputTokens { get; set; } = 80;

    public double CostUsd(int inputTokens, int outputTokens) =>
        inputTokens * InputPricePerM / 1_000_000 + outputTokens * OutputPricePerM / 1_000_000;
}

public sealed class ReportingSettings
{
    /// <summary>ntfy topic URL the developer subscribes to. Empty -> no reports.</summary>
    public string NtfyUrl { get; set; } = "";
    /// <summary>The daily digest is also forwarded to this address by ntfy (its Email header). Empty -> push only.</summary>
    public string DigestEmail { get; set; } = "";
}
