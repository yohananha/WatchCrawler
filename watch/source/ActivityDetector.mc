import Toybox.Activity;
import Toybox.Application;
import Toybox.Lang;
import Toybox.System;
import Toybox.Time;
import Toybox.UserProfile;

// Real Phase 1/2 background detection: compares the newest completed
// activity against the last one already announced. On a new one, computes
// tier/stat locally and returns a "core" event - hero/stat/tier/sound plus
// the raw numbers (km/durationSec/baselineMean/baselineStd) needed to ask
// the Phase 2 server for title/text/reward. It does NOT fill in title/text/
// reward itself anymore: that's AchievementResolver's job (server, with a
// local TextBank fallback), since resolving now involves an async network
// call. See BackgroundService for how the two are wired together.
(:background)
class ActivityDetector {

    static function detectCoreEvent() as Dictionary? {
        if (!(Toybox.UserProfile has :getUserActivityHistory)) {
            return null;
        }

        var newest = nextActivityOrNull();
        if (newest == null || newest.startTime == null) {
            return null;
        }

        var newestEpoch = newest.startTime.value();
        var lastSeen = Application.Storage.getValue("lastSeenActivityStart");

        // First ever check: remember the newest activity without announcing
        // it, so history from before the app was installed never surfaces
        // as an achievement. This only runs from the background timer (see
        // AchievementApp.onStart for why it's not called at app startup).
        if (lastSeen == null) {
            Application.Storage.setValue("lastSeenActivityStart", newestEpoch);
            return null;
        }

        if (newestEpoch <= (lastSeen as Number)) {
            return null; // Nothing new.
        }

        Application.Storage.setValue("lastSeenActivityStart", newestEpoch);
        return buildCore(newest.type, newest.distance, newest.duration);
    }

    // getUserActivityHistory()/next() are documented as non-null but the SDK
    // has a known bug (Garmin forum: "UserProfile.getUserActivityHistory()
    // can return null") where a device/simulator with zero activity FIT
    // files throws "Illegal Access (Out of Bounds)" instead. Treat any
    // failure here the same as "no activity yet".
    private static function nextActivityOrNull() as UserProfile.UserActivity? {
        if (!(Toybox.UserProfile has :getUserActivityHistory)) {
            return null;
        }
        try {
            var iterator = UserProfile.getUserActivityHistory();
            if (iterator == null) {
                return null;
            }
            return iterator.next();
        } catch (ex) {
            System.println("[ACTDET] getUserActivityHistory failed: " + ex.getErrorMessage());
            return null;
        }
    }

    private static function buildCore(sport as Activity.Sport?, distanceM as Number?, duration as Time.Duration?) as Dictionary {
        var sportName = sportNameFor(sport);
        var distance = distanceM == null ? 0 : distanceM;
        var durationSec = duration == null ? 0 : duration.value();
        var km = distance / 1000.0;

        var stats = Baseline.statsFor(sportName); // read BEFORE recording
        var evalResult = Baseline.evaluateWithStats(km, stats[0], stats[1], true);
        var tier = evalResult[0] as Symbol;
        Baseline.record(sportName, km);

        return coreDict(sportName, distance, durationSec, km, tier, stats[0], stats[1]);
    }

    // Debug-only, fully offline (no Baseline history, no server round trip):
    // :test is a hardcoded joke achievement for an instant sanity check of
    // the notify->queue->view->sound pipeline. The five real tiers force a
    // duration that lands roughly on that tier, then resolve text via
    // AchievementResolver same as a real detection would (so this also
    // exercises the Phase 2 server path when a server URL is configured).
    static function buildFakeCore(tier as Symbol) as Dictionary {
        if (tier == :test) {
            return {
                "hero" => "TEST",
                "title" => "Token Wasted Successfully",
                "stat" => "0 REGRETS",
                "text" => "It worked. Good for you. Have a cookie.",
                "reward" => "Nothing of value. As intended.",
                "tier" => "common",
                "sound" => "chime",
                "isComplete" => true, // tells the resolver to skip the server/TextBank entirely
            };
        }

        if (tier == :idle) {
            return DayEvents.buildIdle();
        }
        if (tier == :goal) {
            return DayEvents.buildGoalMissed(null);
        }
        if (tier == :sit) {
            return DayEvents.buildSedentary();
        }
        if (tier == :steps) {
            return DayEvents.buildStepsGoal(null);
        }
        if (tier == :floors) {
            return DayEvents.buildFloorsGoal(null);
        }
        if (tier == :batt) {
            return DayEvents.buildBatteryLow(null);
        }
        if (tier == :rhr) {
            return DayEvents.buildRestingHrDemo();
        }

        var durationSec = 1500; // ~common, 5km @ 5:00/km
        if (tier == :cursed) { durationSec = 2100; }
        else if (tier == :rare) { durationSec = 1400; }
        else if (tier == :epic) { durationSec = 1300; }
        else if (tier == :legendary) { durationSec = 1150; }

        var km = 5.0;
        return coreDict("RUN", 5000, durationSec, km, tier, null, null);
    }

    private static function coreDict(sportName as String, distanceM as Number, durationSec as Number, km as Float,
                                      tier as Symbol, mean as Float?, std as Float?) as Dictionary {
        return {
            "hero" => sportName,
            "stat" => StatLine.forActivity(distanceM, durationSec),
            "tier" => Baseline.tierName(tier),
            "sound" => Baseline.soundFor(tier),
            "km" => km,
            "durationSec" => durationSec,
            "pace" => StatLine.formatPace(km, durationSec),
            "baselineMean" => mean,
            "baselineStd" => std,
            "isComplete" => false,
        };
    }

    private static function sportNameFor(sport as Activity.Sport?) as String {
        if (sport == null) { return "ACTIVITY"; }
        if (sport == Activity.SPORT_RUNNING) { return "RUN"; }
        if (sport == Activity.SPORT_WALKING) { return "WALK"; }
        if (sport == Activity.SPORT_CYCLING) { return "RIDE"; }
        if (sport == Activity.SPORT_SWIMMING) { return "SWIM"; }
        if (sport == Activity.SPORT_HIKING) { return "HIKE"; }
        return "ACTIVITY";
    }
}
