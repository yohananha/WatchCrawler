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
    bool IsFallback);

public sealed record LlmResult(string Content, int InputTokens, int OutputTokens, TimeSpan Latency);

public sealed record GenerationResult(
    Achievement Achievement,
    int InputTokens,
    int OutputTokens,
    TimeSpan Latency,
    int Attempts,
    string? Error);

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
    public string ApiKeyEnv { get; set; } = "";

    /// <summary>OpenAI-compatible reasoning models only: sends thinking={type:disabled} so the token budget goes to the answer.</summary>
    public bool DisableThinking { get; set; }

    /// <summary>USD per 1M tokens, used only for the cost estimate in compare mode.</summary>
    public double InputPricePerM { get; set; }
    public double OutputPricePerM { get; set; }
}
