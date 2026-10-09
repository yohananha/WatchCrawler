import Toybox.Application;
import Toybox.Lang;
import Toybox.Math;

// Local, fully offline fallback text (Phase 1 - Phase 2 swaps this for the
// LLM server, keeping the same [title, text, reward] shape and the same
// character limits and tone rules as server/Achievements.cs PromptBuilder:
// original lines only, roast the effort not the body, sarcastic game-show
// host. $1$=km, $2$=time (M:SS or H:MM:SS), $3$=pace (M:SS/km), $4$=sport.
//
// Limits match the server: title<=40, text<=120, reward<=70 chars.
//
// These are all a watch gets without the AI (no server, wrong key, trial
// over), so every bank avoids its own recent picks - see pickIndex.
//
// The lines themselves live in resources/textbank/textbank.xml, one JSON
// resource per bank, and only the bank an event needs is loaded. As code
// constants all of them sat in the 64 KB background process and were
// allocated together on first use, the one path (no AI text) that touched
// them - which is where the background job kept dying.
(:background)
class TextBank {

    // Local fallback for the non-activity events.
    static function pickEvent(eventType as String, tierName as String) as [String, String, String] {
        var res = Rez.JsonData.tbIdle;
        var key = eventType;
        if (eventType.equals("goal_missed")) { res = Rez.JsonData.tbGoalMissed; }
        else if (eventType.equals("sedentary")) { res = Rez.JsonData.tbSedentary; }
        else if (eventType.equals("steps_goal")) { res = Rez.JsonData.tbStepsGoal; }
        else if (eventType.equals("floors_goal")) { res = Rez.JsonData.tbFloorsGoal; }
        else if (eventType.equals("body_battery_low")) { res = Rez.JsonData.tbBatteryLow; }
        else if (eventType.equals("resting_hr")) {
            var bad = tierName.equals("cursed");
            res = bad ? Rez.JsonData.tbRhrBad : Rez.JsonData.tbRhrGood;
            key = bad ? "resting_hr_bad" : "resting_hr_good";
        }
        var bank = load(res);
        var e = bank[pickIndex(bank.size(), "textbank_recent_e_" + key)];
        return [e[0], e[1], e[2]];
    }

    // No-distance sessions (strength, HIIT, yoga, ...): $2$ = time, $4$ = sport word.
    static function pickWorkout(tier as Symbol, durationSec as Number, sport as String) as [String, String, String] {
        var res = Rez.JsonData.tbWorkoutCommon;
        if (tier == :cursed) { res = Rez.JsonData.tbWorkoutCursed; }
        else if (tier == :rare) { res = Rez.JsonData.tbWorkoutRare; }
        else if (tier == :epic) { res = Rez.JsonData.tbWorkoutEpic; }
        else if (tier == :legendary) { res = Rez.JsonData.tbWorkoutLegendary; }
        var b = load(res);
        var entry = b[pickIndex(b.size(), "textbank_recent_w_" + Baseline.tierName(tier))];
        var args = ["", StatLine.formatDuration(durationSec), "", sport];
        return [Lang.format(entry[0], args), Lang.format(entry[1], args), Lang.format(entry[2], args)];
    }

    // Returns [title, text, reward] for the given tier, formatted with the
    // supplied numbers/sport.
    static function pick(tier as Symbol, km as Float, durationSec as Number, paceStr as String, sport as String) as [String, String, String] {
        var bank = load(bankFor(tier));
        var entry = bank[pickIndex(bank.size(), "textbank_recent_" + Baseline.tierName(tier))];
        var args = [StatLine.formatKm(km), StatLine.formatDuration(durationSec), paceStr, sport];
        return [
            Lang.format(entry[0], args),
            Lang.format(entry[1], args),
            Lang.format(entry[2], args),
        ];
    }

    private static function bankFor(tier as Symbol) as ResourceId {
        if (tier == :cursed) { return Rez.JsonData.tbCursed; }
        if (tier == :rare) { return Rez.JsonData.tbRare; }
        if (tier == :epic) { return Rez.JsonData.tbEpic; }
        if (tier == :legendary) { return Rez.JsonData.tbLegendary; }
        return Rez.JsonData.tbCommon;
    }

    private static function load(res as ResourceId) as Array<Array<String> > {
        return Application.loadResource(res) as Array<Array<String> >;
    }

    // A random index that is not among this bank's last ~2/3 picks (kept in
    // Storage under historyKey). The window must stay smaller than the bank,
    // or every index counts as recent and the choice is plain random again.
    private static function pickIndex(size as Number, historyKey as String) as Number {
        if (size <= 1) {
            return 0;
        }
        var stored = Application.Storage.getValue(historyKey);
        var recent = stored instanceof Array ? stored as Array<Number> : ([] as Array<Number>);
        var window = size * 2 / 3;
        if (window < 1) {
            window = 1;
        }
        while (recent.size() > window) {
            recent = recent.slice(1, null);
        }

        var free = [] as Array<Number>;
        for (var i = 0; i < size; i++) {
            if (recent.indexOf(i) < 0) {
                free.add(i);
            }
        }
        var idx = free[(Math.rand() & 0x7FFFFFFF) % free.size()];

        recent.add(idx);
        if (recent.size() > window) {
            recent = recent.slice(1, null);
        }
        Application.Storage.setValue(historyKey, recent);
        return idx;
    }
}
