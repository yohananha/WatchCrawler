using System.Security.Cryptography;
using System.Text;

namespace GarminAchievements.Tests;

public class LemonSqueezyTests
{
    private const string Secret = "whsec-test";

    private static string Sign(string body) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private const string PaidOrder = """
        {"meta":{"event_name":"order_created","test_mode":true,"custom_data":{"code":"k7p3qx"}},
         "data":{"type":"orders","id":"1234567","attributes":{"status":"paid","user_email":"b@x.y","currency":"EUR",
           "total":699,"total_usd":799,"identifier":"abc-def"}}}
        """;

    [Fact]
    public void Signature_Verifies_CaseInsensitively_AndRejectsTampering()
    {
        Assert.True(LemonSqueezy.VerifySignature(PaidOrder, Sign(PaidOrder), Secret));
        Assert.True(LemonSqueezy.VerifySignature(PaidOrder, Sign(PaidOrder).ToUpperInvariant(), Secret));
        Assert.False(LemonSqueezy.VerifySignature(PaidOrder + " ", Sign(PaidOrder), Secret));
        Assert.False(LemonSqueezy.VerifySignature(PaidOrder, Sign(PaidOrder), "other"));
        Assert.False(LemonSqueezy.VerifySignature(PaidOrder, null, Secret));
        Assert.False(LemonSqueezy.VerifySignature(PaidOrder, "zz", Secret));
    }

    [Fact]
    public void Parse_ReadsStatusUsdTotalAndNormalizedCode()
    {
        var o = LemonSqueezy.Parse(PaidOrder);
        Assert.NotNull(o);
        Assert.Equal("order_created", o!.EventName);
        Assert.Equal("1234567", o.OrderId);
        Assert.Equal("paid", o.Status);
        Assert.Equal(7.99, o.TotalUsd);
        Assert.Equal("K7P3QX", o.Code);
        Assert.Equal("b@x.y", o.Email);
        Assert.True(o.TestMode);
    }

    [Fact]
    public void Parse_ReadsRefundEvent()
    {
        var o = LemonSqueezy.Parse(PaidOrder.Replace("order_created", "order_refunded").Replace("\"paid\"", "\"refunded\""));
        Assert.Equal("order_refunded", o!.EventName);
        Assert.Equal("refunded", o.Status);
        Assert.Equal("1234567", o.OrderId);
    }

    [Fact]
    public void Parse_ToleratesMissingCustomData_AndGarbage()
    {
        var o = LemonSqueezy.Parse("""{"meta":{"event_name":"order_created"},"data":{"id":"1","attributes":{"status":"pending","total":100}}}""");
        Assert.NotNull(o);
        Assert.Null(o!.Code);
        Assert.Equal(1.00, o.TotalUsd);
        Assert.False(o.TestMode);
        Assert.Null(LemonSqueezy.Parse("not json"));
        Assert.Null(LemonSqueezy.Parse("""{"meta":{}}"""));
    }

    [Fact]
    public void CheckoutUrl_CarriesCodeAndOptionalDiscount()
    {
        var u = LemonSqueezy.CheckoutUrl("https://wc.lemonsqueezy.com/checkout/buy/uuid", "K7P3QX", null);
        Assert.Equal("https://wc.lemonsqueezy.com/checkout/buy/uuid?checkout[custom][code]=K7P3QX", u);
        var d = LemonSqueezy.CheckoutUrl("https://wc.lemonsqueezy.com/checkout/buy/uuid?embed=1", "K7P3QX", "HALF OFF");
        Assert.Equal("https://wc.lemonsqueezy.com/checkout/buy/uuid?embed=1&checkout[custom][code]=K7P3QX&checkout[discount_code]=HALF%20OFF", d);
    }

    [Fact]
    public void Settings_NeedBothValues()
    {
        Assert.Null(LemonSqueezy.Settings.FromEnvironment(n => n == "LEMONSQUEEZY_CHECKOUT_URL" ? "https://x" : null));
        var s = LemonSqueezy.Settings.FromEnvironment(n => n == "LEMONSQUEEZY_CHECKOUT_URL" ? "https://x " : n == "LEMONSQUEEZY_WEBHOOK_SECRET" ? "s" : null);
        Assert.Equal("https://x", s!.CheckoutUrl);
    }

    [Fact]
    public void Config_RejectsHalfSetAndBothProviders()
    {
        var hosted = new HostedSettings { Enabled = true, UnlockUrl = "u" };
        Func<string, string?> env = n => n switch
        {
            "DATA_DIR" => "/d", "ADMIN_KEY" => "k",
            "LEMONSQUEEZY_CHECKOUT_URL" => "https://x", _ => null
        };
        Assert.Contains(StartupConfig.ValidateHosted(hosted, env), e => e.Contains("LEMONSQUEEZY_WEBHOOK_SECRET"));
        Func<string, string?> both = n => n switch
        {
            "DATA_DIR" => "/d", "ADMIN_KEY" => "k",
            "LEMONSQUEEZY_CHECKOUT_URL" => "https://x", "LEMONSQUEEZY_WEBHOOK_SECRET" => "s",
            "PAYPAL_CLIENT_ID" => "i", "PAYPAL_CLIENT_SECRET" => "s", _ => null
        };
        Assert.Contains(StartupConfig.ValidateHosted(hosted, both), e => e.Contains("Pick one"));
    }
}
