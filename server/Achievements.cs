using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GarminAchievements;

/// <summary>Tier and sound are deterministic, so animations stay consistent regardless of the LLM.</summary>
public static class TierCalculator
{
    private static readonly HashSet<string> AlwaysFailure =
        new(StringComparer.OrdinalIgnoreCase) { "sedentary", "body_battery_low", "goal_missed", "no_achievement_today" };

    // Good-news events with no baseline: always a modest win.
    private static readonly HashSet<string> AlwaysRare =
        new(StringComparer.OrdinalIgnoreCase) { "steps_goal", "floors_goal" };

    public static (Tier Tier, double? Z) Compute(GameEvent e)
    {
        if (AlwaysRare.Contains(e.Type))
            return (Tier.Rare, null);

        if (AlwaysFailure.Contains(e.Type))
            return (Tier.Cursed, null);

        if (e.Value is not double v || e.BaselineMean is not double mean || e.BaselineStd is not double sd || sd <= 0)
            return (Tier.Common, null);

        var z = (v - mean) / sd * (e.HigherIsBetter ? 1 : -1);

        var tier = z switch
        {
            <= -1.0 => Tier.Cursed,
            >= 2.0 => Tier.Legendary,
            >= 1.3 => Tier.Epic,
            >= 0.6 => Tier.Rare,
            _ => Tier.Common
        };
        return (tier, z);
    }

    public static string SoundFor(Tier tier) => tier switch
    {
        Tier.Cursed => "fail",
        Tier.Legendary => "fanfare_long",
        Tier.Epic => "fanfare",
        _ => "chime"
    };
}

public sealed class RecentHistory(LlmSettings settings)
{
    private readonly Queue<AchievementText> _items = new();
    private readonly object _lock = new();

    public void Add(AchievementText item)
    {
        lock (_lock)
        {
            _items.Enqueue(item);
            while (_items.Count > settings.RecentHistorySize) _items.Dequeue();
        }
    }

    public IReadOnlyList<AchievementText> Snapshot()
    {
        lock (_lock) return _items.ToList();
    }
}

public static class PromptBuilder
{
    public static string System(LlmSettings s)
    {
        var profanity = s.AllowProfanity
            ? "Mild profanity is allowed when it lands a joke."
            : "No profanity.";

        return $$"""
            You are the System AI of an intergalactic dungeon crawl that is also a reality TV show.
            The "dungeon" is the player's ordinary daily life, tracked by their Garmin watch.
            You announce achievements with theatrical, dry, condescending sarcasm, like a bored game-show host
            who secretly enjoys the player's suffering.

            Rules:
            - Every achievement has a short punchy title (like a video-game achievement name), a 1-2 sentence
              commentary, and a fake reward line that is usually useless, backhanded or withheld.
            - Tier "cursed" means the player did worse than usual: mock them with a sarcastic "award" for failing.
            - Event "no_achievement_today" means the player achieved nothing at all today. Roast the empty day
              (use the steps count if given), never the person's body. Always tier "cursed".
            - Event "goal_missed" means the player ended the day under their step goal (details give steps and goal).
              Event "steps_goal" / "floors_goal" mean the player reached that daily goal: grudging, backhanded praise.
              Event "body_battery_low" means the Body Battery is nearly empty. Event "resting_hr" compares resting
              heart rate with the player's usual (lower is better): tier tells you whether to mock or applaud.
              Event "sedentary" means the watch's move bar is maxed out after long inactivity. Roast the
              inactivity, never the body, and give no medical advice.
            - Higher tiers are genuine wins: still sarcastic, but grudgingly impressed.
            - Use the actual numbers and context you are given. Specific beats generic.
            - Write original lines only. Never quote or reuse lines from any book, show or game.
            - Vary structure, openings and joke types. Do not repeat titles, phrases or jokes from the recent list.
            - Roast the effort, never the body: no comments on weight or appearance, and no medical advice.
            - {{profanity}}
            - Write in {{s.Language}}.
            - Hard limits: title <= {{s.MaxTitleChars}} chars, text <= {{s.MaxTextChars}} chars, reward <= {{s.MaxRewardChars}} chars.
              It must fit on a small watch screen, so aim well under: title ~{{s.MaxTitleChars / 8}} words,
              text ~{{s.MaxTextChars / 8}} words, reward ~{{s.MaxRewardChars / 8}} words. Brevity is part of the joke.

            Output ONLY a JSON object, no markdown, no extra text:
            {"title": "...", "text": "...", "reward": "..."}
            """;
    }

    public static string User(GameEvent e, Tier tier, double? z, IReadOnlyList<AchievementText> recent)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.AppendLine($"Event type: {e.Type}");
        sb.AppendLine($"Local time: {e.Timestamp:yyyy-MM-dd HH:mm (dddd)}");

        if (e.Value is double v)
            sb.AppendLine(string.Format(ci, "Value: {0:0.##} {1}", v, e.Unit));

        if (e.BaselineMean is double mean && e.BaselineStd is double sd)
        {
            sb.AppendLine(string.Format(ci, "Player's usual: {0:0.##} ± {1:0.##} {2} ({3} is better)",
                mean, sd, e.Unit, e.HigherIsBetter ? "higher" : "lower"));

            if (e.Value is double val && mean != 0)
            {
                var pct = (val - mean) / Math.Abs(mean) * 100;
                sb.AppendLine(string.Format(ci, "Difference from usual: {0:+0;-0}%", pct));
            }
        }

        if (z is double zz)
            sb.AppendLine(string.Format(ci, "Performance score (z, positive = better): {0:0.0}", zz));

        if (e.Details is { Count: > 0 })
        {
            sb.AppendLine("Details:");
            foreach (var (k, val) in e.Details) sb.AppendLine($"- {k}: {val}");
        }

        sb.AppendLine($"Tier: {tier.ToString().ToLowerInvariant()}");

        if (recent.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Recent achievements (do not repeat these):");
            foreach (var r in recent) sb.AppendLine($"- {r.Title}: {r.Text}");
        }

        return sb.ToString();
    }
}

public sealed class AchievementGenerator(LlmSettings settings, RecentHistory history)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static readonly AchievementText[] FallbackBank =
    [
        new("Offline and Unimpressed", "The commentary satellite is down. Your achievement happened anyway, sadly unwitnessed.", "Reward: silence."),
        new("Achievement Pending", "Something occurred. The System is too busy to care right now.", "Reward: a vague sense of progress."),
        new("Technical Difficulties", "The announcer is on a break. Please imagine something cutting.", "Reward: use your imagination.")
    ];

    public async Task<GenerationResult> GenerateAsync(GameEvent e, ILlmProvider provider, bool recordHistory, CancellationToken ct)
    {
        var (tier, z) = TierCalculator.Compute(e);
        var system = PromptBuilder.System(settings);
        var user = PromptBuilder.User(e, tier, z, recordHistory ? history.Snapshot() : []);

        int inTok = 0, outTok = 0, attempts = 0;
        var latency = TimeSpan.Zero;
        string? error = null;
        AchievementText? lastParsed = null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            attempts = attempt;
            try
            {
                var r = await provider.CompleteAsync(system, user, settings.Temperature, ct);
                inTok += r.InputTokens;
                outTok += r.OutputTokens;
                latency += r.Latency;

                if (TryParse(r.Content, out var text))
                {
                    lastParsed = text;
                    if (FitsLimits(text))
                        return Done(text, isFallback: false, err: null);
                    error = $"Response exceeded length limits: {Overruns(text)}.";
                    user += $"\n\nYour previous answer was too long ({Overruns(text)}). Rewrite it shorter. Return ONLY the JSON object.";
                }
                else
                {
                    error = "Response was not valid JSON.";
                    user += "\n\nYour previous answer was not valid JSON. Return ONLY the JSON object.";
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = ex.Message;
            }
        }

        // Parsed but too long: trim rather than lose a good joke.
        if (lastParsed is not null)
            return Done(Trim(lastParsed), isFallback: false, error);

        var fb = FallbackBank[Random.Shared.Next(FallbackBank.Length)];
        return Done(fb, isFallback: true, error);

        GenerationResult Done(AchievementText t, bool isFallback, string? err)
        {
            if (recordHistory && !isFallback) history.Add(t);

            var achievement = new Achievement(
                t.Title, t.Text, t.Reward,
                tier.ToString().ToLowerInvariant(),
                TierCalculator.SoundFor(tier),
                IsFailure: tier == Tier.Cursed,
                provider.Name, provider.Model, isFallback);

            return new GenerationResult(achievement, inTok, outTok, latency, attempts, err);
        }
    }

    private static bool TryParse(string content, out AchievementText result)
    {
        result = null!;
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<AchievementText>(content[start..(end + 1)], JsonOpts);
            if (parsed is null ||
                string.IsNullOrWhiteSpace(parsed.Title) ||
                string.IsNullOrWhiteSpace(parsed.Text) ||
                string.IsNullOrWhiteSpace(parsed.Reward))
                return false;

            result = parsed with
            {
                Title = parsed.Title.Trim(),
                Text = parsed.Text.Trim(),
                Reward = parsed.Reward.Trim()
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool FitsLimits(AchievementText t) =>
        t.Title.Length <= settings.MaxTitleChars &&
        t.Text.Length <= settings.MaxTextChars &&
        t.Reward.Length <= settings.MaxRewardChars;

    private string Overruns(AchievementText t) => string.Join(", ",
        new[] { ("title", t.Title, settings.MaxTitleChars), ("text", t.Text, settings.MaxTextChars), ("reward", t.Reward, settings.MaxRewardChars) }
            .Where(f => f.Item2.Length > f.Item3)
            .Select(f => $"{f.Item1} was {f.Item2.Length} chars, max {f.Item3}"));

    private AchievementText Trim(AchievementText t) => new(
        Cut(t.Title, settings.MaxTitleChars),
        Cut(t.Text, settings.MaxTextChars),
        Cut(t.Reward, settings.MaxRewardChars));

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
}
