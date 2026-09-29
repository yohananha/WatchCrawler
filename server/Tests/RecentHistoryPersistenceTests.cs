namespace GarminAchievements.Tests;

public class RecentHistoryPersistenceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"history-{Guid.NewGuid():N}.json");
    private readonly LlmSettings _settings = new() { RecentHistorySize = 3 };

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void History_SurvivesARestart()
    {
        var first = new RecentHistory(_settings, _path);
        first.Add(new AchievementText("T1", "x", "r"));
        first.Add(new AchievementText("T2", "y", "r"));

        var afterRestart = new RecentHistory(_settings, _path);

        Assert.Equal(["T1", "T2"], afterRestart.Snapshot().Select(a => a.Title));
    }

    [Fact]
    public void LoadedHistory_IsCappedToTheConfiguredSize()
    {
        var first = new RecentHistory(new LlmSettings { RecentHistorySize = 10 }, _path);
        for (var i = 1; i <= 6; i++) first.Add(new AchievementText($"T{i}", "x", "r"));

        var smaller = new RecentHistory(_settings, _path);

        Assert.Equal(["T4", "T5", "T6"], smaller.Snapshot().Select(a => a.Title));
    }

    [Fact]
    public void CorruptFile_StartsEmpty_AndDoesNotThrow()
    {
        File.WriteAllText(_path, "{not json");

        var history = new RecentHistory(_settings, _path);
        history.Add(new AchievementText("T1", "x", "r"));

        Assert.Single(history.Snapshot());
    }

    [Fact]
    public void NoPath_IsMemoryOnly()
    {
        var history = new RecentHistory(_settings, null);
        history.Add(new AchievementText("T1", "x", "r"));

        Assert.Single(history.Snapshot());
        Assert.False(File.Exists(_path));
    }
}
