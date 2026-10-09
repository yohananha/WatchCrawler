namespace GarminAchievements.Tests;

public class AchievementGeneratorTests
{
    private static GameEvent Event() => new()
    {
        Type = "activity_completed",
        Value = 10, Unit = "km",
        BaselineMean = 8, BaselineStd = 2,
        HigherIsBetter = true,
    };

    private static (AchievementGenerator gen, LlmSettings settings, FakeProvider provider) Setup()
    {
        var settings = new LlmSettings(); // default limits: 40/120/70
        var history = new RecentHistory(settings);
        var provider = new FakeProvider();
        return (new AchievementGenerator(settings, history), settings, provider);
    }

    [Fact]
    public async Task ValidJsonWithinLimits_ReturnsParsedAchievement_NotFallback()
    {
        var (gen, _, provider) = Setup();
        provider.Returns("""{"title":"Nice","text":"You did a thing.","reward":"A cookie."}""");

        var result = await gen.GenerateAsync(Event(), provider, recordHistory: false, CancellationToken.None);

        Assert.False(result.Achievement.IsFallback);
        Assert.Equal("Nice", result.Achievement.Title);
        Assert.Equal("You did a thing.", result.Achievement.Text);
        Assert.Equal("A cookie.", result.Achievement.Reward);
        Assert.Equal(1, result.Attempts);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task FieldLabelInsideValue_IsStripped()
    {
        var (gen, _, provider) = Setup();
        provider.Returns("""{"title":"Title: Nice","text":"You did a thing.","reward":"Reward: a participation sticker, slightly damp."}""");

        var result = await gen.GenerateAsync(Event(), provider, recordHistory: false, CancellationToken.None);

        Assert.False(result.Achievement.IsFallback);
        Assert.Equal("Nice", result.Achievement.Title);
        Assert.Equal("A participation sticker, slightly damp.", result.Achievement.Reward);
    }

    [Theory]
    [InlineData("The couch called\u2014it wants its throne back.", "The couch called-it wants its throne back.")]
    [InlineData("\u201CPersonal best\u201D, it\u2019s fine\u2026", "\"Personal best\", it's fine...")]
    [InlineData("Respect +1; ego & pride @ 100%.", "Respect +1; ego & pride @ 100%.")]
    [InlineData("Caf\u00E9 run \U0001F3C6 \u2192 done", "Caf\u00E9 run done")]
    public void WatchSafe_KeepsWhatTheFontsDraw_AndDropsTheRest(string value, string expected) =>
        Assert.Equal(expected, AchievementGenerator.WatchSafe(value));

    [Theory]
    [InlineData("REWARD - nothing.", "Nothing.")]
    [InlineData("reward:   silence", "Silence")]
    [InlineData("Rewarding yourself is cheating.", "Rewarding yourself is cheating.")]
    [InlineData("A cookie.", "A cookie.")]
    public void WithoutLabel_StripsOnlyALeadingLabel(string value, string expected) =>
        Assert.Equal(expected, AchievementGenerator.WithoutLabel(value, "reward"));

    [Fact]
    public async Task JsonWithSurroundingMarkdown_StillParses()
    {
        var (gen, _, provider) = Setup();
        provider.Returns("Here you go:\n```json\n{\"title\":\"Nice\",\"text\":\"Ok.\",\"reward\":\"Meh.\"}\n```");

        var result = await gen.GenerateAsync(Event(), provider, recordHistory: false, CancellationToken.None);

        Assert.False(result.Achievement.IsFallback);
        Assert.Equal("Nice", result.Achievement.Title);
    }

    [Fact]
    public async Task InvalidJsonBothAttempts_FallsBack()
    {
        var (gen, _, provider) = Setup();
        provider.Returns("not json at all").Returns("still not json");

        var result = await gen.GenerateAsync(Event(), provider, recordHistory: false, CancellationToken.None);

        Assert.True(result.Achievement.IsFallback);
        Assert.Equal(2, result.Attempts);
        Assert.Equal("Response was not valid JSON.", result.Error);
    }

    [Fact]
    public async Task TooLongOnFirstAttempt_RetriesAndSucceeds()
    {
        var (gen, settings, provider) = Setup();
        var tooLongTitle = new string('X', settings.MaxTitleChars + 5);
        provider
            .Returns($$"""{"title":"{{tooLongTitle}}","text":"ok","reward":"ok"}""")
            .Returns("""{"title":"Short","text":"ok","reward":"ok"}""");

        var result = await gen.GenerateAsync(Event(), provider, recordHistory: false, CancellationToken.None);

        Assert.False(result.Achievement.IsFallback);
        Assert.Equal("Short", result.Achievement.Title);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task TooLongBothAttempts_TrimsInsteadOfFallingBack()
    {
        var (gen, settings, provider) = Setup();
        var tooLongText = new string('X', settings.MaxTextChars + 20);
        provider
            .Returns($$"""{"title":"ok","text":"{{tooLongText}}","reward":"ok"}""")
            .Returns($$"""{"title":"ok","text":"{{tooLongText}}","reward":"ok"}""");

        var result = await gen.GenerateAsync(Event(), provider, recordHistory: false, CancellationToken.None);

        Assert.False(result.Achievement.IsFallback); // trimmed, not fallback - we keep the parsed joke
        Assert.True(result.Achievement.Text.Length <= settings.MaxTextChars);
        Assert.EndsWith("…", result.Achievement.Text);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ProviderThrows_FallsBack()
    {
        var (gen, _, provider) = Setup();
        provider.Throws(new HttpRequestException("boom")).Throws(new HttpRequestException("boom again"));

        var result = await gen.GenerateAsync(Event(), provider, recordHistory: false, CancellationToken.None);

        Assert.True(result.Achievement.IsFallback);
        Assert.Contains("boom", result.Error);
    }

    [Fact]
    public async Task Achievement_CarriesTierSoundAndProviderInfo()
    {
        var (gen, _, provider) = Setup();
        provider.Returns("""{"title":"a","text":"b","reward":"c"}""");

        // value=12, mean=8, std=2 -> z=2.0 -> Legendary -> fanfare_long
        var e = new GameEvent { Type = "activity_completed", Value = 12, BaselineMean = 8, BaselineStd = 2, HigherIsBetter = true };
        var result = await gen.GenerateAsync(e, provider, recordHistory: false, CancellationToken.None);

        Assert.Equal("legendary", result.Achievement.Tier);
        Assert.Equal("fanfare_long", result.Achievement.Sound);
        Assert.False(result.Achievement.IsFailure);
        Assert.Equal("Fake", result.Achievement.Provider);
        Assert.Equal("fake-model", result.Achievement.Model);
    }

    [Fact]
    public async Task RecordHistory_AddsSuccessfulGenerationsButNotFallbacks()
    {
        var settings = new LlmSettings { RecentHistorySize = 2 };
        var history = new RecentHistory(settings);
        var gen = new AchievementGenerator(settings, history);

        var okProvider = new FakeProvider().Returns("""{"title":"a","text":"b","reward":"c"}""");
        await gen.GenerateAsync(Event(), okProvider, recordHistory: true, CancellationToken.None);
        Assert.Single(history.Snapshot());

        var failProvider = new FakeProvider().Returns("nope").Returns("still nope");
        await gen.GenerateAsync(Event(), failProvider, recordHistory: true, CancellationToken.None);
        Assert.Single(history.Snapshot()); // fallback doesn't get recorded
    }

    [Fact]
    public async Task HangingProvider_GivesUpWithinBudget_AndReturnsFallback()
    {
        var settings = new LlmSettings();
        var gen = new AchievementGenerator(settings, new RecentHistory(settings)) { Budget = TimeSpan.FromMilliseconds(50) };
        var provider = new FakeProvider().Hangs();

        var result = await gen.GenerateAsync(Event(), provider, recordHistory: false, CancellationToken.None);

        Assert.True(result.Achievement.IsFallback);
        Assert.Equal(LlmErrorKind.Timeout, result.ErrorKind);
        Assert.Equal(1, provider.CallCount); // no retry once the budget is spent
    }

    [Fact]
    public async Task TooLongAnswer_ThenHang_KeepsTheTrimmedFirstAnswer()
    {
        var settings = new LlmSettings();
        var gen = new AchievementGenerator(settings, new RecentHistory(settings)) { Budget = TimeSpan.FromMilliseconds(50) };
        var provider = new FakeProvider()
            .Returns($$"""{"title":"{{new string('x', 60)}}","text":"Fine.","reward":"Fine."}""")
            .Hangs();

        var result = await gen.GenerateAsync(Event(), provider, recordHistory: false, CancellationToken.None);

        Assert.False(result.Achievement.IsFallback);
        Assert.True(result.Achievement.Title.Length <= settings.MaxTitleChars);
    }

    [Fact]
    public void RecentHistory_CapsAtConfiguredSize()
    {
        var settings = new LlmSettings { RecentHistorySize = 2 };
        var history = new RecentHistory(settings);

        history.Add(new AchievementText("1", "t", "r"));
        history.Add(new AchievementText("2", "t", "r"));
        history.Add(new AchievementText("3", "t", "r"));

        var snapshot = history.Snapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.Equal("2", snapshot[0].Title);
        Assert.Equal("3", snapshot[1].Title);
    }
}
