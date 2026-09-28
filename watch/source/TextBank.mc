import Toybox.Application;
import Toybox.Lang;
import Toybox.Math;
import Toybox.Time;

// Local, fully offline fallback text (Phase 1 - Phase 2 swaps this for the
// LLM server, keeping the same [title, text, reward] shape and the same
// character limits and tone rules as server/Achievements.cs PromptBuilder:
// original lines only, roast the effort not the body, sarcastic game-show
// host. $1$=km, $2$=time (M:SS or H:MM:SS), $3$=pace (M:SS/km), $4$=sport.
//
// Limits match the server: title<=40, text<=120, reward<=70 chars.
(:background)
class TextBank {

    private static const CURSED = [
        ["The Reluctant Shuffle", "$1$ km in $2$. The couch put up a real fight and won.", "Reward withheld. The couch says thanks."],
        ["Effort, Technically", "A $4$ that barely counts as one. Even your watch looked unimpressed.", "One participation shrug."],
        ["The Slow Retreat", "$2$ of $4$ that felt more like a hostage negotiation.", "Nothing. As promised."],
    ];

    private static const COMMON = [
        ["Perfectly Average $4$", "$1$ km in $2$. Right on brand for you.", "A shrug of mild acknowledgment."],
        ["Nothing To See Here", "$2$ of $4$, pace $3$/km. The System yawns.", "Unlocked: continued existence."],
        ["The Usual Suspect", "$1$ km, $2$. You again, doing the thing you always do.", "One (1) predictable outcome."],
    ];

    private static const RARE = [
        ["A Notch Above Average", "$1$ km in $2$, a bit sharper than usual for a $4$.", "A gold star you cannot spend."],
        ["Mildly Impressive $4$", "Pace $3$/km. Someone's been putting in a little extra.", "Bragging rights, expiring soon."],
    ];

    private static const EPIC = [
        ["Genuinely Showing Off", "$1$ km at $3$/km. Your usual self has some questions.", "One (1) smug feeling. Non-refundable."],
        ["The Overachiever Emerges", "$2$ of $4$, well past your normal effort. Noted, grudgingly.", "Applause. It is a recording."],
    ];

    private static const LEGENDARY = [
        ["Personal Record, Allegedly", "$1$ km in $2$. We had to check the sensors twice.", "One smug nod. Redeemable never."],
        ["The System Is Impressed", "Pace $3$/km on a $4$. That almost never happens.", "A laurel wreath made of recycled excuses."],
    ];

    private static const IDLE = [
        ["Achievement Recorded: None", "You did nothing today. The System noticed, and so did your watch.", "One empty trophy case."],
        ["A Day Of Pure Potential", "Zero achievements. Potential is doing all the work here.", "Tomorrow, allegedly."],
        ["Horizontal Excellence", "The couch reports a personal best in you not moving.", "A participation trophy for lying down."],
    ];

    // Local fallback for the "no achievement today" event.
    static function pickIdle() as [String, String, String] {
        var day = Time.now().value() / 86400;
        var e = IDLE[day % IDLE.size()];
        return [e[0], e[1], e[2]];
    }

    // Returns [title, text, reward] for the given tier, formatted with the
    // supplied numbers/sport.
    static function pick(tier as Symbol, km as Float, durationSec as Number, paceStr as String, sport as String) as [String, String, String] {
        var bank = bankFor(tier);
        var historyKey = "textbank_recent_" + Baseline.tierName(tier);
        var storedRecent = Application.Storage.getValue(historyKey);
        var recent = storedRecent == null ? ([] as Array<Number>) : (storedRecent as Array<Number>);

        var idx = pickIndex(bank.size(), recent);
        var entry = bank[idx];

        recent.add(idx);
        while (recent.size() > 5) {
            recent.remove(recent[0]);
        }
        Application.Storage.setValue(historyKey, recent);

        var args = [StatLine.formatKm(km), StatLine.formatDuration(durationSec), paceStr, sport];
        return [
            Lang.format(entry[0], args),
            Lang.format(entry[1], args),
            Lang.format(entry[2], args),
        ];
    }

    private static function bankFor(tier as Symbol) as Array<Array<String> > {
        if (tier == :cursed) { return CURSED; }
        if (tier == :rare) { return RARE; }
        if (tier == :epic) { return EPIC; }
        if (tier == :legendary) { return LEGENDARY; }
        return COMMON;
    }

    private static function pickIndex(size as Number, recent as Array<Number>) as Number {
        if (size <= 1) {
            return 0;
        }
        for (var attempt = 0; attempt < 8; attempt++) {
            var idx = Math.rand() % size;
            if (!contains(recent, idx)) {
                return idx;
            }
        }
        return Math.rand() % size;
    }

    private static function contains(arr as Array<Number>, value as Number) as Boolean {
        for (var i = 0; i < arr.size(); i++) {
            if (arr[i] == value) {
                return true;
            }
        }
        return false;
    }
}
