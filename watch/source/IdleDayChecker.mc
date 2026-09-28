import Toybox.ActivityMonitor;
import Toybox.Application;
import Toybox.Lang;
import Toybox.Time;
import Toybox.Time.Gregorian;

// "You did nothing today" event: once per day, after IDLE_HOUR local time,
// if no achievement (activity or otherwise) was announced that day, the
// System roasts you for it. Checked from the background tick, after real
// activity detection. Always cursed tier.
(:background)
class IdleDayChecker {
    static const IDLE_HOUR = 22;

    private static const LAST_ACHIEVEMENT_KEY = "lastAchievementDay";
    private static const LAST_IDLE_KEY = "lastIdleDay";

    // Call whenever any achievement is announced.
    static function noteAchievement() as Void {
        Application.Storage.setValue(LAST_ACHIEVEMENT_KEY, today());
    }

    // Returns a core event when today qualifies (and marks it as sent), else null.
    static function checkForIdleDay() as Dictionary? {
        var now = Gregorian.info(Time.now(), Time.FORMAT_SHORT);
        if (now.hour < IDLE_HOUR) {
            return null;
        }
        var day = today();
        if (Application.Storage.getValue(LAST_ACHIEVEMENT_KEY) == day
            || Application.Storage.getValue(LAST_IDLE_KEY) == day) {
            return null;
        }
        Application.Storage.setValue(LAST_IDLE_KEY, day);
        return buildCore();
    }

    static function buildCore() as Dictionary {
        var steps = 0;
        try {
            var info = ActivityMonitor.getInfo();
            if (info != null && info.steps != null) {
                steps = info.steps;
            }
        } catch (ex) {
            // Steps are just flavour for the joke; the event works without.
        }
        return {
            "eventType" => "no_achievement_today",
            "hero" => "IDLE",
            "stat" => "0 TODAY",
            "tier" => "cursed",
            "sound" => "fail",
            "km" => 0.0,
            "durationSec" => 0,
            "pace" => "",
            "baselineMean" => null,
            "baselineStd" => null,
            "steps" => steps,
            "isComplete" => false,
        };
    }

    // yyyymmdd in local time
    private static function today() as Number {
        var d = Gregorian.info(Time.now(), Time.FORMAT_SHORT);
        return d.year * 10000 + d.month * 100 + d.day;
    }
}
