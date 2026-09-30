using System.Text.Json;
using System.Text.RegularExpressions;

namespace GarminAchievements;

/// <summary>Where the server keeps small state files (install id, usage, report counters). DATA_DIR, or
/// nothing -> memory only (tests, local dev). On Fly/Docker it is the mounted /data volume.</summary>
public static class DataDir
{
    public static string? Path(string file) =>
        Environment.GetEnvironmentVariable("DATA_DIR") is { Length: > 0 } dir ? System.IO.Path.Combine(dir, file) : null;

    public static T Load<T>(string? path) where T : new()
    {
        if (path is null || !File.Exists(path)) return new T();
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new T(); }
        catch (Exception) { return new T(); } // corrupt file: start fresh
    }

    public static void Save<T>(string? path, T value)
    {
        if (path is null) return;
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(value));
        }
        catch (Exception) { /* read-only volume etc.: keep going in memory */ }
    }
}

/// <summary>Anonymous id for this install, so reports from one user's server can be told apart
/// without knowing who they are. Created once and kept on the data volume.</summary>
public sealed class InstallInfo
{
    public string Id { get; }
    public bool IsNew { get; }
    public string Build { get; } = Environment.GetEnvironmentVariable("BUILD_STAMP") is { Length: > 0 } b ? b : "dev";

    public InstallInfo() : this(DataDir.Path("install-id")) { }

    public InstallInfo(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } saved)
            {
                Id = saved;
                return;
            }
        }
        catch (Exception) { /* fall through to a fresh id */ }

        Id = Guid.NewGuid().ToString("N")[..12];
        IsNew = true;
        try
        {
            if (path is not null)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Id);
            }
        }
        catch (Exception) { /* memory only */ }
    }
}

/// <summary>Sends failure reports from a user's server to the developer's ntfy topic, so problems at a user can
/// be seen and fixed. Never sends API keys, the shared key or activity data: every message is scrubbed first.
/// Each error kind goes out at most once an hour; everything is also counted into a daily digest that ntfy
/// forwards by email. Opt out with REPORT_ERRORS=false. State is persisted because Fly stops idle machines.</summary>
public sealed class ErrorReporter
{
    public static readonly TimeSpan RateLimit = TimeSpan.FromHours(1);
    public static readonly TimeSpan DigestEvery = TimeSpan.FromHours(24);

    public sealed class State
    {
        public Dictionary<string, DateTimeOffset> LastSent { get; set; } = new();
        public Dictionary<string, int> Counts { get; set; } = new();
        public DateTimeOffset LastDigest { get; set; }
    }

    private readonly ReportingSettings _settings;
    private readonly LlmSettings _llm;
    private readonly InstallInfo _install;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly ILogger? _log;
    private readonly string? _statePath;
    private readonly State _state;
    private readonly object _lock = new();
    private readonly string[] _secrets;

    public bool Enabled { get; }

    public ErrorReporter(ReportingSettings settings, LlmSettings llm, InstallInfo install, IHttpClientFactory http, ILogger<ErrorReporter> log)
        : this(settings, llm, install, http.CreateClient(), TimeProvider.System, Environment.GetEnvironmentVariable,
               DataDir.Path("reporter.json"), log) { }

    public ErrorReporter(ReportingSettings settings, LlmSettings llm, InstallInfo install, HttpClient http,
        TimeProvider time, Func<string, string?> env, string? statePath, ILogger? log = null)
    {
        _settings = settings;
        _llm = llm;
        _install = install;
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(10);
        _time = time;
        _log = log;
        _statePath = statePath;
        _state = DataDir.Load<State>(statePath);
        Enabled = !string.IsNullOrWhiteSpace(settings.NtfyUrl)
                  && !string.Equals(env("REPORT_ERRORS"), "false", StringComparison.OrdinalIgnoreCase);

        // Exact values to strip, on top of the pattern-based scrubbing.
        _secrets = new[] { "WATCH_SHARED_KEY" }
            .Concat(llm.Providers.Values.Select(p => p.ApiKeyEnv))
            .Select(env)
            .Where(v => !string.IsNullOrEmpty(v) && v.Length >= 6)
            .Cast<string>()
            .ToArray();
    }

    /// <summary>Fire-and-forget: never blocks or fails the request that hit the problem.</summary>
    public void Report(string kind, string message, int priority = 3, string tags = "warning") =>
        _ = ReportAsync(kind, message, priority, tags);

    public async Task<bool> ReportAsync(string kind, string message, int priority = 3, string tags = "warning")
    {
        if (!Enabled) return false;
        var now = _time.GetUtcNow();
        bool send;
        lock (_lock)
        {
            _state.Counts[kind] = _state.Counts.GetValueOrDefault(kind) + 1;
            send = !_state.LastSent.TryGetValue(kind, out var last) || now - last >= RateLimit;
            if (send) _state.LastSent[kind] = now;
            DataDir.Save(_statePath, _state);
        }

        var sent = send && await PostAsync($"WatchCrawler: {kind}", Body(kind, message), priority, tags, email: null);
        await MaybeSendDigestAsync();
        return sent;
    }

    /// <summary>Sends the daily digest if one is due. Called opportunistically (on reports and requests)
    /// rather than from a timer, since Fly machines sleep most of the day.</summary>
    public async Task MaybeSendDigestAsync()
    {
        if (!Enabled) return;
        var now = _time.GetUtcNow();
        Dictionary<string, int> counts;
        lock (_lock)
        {
            if (_state.Counts.Count == 0) return;
            if (_state.LastDigest == default) { _state.LastDigest = now; DataDir.Save(_statePath, _state); return; }
            if (now - _state.LastDigest < DigestEvery) return;
            counts = new Dictionary<string, int>(_state.Counts);
            _state.Counts.Clear();
            _state.LastDigest = now;
            DataDir.Save(_statePath, _state);
        }

        var lines = string.Join("\n", counts.OrderByDescending(c => c.Value).Select(c => $"- {c.Key}: {c.Value}x"));
        await PostAsync("WatchCrawler daily digest", $"{Header()}\nLast 24h:\n{lines}", 2, "calendar",
            email: string.IsNullOrWhiteSpace(_settings.DigestEmail) ? null : _settings.DigestEmail);
    }

    public string Scrub(string text)
    {
        foreach (var s in _secrets) text = text.Replace(s, "[secret]");
        return Scrubber.Clean(text);
    }

    private string Header() =>
        $"install {_install.Id} · build {_install.Build} · {_llm.ActiveProvider}/{ActiveModel()}";

    private string ActiveModel() =>
        _llm.Providers.TryGetValue(_llm.ActiveProvider, out var p) ? p.Model : "?";

    private string Body(string kind, string message)
    {
        var msg = Scrub(message);
        if (msg.Length > 600) msg = msg[..600] + "…";
        return $"{Header()}\n{kind}: {msg}";
    }

    private async Task<bool> PostAsync(string title, string body, int priority, string tags, string? email)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _settings.NtfyUrl) { Content = new StringContent(body) };
            req.Headers.Add("Title", title);
            req.Headers.Add("Priority", priority.ToString());
            req.Headers.Add("Tags", tags);
            if (email is not null) req.Headers.Add("Email", email);
            using var res = await _http.SendAsync(req);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _log?.LogWarning("Error report not sent: {Error}", ex.Message);
            return false;
        }
    }

    /// <summary>For config errors found before the app (and DI) exists: one plain POST, then the caller exits.</summary>
    public static async Task ReportStartupFailureAsync(ReportingSettings settings, string message)
    {
        if (string.IsNullOrWhiteSpace(settings.NtfyUrl)
            || string.Equals(Environment.GetEnvironmentVariable("REPORT_ERRORS"), "false", StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var req = new HttpRequestMessage(HttpMethod.Post, settings.NtfyUrl)
            {
                Content = new StringContent(Scrubber.Clean(message))
            };
            req.Headers.Add("Title", "WatchCrawler: server failed to start");
            req.Headers.Add("Priority", "4");
            req.Headers.Add("Tags", "rotating_light");
            await http.SendAsync(req);
        }
        catch (Exception) { /* nothing more we can do */ }
    }
}

public static partial class Scrubber
{
    // API keys (sk-ant-..., sk-...), bearer tokens, long opaque tokens, and URL query strings.
    [GeneratedRegex(@"sk-[A-Za-z0-9_\-]{6,}")] private static partial Regex SkKey();
    [GeneratedRegex(@"(?i)bearer\s+\S+")] private static partial Regex Bearer();
    [GeneratedRegex(@"\b[A-Za-z0-9_\-]{32,}\b")] private static partial Regex LongToken();
    [GeneratedRegex(@"\?[^\s""']+")] private static partial Regex Query();

    public static string Clean(string text)
    {
        text = SkKey().Replace(text, "[key]");
        text = Bearer().Replace(text, "Bearer [key]");
        text = LongToken().Replace(text, "[token]");
        text = Query().Replace(text, "?[query]");
        return text;
    }
}
