using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace GarminAchievements.Tests;

public class ModelResolverTests
{
    private const string Models = """
        {"data":[
          {"type":"model","id":"claude-sonnet-9","created_at":"2027-06-01T00:00:00Z"},
          {"type":"model","id":"claude-haiku-9-20270301","created_at":"2027-03-01T00:00:00Z"},
          {"type":"model","id":"claude-haiku-9","created_at":"2027-03-01T00:00:00Z"},
          {"type":"model","id":"claude-haiku-4-5","created_at":"2025-10-01T00:00:00Z"}
        ],"has_more":false}
        """;

    private const string Reply = """
        {"content":[{"type":"text","text":"hi"}],"usage":{"input_tokens":5,"output_tokens":2}}
        """;

    static ModelResolverTests() => Environment.SetEnvironmentVariable("TEST_RESOLVER_KEY", "k");

    private static ProviderSettings Settings(string model = "claude-haiku-latest") => new()
    {
        Kind = "anthropic", BaseUrl = "https://api.test", Model = model,
        FallbackModel = "claude-haiku-4-5", ApiKeyEnv = "TEST_RESOLVER_KEY",
    };

    /// <summary>Answers /v1/models with <paramref name="models"/> (null = HTTP 500), and /v1/messages per requested model.</summary>
    private sealed class Api(string? models, Func<string, HttpResponseMessage> messages) : HttpMessageHandler
    {
        public List<string> Calls { get; } = new();
        public Action<string>? OnMessage { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (req.RequestUri!.AbsolutePath == "/v1/models")
            {
                Calls.Add("models");
                return models is null ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Json(HttpStatusCode.OK, models);
            }
            var body = await req.Content!.ReadAsStringAsync(ct);
            OnMessage?.Invoke(body);
            using var doc = JsonDocument.Parse(body);
            var model = doc.RootElement.GetProperty("model").GetString()!;
            Calls.Add(model);
            return messages(model);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Ok() => Json(HttpStatusCode.OK, Reply);

    private static AnthropicProvider Provider(Api api, ModelResolver resolver, ProviderSettings? s = null) =>
        new("Anthropic", s ?? Settings(), new HttpClient(api), resolver);

    private static ModelResolver Resolver() => new(NullLogger<ModelResolver>.Instance);

    [Fact]
    public void PickNewest_TakesNewestOfFamily_PreferringTheAliasOnATie()
    {
        Assert.Equal("claude-haiku-9", ModelResolver.PickNewest("claude-haiku-", Models));
        Assert.Equal("claude-sonnet-9", ModelResolver.PickNewest("claude-sonnet-", Models));
        Assert.Null(ModelResolver.PickNewest("claude-opus-", Models));
    }

    [Fact]
    public async Task LatestAlias_UsesNewestModel_AndLooksItUpOnlyOnce()
    {
        var api = new Api(Models, _ => Ok());
        var resolver = Resolver();

        await Provider(api, resolver).CompleteAsync("s", "u", 1.0, default);
        var p = Provider(api, resolver);
        await p.CompleteAsync("s", "u", 1.0, default);

        Assert.Equal(["models", "claude-haiku-9", "claude-haiku-9"], api.Calls);
        Assert.Equal("claude-haiku-9", p.Model);
    }

    [Fact]
    public async Task NewestModelRejectsRequest_RetriesWithFallback_AndSticksToIt()
    {
        var api = new Api(Models, m => m == "claude-haiku-9"
            ? Json(HttpStatusCode.BadRequest, """{"type":"error","error":{"type":"invalid_request_error","message":"temperature is not supported"}}""")
            : Ok());
        var resolver = Resolver();
        var p = Provider(api, resolver);

        var r = await p.CompleteAsync("s", "u", 1.0, default);
        await p.CompleteAsync("s", "u", 1.0, default);

        Assert.Equal("hi", r.Content);
        Assert.Equal(["models", "claude-haiku-9", "claude-haiku-4-5", "claude-haiku-4-5"], api.Calls);
        Assert.Equal("claude-haiku-4-5", p.Model);
    }

    [Fact]
    public async Task Effort_IsSentToNewestModel_ButNeverToFallback()
    {
        var bodies = new List<string>();
        var api = new Api(Models, m => m == "claude-haiku-9"
            ? Json(HttpStatusCode.BadRequest, """{"type":"error","error":{"type":"invalid_request_error","message":"nope"}}""")
            : Ok());
        api.OnMessage = bodies.Add;
        var s = Settings();
        s.Effort = "low";

        await Provider(api, Resolver(), s).CompleteAsync("s", "u", 1.0, default);

        Assert.Contains("\"effort\":\"low\"", bodies[0]);    // claude-haiku-9
        Assert.DoesNotContain("effort", bodies[1]);            // claude-haiku-4-5
    }

    [Fact]
    public async Task OutOfCredit_IsNotMistakenForARejectedModel()
    {
        var api = new Api(Models, _ => Json(HttpStatusCode.BadRequest,
            """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low"}}"""));

        var ex = await Assert.ThrowsAsync<LlmHttpException>(() => Provider(api, Resolver()).CompleteAsync("s", "u", 1.0, default));

        Assert.True(ex.IsOutOfCredit);
        Assert.Equal(["models", "claude-haiku-9"], api.Calls);
    }

    [Fact]
    public async Task LookupFails_UsesFallbackModel()
    {
        var api = new Api(null, _ => Ok());
        var p = Provider(api, Resolver());

        await p.CompleteAsync("s", "u", 1.0, default);

        Assert.Equal(["models", "claude-haiku-4-5"], api.Calls);
        Assert.Equal("claude-haiku-4-5", p.Model);
    }

    [Fact]
    public async Task FixedModelId_IsUsedAsIs_WithoutLookup()
    {
        var api = new Api(Models, _ => Ok());
        var p = Provider(api, Resolver(), Settings("claude-haiku-4-5"));

        await p.CompleteAsync("s", "u", 1.0, default);

        Assert.Equal(["claude-haiku-4-5"], api.Calls);
        Assert.Equal("claude-haiku-4-5", p.Model);
    }
}
