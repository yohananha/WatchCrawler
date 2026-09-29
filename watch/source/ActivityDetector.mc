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
        return buildCore(newest.type, newest.distance, newest.duration, newestEpoch);
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

    // Any activity type. Distance sports (>= 0.5 km covered) are judged on km
    // vs your own history for that sport; everything else (strength, HIIT,
    // yoga, indoor...) on duration in minutes. The watch API only exposes the
    // main sport, so strength/HIIT/yoga all arrive as "TRAINING".
    private static function buildCore(sport as Activity.Sport?, distanceM as Number?, duration as Time.Duration?, startEpoch as Number) as Dictionary {
        var info = sportInfo(sport);
        // The history only knows the main sport; if a background tick saw this
        // recording while it ran, ProfileCapture knows the exact profile.
        var cap = ProfileCapture.findFor(startEpoch);
        if (cap != null) {
            info = [ProfileCapture.heroFor(cap), ProfileCapture.descFor(cap)];
        }
        var distance = distanceM == null ? 0 : distanceM;
        var durationSec = duration == null ? 0 : duration.value();
        return buildCoreFor(info[0], info[1], distance, durationSec);
    }

    static function buildCoreFor(hero as String, sportDesc as String, distance as Number, durationSec as Number) as Dictionary {
        var km = distance / 1000.0;
        var useKm = km >= 0.5;
        var metric = useKm ? km : durationSec / 60.0;
        var unit = useKm ? "km" : "min";
        var key = useKm ? hero : hero + "_MIN";

        var stats = Baseline.statsFor(key); // read BEFORE recording
        var evalResult = Baseline.evaluateWithStats(metric, stats[0], stats[1], true);
        var tier = evalResult[0] as Symbol;
        Baseline.record(key, metric);

        var core = coreDict(hero, distance, durationSec, km, tier, stats[0], stats[1]);
        core.put("value", metric);
        core.put("unit", unit);
        core.put("details", {
            "sport" => sportDesc,
            "durationMin" => (durationSec / 60).toString(),
            "distanceKm" => km.format("%.1f"),
        });
        return core;
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

        if (tier == :strength) { return fakeWorkout("LIFT", "strength training", 3300, null); }
        if (tier == :hiit) { return fakeWorkout("HIIT", "HIIT workout", 1500, null); }
        if (tier == :yoga) { return fakeWorkout("YOGA", "yoga", 2700, null); }
        if (tier == :swim) { return fakeWorkout("SWIM", "swimming", 2400, 1500); }
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

    // Server test page ("kind" strings) -> the same fake events as the MENU injector.
    static function fakeCoreForKind(kind as String?) as Dictionary {
        var k = kind == null ? "legendary" : kind;
        var tiers = {
            "legendary" => :legendary, "epic" => :epic, "rare" => :rare, "common" => :common,
            "cursed" => :cursed, "test" => :test, "idle" => :idle, "goal" => :goal, "sit" => :sit,
            "steps" => :steps, "floors" => :floors, "batt" => :batt, "rhr" => :rhr,
            "strength" => :strength, "hiit" => :hiit, "yoga" => :yoga, "swim" => :swim,
        };
        var tier = tiers.hasKey(k) ? tiers[k] : :legendary;
        return buildFakeCore(tier as Symbol);
    }

    // Fake workout without going through Baseline (no history pollution).
    private static function fakeWorkout(hero as String, desc as String, durationSec as Number, distance as Number?) as Dictionary {
        var dist = distance == null ? 0 : distance;
        var core = coreDict(hero, dist, durationSec, dist / 1000.0, :common, null, null);
        core.put("value", durationSec / 60.0);
        core.put("unit", "min");
        core.put("details", {
            "sport" => desc,
            "durationMin" => (durationSec / 60).toString(),
            "distanceKm" => (dist / 1000.0).format("%.1f"),
        });
        return core;
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

    // [hero word (<= 5 chars), description for the LLM]
    private static function sportInfo(sport as Activity.Sport?) as [String, String] {
        if (sport == null) { return ["ACT", "an activity"]; }
        if (sport == Activity.SPORT_RUNNING) { return ["RUN", "running"]; }
        if (sport == Activity.SPORT_WALKING) { return ["WALK", "walking"]; }
        if (sport == Activity.SPORT_CYCLING) { return ["RIDE", "cycling"]; }
        if (sport == Activity.SPORT_SWIMMING) { return ["SWIM", "swimming"]; }
        if (sport == Activity.SPORT_HIKING) { return ["HIKE", "hiking"]; }
        if (sport == Activity.SPORT_ROWING) { return ["ROW", "rowing"]; }
        if (sport == Activity.SPORT_TRAINING) { return ["TRAIN", "a training session (strength, HIIT, yoga or similar; exact kind unknown)"]; }
        if (sport == Activity.SPORT_ALPINE_SKIING) { return ["SKI", "alpine skiing"]; }
        if (sport == Activity.SPORT_CROSS_COUNTRY_SKIING) { return ["SKI", "cross-country skiing"]; }
        if (sport == Activity.SPORT_SNOWBOARDING) { return ["SNOW", "snowboarding"]; }
        return ["ACT", "an activity"];
    }
}
