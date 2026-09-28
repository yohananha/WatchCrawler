using GarminAchievements;

namespace GarminAchievements.Tests;

public class TierCalculatorTests
{
    private static GameEvent Event(double? value = 10, double? mean = 8, double? std = 2,
        bool higherIsBetter = true, string type = "activity_completed") => new()
    {
        Type = type,
        Value = value,
        Unit = "km",
        BaselineMean = mean,
        BaselineStd = std,
        HigherIsBetter = higherIsBetter,
    };

    // Values sit comfortably inside each tier's z-range (not exactly on a
    // >= threshold) since (value-mean)/std can drift a hair below an exact
    // boundary due to double-precision rounding.
    [Theory]
    [InlineData(5.0, Tier.Cursed)]      // z = -1.5
    [InlineData(7.5, Tier.Common)]      // z = -0.25
    [InlineData(9.5, Tier.Rare)]        // z = 0.75
    [InlineData(11.0, Tier.Epic)]       // z = 1.5
    [InlineData(13.0, Tier.Legendary)]  // z = 2.5
    public void Compute_HigherIsBetter_MatchesZScoreThresholds(double value, Tier expected)
    {
        var (tier, z) = TierCalculator.Compute(Event(value: value, mean: 8, std: 2, higherIsBetter: true));

        Assert.Equal(expected, tier);
        Assert.NotNull(z);
    }

    [Fact]
    public void Compute_LowerIsBetter_FasterThanUsualIsAGoodTier()
    {
        // Resting HR: lower is better. 3 bpm faster (lower) than usual, std 2 -> z = +1.5 -> Epic.
        var (tier, _) = TierCalculator.Compute(Event(value: 53, mean: 56, std: 2, higherIsBetter: false));

        Assert.Equal(Tier.Epic, tier);
    }

    [Theory]
    [InlineData("sedentary")]
    [InlineData("body_battery_low")]
    [InlineData("goal_missed")]
    [InlineData("SEDENTARY")] // case-insensitive
    public void Compute_AlwaysFailureTypes_AreAlwaysCursedRegardlessOfValue(string type)
    {
        var (tier, z) = TierCalculator.Compute(Event(type: type, value: 999, mean: 1, std: 1));

        Assert.Equal(Tier.Cursed, tier);
        Assert.Null(z);
    }

    [Fact]
    public void Compute_NoBaseline_IsCommonWithNullZ()
    {
        var (tier, z) = TierCalculator.Compute(Event(mean: null, std: null));

        Assert.Equal(Tier.Common, tier);
        Assert.Null(z);
    }

    [Fact]
    public void Compute_ZeroOrNegativeStd_IsCommonWithNullZ()
    {
        var (tier, z) = TierCalculator.Compute(Event(std: 0));

        Assert.Equal(Tier.Common, tier);
        Assert.Null(z);
    }

    [Theory]
    [InlineData(Tier.Cursed, "fail")]
    [InlineData(Tier.Common, "chime")]
    [InlineData(Tier.Rare, "chime")]
    [InlineData(Tier.Epic, "fanfare")]
    [InlineData(Tier.Legendary, "fanfare_long")]
    public void SoundFor_MatchesTier(Tier tier, string expectedSound)
    {
        Assert.Equal(expectedSound, TierCalculator.SoundFor(tier));
    }
}
