import Toybox.Application;
import Toybox.Lang;
import Toybox.Math;

// Port of server/Achievements.cs TierCalculator: same thresholds, same
// z-score formula, so the animation stays consistent whether the text comes
// from the watch's local TextBank (Phase 1) or the LLM server (Phase 2) -
// and so the server independently computes the SAME tier when the watch
// sends it the same baselineMean/Std (see AchievementResolver).
//
// Unlike the server (which is handed a baselineMean/Std with the event),
// the watch keeps its own rolling per-sport history in Storage and computes
// the baseline itself. statsFor()/record() are split (rather than one
// evaluateAndRecord) so Phase 2 can read the mean/std to send to the server
// BEFORE recording the new value into its own baseline.
class Baseline {
    private static const HISTORY_SIZE = 14;
    private static const MIN_SAMPLES = 3;

    private static const ALWAYS_CURSED = ["sedentary", "body_battery_low", "goal_missed"];

    // [mean, std] from the PRIOR rolling history for `sport` (not including
    // any value not yet recorded). [null, null] if there's fewer than 3
    // samples yet - matches the server's "no baseline" behaviour.
    static function statsFor(sport as String) as [Float?, Float?] {
        var history = historyFor(sport);
        if (history.size() < MIN_SAMPLES) {
            return [null, null];
        }
        var mean = meanOf(history);
        var std = stdOf(history, mean);
        return [mean, std];
    }

    // Appends `value` to `sport`'s rolling history (call AFTER statsFor(),
    // so the new value isn't part of its own baseline).
    static function record(sport as String, value as Float) as Void {
        var key = "baseline_" + sport;
        var history = historyFor(sport);
        history.add(value);
        while (history.size() > HISTORY_SIZE) {
            history.remove(history[0]);
        }
        Application.Storage.setValue(key, history);
    }

    // Pure tier calc given an explicit mean/std (same formula the server
    // uses). null mean/std -> :common (no baseline yet).
    static function evaluateWithStats(value as Float, mean as Float?, std as Float?, higherIsBetter as Boolean) as [Symbol, Float?] {
        if (mean == null || std == null || (std as Float) <= 0) {
            return [:common, null];
        }
        var z = (value - (mean as Float)) / (std as Float) * (higherIsBetter ? 1.0 : -1.0);
        var tier = :common;
        if (z <= -1.0) {
            tier = :cursed;
        } else if (z >= 2.0) {
            tier = :legendary;
        } else if (z >= 1.3) {
            tier = :epic;
        } else if (z >= 0.6) {
            tier = :rare;
        }
        return [tier, z];
    }

    // Convenience for the debug injector: evaluate against current history
    // and record the new value in one call.
    static function evaluateAndRecord(sport as String, value as Float, higherIsBetter as Boolean) as [Symbol, Float?] {
        var stats = statsFor(sport);
        var result = evaluateWithStats(value, stats[0], stats[1], higherIsBetter);
        record(sport, value);
        return result;
    }

    static function isAlwaysCursed(eventType as String) as Boolean {
        for (var i = 0; i < ALWAYS_CURSED.size(); i++) {
            if (ALWAYS_CURSED[i].equals(eventType)) {
                return true;
            }
        }
        return false;
    }

    static function soundFor(tier as Symbol) as String {
        if (tier == :cursed) {
            return "fail";
        }
        if (tier == :legendary) {
            return "fanfare_long";
        }
        if (tier == :epic) {
            return "fanfare";
        }
        return "chime";
    }

    // Matches the server's tier.ToString().ToLowerInvariant().
    static function tierName(tier as Symbol) as String {
        if (tier == :cursed) { return "cursed"; }
        if (tier == :rare) { return "rare"; }
        if (tier == :epic) { return "epic"; }
        if (tier == :legendary) { return "legendary"; }
        return "common";
    }

    static function tierFromName(name as String) as Symbol {
        if (name.equals("cursed")) { return :cursed; }
        if (name.equals("rare")) { return :rare; }
        if (name.equals("epic")) { return :epic; }
        if (name.equals("legendary")) { return :legendary; }
        return :common;
    }

    private static function historyFor(sport as String) as Array<Float> {
        var stored = Application.Storage.getValue("baseline_" + sport);
        return stored == null ? ([] as Array<Float>) : (stored as Array<Float>);
    }

    private static function meanOf(arr as Array<Float>) as Float {
        var sum = 0.0;
        for (var i = 0; i < arr.size(); i++) {
            sum += arr[i];
        }
        return sum / arr.size();
    }

    private static function stdOf(arr as Array<Float>, mean as Float) as Float {
        var sumSq = 0.0;
        for (var i = 0; i < arr.size(); i++) {
            var d = arr[i] - mean;
            sumSq += d * d;
        }
        return Math.sqrt(sumSq / arr.size());
    }
}
