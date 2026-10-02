using System.Globalization;

namespace GarminAchievements;

/// <summary>`dotnet GarminAchievements.dll coupon ...`: manage hosted-mode coupons straight on the database, for
/// when you are already on the machine (`fly ssh console`). The /admin/coupons endpoints do the same over HTTP.</summary>
public static class CouponCli
{
    public static int Run(string[] args, LicenseStore store, TextWriter? output = null)
    {
        var o = output ?? Console.Out;
        var now = DateTimeOffset.UtcNow;
        switch (args.FirstOrDefault()?.ToLowerInvariant())
        {
            case "add":
            {
                if (args.Length < 3 || !int.TryParse(args[2], out var pct))
                {
                    o.WriteLine("Usage: coupon add CODE PERCENT [--uses N] [--days D] [--note TEXT]");
                    return 2;
                }
                var uses = 1;
                int? days = null;
                string? note = null;
                for (var i = 3; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--uses" when i + 1 < args.Length && int.TryParse(args[++i], out var u): uses = u; break;
                        case "--days" when i + 1 < args.Length && int.TryParse(args[++i], out var d): days = d; break;
                        case "--note" when i + 1 < args.Length: note = args[++i]; break;
                        default: o.WriteLine($"Unknown option {args[i]}"); return 2;
                    }
                }
                try
                {
                    var c = store.UpsertCoupon(args[1], pct, uses, days is { } dd ? now.AddDays(dd) : null, note);
                    o.WriteLine($"Saved {Describe(c)}");
                    return 0;
                }
                catch (ArgumentException ex) { o.WriteLine(ex.Message); return 2; }
            }
            case "list":
            {
                var all = store.Coupons();
                if (all.Count == 0) o.WriteLine("No coupons.");
                foreach (var c in all) o.WriteLine(Describe(c) + (c.IsUsable(now) ? "" : "  [not usable]"));
                return 0;
            }
            case "del" or "delete" or "rm":
                if (args.Length < 2) { o.WriteLine("Usage: coupon del CODE"); return 2; }
                o.WriteLine(store.DeleteCoupon(args[1]) ? $"Deleted {args[1].ToUpperInvariant()}" : "No such coupon.");
                return 0;
            default:
                o.WriteLine("Usage: coupon add CODE PERCENT [--uses N] [--days D] [--note TEXT] | coupon list | coupon del CODE");
                return 2;
        }
    }

    private static string Describe(Coupon c) =>
        $"{c.Code}: {c.PercentOff}% off, {c.Uses}/{c.MaxUses} used" +
        (c.ExpiresAt is { } e ? $", expires {e.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" : "") +
        (c.Note is { Length: > 0 } n ? $" ({n})" : "");
}
