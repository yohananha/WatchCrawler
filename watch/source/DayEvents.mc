import Toybox.ActivityMonitor;
import Toybox.Application;
import Toybox.Lang;
import Toybox.Time;
import Toybox.Time.Gregorian;

// Once-a-day "roast" events, checked from the background tick after real
// activity detection (at most one fires per tick; each fires at most once a
// day). All are cursed tier. None counts as an achievement: only real
// achievements (see noteAchievement) suppress the "nothing today" roast.
//   no_achievement_today  after IDLE_HOUR, if no achievement was announced today
//   goal_missed           after GOAL_HOUR, if today's steps are under the step goal
//   sedentary             daytime, when the watch's move bar is maxed out
(:background)
class DayEvents {
    static const IDLE_HOUR = 22;
    static const GOAL_HOUR = 21;
    static const SEDENTARY_FROM_HOUR = 9;
    static const SEDENTARY_TO_HOUR = 22;

    private static const LAST_ACHIEVEMENT_KEY = "lastAchievementDay";

    // Call whenever a real achievement is announced.
    static function noteAchievement() as Void {
        Application.Storage.setValue(LAST_ACHIEVEMENT_KEY, today());
    }

    // Returns a core event for the first roast that qualifies right now (and
    // marks it as sent for today), else null.
    static function checkForDayEvent() as Dictionary? {
        var hour = Gregorian.info(Time.now(), Time.FORMAT_SHORT).hour;
        var day = today();
        var info = safeInfo();

        // Sedentary first: it is time-sensitive, the end-of-day ones are not.
        if (hour >= SEDENTARY_FROM_HOUR && hour < SEDENTARY_TO_HOUR && once("sedentary", day)
            && info != null && info.moveBarLevel != null
            && info.moveBarLevel >= ActivityMonitor.MOVE_BAR_LEVEL_MAX) {
            return mark("sedentary", day, buildSedentary(info));
        }
        if (hour >= GOAL_HOUR && once("goal_missed", day)
            && info != null && info.steps != null && info.stepGoal != null
            && info.stepGoal > 0 && info.steps < info.stepGoal) {
            return mark("goal_missed", day, buildGoalMissed(info));
        }
        if (hour >= IDLE_HOUR && once("no_achievement_today", day)
            && Application.Storage.getValue(LAST_ACHIEVEMENT_KEY) != day) {
            return mark("no_achievement_today", day, buildIdle());
        }
        return null;
    }

    static function buildIdle() as Dictionary {
        var steps = stepsNow();
        return base("no_achievement_today", "IDLE", "0 TODAY", { "stepsToday" => steps.toString() });
    }

    static function buildGoalMissed(info as ActivityMonitor.Info?) as Dictionary {
        var steps = 0;
        var goal = 10000;
        if (info != null) {
            if (info.steps != null) { steps = info.steps; }
            if (info.stepGoal != null) { goal = info.stepGoal; }
        }
        return base("goal_missed", "GOAL", steps + "/" + goal,
            { "stepsToday" => steps.toString(), "stepGoal" => goal.toString() });
    }

    static function buildSedentary(info as ActivityMonitor.Info?) as Dictionary {
        return base("sedentary", "SIT", "MOVE BAR MAX",
            { "stepsToday" => stepsNow().toString(), "note" => "move bar at maximum after hours without moving" });
    }

    private static function base(eventType as String, hero as String, stat as String, details as Dictionary) as Dictionary {
        return {
            "eventType" => eventType,
            "hero" => hero,
            "stat" => stat,
            "tier" => "cursed",
            "sound" => "fail",
            "km" => 0.0,
            "durationSec" => 0,
            "pace" => "",
            "baselineMean" => null,
            "baselineStd" => null,
            "details" => details,
            "isComplete" => false,
        };
    }

    private static function safeInfo() as ActivityMonitor.Info? {
        try {
            return ActivityMonitor.getInfo();
        } catch (ex) {
            return null;
        }
    }

    private static function stepsNow() as Number {
        var info = safeInfo();
        return (info != null && info.steps != null) ? info.steps : 0;
    }

    // Has this event NOT been sent yet today?
    private static function once(eventType as String, day as Number) as Boolean {
        return Application.Storage.getValue("lastDay_" + eventType) != day;
    }

    private static function mark(eventType as String, day as Number, core as Dictionary) as Dictionary {
        Application.Storage.setValue("lastDay_" + eventType, day);
        return core;
    }

    // yyyymmdd in local time
    private static function today() as Number {
        var d = Gregorian.info(Time.now(), Time.FORMAT_SHORT);
        return d.year * 10000 + d.month * 100 + d.day;
    }
}
