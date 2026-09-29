namespace GarminAchievements;

/// <summary>When the watch's daily events (goal missed, nothing today, sedentary...) may fire. Served to the
/// watch with every trigger poll so they can be tuned with Fly env vars, no reinstall needed. Hours are
/// 0-23 in the watch's local time.</summary>
public sealed record DayEventSettings(
    int QuietFrom,
    int QuietTo,
    int MaxPerDay,
    int IdleHour,
    int GoalHour)
{
    public static readonly DayEventSettings Defaults = new(QuietFrom: 23, QuietTo: 7, MaxPerDay: 10, IdleHour: 22, GoalHour: 21);

    public static DayEventSettings FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static DayEventSettings From(Func<string, string?> get) => new(
        QuietFrom: Hour(get("DAY_QUIET_FROM"), Defaults.QuietFrom),
        QuietTo: Hour(get("DAY_QUIET_TO"), Defaults.QuietTo),
        MaxPerDay: Count(get("DAY_MAX_PER_DAY"), Defaults.MaxPerDay),
        IdleHour: Hour(get("DAY_IDLE_HOUR"), Defaults.IdleHour),
        GoalHour: Hour(get("DAY_GOAL_HOUR"), Defaults.GoalHour));

    private static int Hour(string? raw, int fallback) =>
        int.TryParse(raw, out var v) && v is >= 0 and <= 23 ? v : fallback;

    private static int Count(string? raw, int fallback) =>
        int.TryParse(raw, out var v) && v is >= 1 and <= 100 ? v : fallback;
}
