namespace GarminAchievements.Tests;

// TestTrigger is process-global static state, so these run serially (xUnit
// runs tests in a class sequentially by default, and this is the only class
// touching it) - a test-only concession for a single-user personal app.
public class TestTriggerTests
{
    public TestTriggerTests() => TestTrigger.ConsumeIfArmed(); // reset before each test

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
}
