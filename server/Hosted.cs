using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace GarminAchievements;

/// <summary>Hosted mode: one server, many watches, the developer's API key. Every watch identifies itself
/// with X-Device-Id, gets a free trial, then needs a one-time unlock (PayPal, or a coupon) for
/// <see cref="LicenseYears"/> of AI narration, capped at <see cref="AiPerDay"/> AI-written achievements a
/// day. Outside that, the watch uses its built-in lines. Off (the default) = the original one-server-per-user
/// bring-your-own-key mode.</summary>
public sealed class HostedSettings
{
    public bool Enabled { get; set; }
    public int TrialDays { get; set; } = 7;
    public int LicenseYears { get; set; } = 3;
    public int AiPerDay { get; set; } = 6;
    public double PriceUsd { get; set; } = 7.99;
    /// <summary>0 = no global cap. When this month's LLM spend passes it, every watch gets built-in lines.</summary>
    public double MonthlyBudgetUsd { get; set; }
    /// <summary>Public address of the unlock page, as shown on the watch (short: it has to fit the screen).</summary>
    public string UnlockUrl { get; set; } = "";
    /// <summary>Warn the trial user this many days before the trial ends.</summary>
    public int TrialWarnDays { get; set; } = 2;

    public static HostedSettings FromEnvironment(HostedSettings fromConfig, Func<string, string?> env)
    {
        var s = fromConfig;
        if (string.Equals(env("HOSTED"), "true", StringComparison.OrdinalIgnoreCase)) s.Enabled = true;
        if (env("HOSTED_UNLOCK_URL") is { Length: > 0 } url) s.UnlockUrl = url;
        if (double.TryParse(env("HOSTED_PRICE_USD"), NumberStyles.Float, CultureInfo.InvariantCulture, out var price) && price >= 0) s.PriceUsd = price;
        if (double.TryParse(env("HOSTED_MONTHLY_BUDGET_USD"), NumberStyles.Float, CultureInfo.InvariantCulture, out var budget) && budget >= 0) s.MonthlyBudgetUsd = budget;
        if (int.TryParse(env("HOSTED_TRIAL_DAYS"), out var trial) && trial >= 0) s.TrialDays = trial;
        if (int.TryParse(env("HOSTED_AI_PER_DAY"), out var perDay) && perDay >= 1) s.AiPerDay = perDay;
        if (int.TryParse(env("HOSTED_LICENSE_YEARS"), out var years) && years >= 1) s.LicenseYears = years;
        return s;
    }
}

public sealed record Device(
    string Id,
    string Code,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    DateTimeOffset? LicensedUntil,
    string? OrderId,
    string AiDay,
    int AiCount,
    int AiTotal,
    double SpendUsd);

public sealed record Coupon(string Code, int PercentOff, int MaxUses, int Uses, DateTimeOffset? ExpiresAt, string? Note)
{
    public bool IsUsable(DateTimeOffset now) => Uses < MaxUses && (ExpiresAt is null || ExpiresAt > now);
}

public sealed record Order(string OrderId, string DeviceId, string Code, double AmountUsd, string? Coupon, DateTimeOffset Created, DateTimeOffset? Captured);

public sealed record Stats(int Devices, int Trials, int Licensed, int Expired, int AiToday, double SpendMonthUsd, double SpendTotalUsd, int Orders, int Redeemed);

/// <summary>Where a device stands right now. Only <see cref="AiAllowed"/> decides whether the LLM is called;
/// the rest is for the notice and the logs.</summary>
public sealed record Entitlement(bool AiAllowed, string State, int? DaysLeft)
{
    public const string Trial = "trial", Licensed = "licensed", Expired = "expired", Capped = "capped", Budget = "budget";
}

/// <summary>What the watch learns about its own licence with every answer (shown on its Unlock screen).</summary>
public sealed record LicenseInfo(string State, string Code, string Url, int? DaysLeft, string? LicensedUntil);

public static class Licensing
{
    public static Entitlement Check(Device d, HostedSettings s, DateTimeOffset now, bool overBudget = false)
    {
        var trialEnd = d.FirstSeen.AddDays(s.TrialDays);
        var today = Day(now);
        var usedToday = d.AiDay == today ? d.AiCount : 0;

        if (d.LicensedUntil is { } until && until > now)
            return Gate(Entitlement.Licensed, (int)Math.Ceiling((until - now).TotalDays));
        if (trialEnd > now && d.LicensedUntil is null)
            return Gate(Entitlement.Trial, (int)Math.Ceiling((trialEnd - now).TotalDays));
        return new Entitlement(false, Entitlement.Expired, null);

        Entitlement Gate(string state, int daysLeft)
        {
            if (overBudget) return new Entitlement(false, Entitlement.Budget, daysLeft);
            if (usedToday >= s.AiPerDay) return new Entitlement(false, Entitlement.Capped, daysLeft);
            return new Entitlement(true, state, daysLeft);
        }
    }

    /// <summary>The once-a-day System message, or null when there is nothing to say. Kept short: the watch
    /// shows it on a round screen (MaxTextChars applies to achievements, this follows the same budget).</summary>
    public static CreditNotice? Notice(Device d, Entitlement e, HostedSettings s)
    {
        var where = $"{s.UnlockUrl}, code {d.Code}";
        return e.State switch
        {
            Entitlement.Expired when d.LicensedUntil is null => new CreditNotice("out", "Out of Mana",
                $"Free narration ended. Restore it at {where}.", "Reward: silence, until you pay."),
            Entitlement.Expired => new CreditNotice("out", "Out of Mana",
                $"Your {s.LicenseYears} years of mana are spent. Renew at {where}.", "Reward: silence, until you pay."),
            Entitlement.Trial or Entitlement.Capped when d.LicensedUntil is null && e.DaysLeft is { } left && left <= s.TrialWarnDays =>
                new CreditNotice("trial", "Trial Mana Fading",
                    $"{(left <= 1 ? "Last day" : left + " days")} of free narration. Unlock at {where}.", "Reward: a decision."),
            _ => null
        };
    }

    public static LicenseInfo Info(Device d, Entitlement e, HostedSettings s) =>
        new(e.State, d.Code, s.UnlockUrl, e.DaysLeft, d.LicensedUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    public static string Day(DateTimeOffset now) => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static double Price(HostedSettings s, Coupon? coupon) =>
        coupon is null ? Math.Round(s.PriceUsd, 2) : Math.Round(s.PriceUsd * (100 - coupon.PercentOff) / 100.0, 2);

    /// <summary>Codes the user reads off the watch and types on a phone: no I/O/0/1 look-alikes.</summary>
    public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string NewCode(int length = 6)
    {
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++) chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(chars);
    }

    /// <summary>Upper-cases and drops anything not in the alphabet (spaces, dashes, typos like 0/O).</summary>
    public static string NormalizeCode(string? raw) =>
        new((raw ?? "").ToUpperInvariant().Where(CodeAlphabet.Contains).ToArray());
}

/// <summary>All hosted-mode state in one SQLite file on the data volume: devices (trial/licence/daily AI count),
/// per-device joke history, coupons and PayPal orders. One open connection, serialized with a lock: a few
/// hundred watches making ~15 tiny requests a day each is nowhere near a contention problem.</summary>
public sealed class LicenseStore : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly object _lock = new();

    public LicenseStore() : this(DataDir.Path("licenses.db")) { }

    /// <summary>null path = in-memory (tests, local dev without DATA_DIR).</summary>
    public LicenseStore(string? path)
    {
        if (path is not null && System.IO.Path.GetDirectoryName(path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
        _db = new SqliteConnection(path is null ? "Data Source=:memory:" : $"Data Source={path}");
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("""
            CREATE TABLE IF NOT EXISTS devices(
                id TEXT PRIMARY KEY, code TEXT UNIQUE NOT NULL, first_seen TEXT NOT NULL, last_seen TEXT NOT NULL,
                licensed_until TEXT, order_id TEXT, ai_day TEXT NOT NULL DEFAULT '', ai_count INTEGER NOT NULL DEFAULT 0,
                ai_total INTEGER NOT NULL DEFAULT 0, spend_usd REAL NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS history(
                id INTEGER PRIMARY KEY AUTOINCREMENT, device_id TEXT NOT NULL, title TEXT NOT NULL, text TEXT NOT NULL, reward TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS history_device ON history(device_id, id);
            CREATE TABLE IF NOT EXISTS coupons(
                code TEXT PRIMARY KEY, percent_off INTEGER NOT NULL, max_uses INTEGER NOT NULL, uses INTEGER NOT NULL DEFAULT 0,
                expires_at TEXT, note TEXT);
            CREATE TABLE IF NOT EXISTS orders(
                order_id TEXT PRIMARY KEY, device_id TEXT NOT NULL, code TEXT NOT NULL, amount_usd REAL NOT NULL,
                coupon TEXT, created TEXT NOT NULL, captured TEXT);
            CREATE TABLE IF NOT EXISTS spend(month TEXT PRIMARY KEY, usd REAL NOT NULL DEFAULT 0, calls INTEGER NOT NULL DEFAULT 0);
            """);
    }

    // ---- devices -----------------------------------------------------------

    /// <summary>The device's row, created on first contact (that starts the trial). Updates last_seen.</summary>
    public Device Touch(string deviceId, DateTimeOffset now)
    {
        lock (_lock)
        {
            var existing = ReadDevice("id = $v", deviceId);
            if (existing is not null)
            {
                Exec("UPDATE devices SET last_seen = $now WHERE id = $id", ("$now", Iso(now)), ("$id", deviceId));
                return existing with { LastSeen = now };
            }
            for (var attempt = 0; ; attempt++)
            {
                var code = Licensing.NewCode();
                try
                {
                    Exec("INSERT INTO devices(id, code, first_seen, last_seen) VALUES($id, $code, $now, $now)",
                        ("$id", deviceId), ("$code", code), ("$now", Iso(now)));
                    return ReadDevice("id = $v", deviceId)!;
                }
                catch (SqliteException) when (attempt < 5) { /* code collision: try another */ }
            }
        }
    }

    public Device? ById(string deviceId) { lock (_lock) return ReadDevice("id = $v", deviceId); }
    public Device? ByCode(string code) { lock (_lock) return ReadDevice("code = $v", Licensing.NormalizeCode(code)); }

    /// <summary>One LLM call was made for this device: bump today's count (resetting on a new day) and the spend.</summary>
    public void RecordAi(string deviceId, DateTimeOffset now, double costUsd)
    {
        var day = Licensing.Day(now);
        var month = now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        lock (_lock)
        {
            Exec("""
                UPDATE devices SET ai_count = CASE WHEN ai_day = $day THEN ai_count + 1 ELSE 1 END, ai_day = $day,
                    ai_total = ai_total + 1, spend_usd = spend_usd + $cost WHERE id = $id
                """, ("$day", day), ("$cost", costUsd), ("$id", deviceId));
            Exec("INSERT INTO spend(month, usd, calls) VALUES($m, $cost, 1) ON CONFLICT(month) DO UPDATE SET usd = usd + $cost, calls = calls + 1",
                ("$m", month), ("$cost", costUsd));
        }
    }

    public double SpendThisMonth(DateTimeOffset now)
    {
        lock (_lock) return Scalar<double>("SELECT COALESCE(usd, 0) FROM spend WHERE month = $v", now.ToString("yyyy-MM", CultureInfo.InvariantCulture));
    }

    /// <summary>Grants (or extends: from the later of now and the current expiry) LicenseYears of narration.</summary>
    public Device License(string deviceId, string orderId, DateTimeOffset now, int years)
    {
        lock (_lock)
        {
            var d = ReadDevice("id = $v", deviceId) ?? throw new InvalidOperationException($"Unknown device {deviceId}.");
            var from = d.LicensedUntil is { } u && u > now ? u : now;
            var until = from.AddYears(years);
            Exec("UPDATE devices SET licensed_until = $until, order_id = $order WHERE id = $id",
                ("$until", Iso(until)), ("$order", orderId), ("$id", deviceId));
            return d with { LicensedUntil = until, OrderId = orderId };
        }
    }

    public void Revoke(string deviceId)
    {
        lock (_lock) Exec("UPDATE devices SET licensed_until = NULL, order_id = NULL WHERE id = $id", ("$id", deviceId));
    }

    /// <summary>A user got a new watch: the licence follows the order, so re-key it to the new device.</summary>
    public Device? Transfer(string orderId, string toDeviceId, DateTimeOffset now)
    {
        lock (_lock)
        {
            var from = ReadDevice("order_id = $v", orderId);
            if (from is null || from.LicensedUntil is null) return null;
            Exec("UPDATE devices SET licensed_until = NULL, order_id = NULL WHERE id = $id", ("$id", from.Id));
            if (ReadDevice("id = $v", toDeviceId) is null)
                Touch(toDeviceId, now);
            Exec("UPDATE devices SET licensed_until = $until, order_id = $order WHERE id = $id",
                ("$until", Iso(from.LicensedUntil.Value)), ("$order", orderId), ("$id", toDeviceId));
            return ReadDevice("id = $v", toDeviceId);
        }
    }

    /// <summary>GDPR delete: everything about one device.</summary>
    public void Forget(string deviceId)
    {
        lock (_lock)
        {
            Exec("DELETE FROM history WHERE device_id = $id", ("$id", deviceId));
            Exec("DELETE FROM orders WHERE device_id = $id", ("$id", deviceId));
            Exec("DELETE FROM devices WHERE id = $id", ("$id", deviceId));
        }
    }

    // ---- per-device joke history ---------------------------------------------

    public IReadOnlyList<AchievementText> History(string deviceId, int max)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT title, text, reward FROM (SELECT * FROM history WHERE device_id = $id ORDER BY id DESC LIMIT $n) ORDER BY id";
            cmd.Parameters.AddWithValue("$id", deviceId);
            cmd.Parameters.AddWithValue("$n", max);
            using var r = cmd.ExecuteReader();
            var list = new List<AchievementText>();
            while (r.Read()) list.Add(new AchievementText(r.GetString(0), r.GetString(1), r.GetString(2)));
            return list;
        }
    }

    public void AddHistory(string deviceId, AchievementText t, int keep)
    {
        lock (_lock)
        {
            Exec("INSERT INTO history(device_id, title, text, reward) VALUES($id, $t, $x, $r)",
                ("$id", deviceId), ("$t", t.Title), ("$x", t.Text), ("$r", t.Reward));
            Exec("DELETE FROM history WHERE device_id = $id AND id NOT IN (SELECT id FROM history WHERE device_id = $id ORDER BY id DESC LIMIT $n)",
                ("$id", deviceId), ("$n", keep));
        }
    }

    // ---- coupons ------------------------------------------------------------

    public Coupon UpsertCoupon(string code, int percentOff, int maxUses, DateTimeOffset? expiresAt, string? note)
    {
        if (percentOff is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(percentOff), "1-100");
        if (maxUses < 1) throw new ArgumentOutOfRangeException(nameof(maxUses), ">= 1");
        code = code.Trim().ToUpperInvariant();
        if (code.Length == 0) throw new ArgumentException("Empty coupon code.", nameof(code));
        lock (_lock)
        {
            Exec("""
                INSERT INTO coupons(code, percent_off, max_uses, expires_at, note) VALUES($c, $p, $m, $e, $n)
                ON CONFLICT(code) DO UPDATE SET percent_off = $p, max_uses = $m, expires_at = $e, note = $n
                """, ("$c", code), ("$p", percentOff), ("$m", maxUses), ("$e", expiresAt is { } x ? Iso(x) : DBNull.Value), ("$n", (object?)note ?? DBNull.Value));
            return GetCoupon(code)!;
        }
    }

    public Coupon? GetCoupon(string code)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT code, percent_off, max_uses, uses, expires_at, note FROM coupons WHERE code = $c";
            cmd.Parameters.AddWithValue("$c", code.Trim().ToUpperInvariant());
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadCoupon(r) : null;
        }
    }

    /// <summary>The coupon if it can still be used now, else null.</summary>
    public Coupon? UsableCoupon(string? code, DateTimeOffset now) =>
        string.IsNullOrWhiteSpace(code) ? null : GetCoupon(code) is { } c && c.IsUsable(now) ? c : null;

    public IReadOnlyList<Coupon> Coupons()
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT code, percent_off, max_uses, uses, expires_at, note FROM coupons ORDER BY code";
            using var r = cmd.ExecuteReader();
            var list = new List<Coupon>();
            while (r.Read()) list.Add(ReadCoupon(r));
            return list;
        }
    }

    public bool DeleteCoupon(string code)
    {
        lock (_lock) return Exec("DELETE FROM coupons WHERE code = $c", ("$c", code.Trim().ToUpperInvariant())) > 0;
    }

    // ---- orders (PayPal, or a 100% coupon "order") ---------------------------------

    public void CreateOrder(string orderId, string deviceId, string code, double amountUsd, string? coupon, DateTimeOffset now)
    {
        lock (_lock)
            Exec("INSERT OR IGNORE INTO orders(order_id, device_id, code, amount_usd, coupon, created) VALUES($o, $d, $c, $a, $cp, $t)",
                ("$o", orderId), ("$d", deviceId), ("$c", code), ("$a", amountUsd), ("$cp", (object?)coupon ?? DBNull.Value), ("$t", Iso(now)));
    }

    public Order? GetOrder(string orderId)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT order_id, device_id, code, amount_usd, coupon, created, captured FROM orders WHERE order_id = $o";
            cmd.Parameters.AddWithValue("$o", orderId);
            using var r = cmd.ExecuteReader();
            return r.Read()
                ? new Order(r.GetString(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.IsDBNull(4) ? null : r.GetString(4),
                    Parse(r.GetString(5)), r.IsDBNull(6) ? null : Parse(r.GetString(6)))
                : null;
        }
    }

    /// <summary>Payment confirmed (or a free coupon redeemed): license the device and count the coupon use.
    /// Idempotent: a second capture of the same order changes nothing and returns the device as it is.</summary>
    public Device Fulfil(string orderId, DateTimeOffset now, int years)
    {
        lock (_lock)
        {
            var o = GetOrder(orderId) ?? throw new InvalidOperationException($"Unknown order {orderId}.");
            if (o.Captured is not null) return ReadDevice("id = $v", o.DeviceId)!;
            var d = License(o.DeviceId, orderId, now, years);
            Exec("UPDATE orders SET captured = $t WHERE order_id = $o", ("$t", Iso(now)), ("$o", orderId));
            if (o.Coupon is not null) Exec("UPDATE coupons SET uses = uses + 1 WHERE code = $c", ("$c", o.Coupon));
            return d;
        }
    }

    // ---- reporting ----------------------------------------------------------

    public Stats Stats(DateTimeOffset now, HostedSettings s)
    {
        lock (_lock)
        {
            var nowIso = Iso(now);
            var trialStart = Iso(now.AddDays(-s.TrialDays));
            return new Stats(
                Devices: Scalar<int>("SELECT COUNT(*) FROM devices"),
                Trials: Scalar<int>("SELECT COUNT(*) FROM devices WHERE licensed_until IS NULL AND first_seen > $v", trialStart),
                Licensed: Scalar<int>("SELECT COUNT(*) FROM devices WHERE licensed_until > $v", nowIso),
                Expired: Scalar<int>("SELECT COUNT(*) FROM devices WHERE (licensed_until IS NULL AND first_seen <= $v) OR licensed_until <= $v2", trialStart, nowIso),
                AiToday: Scalar<int>("SELECT COALESCE(SUM(ai_count), 0) FROM devices WHERE ai_day = $v", Licensing.Day(now)),
                SpendMonthUsd: Math.Round(SpendThisMonthUnlocked(now), 4),
                SpendTotalUsd: Math.Round(Scalar<double>("SELECT COALESCE(SUM(usd), 0) FROM spend"), 4),
                Orders: Scalar<int>("SELECT COUNT(*) FROM orders WHERE captured IS NOT NULL AND amount_usd > 0"),
                Redeemed: Scalar<int>("SELECT COUNT(*) FROM orders WHERE captured IS NOT NULL AND amount_usd = 0"));
        }
    }

    /// <summary>Devices that cost the most, for spotting abuse.</summary>
    public IReadOnlyList<Device> TopSpenders(int n)
    {
        lock (_lock)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = $"SELECT {DeviceColumns} FROM devices ORDER BY spend_usd DESC LIMIT $n";
            cmd.Parameters.AddWithValue("$n", n);
            using var r = cmd.ExecuteReader();
            var list = new List<Device>();
            while (r.Read()) list.Add(ReadDevice(r));
            return list;
        }
    }

    // ---- plumbing -----------------------------------------------------------

    private const string DeviceColumns = "id, code, first_seen, last_seen, licensed_until, order_id, ai_day, ai_count, ai_total, spend_usd";

    private Device? ReadDevice(string where, string value)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT {DeviceColumns} FROM devices WHERE {where}";
        cmd.Parameters.AddWithValue("$v", value);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadDevice(r) : null;
    }

    private static Device ReadDevice(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), Parse(r.GetString(2)), Parse(r.GetString(3)),
        r.IsDBNull(4) ? null : Parse(r.GetString(4)), r.IsDBNull(5) ? null : r.GetString(5),
        r.GetString(6), r.GetInt32(7), r.GetInt32(8), r.GetDouble(9));

    private static Coupon ReadCoupon(SqliteDataReader r) => new(
        r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3),
        r.IsDBNull(4) ? null : Parse(r.GetString(4)), r.IsDBNull(5) ? null : r.GetString(5));

    private double SpendThisMonthUnlocked(DateTimeOffset now) =>
        Scalar<double>("SELECT COALESCE(usd, 0) FROM spend WHERE month = $v", now.ToString("yyyy-MM", CultureInfo.InvariantCulture));

    private int Exec(string sql, params (string Name, object Value)[] args)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return cmd.ExecuteNonQuery();
    }

    private T Scalar<T>(string sql, string? v = null, string? v2 = null)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        if (v is not null) cmd.Parameters.AddWithValue("$v", v);
        if (v2 is not null) cmd.Parameters.AddWithValue("$v2", v2);
        var result = cmd.ExecuteScalar();
        return result is null || result is DBNull ? default! : (T)Convert.ChangeType(result, typeof(T), CultureInfo.InvariantCulture);
    }

    private static string Iso(DateTimeOffset t) => t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearPool(_db); // the pool keeps the file handle open otherwise (matters on Windows / in tests)
    }
}
