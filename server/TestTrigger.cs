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

    public static void Arm()
    {
        lock (Gate)
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(Path, DateTimeOffset.UtcNow.ToString("O"));
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
