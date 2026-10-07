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
(:background)
class TextBank {

    private static const CURSED = [
        ["The Reluctant Shuffle", "$1$ km in $2$. The couch put up a real fight and won.", "Reward withheld. The couch says thanks."],
        ["Effort, Technically", "A $4$ that barely counts as one. Even your watch looked unimpressed.", "One participation shrug."],
        ["The Slow Retreat", "$2$ of $4$ that felt more like a hostage negotiation.", "Nothing. As promised."],
        ["Downhill From Here", "$1$ km at $3$/km. Your usual pace sent a search party.", "A map back to your old form."],
        ["Personal Worst-ish", "$2$ for $1$ km. The clock kept going out of pure politeness.", "Reward delayed, like you."],
        ["Strategic Slowness", "$3$/km. You call it recovery. The System calls it something else.", "One excuse, pre-written."],
        ["Plot Twist: Slower", "$1$ km of $4$, below your usual. The audience gasped, then left.", "Refund on effort: denied."],
    ];

    private static const COMMON = [
        ["Perfectly Average $4$", "$1$ km in $2$. Right on brand for you.", "A shrug of mild acknowledgment."],
        ["Nothing To See Here", "$2$ of $4$, pace $3$/km. The System yawns.", "Unlocked: continued existence."],
        ["The Usual Suspect", "$1$ km, $2$. You again, doing the thing you always do.", "One (1) predictable outcome."],
        ["Statistically Unremarkable", "$1$ km at $3$/km. The spreadsheet did not even flinch.", "One beige ribbon."],
        ["Same Time Next Week", "$2$ of $4$. Exactly what the System predicted. Yawn.", "A rerun, on repeat."],
        ["Dead Center", "$1$ km in $2$. The exact middle of your bell curve.", "Average points, for average things."],
        ["Routine Maintenance", "$4$ done: $1$ km at $3$/km. The engine idles politely.", "A clipboard tick."],
    ];

    private static const RARE = [
        ["A Notch Above Average", "$1$ km in $2$, a bit sharper than usual for a $4$.", "A gold star you cannot spend."],
        ["Mildly Impressive $4$", "Pace $3$/km. Someone's been putting in a little extra.", "Bragging rights, expiring soon."],
        ["Suspiciously Brisk", "$1$ km at $3$/km. Better than usual. The System is checking for wind.", "A sticker. A small one."],
        ["Above Your Pay Grade", "$2$ for $1$ km. You beat your own average. Don't make it weird.", "A raise of one eyebrow."],
        ["Improvement Detected", "Pace $3$/km. The algorithm updated its opinion of you. Slightly.", "Respect +1. Capped."],
        ["Quietly Faster", "$1$ km of $4$ in $2$. Nobody noticed except the System. Annoyingly.", "A whisper of applause."],
    ];

    private static const EPIC = [
        ["Genuinely Showing Off", "$1$ km at $3$/km. Your usual self has some questions.", "One (1) smug feeling. Non-refundable."],
        ["The Overachiever Emerges", "$2$ of $4$, well past your normal effort. Noted, grudgingly.", "Applause. It is a recording."],
        ["Who Ordered This?", "$1$ km at $3$/km. Your usual self would like to see the receipts.", "An audit, scheduled."],
        ["Main Character Energy", "$2$ of $4$, well past normal. The camera finally found you.", "Five seconds of screen time."],
        ["Unexpectedly Competent", "$1$ km in $2$. The System had a joke ready and had to bin it.", "A rare compliment. Do not frame it."],
        ["The Bar Has Moved", "Pace $3$/km. Now we have to expect this from you. Thanks a lot.", "Higher expectations, forever."],
    ];

    private static const LEGENDARY = [
        ["Personal Record, Allegedly", "$1$ km in $2$. We had to check the sensors twice.", "One smug nod. Redeemable never."],
        ["The System Is Impressed", "Pace $3$/km on a $4$. That almost never happens.", "A laurel wreath made of recycled excuses."],
        ["Sensors Under Review", "$1$ km at $3$/km. Legal is checking whether you had a scooter.", "A trophy, pending investigation."],
        ["Rewrite The Record Book", "$2$ for $1$ km of $4$. The old you has been formally retired.", "A statue. Pocket-sized."],
        ["The Crowd Goes Mild", "$1$ km in $2$. Genuinely great. The System will deny saying that.", "A medal made of real tin."],
        ["Off The Charts, Literally", "Pace $3$/km broke the graph. An intern is redrawing the axis.", "Your name, misspelled, on a wall."],
    ];

    private static const IDLE = [
        ["Achievement Recorded: None", "You did nothing today. The System noticed, and so did your watch.", "One empty trophy case."],
        ["A Day Of Pure Potential", "Zero achievements. Potential is doing all the work here.", "Tomorrow, allegedly."],
        ["Horizontal Excellence", "The couch reports a personal best in you not moving.", "A participation trophy for lying down."],
        ["Nothing Ventured", "Zero achievements today. Nothing gained either, for balance.", "One blank certificate."],
        ["The Unapproved Day Off", "Not a single achievement. The System checked under the sofa.", "Tomorrow. Again."],
        ["Quest Log: Empty", "Today's quest log has fewer entries than a brand new notebook.", "A bookmark for tomorrow."],
    ];

    private static const GOAL_MISSED = [
        ["Goal: Politely Declined", "The step goal was right there. You let it stay there.", "A bronze medal for staying put."],
        ["So Close, Yet Seated", "Your step goal waited all day. It is not angry, just disappointed.", "One rain check. Expired."],
        ["Steps: An Optional Extra", "You missed your step goal. The floor barely noticed you.", "Tomorrow's goal, unchanged and judging you."],
        ["Almost Is A Strong Word", "The step goal got away. It did not even run. It walked.", "Directions to the door."],
        ["Goal Left On Read", "Your step goal sent reminders all day. You left it on read.", "One unread notification."],
        ["Steps Under Budget", "You finished under your step goal. Accounting is not impressed.", "A frugal pat on the back."],
    ];

    private static const SEDENTARY = [
        ["Professional Sitter", "Your move bar is maxed out. The chair has filed a joint tenancy request.", "One cushion. Non-transferable."],
        ["Stationary Excellence", "Hours of stillness. Statues everywhere are taking notes.", "A standing ovation, from others."],
        ["Motion Not Found", "Your watch begs you to move. You scrolled past it.", "A pat on the back. Please stand to receive."],
        ["Furniture Mode Engaged", "Still for so long that the dust started taking notes.", "A free dusting."],
        ["Glued To The Seat", "The move bar is full and so is the chair's patience.", "A reminder that legs exist."],
        ["Low Power Mode", "No motion detected for ages. Even the screensaver is worried.", "One stretch, highly recommended."],
    ];

    private static const STEPS_GOAL = [
        ["Steps: Completed, Somehow", "You hit your step goal. The System checked twice and is suspicious.", "A tiny imaginary parade."],
        ["Walked Enough, Allegedly", "Step goal reached. Do not let it go to your head, it is only walking.", "One gold star. Wipe clean after use."],
        ["Quota Met, Barely Noticed", "Step goal done. The System will pretend it was always confident.", "One lukewarm high five."],
        ["Feet: Accounted For", "Daily steps achieved. Your shoes are demanding overtime pay.", "A shoe-shaped sticker."],
        ["Bare Minimum, Achieved", "You reached the goal you set yourself. Well done on the homework.", "A participation step."],
        ["Walk Of Fame-ish", "Step goal reached. No paparazzi, but the watch noticed.", "A star on a very short pavement."],
    ];

    private static const FLOORS_GOAL = [
        ["Stairs: Conquered", "Your floors goal is done. The elevator feels personally betrayed.", "A very small summit flag."],
        ["Up, For Once", "You climbed your floors goal. Gravity is reviewing the footage.", "One handrail salute."],
        ["Stairway To Average", "Floors goal done. The escalator lobby is drafting a statement.", "A step stool of honour."],
        ["Vertical Progress", "You went up enough floors today. Down doesn't count, we checked.", "One elevator token, unused."],
        ["Altitude Adjusted", "Floors goal complete. Your knees have been informed.", "A tiny summit selfie."],
    ];

    private static const BATTERY_LOW = [
        ["Running On Fumes", "Body Battery is nearly empty. The System suggests a nap, not a lecture.", "One imaginary charging cable."],
        ["Critical Battery, Human Edition", "You are at the bottom of the bar. Even the watch is worried.", "A sympathetic beep."],
        ["Low Charge Warning", "Body Battery is near zero. Heroics can wait until tomorrow.", "One power nap voucher."],
        ["Energy Not Found", "The tank is empty. Even the sarcasm is running on reserve.", "A blanket. Use it."],
        ["Recharge Required", "Body Battery hit the floor. Time to plug yourself in, figuratively.", "A pillow, fully charged."],
    ];

    private static const RHR_GOOD = [
        ["Heart: Suspiciously Calm", "Resting heart rate is better than usual. Who are you and what did you do with the couch potato?", "One (1) unearned smugness."],
        ["Calm Under Pressure", "Resting heart rate below your usual. Zen, or just sleepy?", "A meditation bell. Silent."],
        ["Steady As A Metronome", "Your resting pulse is lower than normal. Smugness permitted, briefly.", "One deep breath."],
        ["Chill Mode Unlocked", "Resting heart rate improved. Your heart is coasting. Show-off.", "A lounge chair for the heart."],
    ];

    private static const RHR_BAD = [
        ["Heart: Working Overtime", "Resting heart rate is up on your usual. The System is judging last night.", "A glass of water, unspoken."],
        ["Pulse: Slightly Dramatic", "Resting heart rate is higher than usual. Your heart has opinions today.", "A cup of tea, imagined."],
        ["Restless Resting", "Your resting pulse is up on normal. Something kept it busy.", "One quiet evening."],
        ["Idle Speed Elevated", "Resting heart rate is above your usual. The engine idles a bit high.", "A slower playlist."],
    ];

    // Local fallback for the non-activity events.
    static function pickEvent(eventType as String, tierName as String) as [String, String, String] {
        var bank = IDLE;
        var key = eventType;
        if (eventType.equals("goal_missed")) { bank = GOAL_MISSED; }
        else if (eventType.equals("sedentary")) { bank = SEDENTARY; }
        else if (eventType.equals("steps_goal")) { bank = STEPS_GOAL; }
        else if (eventType.equals("floors_goal")) { bank = FLOORS_GOAL; }
        else if (eventType.equals("body_battery_low")) { bank = BATTERY_LOW; }
        else if (eventType.equals("resting_hr")) {
            var bad = tierName.equals("cursed");
            bank = bad ? RHR_BAD : RHR_GOOD;
            key = bad ? "resting_hr_bad" : "resting_hr_good";
        }
        var e = bank[pickIndex(bank.size(), "textbank_recent_e_" + key)];
        return [e[0], e[1], e[2]];
    }

    // No-distance sessions (strength, HIIT, yoga, ...): $2$ = time, $4$ = sport word.
    private static const WORKOUT = {
        :cursed => [
            ["Technically Showed Up", "$2$ of $4$, if you squint. The mat has seen more effort.", "One participation sigh."],
            ["Short And Not Sweet", "$2$ of $4$. The warm-up was the main event.", "Half a participation ribbon."],
            ["Effort Pending", "$2$ of $4$, below your usual. The mat filed a boredom complaint.", "A nap, pre-approved."],
            ["Brief Encounter", "$2$ of $4$. Blink and the System missed it. It did.", "Nothing. Briefly."],
        ],
        :common => [
            ["A Workout Occurred", "$2$ of $4$. The System notes it happened.", "A polite nod."],
            ["Session Logged", "$2$ of $4$. Filed under things that happen on weekdays.", "One standard-issue nod."],
            ["Business As Usual", "$2$ of $4$, right on your average. The System yawns in sync.", "An average gold star. It's grey."],
            ["Reps Were Had", "$2$ of $4$. Nothing broke, nothing improved. Balance.", "A sticker that says 'fine'."],
        ],
        :rare => [
            ["Actual Effort Detected", "$2$ of $4$, better than your usual. Suspicious.", "One gold star, non-transferable."],
            ["A Little Extra", "$2$ of $4$, more than usual. The System raised one eyebrow.", "A small towel of honour."],
            ["Overtime, Voluntary", "$2$ of $4$. Nobody asked for that much. Noted anyway.", "One unpaid bonus."],
            ["Above The Usual", "$2$ of $4$ and you kept going. Suspicious, but logged.", "A gold star with fine print."],
        ],
        :epic => [
            ["Suspiciously Committed", "$2$ of $4$, well beyond normal. Your usual self is worried.", "A round of imaginary applause."],
            ["Who Is This Person?", "$2$ of $4$, way past normal. Your couch filed a missing report.", "A medal, slightly sweaty."],
            ["Grinding, Apparently", "$2$ of $4$. The System had to scroll to see it all.", "A longer progress bar."],
            ["Endurance Unlocked", "$2$ of $4$. The playlist ran out before you did.", "One extra song. Imaginary."],
        ],
        :legendary => [
            ["Beast Mode, Allegedly", "$2$ of $4$. We checked the sensors twice.", "One smug nod. Redeemable never."],
            ["Clock Defeated", "$2$ of $4$. The timer asked for a break. You said no.", "A crown made of resistance bands."],
            ["Session Of Legend", "$2$ of $4$. Bards will sing of this. Badly.", "One ballad, off-key."],
            ["Record Set, Reluctantly", "$2$ of $4$. The System checked twice, then sat down.", "A plaque. Assembly required."],
        ],
    };

    static function pickWorkout(tier as Symbol, durationSec as Number, sport as String) as [String, String, String] {
        var t = WORKOUT.hasKey(tier) ? tier : :common;
        var b = WORKOUT[t] as Array<Array<String> >;
        var entry = b[pickIndex(b.size(), "textbank_recent_w_" + Baseline.tierName(t))];
        var args = ["", StatLine.formatDuration(durationSec), "", sport];
        return [Lang.format(entry[0], args), Lang.format(entry[1], args), Lang.format(entry[2], args)];
    }

    // Returns [title, text, reward] for the given tier, formatted with the
    // supplied numbers/sport.
    static function pick(tier as Symbol, km as Float, durationSec as Number, paceStr as String, sport as String) as [String, String, String] {
        var bank = bankFor(tier);
        var entry = bank[pickIndex(bank.size(), "textbank_recent_" + Baseline.tierName(tier))];
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
