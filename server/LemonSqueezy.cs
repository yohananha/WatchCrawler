using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GarminAchievements;

/// <summary>Lemon Squeezy as merchant of record: they run the checkout, collect tax, handle refunds and pay the
/// developer out. We only (1) send the buyer to the hosted checkout with the watch code in the custom data and
/// (2) license the device when the signed <c>order_created</c> webhook says the order is paid, and revoke it when
/// <c>order_refunded</c> says it was refunded in full (both events must be ticked on the webhook).
/// Env: LEMONSQUEEZY_CHECKOUT_URL (the product's "Buy" link), LEMONSQUEEZY_WEBHOOK_SECRET (set on the webhook).</summary>
public static class LemonSqueezy
{
    public sealed record Settings(string CheckoutUrl, string WebhookSecret)
    {
        public static Settings? FromEnvironment(Func<string, string?> env)
        {
            var url = env("LEMONSQUEEZY_CHECKOUT_URL");
            var secret = env("LEMONSQUEEZY_WEBHOOK_SECRET");
            return string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret) ? null : new(url.Trim(), secret.Trim());
        }
    }

    /// <summary>What we need from an order webhook.</summary>
    public sealed record WebhookOrder(string EventName, string OrderId, string Status, double TotalUsd, string? Code, string? Email, bool TestMode);

    /// <summary>The buy link with the watch code attached (comes back in the webhook as custom data) and, when
    /// given, a Lemon Squeezy discount code pre-applied (made in their dashboard, not our coupons table).</summary>
    public static string CheckoutUrl(string baseUrl, string code, string? discountCode)
    {
        var sep = baseUrl.Contains('?') ? "&" : "?";
        var url = $"{baseUrl}{sep}checkout[custom][code]={Uri.EscapeDataString(code)}";
        if (!string.IsNullOrWhiteSpace(discountCode))
            url += $"&checkout[discount_code]={Uri.EscapeDataString(discountCode.Trim())}";
        return url;
    }

    /// <summary>X-Signature is hex HMAC-SHA256 of the raw body with the webhook's signing secret.</summary>
    public static bool VerifySignature(string rawBody, string? signatureHeader, string secret)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader)) return false;
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody)));
        var given = signatureHeader.Trim();
        return given.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(given.ToUpperInvariant()), Encoding.ASCII.GetBytes(expected));
    }

    public static WebhookOrder? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var meta = root.GetProperty("meta");
            var eventName = meta.TryGetProperty("event_name", out var ev) ? ev.GetString() ?? "" : "";
            string? code = null;
            if (meta.TryGetProperty("custom_data", out var custom) && custom.ValueKind == JsonValueKind.Object
                && custom.TryGetProperty("code", out var c))
                code = c.GetString();
            var testMode = meta.TryGetProperty("test_mode", out var tm) && tm.ValueKind == JsonValueKind.True;

            var data = root.GetProperty("data");
            var id = data.GetProperty("id").GetString() ?? "";
            var attrs = data.GetProperty("attributes");
            var status = attrs.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
            var email = attrs.TryGetProperty("user_email", out var em) ? em.GetString() : null;
            // Totals are in cents; total_usd is what we actually got in USD regardless of the buyer's currency.
            double total = 0;
            if (attrs.TryGetProperty("total_usd", out var tu) && tu.ValueKind == JsonValueKind.Number) total = tu.GetDouble() / 100.0;
            else if (attrs.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number) total = t.GetDouble() / 100.0;
            return new WebhookOrder(eventName, id, status, Math.Round(total, 2), code is null ? null : Licensing.NormalizeCode(code), email, testMode);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
