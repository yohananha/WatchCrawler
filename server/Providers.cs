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

internal static class ApiKeys
{
    public static string Get(string envVar) =>
        Environment.GetEnvironmentVariable(envVar) is { Length: > 0 } key
            ? key
            : throw new InvalidOperationException($"Environment variable '{envVar}' is not set.");
}

/// <summary>Anthropic Messages API (Claude Haiku etc.).</summary>
public sealed class AnthropicProvider(string name, ProviderSettings settings, HttpClient http) : ILlmProvider
{
    public string Name => name;
    public string Model => settings.Model;

    public async Task<LlmResult> CompleteAsync(string system, string user, double temperature, CancellationToken ct)
    {
        var body = new
        {
            model = settings.Model,
            max_tokens = 400,
            temperature,
            system,
            messages = new[] { new { role = "user", content = user } }
        };

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
            throw new HttpRequestException($"{name}: HTTP {(int)res.StatusCode} {json}");

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
            throw new HttpRequestException($"{name}: HTTP {(int)res.StatusCode} {json}");

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

public sealed class ProviderFactory(LlmSettings settings, IHttpClientFactory httpFactory)
{
    public ILlmProvider Create(string name)
    {
        if (!settings.Providers.TryGetValue(name, out var p))
            throw new InvalidOperationException($"Provider '{name}' is not configured in appsettings.json.");

        var http = httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        return p.Kind.ToLowerInvariant() switch
        {
            "anthropic" => new AnthropicProvider(name, p, http),
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
