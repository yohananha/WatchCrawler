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

    /// <summary>Hosted mode needs a public unlock address (it is printed on the watch) and somewhere to keep the
    /// licence database. PayPal credentials are optional (coupon-only distribution works without them).</summary>
    public static List<string> ValidateHosted(HostedSettings hosted, Func<string, string?> env)
    {
        var errors = new List<string>();
        if (!hosted.Enabled) return errors;
        if (string.IsNullOrWhiteSpace(hosted.UnlockUrl))
            errors.Add("HOSTED=true needs HOSTED_UNLOCK_URL (the unlock page address shown on the watch, e.g. watchcrawler.fly.dev/unlock).");
        if (string.IsNullOrWhiteSpace(env("DATA_DIR")))
            errors.Add("HOSTED=true needs DATA_DIR (a mounted volume): licences live in DATA_DIR/licenses.db.");
        if (string.IsNullOrWhiteSpace(env("ADMIN_KEY")))
            errors.Add("HOSTED=true needs ADMIN_KEY (a long random secret for the /admin endpoints and /usage).");
        var hasId = !string.IsNullOrWhiteSpace(env("PAYPAL_CLIENT_ID"));
        var hasSecret = !string.IsNullOrWhiteSpace(env("PAYPAL_CLIENT_SECRET"));
        if (hasId != hasSecret)
            errors.Add("Set both PAYPAL_CLIENT_ID and PAYPAL_CLIENT_SECRET, or neither.");
        var hasLsUrl = !string.IsNullOrWhiteSpace(env("LEMONSQUEEZY_CHECKOUT_URL"));
        var hasLsSecret = !string.IsNullOrWhiteSpace(env("LEMONSQUEEZY_WEBHOOK_SECRET"));
        if (hasLsUrl != hasLsSecret)
            errors.Add("Set both LEMONSQUEEZY_CHECKOUT_URL and LEMONSQUEEZY_WEBHOOK_SECRET, or neither.");
        if (hasId && hasLsUrl)
            errors.Add("Pick one payment provider: Lemon Squeezy or PayPal, not both.");
        return errors;
    }
}
