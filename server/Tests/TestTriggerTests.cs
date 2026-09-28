namespace GarminAchievements.Tests;

// TestTrigger.Path is process-global static state, so these run serially
// (xUnit runs tests in one class sequentially, and this is the only class
// touching it). Each test gets its own temp file.
public class TestTriggerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"trigger-test-{Guid.NewGuid():N}.flag");
    private readonly string _originalPath = TestTrigger.Path;

    public TestTriggerTests() => TestTrigger.Path = _path;

    public void Dispose()
    {
        TestTrigger.Path = _originalPath;
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void StartsUnarmed()
    {
        Assert.False(TestTrigger.IsArmed());
    }

    [Fact]
    public void Arm_SetsArmed()
    {
        TestTrigger.Arm();
        Assert.True(TestTrigger.IsArmed());
    }

    [Fact]
    public void ConsumeIfArmed_ClearsFlag_AndOnlyFiresOnce()
    {
        TestTrigger.Arm();

        Assert.True(TestTrigger.ConsumeIfArmed());
        Assert.False(TestTrigger.IsArmed());
        Assert.False(TestTrigger.ConsumeIfArmed());
    }

    [Fact]
    public void ConsumeIfArmed_WhenNotArmed_ReturnsFalse()
    {
        Assert.False(TestTrigger.ConsumeIfArmed());
    }

    [Fact]
    public void Arm_IsBackedByAFile_SoItSurvivesAProcessRestart()
    {
        // The whole point of the file: Fly wipes process memory on every
        // idle stop/start. Nothing in memory here - a "new process" only
        // sees whatever is on disk.
        TestTrigger.Arm();

        Assert.True(File.Exists(_path));
        Assert.True(TestTrigger.IsArmed());
    }

    [Fact]
    public void Arm_CreatesMissingDirectory()
    {
        var nested = Path.Combine(Path.GetTempPath(), $"trigger-dir-{Guid.NewGuid():N}", "sub", "flag");
        TestTrigger.Path = nested;
        try
        {
            TestTrigger.Arm();
            Assert.True(TestTrigger.IsArmed());
        }
        finally
        {
            TestTrigger.ConsumeIfArmed();
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(nested))!, recursive: true);
        }
    }
}
