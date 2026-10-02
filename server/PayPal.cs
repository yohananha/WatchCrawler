using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GarminAchievements;

/// <summary>The two PayPal Checkout REST calls the unlock page needs: create an order for a price, and capture it
/// once the buyer approved it in PayPal's pop-up (the page's JS does the approving; the server does both calls
/// so the price and the result can't be tampered with). Credentials come from PAYPAL_CLIENT_ID /
/// PAYPAL_CLIENT_SECRET; PAYPAL_ENV=sandbox (default) or live.</summary>
public sealed class PayPalClient
{
    public sealed record Capture(string Status, double AmountUsd, string? CustomId, string? CaptureId, string? PayerEmail);

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly string _secret;
    private readonly TimeProvider _time;
    private string? _token;
    private DateTimeOffset _tokenExpires;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public string BaseUrl { get; }
    public bool IsLive { get; }
    public string ClientId => _clientId;

    public PayPalClient(HttpClient http, string clientId, string secret, bool live, TimeProvider? time = null)
    {
        _http = http;
        _clientId = clientId;
        _secret = secret;
        _time = time ?? TimeProvider.System;
        IsLive = live;
        BaseUrl = live ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";
    }

    public static PayPalClient? FromEnvironment(IHttpClientFactory factory, Func<string, string?> env)
    {
        var id = env("PAYPAL_CLIENT_ID");
        var secret = env("PAYPAL_CLIENT_SECRET");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret)) return null;
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        return new PayPalClient(client, id.Trim(), secret.Trim(), string.Equals(env("PAYPAL_ENV"), "live", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Creates an order and returns PayPal's order id (the page hands it to the PayPal buttons).</summary>
    public async Task<string> CreateOrderAsync(double amountUsd, string customId, string description, CancellationToken ct)
    {
        var body = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new
                {
                    custom_id = customId,
                    description,
                    amount = new { currency_code = "USD", value = amountUsd.ToString("0.00", CultureInfo.InvariantCulture) }
                }
            }
        };
        using var doc = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", body, ct);
        return doc.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("PayPal returned no order id.");
    }

    public async Task<Capture> CaptureAsync(string orderId, CancellationToken ct)
    {
        using var doc = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{Uri.EscapeDataString(orderId)}/capture", null, ct);
        return ParseCapture(doc.RootElement);
    }

    /// <summary>Reads the result of a capture (or of GET order): status, the captured amount, our custom id.</summary>
    public static Capture ParseCapture(JsonElement root)
    {
        var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
        double amount = 0;
        string? customId = null, captureId = null, email = null;
        if (root.TryGetProperty("purchase_units", out var units))
        {
            foreach (var unit in units.EnumerateArray())
            {
                if (unit.TryGetProperty("custom_id", out var cid)) customId ??= cid.GetString();
                if (unit.TryGetProperty("payments", out var payments) && payments.TryGetProperty("captures", out var captures))
                {
                    foreach (var cap in captures.EnumerateArray())
                    {
                        if (cap.TryGetProperty("status", out var cs) && cs.GetString() != "COMPLETED") continue;
                        captureId ??= cap.TryGetProperty("id", out var id) ? id.GetString() : null;
                        if (cap.TryGetProperty("custom_id", out var ccid)) customId ??= ccid.GetString();
                        if (cap.TryGetProperty("amount", out var amt) && amt.TryGetProperty("value", out var val)
                            && double.TryParse(val.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                            amount += v;
                    }
                }
            }
        }
        if (root.TryGetProperty("payer", out var payer) && payer.TryGetProperty("email_address", out var em)) email = em.GetString();
        return new Capture(status, Math.Round(amount, 2), customId, captureId, email);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var token = await TokenAsync(ct);
        using var req = new HttpRequestMessage(method, BaseUrl + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(body is null ? "{}" : JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await _http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"PayPal {method} {path} -> {(int)res.StatusCode}: {Scrub(text)}");
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (_token is not null && _time.GetUtcNow() < _tokenExpires) return _token;
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is not null && _time.GetUtcNow() < _tokenExpires) return _token;
            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/v1/oauth2/token");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_clientId}:{_secret}")));
            req.Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("grant_type", "client_credentials")]);
            using var res = await _http.SendAsync(req, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new HttpRequestException($"PayPal token -> {(int)res.StatusCode}: {Scrub(text)}");
            using var doc = JsonDocument.Parse(text);
            _token = doc.RootElement.GetProperty("access_token").GetString();
            var seconds = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
            _tokenExpires = _time.GetUtcNow().AddSeconds(Math.Max(60, seconds - 60));
            return _token!;
        }
        finally { _tokenLock.Release(); }
    }

    private static string Scrub(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
