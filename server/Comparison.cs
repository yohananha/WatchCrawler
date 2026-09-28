using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GarminAchievements;

/// <summary>
/// Runs every provider that has an API key on the same events, shuffles the results
/// and writes a blind report (compare-report.md) plus the answer key (compare-key.json).
/// </summary>
public static class Comparison
{
    private sealed class Stats
    {
        public int Count, InTok, OutTok, Fallbacks, Errors;
        public TimeSpan Latency;
    }

    public static async Task RunAsync(IServiceProvider services, string eventsPath, bool blind)
    {
        var settings = services.GetRequiredService<LlmSettings>();
        var generator = services.GetRequiredService<AchievementGenerator>();
        var factory = services.GetRequiredService<ProviderFactory>();

        if (!File.Exists(eventsPath))
        {
            Console.WriteLine($"Events file not found: {eventsPath}");
            return;
        }

        var events = JsonSerializer.Deserialize<List<GameEvent>>(
            await File.ReadAllTextAsync(eventsPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];

        var providerNames = factory.ConfiguredWithKeys();
        if (providerNames.Count == 0)
        {
            Console.WriteLine("No provider has an API key set. Set at least one of the ApiKeyEnv variables from appsettings.json.");
            return;
        }

        var providers = providerNames.Select(factory.Create).ToList();
        var stats = providers.ToDictionary(p => p.Name, _ => new Stats());
        var key = new List<object>();
        var md = new StringBuilder();

        Console.WriteLine($"Comparing {string.Join(", ", providerNames)} on {events.Count} events...");

        md.AppendLine(blind ? "# Blind comparison" : "# Comparison");
        md.AppendLine();
        md.AppendLine(blind
            ? "Score each option 1-5 before opening compare-key.json."
            : "Score each option 1-5. Run with --blind to hide the models.");
        md.AppendLine();

        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            var results = await Task.WhenAll(providers.Select(p =>
                generator.GenerateAsync(e, p, recordHistory: false, CancellationToken.None)));

            var shuffled = blind ? results.OrderBy(_ => Random.Shared.Next()).ToList() : results.ToList();
            var mapping = new Dictionary<string, string>();
            var errors = new Dictionary<string, string>();

            md.AppendLine($"## {i + 1}. {Describe(e)}");
            md.AppendLine();
            md.AppendLine($"Tier: **{shuffled[0].Achievement.Tier}**");
            md.AppendLine();
            md.AppendLine(blind ? "| | Title | Text | Reward | Score |" : "| | Model | Title | Text | Reward | Score |");
            md.AppendLine(blind ? "|---|---|---|---|---|" : "|---|---|---|---|---|---|");

            for (var j = 0; j < shuffled.Count; j++)
            {
                var label = ((char)('A' + j)).ToString();
                var r = shuffled[j];
                var a = r.Achievement;
                mapping[label] = a.Provider;

                var flag = a.IsFallback ? " ⚠️ fallback" : "";
                var model = blind ? "" : $" {a.Provider} (`{a.Model}`) |";
                md.AppendLine($"| {label}{flag} |{model} {Cell(a.Title)} | {Cell(a.Text)} | {Cell(a.Reward)} | |");

                var s = stats[a.Provider];
                s.Count++;
                s.InTok += r.InputTokens;
                s.OutTok += r.OutputTokens;
                s.Latency += r.Latency;
                if (a.IsFallback) s.Fallbacks++;
                if (r.Error is not null)
                {
                    s.Errors++;
                    errors[label] = r.Error;
                }
            }

            md.AppendLine();
            // Errors live in the key (not the console/report) so they don't unblind the scoring.
            key.Add(new { eventIndex = i + 1, mapping, errors });
            Console.WriteLine($"  [{i + 1}/{events.Count}] {e.Type} done");
        }

        await File.WriteAllTextAsync("compare-report.md", md.ToString());
        await File.WriteAllTextAsync("compare-key.json",
            JsonSerializer.Serialize(key, new JsonSerializerOptions { WriteIndented = true }));

        PrintStats(settings, providers, stats);
        Console.WriteLine("\nWrote compare-report.md and compare-key.json");
    }

    private static void PrintStats(LlmSettings settings, List<ILlmProvider> providers, Dictionary<string, Stats> stats)
    {
        var ci = CultureInfo.InvariantCulture;
        Console.WriteLine();
        Console.WriteLine($"{"Provider",-12} {"Model",-24} {"Avg ms",8} {"Avg in",8} {"Avg out",8} {"Fallback",9} {"Errors",7} {"$/month",9}");

        foreach (var p in providers)
        {
            var s = stats[p.Name];
            if (s.Count == 0) continue;

            var avgIn = (double)s.InTok / s.Count;
            var avgOut = (double)s.OutTok / s.Count;
            var cfg = settings.Providers[p.Name];
            var perEvent = avgIn * cfg.InputPricePerM / 1_000_000 + avgOut * cfg.OutputPricePerM / 1_000_000;
            var monthly = perEvent * settings.EstimatedEventsPerDay * 30;

            Console.WriteLine(string.Format(ci,
                "{0,-12} {1,-24} {2,8:0} {3,8:0} {4,8:0} {5,9} {6,7} {7,9:0.00}",
                p.Name, Truncate(p.Model, 24), s.Latency.TotalMilliseconds / s.Count,
                avgIn, avgOut, s.Fallbacks, s.Errors, monthly));
        }
    }

    private static string Describe(GameEvent e)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(e.Type);
        if (e.Value is double v) sb.Append(string.Format(ci, " — {0:0.##} {1}", v, e.Unit));
        if (e.BaselineMean is double m) sb.Append(string.Format(ci, " (usual {0:0.##})", m));
        return sb.ToString();
    }

    private static string Cell(string s) => s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
