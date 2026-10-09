namespace GarminAchievements.Tests;

/// <summary>Returns one canned response per call (in order), or throws if configured to.
/// Lets tests drive AchievementGenerator's retry/fallback/trim logic deterministically,
/// without hitting a real LLM API.</summary>
public sealed class FakeProvider : ILlmProvider
{
    private readonly Queue<Func<CancellationToken, Task<LlmResult>>> _responses = new();

    public string Name => "Fake";
    public string Model => "fake-model";
    public int CallCount { get; private set; }

    public FakeProvider Returns(string content, int inTok = 10, int outTok = 10)
    {
        _responses.Enqueue(_ => Task.FromResult(new LlmResult(content, inTok, outTok, TimeSpan.FromMilliseconds(1))));
        return this;
    }

    public FakeProvider Throws(Exception ex)
    {
        _responses.Enqueue(_ => throw ex);
        return this;
    }

    /// <summary>Never answers: waits until the call is cancelled, like an LLM API that hangs.</summary>
    public FakeProvider Hangs()
    {
        _responses.Enqueue(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        return this;
    }

    public Task<LlmResult> CompleteAsync(string system, string user, double temperature, CancellationToken ct)
    {
        CallCount++;
        if (_responses.Count == 0)
            throw new InvalidOperationException("FakeProvider has no more queued responses.");

        return _responses.Dequeue()(ct);
    }
}
