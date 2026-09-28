namespace GarminAchievements;

/// <summary>Lets a browser/curl "arm" a test achievement that the watch picks up on its next
/// 5-minute background check (Connect IQ has no server-to-watch push for a private/sideloaded
/// app - the watch only ever polls). A single in-memory flag is enough: this is a personal app,
/// one watch, one user.</summary>
public static class TestTrigger
{
    private static readonly Lock Gate = new();
    private static bool _armed;

    public static void Arm()
    {
        lock (Gate) _armed = true;
    }

    public static bool IsArmed()
    {
        lock (Gate) return _armed;
    }

    /// <summary>Atomically checks and clears the flag, so it fires exactly once.</summary>
    public static bool ConsumeIfArmed()
    {
        lock (Gate)
        {
            if (!_armed) return false;
            _armed = false;
            return true;
        }
    }
}
