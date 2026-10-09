using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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

/// <summary>The last few announced achievements, fed back into the prompt so the LLM does not repeat
/// itself. Persisted as JSON on the Fly Volume (<c>HISTORY_STATE_PATH</c>): Fly wipes process memory on every
/// idle stop, which used to reset "do not repeat" many times a day. No path -> memory only (tests, local dev).</summary>
public sealed class RecentHistory
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly LlmSettings _settings;
    private readonly string? _path;
    private readonly Queue<AchievementText> _items = new();
    private readonly object _lock = new();

    public RecentHistory(LlmSettings settings) : this(settings, Environment.GetEnvironmentVariable("HISTORY_STATE_PATH")) { }

    public RecentHistory(LlmSettings settings, string? path)
    {
        _settings = settings;
        _path = string.IsNullOrWhiteSpace(path) ? null : path;
        Load();
    }

    public void Add(AchievementText item)
    {
        lock (_lock)
        {
            _items.Enqueue(item);
            while (_items.Count > _settings.RecentHistorySize) _items.Dequeue();
            Save();
        }
    }

    // Persistence must never break generating an achievement, so any I/O or parse problem is swallowed.
    private void Load()
    {
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<List<AchievementText>>(File.ReadAllText(_path), Json);
            if (saved is null) return;
            foreach (var item in saved.TakeLast(_settings.RecentHistorySize)) _items.Enqueue(item);
        }
        catch (Exception) { /* corrupt file: start empty */ }
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(_items.ToList(), Json));
        }
        catch (Exception) { /* read-only volume etc.: keep going in memory */ }
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
            - Event "activity_completed" can be ANY sport or workout (running, walking, cycling, swimming, strength,
              HIIT, yoga, "a training session of unknown kind"...): use the sport from the details, and only
              talk about pace or distance when they are given. For sessions measured in minutes, joke about time.
              Event "goal_missed" means the player ended the day under their step goal (details give steps and goal).
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
            - The watch's pixel font has plain ASCII only: no emoji, curly quotes, long dashes or ellipsis characters.
            - Hard limits: title <= {{s.MaxTitleChars}} chars, text <= {{s.MaxTextChars}} chars, reward <= {{s.MaxRewardChars}} chars.
              It must fit on a small watch screen, so aim well under: title ~{{s.MaxTitleChars / 8}} words,
              text ~{{s.MaxTextChars / 8}} words, reward ~{{s.MaxRewardChars / 8}} words. Brevity is part of the joke.

            - Each field is only its content, with no label: the watch already shows "Reward" as a heading,
              so write "A participation sticker.", never "Reward: A participation sticker.".

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

    /// <summary>Every character the watch's pixel fonts can draw (printable ASCII plus a few accented letters);
    /// anything else shows as a box. Fonts come from watch/tools/regen_fonts.py, and WatchGlyphTests checks
    /// this list against them.</summary>
    public const string WatchGlyphs =
        " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~"
        + "\u00C8\u00C9\u00CF\u00E8\u00E9\u00EF"; // È É Ï è é ï

    private static readonly HashSet<char> WatchGlyphSet = [.. WatchGlyphs];

    /// <summary>Longest a whole generation may take, retries and model fallback included. The watch's background
    /// job is killed after 30 s, phone relay included; an answer later than that is never seen and the event
    /// is shown with the watch's own text one run later. Past this, the canned/local text is returned instead.</summary>
    public TimeSpan Budget { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Text the watch can draw: typographic dashes, quotes and ellipses become their ASCII versions,
    /// and anything else outside <see cref="WatchGlyphs"/> (emoji, arrows...) is removed - an allowlist, so
    /// nothing from the model can turn into a box.</summary>
    public static string WatchSafe(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(c switch
            {
                '\u2012' or '\u2013' or '\u2014' or '\u2212' => "-",
                '\u2018' or '\u2019' => "'",
                '\u201C' or '\u201D' => "\"",
                '\u2026' => "...",
                '\t' or '\n' or '\r' => " ",
                _ => WatchGlyphSet.Contains(c) ? c.ToString() : ""
            });
        return Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim();
    }

    /// <summary>"Reward: a sticker." -> "A sticker.": the watch already shows each field's heading, and some
    /// models (Haiku 5.5) repeat it inside the value.</summary>
    public static string WithoutLabel(string value, string label)
    {
        var v = Regex.Replace(value.Trim(), $@"^{label}\s*[:\-\u2013\u2014]\s*", "", RegexOptions.IgnoreCase);
        return v.Length > 0 && v != value.Trim() ? char.ToUpperInvariant(v[0]) + v[1..] : v;
    }

    private static readonly AchievementText[] FallbackBank =
    [
        new("Offline and Unimpressed", "The commentary satellite is down. Your achievement happened anyway, sadly unwitnessed.", "Silence."),
        new("Achievement Pending", "Something occurred. The System is too busy to care right now.", "A vague sense of progress."),
        new("Technical Difficulties", "The announcer is on a break. Please imagine something cutting.", "Use your imagination.")
    ];

    /// <summary>Single-user mode: the shared <see cref="RecentHistory"/> is the "do not repeat" list.</summary>
    public Task<GenerationResult> GenerateAsync(GameEvent e, ILlmProvider provider, bool recordHistory, CancellationToken ct) =>
        GenerateAsync(e, provider, recordHistory ? history.Snapshot() : [], recordHistory ? history.Add : null, ct);

    /// <summary>Hosted mode passes each watch's own recent list and a callback that stores the new line for it.</summary>
    public async Task<GenerationResult> GenerateAsync(GameEvent e, ILlmProvider provider, IReadOnlyList<AchievementText> recent,
        Action<AchievementText>? record, CancellationToken ct)
    {
        var (tier, z) = TierCalculator.Compute(e);
        var system = PromptBuilder.System(settings);
        var user = PromptBuilder.User(e, tier, z, recent);

        int inTok = 0, outTok = 0, attempts = 0;
        var latency = TimeSpan.Zero;
        string? error = null;
        var errorKind = LlmErrorKind.None;
        AchievementText? lastParsed = null;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Budget);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            attempts = attempt;
            try
            {
                var r = await provider.CompleteAsync(system, user, settings.Temperature, deadline.Token);
                inTok += r.InputTokens;
                outTok += r.OutputTokens;
                latency += r.Latency;

                if (TryParse(r.Content, out var text))
                {
                    lastParsed = text;
                    if (FitsLimits(text))
                        return Done(text, isFallback: false, err: null);
                    error = $"Response exceeded length limits: {Overruns(text)}.";
                    errorKind = LlmErrorKind.BadOutput;
                    user += $"\n\nYour previous answer was too long ({Overruns(text)}). Rewrite it shorter. Return ONLY the JSON object.";
                }
                else
                {
                    error = "Response was not valid JSON.";
                    errorKind = LlmErrorKind.BadOutput;
                    user += "\n\nYour previous answer was not valid JSON. Return ONLY the JSON object.";
                }
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                error = $"No answer within {Budget.TotalSeconds:0} s, the watch would have given up.";
                errorKind = LlmErrorKind.Timeout;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                error = ex.Message;
                errorKind = LlmErrors.Classify(ex);
                // Retrying won't help until the user tops up / fixes the key.
                if (errorKind is LlmErrorKind.OutOfCredit or LlmErrorKind.BadKey) break;
            }
        }

        // Parsed but too long: trim rather than lose a good joke.
        if (lastParsed is not null)
            return Done(Trim(lastParsed), isFallback: false, error);

        var fb = FallbackBank[Random.Shared.Next(FallbackBank.Length)];
        return Done(fb, isFallback: true, error);

        GenerationResult Done(AchievementText t, bool isFallback, string? err)
        {
            if (!isFallback) record?.Invoke(t);

            var achievement = new Achievement(
                t.Title, t.Text, t.Reward,
                tier.ToString().ToLowerInvariant(),
                TierCalculator.SoundFor(tier),
                IsFailure: tier == Tier.Cursed,
                provider.Name, provider.Model, isFallback);

            return new GenerationResult(achievement, inTok, outTok, latency, attempts, err)
            {
                ErrorKind = err is null ? LlmErrorKind.None : errorKind
            };
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
                Title = WatchSafe(WithoutLabel(parsed.Title, "title")),
                Text = WatchSafe(WithoutLabel(parsed.Text, "text")),
                Reward = WatchSafe(WithoutLabel(parsed.Reward, "reward"))
            };
            if (result.Title.Length == 0 || result.Text.Length == 0 || result.Reward.Length == 0)
                return false;
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
