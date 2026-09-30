namespace GarminAchievements;

/// <summary>Env-var driven setup checks, so a misconfigured deploy fails loudly at start instead of quietly
/// serving fallback text (or, with no shared key, letting anyone spend the owner's API credit).</summary>
public static class StartupConfig
{
    /// <summary>LLM_PROVIDER=anthropic|deepseek|openrouter picks the provider (case-insensitive name from
    /// appsettings "Providers"). Llm__ActiveProvider also works, via normal ASP.NET config binding.</summary>
    public static void ApplyProvider(LlmSettings settings, Func<string, string?> env)
    {
        var wanted = env("LLM_PROVIDER");
        if (string.IsNullOrWhiteSpace(wanted)) return;
        settings.ActiveProvider = settings.Providers.Keys
            .FirstOrDefault(k => k.Equals(wanted.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"LLM_PROVIDER='{wanted}' is not one of: {string.Join(", ", settings.Providers.Keys)}.");
    }

    /// <summary>Problems that should stop the server from starting. Empty = fine.</summary>
    public static List<string> Validate(LlmSettings settings, Func<string, string?> env)
    {
        var errors = new List<string>();

        if (!settings.Providers.TryGetValue(settings.ActiveProvider, out var p))
            errors.Add($"Active provider '{settings.ActiveProvider}' is not configured. Options: {string.Join(", ", settings.Providers.Keys)}.");
        else if (string.IsNullOrWhiteSpace(env(p.ApiKeyEnv)))
            errors.Add($"{p.ApiKeyEnv} is not set, so {settings.ActiveProvider} can't be called. Set it as a secret (setup does this for you).");

        var allowNoKey = string.Equals(env("ALLOW_NO_WATCH_KEY"), "true", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(env("WATCH_SHARED_KEY")) && !allowNoKey)
            errors.Add("WATCH_SHARED_KEY is not set: anyone could use your API credit. Set it (setup generates one), " +
                       "or ALLOW_NO_WATCH_KEY=true for local testing only.");

        return errors;
    }
}
