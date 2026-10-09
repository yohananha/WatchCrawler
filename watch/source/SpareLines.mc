import Toybox.Application;
import Toybox.Lang;

// AI-written lines kept for later: when the server can't write one (phone out
// of range, server down), the watch reuses an earlier AI line before falling
// back to its built-in TextBank. Kept small and bounded: at most MAX lines,
// the oldest dropped when a new one comes in, and each line is used once.
//
// A line was written about one specific event, so it is only reused for the
// same kind (sport/event type) at the same tier, and lines with digits are
// never kept - "10 km in 50 min" would be wrong for any other activity (the
// real numbers are on the stat line anyway).
(:background)
class SpareLines {
    private static const KEY = "spareLines";
    private static const MAX = 10;

    // After a server answer: keep its text for a later offline event of the same kind.
    static function save(core as Dictionary, title as String, text as String, reward as String) as Void {
        if (hasDigit(title) || hasDigit(text) || hasDigit(reward)) {
            return;
        }
        var lines = list();
        lines.add({ "k" => keyFor(core), "t" => title, "x" => text, "r" => reward });
        while (lines.size() > MAX) {
            lines.remove(lines[0]);
        }
        Application.Storage.setValue(KEY, lines);
    }

    // [title, text, reward] of the oldest kept line matching this event, removed so it isn't
    // used twice; null when there is none.
    static function take(core as Dictionary) as [String, String, String]? {
        var lines = list();
        var key = keyFor(core);
        for (var i = 0; i < lines.size(); i++) {
            var l = lines[i];
            if (key.equals(l["k"])) {
                lines.remove(l);
                Application.Storage.setValue(KEY, lines);
                return [l["t"] as String, l["x"] as String, l["r"] as String];
            }
        }
        return null;
    }

    // e.g. "RUN/rare", "LIFT/cursed", "goal_missed/cursed".
    private static function keyFor(core as Dictionary) as String {
        var kind = core.hasKey("eventType") ? core["eventType"] : core["hero"];
        return "" + kind + "/" + core["tier"];
    }

    private static function list() as Array<Dictionary> {
        var stored = Application.Storage.getValue(KEY);
        return stored instanceof Array ? stored as Array<Dictionary> : ([] as Array<Dictionary>);
    }

    private static function hasDigit(s as String) as Boolean {
        var chars = s.toCharArray();
        for (var i = 0; i < chars.size(); i++) {
            var n = chars[i].toNumber();
            if (n >= 48 && n <= 57) {
                return true;
            }
        }
        return false;
    }
}
