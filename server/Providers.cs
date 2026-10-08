using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GarminAchievements;

public interface ILlmProvider
{
    string Name { get; }
    string Model { get; }
    Task<LlmResult> CompleteAsync(string system, string user, double temperature, CancellationToken ct);
}

/// <summary>A non-2xx answer from the LLM API, kept structured so callers can tell "out of credit" from a bug.</summary>
public sealed class LlmHttpException(string provider, int status, string body)
    : HttpRequestException($"{provider}: HTTP {status} {body}")
{
    public int Status => status;
    public string Body => body;

    // Anthropic: 400 invalid_request_error "Your credit balance is too low...".
    // DeepSeek: 402 "Insufficient Balance".
    public bool IsOutOfCredit =>
        status == 402 ||
        (status == 400 && (body.Contains("credit balance", StringComparison.OrdinalIgnoreCase)
                           || body.Contains("insufficient balance", StringComparison.OrdinalIgnoreCase)));

    public bool IsBadKey => status is 401 or 403;
}

public static class LlmErrors
{
    public static LlmErrorKind Classify(Exception ex) => ex switch
    {
        LlmHttpException { IsOutOfCredit: true } => LlmErrorKind.OutOfCredit,
        LlmHttpException { IsBadKey: true } => LlmErrorKind.BadKey,
        LlmHttpException { Status: 429 } => LlmErrorKind.RateLimited,
        LlmHttpException => LlmErrorKind.Http,
        TaskCanceledException or TimeoutException => LlmErrorKind.Timeout,
        _ => LlmErrorKind.Other
    };
}

internal static class ApiKeys
{
    public static string Get(string envVar) =>
        Environment.GetEnvironmentVariable(envVar) is { Length: > 0 } key
            ? key
            : throw new InvalidOperationException($"Environment variable '{envVar}' is not set.");
}

/// <summary>Anthropic Messages API (Claude Haiku etc.).</summary>
public sealed class AnthropicProvider(string name, ProviderSettings settings, HttpClient http, ModelResolver? resolver = null) : ILlmProvider
{
    public string Name => name;
    public string Model => resolver?.Current(settings) ?? settings.Model;

    public async Task<LlmResult> CompleteAsync(string system, string user, double temperature, CancellationToken ct)
    {
        if (resolver is null || !ModelResolver.IsLatestAlias(settings.Model))
            return await SendAsync(settings.Model, settings.Effort, system, user, temperature, ct);

        var model = await resolver.ResolveAsync(settings, http, ct);
        try
        {
            return await SendAsync(model, model == settings.FallbackModel ? null : settings.Effort, system, user, temperature, ct);
        }
        // A newer model can refuse what this request sends (a newer generation may reject temperature, say):
        // answer with the known-good model instead of failing every achievement until someone notices.
        catch (LlmHttpException ex) when (ex.Status is 400 or 404 && !ex.IsOutOfCredit
                                          && settings.FallbackModel is { Length: > 0 } fallback && model != fallback)
        {
            resolver.Reject(settings, model, ex);
            return await SendAsync(settings.FallbackModel, null, system, user, temperature, ct);
        }
    }

    // effort is null for models that reject it (Haiku 4.5, the usual FallbackModel).
    private async Task<LlmResult> SendAsync(string model, string? effort, string system, string user, double temperature, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            // Room for adaptive thinking (Haiku 5.5 thinks by default; it counts toward max_tokens) plus the
            // short JSON answer. Only tokens actually used are billed.
            ["max_tokens"] = 2000,
            ["temperature"] = temperature,
            ["system"] = system,
            ["messages"] = new[] { new { role = "user", content = user } }
        };
        if (effort is { Length: > 0 })
            body["output_config"] = new { effort };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/v1/messages")
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Add("x-api-key", ApiKeys.Get(settings.ApiKeyEnv));
        req.Headers.Add("anthropic-version", "2023-06-01");

        var sw = Stopwatch.StartNew();
        using var res = await http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        sw.Stop();

        if (!res.IsSuccessStatusCode)
            throw new LlmHttpException(name, (int)res.StatusCode, json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var text = string.Concat(root.GetProperty("content").EnumerateArray()
            .Where(b => b.GetProperty("type").GetString() == "text")
            .Select(b => b.GetProperty("text").GetString()));

        int inTok = 0, outTok = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            inTok = usage.TryGetProperty("input_tokens", out var i) ? i.GetInt32() : 0;
            outTok = usage.TryGetProperty("output_tokens", out var o) ? o.GetInt32() : 0;
        }

        return new LlmResult(text, inTok, outTok, sw.Elapsed);
    }
}

/// <summary>Any OpenAI-compatible endpoint: DeepSeek, OpenRouter, etc.</summary>
public sealed class OpenAiCompatibleProvider(string name, ProviderSettings settings, HttpClient http) : ILlmProvider
{
    public string Name => name;
    public string Model => settings.Model;

    public async Task<LlmResult> CompleteAsync(string system, string user, double temperature, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = settings.Model,
            ["max_tokens"] = 400,
            ["temperature"] = temperature,
            ["messages"] = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };
        if (settings.DisableThinking)
            body["thinking"] = new { type = "disabled" };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKeys.Get(settings.ApiKeyEnv));

        var sw = Stopwatch.StartNew();
        using var res = await http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        sw.Stop();

        if (!res.IsSuccessStatusCode)
            throw new LlmHttpException(name, (int)res.StatusCode, json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var choice = root.GetProperty("choices")[0];
        var text = choice.GetProperty("message").GetProperty("content").GetString() ?? "";

        // Reasoning models can spend the whole budget thinking and return empty content.
        if (string.IsNullOrWhiteSpace(text))
        {
            var finish = choice.TryGetProperty("finish_reason", out var f) ? f.GetString() : null;
            throw new InvalidOperationException($"{name}: empty content (finish_reason={finish ?? "?"}).");
        }

        int inTok = 0, outTok = 0;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            inTok = usage.TryGetProperty("prompt_tokens", out var i) ? i.GetInt32() : 0;
            outTok = usage.TryGetProperty("completion_tokens", out var o) ? o.GetInt32() : 0;
        }

        return new LlmResult(text, inTok, outTok, sw.Elapsed);
    }
}

/// <summary>
/// Anthropic has no "latest Haiku" alias, so a configured Model like "claude-haiku-latest" is looked up here:
/// the newest "claude-haiku-*" model in GET /v1/models, re-checked daily. A lookup failure, or a model that
/// rejects our requests (see AnthropicProvider), means FallbackModel until the next check - with a warning
/// and an error report, so a newer generation that needs code changes doesn't go unnoticed.
/// </summary>
public sealed class ModelResolver(ILogger<ModelResolver> log, ErrorReporter? reporter = null, TimeProvider? time = null)
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(24);
    public static readonly TimeSpan RetryLookupAfter = TimeSpan.FromMinutes(10);
    private const string LatestSuffix = "-latest";

    private sealed record Entry(string Model, DateTimeOffset Expires);
    private readonly ConcurrentDictionary<string, Entry> _cache = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public static bool IsLatestAlias(string model) => model.EndsWith(LatestSuffix, StringComparison.Ordinal);

    /// <summary>The model currently in use for this alias (FallbackModel until the first lookup).</summary>
    public string Current(ProviderSettings p) =>
        !IsLatestAlias(p.Model) ? p.Model
        : _cache.TryGetValue(p.Model, out var e) ? e.Model
        : FallbackOrAlias(p);

    public async Task<string> ResolveAsync(ProviderSettings p, HttpClient http, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        _cache.TryGetValue(p.Model, out var hit);
        if (hit is not null && hit.Expires > now)
            return hit.Model;

        var family = p.Model[..^LatestSuffix.Length] + "-"; // "claude-haiku-latest" -> "claude-haiku-"
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{p.BaseUrl.TrimEnd('/')}/v1/models?limit=1000");
            req.Headers.Add("x-api-key", ApiKeys.Get(p.ApiKeyEnv));
            req.Headers.Add("anthropic-version", "2023-06-01");
            using var res = await http.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new LlmHttpException("models", (int)res.StatusCode, json);

            var newest = PickNewest(family, json)
                         ?? throw new InvalidOperationException($"no model starting with '{family}' in the models list");
            if (hit?.Model != newest)
                log.LogInformation("Model {Alias} -> {Model}", p.Model, newest);
            _cache[p.Model] = new Entry(newest, now + RefreshEvery);
            return newest;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var fallback = FallbackOrAlias(p);
            log.LogWarning("Model {Alias}: lookup failed ({Error}), using {Fallback}", p.Model, ex.Message, fallback);
            reporter?.Report("model lookup", $"{p.Model}: {ex.Message} - using {fallback}");
            _cache[p.Model] = new Entry(fallback, now + RetryLookupAfter);
            return fallback;
        }
    }

    /// <summary>The newest model refused a request: use FallbackModel until the next daily check.</summary>
    public void Reject(ProviderSettings p, string model, Exception ex)
    {
        log.LogWarning("Model {Model} rejected a request ({Error}), falling back to {Fallback}", model, ex.Message, p.FallbackModel);
        reporter?.Report("model rejected",
            $"{model} (newest for {p.Model}) refused a request, using {p.FallbackModel} - the provider code may need an update: {ex.Message}");
        _cache[p.Model] = new Entry(p.FallbackModel, _time.GetUtcNow() + RefreshEvery);
    }

    /// <summary>Newest id starting with <paramref name="family"/> by created_at; on a tie the shorter id (an alias over its dated snapshot).</summary>
    public static string? PickNewest(string family, string modelsJson)
    {
        using var doc = JsonDocument.Parse(modelsJson);
        return doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(m => (Id: m.GetProperty("id").GetString() ?? "",
                          Created: m.TryGetProperty("created_at", out var c) && c.TryGetDateTimeOffset(out var d) ? d : DateTimeOffset.MinValue))
            .Where(m => m.Id.StartsWith(family, StringComparison.Ordinal))
            .OrderByDescending(m => m.Created).ThenBy(m => m.Id.Length)
            .Select(m => m.Id)
            .FirstOrDefault();
    }

    private static string FallbackOrAlias(ProviderSettings p) => p.FallbackModel is { Length: > 0 } f ? f : p.Model;
}

public sealed class ProviderFactory(LlmSettings settings, IHttpClientFactory httpFactory, ModelResolver? resolver = null)
{
    public ILlmProvider Create(string name)
    {
        if (!settings.Providers.TryGetValue(name, out var p))
            throw new InvalidOperationException($"Provider '{name}' is not configured in appsettings.json.");

        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        return p.Kind.ToLowerInvariant() switch
        {
            "anthropic" => new AnthropicProvider(name, p, http, resolver),
            "openai" => new OpenAiCompatibleProvider(name, p, http),
            _ => throw new InvalidOperationException($"Unknown provider kind '{p.Kind}' for '{name}'.")
        };
    }

    /// <summary>Providers whose API key env var is set.</summary>
    public IReadOnlyList<string> ConfiguredWithKeys() =>
        settings.Providers
            .Where(kv => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(kv.Value.ApiKeyEnv)))
            .Select(kv => kv.Key)
            .ToList();
}
