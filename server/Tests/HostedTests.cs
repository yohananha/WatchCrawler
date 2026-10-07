using System.Net;
using System.Text.Json;

namespace GarminAchievements.Tests;

public class LicensingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static HostedSettings Settings() => new() { Enabled = true, TrialDays = 7, LicenseYears = 3, AiPerDay = 6, UnlockUrl = "wc.test/unlock" };

    private static Device Device(DateTimeOffset firstSeen, DateTimeOffset? licensedUntil = null, string aiDay = "", int aiCount = 0) =>
        new("dev1", "ABCDEF", firstSeen, firstSeen, licensedUntil, null, aiDay, aiCount, 0, 0);

    [Fact]
    public void NewDevice_IsOnTrial_WithDaysLeft()
    {
        var e = Licensing.Check(Device(T0), Settings(), T0.AddDays(2));
        Assert.True(e.AiAllowed);
        Assert.Equal(Entitlement.Trial, e.State);
        Assert.Equal(5, e.DaysLeft);
    }

    [Fact]
    public void TrialOver_NoAi_AndOutOfManaNotice()
    {
        var d = Device(T0);
        var e = Licensing.Check(d, Settings(), T0.AddDays(8));
        Assert.False(e.AiAllowed);
        Assert.Equal(Entitlement.Expired, e.State);
        var n = Licensing.Notice(d, e, Settings());
        Assert.NotNull(n);
        Assert.Equal("out", n!.State);
        Assert.Contains("ABCDEF", n.Text);
        Assert.Contains("wc.test/unlock", n.Text);
        Assert.True(n.Text.Length <= 120, n.Text);
    }

    [Fact]
    public void Licensed_HasAi_UntilExpiry()
    {
        var d = Device(T0, licensedUntil: T0.AddYears(3));
        Assert.True(Licensing.Check(d, Settings(), T0.AddYears(2)).AiAllowed);
        var e = Licensing.Check(d, Settings(), T0.AddYears(3).AddDays(1));
        Assert.False(e.AiAllowed);
        Assert.Contains("3 years", Licensing.Notice(d, e, Settings())!.Text);
    }

    [Fact]
    public void DailyCap_StopsAi_ButIsNotANotice()
    {
        var d = Device(T0, aiDay: "2026-10-02", aiCount: 6);
        var e = Licensing.Check(d, Settings(), new DateTimeOffset(2026, 10, 2, 20, 0, 0, TimeSpan.Zero));
        Assert.False(e.AiAllowed);
        Assert.Equal(Entitlement.Capped, e.State);
        Assert.Null(Licensing.Notice(d, e, Settings()));
        // A new day resets it.
        Assert.True(Licensing.Check(d, Settings(), new DateTimeOffset(2026, 10, 3, 1, 0, 0, TimeSpan.Zero)).AiAllowed);
    }

    [Fact]
    public void TrialEndingSoon_Warns()
    {
        var d = Device(T0);
        var e = Licensing.Check(d, Settings(), T0.AddDays(5.5));
        Assert.True(e.AiAllowed);
        var n = Licensing.Notice(d, e, Settings());
        Assert.NotNull(n);
        Assert.Equal("trial", n!.State);
        Assert.Null(Licensing.Notice(d, Licensing.Check(d, Settings(), T0.AddDays(1)), Settings()));
    }

    [Fact]
    public void OverBudget_StopsAi_ForEveryone()
    {
        var e = Licensing.Check(Device(T0, licensedUntil: T0.AddYears(3)), Settings(), T0.AddDays(1), overBudget: true);
        Assert.False(e.AiAllowed);
        Assert.Equal(Entitlement.Budget, e.State);
    }

    [Fact]
    public void Codes_AreReadable_AndNormalized()
    {
        var code = Licensing.NewCode();
        Assert.Equal(6, code.Length);
        Assert.All(code, c => Assert.Contains(c, Licensing.CodeAlphabet));
        Assert.Equal("K7P3QX", Licensing.NormalizeCode(" k7p-3qx "));
    }

    [Fact]
    public void Price_AppliesCouponPercent()
    {
        var s = Settings();
        s.PriceUsd = 7.99;
        Assert.Equal(7.99, Licensing.Price(s, null));
        Assert.Equal(4.0, Licensing.Price(s, new Coupon("HALF", 50, 10, 0, null, null)));
        Assert.Equal(0, Licensing.Price(s, new Coupon("FREE", 100, 1, 0, null, null)));
    }
}

public class LicenseStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly LicenseStore _store = new(path: null);
    private readonly HostedSettings _s = new() { Enabled = true, TrialDays = 7, LicenseYears = 3, AiPerDay = 6, PriceUsd = 7.99, UnlockUrl = "u" };

    public void Dispose() => _store.Dispose();

    [Fact]
    public void Touch_CreatesOnce_AndKeepsTheCode()
    {
        var a = _store.Touch("watch-1", T0);
        var b = _store.Touch("watch-1", T0.AddDays(1));
        Assert.Equal(a.Code, b.Code);
        Assert.Equal(T0, b.FirstSeen);
        Assert.Equal(T0.AddDays(1), b.LastSeen);
        Assert.Equal(a.Id, _store.ByCode(a.Code.ToLowerInvariant())!.Id);
        Assert.NotEqual(a.Code, _store.Touch("watch-2", T0).Code);
    }

    [Fact]
    public void RecordAi_CountsPerDay_AndSpendPerMonth()
    {
        var d = _store.Touch("w", T0);
        for (var i = 0; i < 3; i++) _store.RecordAi(d.Id, T0, 0.001);
        var after = _store.ById(d.Id)!;
        Assert.Equal(3, after.AiCount);
        Assert.Equal(3, after.AiTotal);
        Assert.Equal(0.003, after.SpendUsd, 6);
        _store.RecordAi(d.Id, T0.AddDays(1), 0.001);
        Assert.Equal(1, _store.ById(d.Id)!.AiCount);
        Assert.Equal(0.004, _store.SpendThisMonth(T0), 6);
        Assert.Equal(0, _store.SpendThisMonth(T0.AddMonths(1)));
    }

    [Fact]
    public void History_IsPerDevice_AndCapped()
    {
        _store.Touch("a", T0);
        _store.Touch("b", T0);
        for (var i = 0; i < 12; i++) _store.AddHistory("a", new AchievementText($"t{i}", "x", "r"), keep: 10);
        _store.AddHistory("b", new AchievementText("other", "x", "r"), keep: 10);
        var a = _store.History("a", 10);
        Assert.Equal(10, a.Count);
        Assert.Equal("t2", a[0].Title);
        Assert.Equal("t11", a[^1].Title);
        Assert.Single(_store.History("b", 10));
    }

    [Fact]
    public void Order_LicensesOnce_AndIsIdempotent()
    {
        var d = _store.Touch("w", T0);
        _store.CreateOrder("PP1", d.Id, d.Code, 7.99, null, T0);
        var first = _store.Fulfil("PP1", T0, 3);
        Assert.Equal(T0.AddYears(3), first.LicensedUntil);
        var again = _store.Fulfil("PP1", T0.AddDays(1), 3);
        Assert.Equal(T0.AddYears(3), again.LicensedUntil);
        Assert.NotNull(_store.GetOrder("PP1")!.Captured);
        Assert.True(Licensing.Check(again, _s, T0.AddYears(2)).AiAllowed);
    }

    [Fact]
    public void SecondPurchase_ExtendsFromCurrentExpiry()
    {
        var d = _store.Touch("w", T0);
        _store.CreateOrder("PP1", d.Id, d.Code, 7.99, null, T0);
        _store.Fulfil("PP1", T0, 3);
        _store.CreateOrder("PP2", d.Id, d.Code, 7.99, null, T0.AddYears(1));
        Assert.Equal(T0.AddYears(6), _store.Fulfil("PP2", T0.AddYears(1), 3).LicensedUntil);
    }

    [Fact]
    public void Coupons_CountUses_OnlyWhenFulfilled_AndRespectLimits()
    {
        _store.UpsertCoupon("friend", 100, maxUses: 1, expiresAt: null, note: "Dana");
        Assert.NotNull(_store.UsableCoupon("FRIEND", T0));
        var d = _store.Touch("w", T0);
        _store.CreateOrder("c1", d.Id, d.Code, 0, "FRIEND", T0);
        Assert.Equal(0, _store.GetCoupon("FRIEND")!.Uses); // pending order: not used yet
        _store.Fulfil("c1", T0, 3);
        Assert.Equal(1, _store.GetCoupon("FRIEND")!.Uses);
        Assert.Null(_store.UsableCoupon("FRIEND", T0)); // used up
        _store.Fulfil("c1", T0, 3); // replay
        Assert.Equal(1, _store.GetCoupon("FRIEND")!.Uses);

        _store.UpsertCoupon("OLD", 50, 100, T0.AddDays(-1), null);
        Assert.Null(_store.UsableCoupon("OLD", T0));
        Assert.Null(_store.UsableCoupon("NOPE", T0));
        Assert.Null(_store.UsableCoupon("", T0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _store.UpsertCoupon("BAD", 0, 1, null, null));
        Assert.True(_store.DeleteCoupon("old"));
        Assert.Single(_store.Coupons());
    }

    [Fact]
    public void Revoke_Transfer_Forget()
    {
        var d = _store.Touch("old-watch", T0);
        _store.CreateOrder("PP1", d.Id, d.Code, 7.99, null, T0);
        _store.Fulfil("PP1", T0, 3);

        var moved = _store.Transfer("PP1", "new-watch", T0.AddDays(10));
        Assert.NotNull(moved);
        Assert.Equal(T0.AddYears(3), moved!.LicensedUntil);
        Assert.Null(_store.ById("old-watch")!.LicensedUntil);
        Assert.Null(_store.Transfer("nope", "x", T0));

        _store.Revoke("new-watch");
        Assert.Null(_store.ById("new-watch")!.LicensedUntil);

        _store.Forget("old-watch");
        Assert.Null(_store.ById("old-watch"));
    }

    [Fact]
    public void RefundOrder_FirstUnlock_EndsTheLicence_Once()
    {
        var d = _store.Touch("w", T0);
        _store.CreateOrder("ls:1", d.Id, d.Code, 7.99, null, T0);
        _store.Fulfil("ls:1", T0, 3);

        var after = _store.RefundOrder("ls:1", T0.AddDays(3), 3);
        Assert.Null(after!.LicensedUntil);
        Assert.Null(_store.ById("w")!.LicensedUntil);
        Assert.Null(_store.RefundOrder("ls:1", T0.AddDays(4), 3));   // a repeated webhook changes nothing
        Assert.Null(_store.RefundOrder("nope", T0, 3));
    }

    [Fact]
    public void RefundOrder_Extension_TakesBackOnlyItsYears()
    {
        var d = _store.Touch("w", T0);
        _store.CreateOrder("ls:1", d.Id, d.Code, 7.99, null, T0);
        _store.Fulfil("ls:1", T0, 3);
        _store.CreateOrder("ls:2", d.Id, d.Code, 7.99, null, T0.AddDays(5));
        _store.Fulfil("ls:2", T0.AddDays(5), 3);
        Assert.Equal(T0.AddYears(6), _store.ById("w")!.LicensedUntil);

        var after = _store.RefundOrder("ls:2", T0.AddDays(6), 3);
        Assert.Equal(T0.AddYears(3), after!.LicensedUntil);
        Assert.Equal("ls:1", _store.ById("w")!.OrderId);   // the licence rests on the order that's left
    }

    [Fact]
    public void RefundOrder_AfterTransfer_HitsTheNewWatch()
    {
        var d = _store.Touch("old-watch", T0);
        _store.CreateOrder("ls:1", d.Id, d.Code, 7.99, null, T0);
        _store.Fulfil("ls:1", T0, 3);
        _store.Transfer("ls:1", "new-watch", T0.AddDays(1));

        var after = _store.RefundOrder("ls:1", T0.AddDays(2), 3);
        Assert.Equal("new-watch", after!.Id);
        Assert.Null(_store.ById("new-watch")!.LicensedUntil);
    }

    [Fact]
    public void OldDatabase_GetsTheRefundedColumn()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wc-{Guid.NewGuid():N}.db");
        try
        {
            using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "CREATE TABLE orders(order_id TEXT PRIMARY KEY, device_id TEXT NOT NULL, code TEXT NOT NULL, amount_usd REAL NOT NULL, coupon TEXT, created TEXT NOT NULL, captured TEXT)";
                cmd.ExecuteNonQuery();
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            using var store = new LicenseStore(path);
            var d = store.Touch("w", T0);
            store.CreateOrder("ls:1", d.Id, d.Code, 7.99, null, T0);
            store.Fulfil("ls:1", T0, 3);
            Assert.NotNull(store.RefundOrder("ls:1", T0.AddDays(1), 3));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Stats_CountStates()
    {
        _store.Touch("trial", T0);
        var lic = _store.Touch("paid", T0.AddDays(-30));
        _store.CreateOrder("PP1", lic.Id, lic.Code, 7.99, null, T0);
        _store.Fulfil("PP1", T0, 3);
        _store.Touch("expired", T0.AddDays(-30));
        _store.RecordAi("paid", T0, 0.002);
        var st = _store.Stats(T0, _s);
        Assert.Equal(3, st.Devices);
        Assert.Equal(1, st.Trials);
        Assert.Equal(1, st.Licensed);
        Assert.Equal(1, st.Expired);
        Assert.Equal(1, st.AiToday);
        Assert.Equal(1, st.Orders);
        Assert.Equal(0.002, st.SpendMonthUsd, 6);
        Assert.Equal("paid", _store.TopSpenders(1)[0].Id);
    }

    [Fact]
    public void Store_SurvivesARestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wc-test-{Guid.NewGuid():N}.db");
        try
        {
            string code;
            using (var s1 = new LicenseStore(path)) code = s1.Touch("w", T0).Code;
            using (var s2 = new LicenseStore(path)) Assert.Equal(code, s2.ById("w")!.Code);
        }
        finally
        {
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*")) File.Delete(f);
        }
    }

    [Fact]
    public void CouponCli_AddsListsDeletes()
    {
        var o = new StringWriter();
        Assert.Equal(0, CouponCli.Run(["add", "half", "50", "--uses", "20", "--days", "30", "--note", "forum"], _store, o));
        Assert.Equal(0, CouponCli.Run(["list"], _store, o));
        Assert.Contains("HALF: 50% off, 0/20 used", o.ToString());
        Assert.Equal(2, CouponCli.Run(["add", "x"], _store, o));
        Assert.Equal(0, CouponCli.Run(["del", "half"], _store, o));
        Assert.Empty(_store.Coupons());
    }
}

public class PayPalClientTests
{
    private sealed class Handler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Calls { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct);
            Calls.Add((req.RequestUri!.ToString(), body));
            return respond(req, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task CreateOrder_SendsPriceAndCode_AndReusesToken()
    {
        var h = new Handler((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/v1/oauth2/token" => Json("""{"access_token":"tok","expires_in":3600}"""),
            "/v2/checkout/orders" => Json("""{"id":"ORDER1","status":"CREATED"}"""),
            _ => Json("{}", HttpStatusCode.NotFound)
        });
        var pp = new PayPalClient(new HttpClient(h), "id", "secret", live: false);
        Assert.Equal("ORDER1", await pp.CreateOrderAsync(4.00, "K7P3QX", "WatchCrawler", CancellationToken.None));
        Assert.Equal("ORDER1", await pp.CreateOrderAsync(7.99, "K7P3QX", "WatchCrawler", CancellationToken.None));
        Assert.Equal(1, h.Calls.Count(c => c.Url.Contains("oauth2")));
        var order = h.Calls.First(c => c.Url.EndsWith("/orders")).Body;
        Assert.Contains("\"value\":\"4.00\"", order);
        Assert.Contains("\"custom_id\":\"K7P3QX\"", order);
        Assert.StartsWith("https://api-m.sandbox.paypal.com", h.Calls[0].Url);
    }

    [Fact]
    public async Task Capture_ParsesAmountAndStatus()
    {
        var h = new Handler((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/v1/oauth2/token" => Json("""{"access_token":"tok","expires_in":3600}"""),
            "/v2/checkout/orders/ORDER1/capture" => Json("""
                {"id":"ORDER1","status":"COMPLETED","payer":{"email_address":"b@x.y"},
                 "purchase_units":[{"custom_id":"K7P3QX","payments":{"captures":[{"id":"CAP1","status":"COMPLETED","amount":{"currency_code":"USD","value":"7.99"}}]}}]}
                """),
            _ => Json("{}", HttpStatusCode.NotFound)
        });
        var cap = await new PayPalClient(new HttpClient(h), "id", "secret", live: true).CaptureAsync("ORDER1", CancellationToken.None);
        Assert.Equal("COMPLETED", cap.Status);
        Assert.Equal(7.99, cap.AmountUsd);
        Assert.Equal("K7P3QX", cap.CustomId);
        Assert.Equal("CAP1", cap.CaptureId);
        Assert.StartsWith("https://api-m.paypal.com", h.Calls[0].Url);
    }

    [Fact]
    public async Task ApiError_Throws()
    {
        var h = new Handler((req, _) => req.RequestUri!.AbsolutePath == "/v1/oauth2/token"
            ? Json("""{"access_token":"tok"}""")
            : Json("""{"name":"UNPROCESSABLE_ENTITY"}""", HttpStatusCode.UnprocessableEntity));
        var pp = new PayPalClient(new HttpClient(h), "id", "secret", live: false);
        await Assert.ThrowsAsync<HttpRequestException>(() => pp.CaptureAsync("X", CancellationToken.None));
    }

    [Fact]
    public void ParseCapture_IgnoresNonCompletedCaptures()
    {
        using var doc = JsonDocument.Parse("""
            {"status":"COMPLETED","purchase_units":[{"payments":{"captures":[
              {"id":"A","status":"DECLINED","amount":{"value":"7.99"}},
              {"id":"B","status":"COMPLETED","amount":{"value":"3.00"}}]}}]}
            """);
        var cap = PayPalClient.ParseCapture(doc.RootElement);
        Assert.Equal(3.00, cap.AmountUsd);
        Assert.Equal("B", cap.CaptureId);
    }
}

public class HostedConfigTests
{
    private static Func<string, string?> Env(params (string, string)[] vars) =>
        name => vars.FirstOrDefault(v => v.Item1 == name).Item2;

    [Fact]
    public void Disabled_NeedsNothing()
    {
        Assert.Empty(StartupConfig.ValidateHosted(new HostedSettings(), Env()));
    }

    [Fact]
    public void Enabled_NeedsUrlDataDirAndAdminKey_AndBothPayPalValuesOrNeither()
    {
        var s = HostedSettings.FromEnvironment(new HostedSettings(), Env(("HOSTED", "true")));
        Assert.True(s.Enabled);
        var errors = StartupConfig.ValidateHosted(s, Env(("PAYPAL_CLIENT_ID", "x")));
        Assert.Contains(errors, e => e.Contains("HOSTED_UNLOCK_URL"));
        Assert.Contains(errors, e => e.Contains("DATA_DIR"));
        Assert.Contains(errors, e => e.Contains("ADMIN_KEY"));
        Assert.Contains(errors, e => e.Contains("PAYPAL_CLIENT_SECRET"));

        var ok = HostedSettings.FromEnvironment(new HostedSettings(), Env(("HOSTED", "true"), ("HOSTED_UNLOCK_URL", "wc.test/unlock"), ("HOSTED_PRICE_USD", "5.99")));
        Assert.Equal(5.99, ok.PriceUsd);
        Assert.Empty(StartupConfig.ValidateHosted(ok, Env(("DATA_DIR", "/data"), ("ADMIN_KEY", "k"))));
    }
}
