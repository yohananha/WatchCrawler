import Toybox.ActivityMonitor;
import Toybox.Application;
import Toybox.Lang;
import Toybox.SensorHistory;
import Toybox.Time;
import Toybox.Time.Gregorian;
import Toybox.UserProfile;

// Non-activity events, checked from the background tick after real activity
// detection. At most one fires per tick and each type at most once a day.
// All are gated by quiet hours and a daily cap (every announced achievement
// counts toward the cap, see noteSent). Only REAL achievements (activities,
// goals reached) suppress the "nothing today" roast - see noteAchievement.
//
//   steps_goal / floors_goal   reached today (rare tier, good news)
//   resting_hr                 morning, vs your own 14-day baseline; only when
//                              it is notably better or worse than usual
//   body_battery_low           Body Battery under BATTERY_LOW (cursed)
//   sedentary                  move bar maxed out during the day (cursed)
//   goal_missed                after GOAL_HOUR, steps under the goal (cursed)
//   no_achievement_today       after IDLE_HOUR, nothing announced today (cursed)
(:background)
class DayEvents {
    // Tunable from the server (Fly env vars, delivered with each trigger poll and
    // cached in Storage by TriggerChecker); these are the defaults until the first poll.
    private static const DEFAULTS = { "idleHour" => 22, "goalHour" => 21, "quietFrom" => 23, "quietTo" => 7, "maxPerDay" => 10 };
    static const SEDENTARY_FROM_HOUR = 9;
    static const SEDENTARY_TO_HOUR = 22;
    static const BATTERY_LOW = 15;
    static const RHR_FROM_HOUR = 8;

    // Quiet hours (default 23:00-06:59): no day-events; activities you actually do are exempt.

    private static const LAST_ACHIEVEMENT_KEY = "lastAchievementDay";
    private static const CAP_DAY_KEY = "capDay";
    private static const CAP_COUNT_KEY = "capCount";

    // Call whenever a REAL achievement (not a roast) is announced.
    static function noteAchievement() as Void {
        Application.Storage.setValue(LAST_ACHIEVEMENT_KEY, today());
    }

    // Call for EVERY announced achievement/roast: counts toward the daily cap.
    static function noteSent() as Void {
        var day = today();
        var n = Application.Storage.getValue(CAP_DAY_KEY) == day ? Application.Storage.getValue(CAP_COUNT_KEY) : 0;
        Application.Storage.setValue(CAP_DAY_KEY, day);
        Application.Storage.setValue(CAP_COUNT_KEY, (n == null ? 0 : n as Number) + 1);
    }

    // One tunable value: server-provided (Storage "dayCfg") or the default.
    private static function cfg(key as String) as Number {
        var saved = Application.Storage.getValue("dayCfg");
        if (saved instanceof Dictionary && saved.hasKey(key) && saved[key] instanceof Number) {
            return saved[key] as Number;
        }
        return DEFAULTS[key] as Number;
    }

    static function isQuiet(hour as Number) as Boolean {
        var from = cfg("quietFrom");
        var to = cfg("quietTo");
        // from > to wraps midnight (23 -> 7); from <= to is a same-day window.
        return from > to ? (hour >= from || hour < to) : (hour >= from && hour < to);
    }

    static function capReached(day as Number) as Boolean {
        if (Application.Storage.getValue(CAP_DAY_KEY) != day) {
            return false;
        }
        var n = Application.Storage.getValue(CAP_COUNT_KEY);
        return n != null && (n as Number) >= cfg("maxPerDay");
    }

    // Returns a core event for the first event that qualifies right now (and
    // marks it as sent for today), else null.
    static function checkForDayEvent() as Dictionary? {
        var hour = Gregorian.info(Time.now(), Time.FORMAT_SHORT).hour;
        var day = today();
        if (isQuiet(hour) || capReached(day)) {
            return null;
        }
        var info = safeInfo();

        var core = stepsGoalReached(day, info);
        if (core == null) { core = floorsGoalReached(day, info); }
        if (core == null) { core = restingHr(hour, day); }
        if (core == null) { core = batteryLow(hour, day); }
        if (core == null) { core = sedentary(hour, day, info); }
        if (core == null) { core = goalMissed(hour, day, info); }
        if (core == null) { core = idleDay(hour, day); }
        return core;
    }

    // ---- individual checks -------------------------------------------------

    private static function stepsGoalReached(day as Number, info as ActivityMonitor.Info?) as Dictionary? {
        if (!once("steps_goal", day) || info == null || info.steps == null || info.stepGoal == null
            || info.stepGoal <= 0 || info.steps < info.stepGoal) {
            return null;
        }
        noteAchievement(); // a real win: no "nothing today" roast later
        return mark("steps_goal", day, buildStepsGoal(info));
    }

    private static function floorsGoalReached(day as Number, info as ActivityMonitor.Info?) as Dictionary? {
        if (!once("floors_goal", day) || info == null || !(info has :floorsClimbed)
            || info.floorsClimbed == null || info.floorsClimbedGoal == null
            || info.floorsClimbedGoal <= 0 || info.floorsClimbed < info.floorsClimbedGoal) {
            return null;
        }
        noteAchievement();
        return mark("floors_goal", day, buildFloorsGoal(info));
    }

    private static function restingHr(hour as Number, day as Number) as Dictionary? {
        if (hour < RHR_FROM_HOUR || !once("resting_hr", day)) {
            return null;
        }
        var rhr = null;
        try {
            rhr = UserProfile.getProfile().restingHeartRate;
        } catch (ex) {
            return null;
        }
        if (rhr == null) {
            return null;
        }
        Application.Storage.setValue("lastDay_resting_hr", day);
        var value = (rhr as Number).toFloat();
        var stats = Baseline.statsFor("rhr");
        var result = Baseline.evaluateWithStats(value, stats[0], stats[1], false);
        Baseline.record("rhr", value); // record every day, announce only the notable ones
        if (result[0] == :common) {
            return null;
        }
        var core = base("resting_hr", "BPM", rhr + " BPM", result[0], { "restingHr" => rhr.toString() });
        core.put("value", value);
        core.put("unit", "bpm");
        core.put("baselineMean", stats[0]);
        core.put("baselineStd", stats[1]);
        core.put("higherIsBetter", false);
        return core;
    }

    private static function batteryLow(hour as Number, day as Number) as Dictionary? {
        if (hour < SEDENTARY_FROM_HOUR || hour >= SEDENTARY_TO_HOUR || !once("body_battery_low", day)) {
            return null;
        }
        var level = bodyBattery();
        if (level == null || (level as Number) >= BATTERY_LOW) {
            return null;
        }
        return mark("body_battery_low", day, buildBatteryLow(level as Number));
    }

    private static function sedentary(hour as Number, day as Number, info as ActivityMonitor.Info?) as Dictionary? {
        if (hour < SEDENTARY_FROM_HOUR || hour >= SEDENTARY_TO_HOUR || !once("sedentary", day)
            || info == null || info.moveBarLevel == null
            || info.moveBarLevel < ActivityMonitor.MOVE_BAR_LEVEL_MAX) {
            return null;
        }
        return mark("sedentary", day, buildSedentary());
    }

    private static function goalMissed(hour as Number, day as Number, info as ActivityMonitor.Info?) as Dictionary? {
        if (hour < cfg("goalHour") || !once("goal_missed", day) || info == null || info.steps == null
            || info.stepGoal == null || info.stepGoal <= 0 || info.steps >= info.stepGoal) {
            return null;
        }
        return mark("goal_missed", day, buildGoalMissed(info));
    }

    private static function idleDay(hour as Number, day as Number) as Dictionary? {
        if (hour < cfg("idleHour") || !once("no_achievement_today", day)
            || Application.Storage.getValue(LAST_ACHIEVEMENT_KEY) == day) {
            return null;
        }
        return mark("no_achievement_today", day, buildIdle());
    }

    // ---- builders (also used by the debug injector, with info == null) -----

    static function buildIdle() as Dictionary {
        return base("no_achievement_today", "IDLE", "0 TODAY", :cursed, { "stepsToday" => stepsNow().toString() });
    }

    static function buildGoalMissed(info as ActivityMonitor.Info?) as Dictionary {
        var steps = 0;
        var goal = 10000;
        if (info != null) {
            if (info.steps != null) { steps = info.steps; }
            if (info.stepGoal != null) { goal = info.stepGoal; }
        }
        return base("goal_missed", "GOAL", steps + "/" + goal, :cursed,
            { "stepsToday" => steps.toString(), "stepGoal" => goal.toString() });
    }

    static function buildStepsGoal(info as ActivityMonitor.Info?) as Dictionary {
        var steps = 10000;
        var goal = 10000;
        if (info != null) {
            if (info.steps != null) { steps = info.steps; }
            if (info.stepGoal != null) { goal = info.stepGoal; }
        }
        return base("steps_goal", "STEPS", steps + " STEPS", :rare,
            { "stepsToday" => steps.toString(), "stepGoal" => goal.toString() });
    }

    static function buildFloorsGoal(info as ActivityMonitor.Info?) as Dictionary {
        var floors = 10;
        var goal = 10;
        if (info != null && info has :floorsClimbed) {
            if (info.floorsClimbed != null) { floors = info.floorsClimbed; }
            if (info.floorsClimbedGoal != null) { goal = info.floorsClimbedGoal; }
        }
        return base("floors_goal", "FLOOR", floors + " FLOORS", :rare,
            { "floorsToday" => floors.toString(), "floorsGoal" => goal.toString() });
    }

    static function buildBatteryLow(level as Number?) as Dictionary {
        var lv = level == null ? 8 : level;
        return base("body_battery_low", "BATT", lv + "% BATTERY", :cursed, { "bodyBattery" => lv.toString() });
    }

    static function buildSedentary() as Dictionary {
        return base("sedentary", "SIT", "MOVE BAR MAX", :cursed,
            { "stepsToday" => stepsNow().toString(), "note" => "move bar at maximum after hours without moving" });
    }

    // Debug-only: a resting-HR event that is clearly better than usual.
    static function buildRestingHrDemo() as Dictionary {
        var core = base("resting_hr", "BPM", "48 BPM", :epic, { "restingHr" => "48" });
        core.put("value", 48.0f);
        core.put("unit", "bpm");
        core.put("baselineMean", 55.0f);
        core.put("baselineStd", 4.0f);
        core.put("higherIsBetter", false);
        return core;
    }

    private static function base(eventType as String, hero as String, stat as String, tier as Symbol, details as Dictionary) as Dictionary {
        return {
            "eventType" => eventType,
            "hero" => hero,
            "stat" => stat,
            "tier" => Baseline.tierName(tier),
            "sound" => Baseline.soundFor(tier),
            "km" => 0.0,
            "durationSec" => 0,
            "pace" => "",
            "baselineMean" => null,
            "baselineStd" => null,
            "details" => details,
            "isComplete" => false,
        };
    }

    // ---- helpers -----------------------------------------------------------

    private static function bodyBattery() as Number? {
        try {
            if (!(Toybox has :SensorHistory) || !(SensorHistory has :getBodyBatteryHistory)) {
                return null;
            }
            var it = SensorHistory.getBodyBatteryHistory({ :period => 1, :order => SensorHistory.ORDER_NEWEST_FIRST });
            var sample = it == null ? null : it.next();
            if (sample == null || sample.data == null) {
                return null;
            }
            return (sample.data as Float).toNumber();
        } catch (ex) {
            return null;
        }
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
