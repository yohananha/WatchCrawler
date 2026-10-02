using System.Globalization;
using System.Text.Json;

namespace GarminAchievements;

/// <summary>Tracks what the LLM calls cost (tokens x the provider's prices) and how much API credit is left, so
/// the watch can warn before the credit runs out. Remaining credit comes from, in order:
/// the provider's own balance (DeepSeek has an endpoint; Anthropic keys can't read theirs),
/// else CREDIT_BALANCE_USD (what the user said they bought, set by setup) minus what was spent since.
/// An "out of credit" error from the API always wins. State is kept on the data volume.</summary>
public sealed class UsageTracker
{
    public const int LowDays = 14;

    public sealed class State
    {
        public string Month { get; set; } = "";
        public double MonthUsd { get; set; }
        public int MonthEvents { get; set; }
        public long MonthInputTokens { get; set; }
        public long MonthOutputTokens { get; set; }
        /// <summary>yyyy-MM-dd -> USD, last few weeks only.</summary>
        public Dictionary<string, double> Daily { get; set; } = new();
        /// <summary>The CREDIT_BALANCE_USD value SpentSinceBaseline counts from; a new value (top-up) resets it.</summary>
        public string? BaselineRaw { get; set; }
        public double SpentSinceBaseline { get; set; }
        public double? ProviderBalanceUsd { get; set; }
        public DateTimeOffset? ProviderBalanceAt { get; set; }
        public bool OutOfCredit { get; set; }
        /// <summary>Last credit state the owner/user were told about, so a state change is announced once.</summary>
        public string AnnouncedState { get; set; } = "ok";
    }

    public sealed record Report(
        string Provider,
        string Month,
        double MonthToDateUsd,
        int MonthEvents,
        double AvgDailyUsd,
        double ProjectedMonthUsd,
        double? RemainingUsd,
        double? DaysLeft,
        string State);

    private readonly LlmSettings _llm;
    private readonly TimeProvider _time;
    private readonly string? _path;
    private readonly double? _baseline;
    private readonly string? _baselineRaw;
    private readonly State _state;
    private readonly object _lock = new();

    public UsageTracker(LlmSettings llm)
        : this(llm, TimeProvider.System, Environment.GetEnvironmentVariable("CREDIT_BALANCE_USD"), DataDir.Path("usage.json")) { }

    public UsageTracker(LlmSettings llm, TimeProvider time, string? creditBalanceUsd, string? path)
    {
        _llm = llm;
        _time = time;
        _path = path;
        _baselineRaw = string.IsNullOrWhiteSpace(creditBalanceUsd) ? null : creditBalanceUsd.Trim();
        _baseline = double.TryParse(_baselineRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b >= 0 ? b : null;
        _state = DataDir.Load<State>(path);
        if (_state.BaselineRaw != _baselineRaw)
        {
            // New or changed credit amount (e.g. `setup --topup`): count spending from here.
            _state.BaselineRaw = _baselineRaw;
            _state.SpentSinceBaseline = 0;
            _state.OutOfCredit = false;
        }
    }

    private ProviderSettings? Active => _llm.Providers.GetValueOrDefault(_llm.ActiveProvider);

    public void Record(int inputTokens, int outputTokens)
    {
        var p = Active;
        if (p is null || (inputTokens == 0 && outputTokens == 0)) return;
        var cost = p.CostUsd(inputTokens, outputTokens);
        var now = _time.GetLocalNow();
        lock (_lock)
        {
            var month = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (_state.Month != month)
            {
                _state.Month = month;
                _state.MonthUsd = 0;
                _state.MonthEvents = 0;
                _state.MonthInputTokens = 0;
                _state.MonthOutputTokens = 0;
            }
            _state.MonthUsd += cost;
            _state.MonthEvents++;
            _state.MonthInputTokens += inputTokens;
            _state.MonthOutputTokens += outputTokens;
            _state.SpentSinceBaseline += cost;

            var day = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            _state.Daily[day] = _state.Daily.GetValueOrDefault(day) + cost;
            foreach (var old in _state.Daily.Keys.OrderByDescending(k => k).Skip(LowDays * 2).ToList())
                _state.Daily.Remove(old);
            Save();
        }
    }

    /// <summary>Hosted mode: the global "stop calling the API" switch is the provider saying the account is empty.</summary>
    public bool IsOutOfCredit { get { lock (_lock) return _state.OutOfCredit; } }

    /// <summary>The API said the account has no credit left.</summary>
    public void MarkOutOfCredit()
    {
        lock (_lock) { _state.OutOfCredit = true; Save(); }
    }

    /// <summary>A real (non-fallback) answer came back, so there is credit again.</summary>
    public void MarkCallSucceeded()
    {
        lock (_lock)
        {
            if (!_state.OutOfCredit) return;
            _state.OutOfCredit = false;
            Save();
        }
    }

    public void SetProviderBalance(double usd, bool isAvailable)
    {
        lock (_lock)
        {
            _state.ProviderBalanceUsd = usd;
            _state.ProviderBalanceAt = _time.GetUtcNow();
            if (!isAvailable) _state.OutOfCredit = true;
            Save();
        }
    }

    public bool ProviderBalanceStale =>
        _state.ProviderBalanceAt is not { } at || _time.GetUtcNow() - at > TimeSpan.FromHours(12);

    public Report Snapshot()
    {
        lock (_lock)
        {
            var avgDaily = AvgDailyUsd();
            double? remaining = _state.ProviderBalanceUsd is { } pb && !ProviderBalanceStale
                ? pb
                : _baseline is { } b ? Math.Max(0, b - _state.SpentSinceBaseline) : null;
            double? daysLeft = remaining is { } r && avgDaily > 0 ? r / avgDaily : null;

            var state = _state.OutOfCredit || remaining is <= 0 ? "out"
                : daysLeft is < LowDays ? "low"
                : "ok";

            return new Report(_llm.ActiveProvider, _state.Month, Math.Round(_state.MonthUsd, 4), _state.MonthEvents,
                Math.Round(avgDaily, 4), Math.Round(avgDaily * 30, 2),
                remaining is null ? null : Math.Round(remaining.Value, 2),
                daysLeft is null ? null : Math.Round(daysLeft.Value, 1), state);
        }
    }

    /// <summary>Average spend per day over the last two weeks of real use, or the configured estimate
    /// (EstimatedEventsPerDay x typical tokens) before there is any.</summary>
    private double AvgDailyUsd()
    {
        var today = _time.GetLocalNow().Date;
        var recent = _state.Daily
            .Select(kv => (Day: DateTime.ParseExact(kv.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture), Usd: kv.Value))
            .Where(d => (today - d.Day).TotalDays < LowDays)
            .ToList();
        if (recent.Count >= 3)
        {
            var span = (today - recent.Min(d => d.Day)).TotalDays + 1;
            return recent.Sum(d => d.Usd) / span;
        }
        return Estimate(Active, _llm.EstimatedEventsPerDay) / 30;
    }

    /// <summary>Monthly cost estimate for a provider at a number of events per day.</summary>
    public static double Estimate(ProviderSettings? p, int eventsPerDay) =>
        p is null ? 0 : p.CostUsd(p.AvgInputTokens, p.AvgOutputTokens) * eventsPerDay * 30;

    /// <summary>The message for the watch, or null when credit is fine.</summary>
    public CreditNotice? Notice()
    {
        var r = Snapshot();
        var topUp = Active?.Kind == "anthropic" ? "console.anthropic.com" : "platform.deepseek.com";
        return r.State switch
        {
            "out" => new CreditNotice("out", "Out of Mana",
                $"Your API credit is empty. Using backup lines until you top up at {topUp}.",
                "Reward: silence, until you pay."),
            "low" => new CreditNotice("low", "Mana Reserves Low",
                r.DaysLeft is { } d ? $"About {Math.Max(1, (int)d)} days of API credit left. Top up at {topUp}." : $"API credit is running low. Top up at {topUp}.",
                "Reward: a bill. Soon."),
            _ => null
        };
    }

    /// <summary>Returns the new state if it changed since the last call (so it is announced once), else null.</summary>
    public string? TakeStateChange()
    {
        var state = Snapshot().State;
        lock (_lock)
        {
            if (state == _state.AnnouncedState) return null;
            _state.AnnouncedState = state;
            Save();
            return state;
        }
    }

    private void Save() => DataDir.Save(_path, _state);

    /// <summary>Parses DeepSeek's GET /user/balance. Returns (USD, is_available); CNY balances are converted roughly.</summary>
    public static (double Usd, bool IsAvailable)? ParseDeepSeekBalance(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var available = !root.TryGetProperty("is_available", out var a) || a.GetBoolean();
            double usd = 0;
            if (root.TryGetProperty("balance_infos", out var infos))
            {
                foreach (var info in infos.EnumerateArray())
                {
                    var total = double.Parse(info.GetProperty("total_balance").GetString() ?? "0", CultureInfo.InvariantCulture);
                    usd += info.GetProperty("currency").GetString() == "CNY" ? total / 7.2 : total;
                }
            }
            return (usd, available);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
