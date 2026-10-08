using System.Text.RegularExpressions;

namespace GarminAchievements.Tests;

/// <summary>The watch's pixel fonts (watch/resources/fonts/*.fnt) decide which characters can be shown; anything
/// else is drawn as a box. These keep the server's allowlist and the watch's built-in lines in step with them.</summary>
public class WatchGlyphTests
{
    private static readonly string Repo = FindRepo();

    private static string FindRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "watch", "resources", "fonts")))
                return d.FullName;
        throw new DirectoryNotFoundException("watch/resources/fonts not found above the test directory.");
    }

    /// <summary>Characters every font has; lowercase counts when its uppercase is there (the watch uppercases).</summary>
    private static HashSet<char> Drawable()
    {
        var fonts = Directory.GetFiles(Path.Combine(Repo, "watch", "resources", "fonts"), "*.fnt")
            .Select(f => Regex.Matches(File.ReadAllText(f), @"char id=(\d+)").Select(m => (char)int.Parse(m.Groups[1].Value)).ToHashSet())
            .ToList();
        Assert.NotEmpty(fonts);
        var common = fonts.Aggregate((a, b) => a.Intersect(b).ToHashSet());
        common.UnionWith(common.Where(char.IsUpper).Select(char.ToLowerInvariant).ToList());
        return common;
    }

    [Fact]
    public void ServerAllowlist_MatchesTheWatchFonts() =>
        Assert.Equal(Drawable().OrderBy(c => c), AchievementGenerator.WatchGlyphs.OrderBy(c => c));

    [Theory]
    [InlineData("TextBank.mc")]
    [InlineData("SystemNotice.mc")]
    public void BuiltInLines_UseOnlyDrawableCharacters(string file)
    {
        var drawable = Drawable();
        var src = File.ReadAllText(Path.Combine(Repo, "watch", "source", file));
        // Display strings: quoted text with a space in it, minus $1$-style placeholders (filled with numbers/labels).
        var bad = Regex.Matches(src, @"""((?:[^""\\\n]|\\.)*)""")
            .Select(m => Regex.Replace(m.Groups[1].Value, @"\$\d\$", ""))
            .Where(s => s.Contains(' ') && !s.Contains("=>"))
            .SelectMany(s => s.Where(c => !drawable.Contains(c)).Select(c => $"'{c}' in \"{s}\""))
            .ToList();
        Assert.Empty(bad);
    }
}
