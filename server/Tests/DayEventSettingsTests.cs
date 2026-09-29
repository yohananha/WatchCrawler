namespace GarminAchievements.Tests;

public class DayEventSettingsTests
{
    private static DayEventSettings Parse(Dictionary<string, string> env) =>
        DayEventSettings.From(k => env.GetValueOrDefault(k));

    [Fact]
    public void NoEnv_UsesDefaults()
    {
        Assert.Equal(DayEventSettings.Defaults, Parse(new()));
    }

    [Fact]
    public void ValidValues_AreUsed()
    {
        var s = Parse(new() { ["DAY_QUIET_FROM"] = "22", ["DAY_QUIET_TO"] = "6", ["DAY_MAX_PER_DAY"] = "5",
                              ["DAY_IDLE_HOUR"] = "21", ["DAY_GOAL_HOUR"] = "20" });

        Assert.Equal(new DayEventSettings(22, 6, 5, 21, 20), s);
    }

    [Theory]
    [InlineData("24")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("")]
    public void InvalidHour_FallsBackToDefault(string raw)
    {
        var s = Parse(new() { ["DAY_IDLE_HOUR"] = raw });

        Assert.Equal(DayEventSettings.Defaults.IdleHour, s.IdleHour);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    public void InvalidCap_FallsBackToDefault(string raw)
    {
        var s = Parse(new() { ["DAY_MAX_PER_DAY"] = raw });

        Assert.Equal(DayEventSettings.Defaults.MaxPerDay, s.MaxPerDay);
    }
}
