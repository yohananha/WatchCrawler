namespace GarminAchievements.Tests;

public class SitePagesTests
{
    private static HostedSettings Settings() => new() { Enabled = true, TrialDays = 7, LicenseYears = 3, AiPerDay = 6, PriceUsd = 7.99 };
    private const string Email = "support@example.com";

    [Fact]
    public void Landing_ShowsPriceTrialAndLegalLinks()
    {
        var html = LandingPage.Html(Settings(), Email, null);
        Assert.Contains("$7.99", html);
        Assert.Contains("7-day trial", html);
        Assert.Contains("3 years", html);
        foreach (var link in new[] { "href=\"/terms\"", "href=\"/refunds\"", "href=\"/privacy\"", "href=\"/unlock\"", "mailto:support@example.com" })
            Assert.Contains(link, html);
    }

    [Fact]
    public void Landing_StoreButton_OnlyWithStoreUrl()
    {
        Assert.Contains("Coming soon", LandingPage.Html(Settings(), Email, null));
        var html = LandingPage.Html(Settings(), Email, "https://apps.garmin.com/apps/abc");
        Assert.Contains("href=\"https://apps.garmin.com/apps/abc\"", html);
        Assert.DoesNotContain("Coming soon", html);
    }

    [Fact]
    public void AllPages_FillEveryPlaceholder()
    {
        foreach (var html in new[]
        {
            LandingPage.Html(Settings(), Email, null), LegalPages.Terms(Settings(), Email),
            LegalPages.Refunds(Settings(), Email), PrivacyPage.Html(Settings(), Email),
        })
        {
            Assert.DoesNotContain("{{", html);
            Assert.Contains("href=\"/refunds\"", html);
        }
    }

    [Fact]
    public void Contact_EmailIsMailto_NothingFallsBackToGitHub_OtherTextIsEncoded()
    {
        Assert.Equal("<a href=\"mailto:a@b.c\">a@b.c</a>", Site.ContactHtml("a@b.c"));
        Assert.Contains("github.com/yohananha/WatchCrawler/issues", Site.ContactHtml(null));
        Assert.Equal("&lt;b&gt;", Site.ContactHtml("<b>"));
    }

    [Fact]
    public void Refunds_StatesTheWindow()
    {
        Assert.Contains("14 days", LegalPages.Refunds(Settings(), Email));
        Assert.Contains("14 days", LegalPages.Terms(Settings(), Email));
    }
}
