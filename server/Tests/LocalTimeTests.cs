namespace GarminAchievements.Tests;

public class LocalTimeTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 12, 5, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Prompt_UsesWatchOffset_NotServerUtc()
    {
        var e = new GameEvent
        {
            Type = "activity_completed", Timestamp = At, UtcOffset = 3 * 3600,
            StartedAt = new DateTimeOffset(2026, 10, 12, 5, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
        };

        var prompt = PromptBuilder.User(e, Tier.Common, null, []);

        Assert.Contains("Local time: 2026-10-12 08:30 (Monday)", prompt);
        Assert.Contains("Started at (local time): 2026-10-12 08:00 (Monday)", prompt);
    }

    [Fact]
    public void NoOffset_KeepsServerTime_AndNoStartLine()
    {
        var e = new GameEvent { Type = "sedentary", Timestamp = At };

        var prompt = PromptBuilder.User(e, Tier.Common, null, []);

        Assert.Contains("Local time: 2026-10-12 05:30", prompt);
        Assert.DoesNotContain("Started at", prompt);
    }

    [Theory]
    [InlineData(30)]          // not whole minutes
    [InlineData(15 * 3600)]   // beyond +-14 h
    public void BadOffset_IsIgnored(int offset)
    {
        var e = new GameEvent { Type = "sedentary", Timestamp = At, UtcOffset = offset };

        Assert.Equal(At, e.LocalTimestamp);
        Assert.Equal(TimeSpan.Zero, e.LocalTimestamp.Offset);
    }
}
