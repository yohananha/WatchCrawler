namespace GarminAchievements;

/// <summary>Lets a browser/curl "arm" a test achievement that the watch picks up on its next
/// background check (Connect IQ has no server-to-watch push for a private/sideloaded app - the
/// watch only ever polls). The flag is a file, not an in-memory bool: Fly stops the machine when
/// idle and every restart wipes process memory, which silently lost armed triggers (found by
/// testing on the real watch). Point <c>TRIGGER_STATE_PATH</c> at a mounted Fly Volume in
/// production; locally it defaults to the OS temp dir. One file = one flag, which is enough for
/// a personal app with one watch.</summary>
public static class TestTrigger
{
    private static readonly Lock Gate = new();

    /// <summary>Overridable for tests.</summary>
    public static string Path { get; set; } =
        Environment.GetEnvironmentVariable("TRIGGER_STATE_PATH")
        ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "watchcrawler-trigger.flag");

    /// <summary>Kinds the test page can fire: id (what the watch's debug injector understands) -> label.</summary>
    public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>
    {
        ["legendary"] = "Run: legendary (personal record)",
        ["epic"] = "Run: epic",
        ["rare"] = "Run: rare",
        ["common"] = "Run: common",
        ["cursed"] = "Run: cursed (worse than usual)",
        ["strength"] = "Strength training",
        ["hiit"] = "HIIT",
        ["yoga"] = "Yoga",
        ["swim"] = "Swim",
        ["idle"] = "Nothing achieved today",
        ["goal"] = "Step goal missed",
        ["sit"] = "Sedentary (move bar maxed)",
        ["steps"] = "Step goal reached",
        ["floors"] = "Floors goal reached",
        ["batt"] = "Body Battery low",
        ["rhr"] = "Resting heart rate (better than usual)",
        ["test"] = "Canned joke (no server text)",
    };

    public const string DefaultKind = "legendary";

    public static void Arm(string kind = DefaultKind)
    {
        lock (Gate)
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(Path, Kinds.ContainsKey(kind) ? kind : DefaultKind);
        }
    }

    /// <summary>Like <see cref="ConsumeIfArmed"/> but returns which kind was armed (null if none).</summary>
    public static string? ConsumeKind()
    {
        lock (Gate)
        {
            if (!File.Exists(Path)) return null;
            var kind = File.ReadAllText(Path).Trim();
            File.Delete(Path);
            return Kinds.ContainsKey(kind) ? kind : DefaultKind; // old flag files held a timestamp
        }
    }

    public static bool IsArmed()
    {
        lock (Gate) return File.Exists(Path);
    }

    /// <summary>Atomically checks and clears the flag, so it fires exactly once.</summary>
    public static bool ConsumeIfArmed()
    {
        lock (Gate)
        {
            if (!File.Exists(Path)) return false;
            File.Delete(Path);
            return true;
        }
    }
}
