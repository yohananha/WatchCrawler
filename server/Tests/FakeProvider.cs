namespace GarminAchievements.Tests;

/// <summary>Returns one canned response per call (in order), or throws if configured to.
/// Lets tests drive AchievementGenerator's retry/fallback/trim logic deterministically,
/// without hitting a real LLM API.</summary>
public sealed class FakeProvider : ILlmProvider
{
    private readonly Queue<Func<LlmResult>> _responses = new();

    public string Name => "Fake";
    public string Model => "fake-model";
    public int CallCount { get; private set; }

    public FakeProvider Returns(string content, int inTok = 10, int outTok = 10)
    {
        _responses.Enqueue(() => new LlmResult(content, inTok, outTok, TimeSpan.FromMilliseconds(1)));
        return this;
    }

    public FakeProvider Throws(Exception ex)
    {
        _responses.Enqueue(() => throw ex);
        return this;
    }

    public Task<LlmResult> CompleteAsync(string system, string user, double temperature, CancellationToken ct)
    {
        CallCount++;
        if (_responses.Count == 0)
            throw new InvalidOperationException("FakeProvider has no more queued responses.");

        return Task.FromResult(_responses.Dequeue()());
    }
}
