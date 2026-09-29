import Toybox.Activity;
import Toybox.Application;
import Toybox.Lang;
import Toybox.Time;

// The watch's activity HISTORY only says "TRAINING" for strength/HIIT/yoga
// (UserProfile.UserActivity has no sub-sport). The CURRENT recording does
// know: Activity.getProfileInfo() gives the profile name ("Strength", "HIIT",
// a custom one...), sport and sub-sport. So every background tick that finds
// a recording in progress saves that profile (with the recording's start
// time); when the finished activity later appears in history,
// ActivityDetector matches it by start time and uses the exact kind.
//
// While a recording is running the background job stays silent (see
// BackgroundService): no notification mid-workout. A recording shorter than
// the tick interval is never seen and simply falls back to the generic label.
//
// Whether the watch lets the background process read these is unverified
// (docs only promise it "within apps"): every call is guarded, and
// summary() shows on the diagnostics screen what was actually captured.
(:background)
class ProfileCapture {
    private static const KEY = "profCaptures";
    private static const KEY_ERR = "profErr";
    private static const MAX_KEPT = 6;
    private static const MATCH_WINDOW_SEC = 300;

    // True when an activity is being recorded (timer on, paused or stopped
    // but not yet saved) - the caller then stays quiet. Saves its profile.
    static function captureIfRecording() as Boolean {
        try {
            if (!(Toybox has :Activity)) {
                return false;
            }
            var info = Activity.getActivityInfo();
            if (info == null || info.timerState == null || info.timerState == Activity.TIMER_STATE_OFF) {
                return false;
            }
            var now = Time.now().value();
            var start = now;
            if (info.startTime != null) {
                start = info.startTime.value();
            } else if (info.elapsedTime != null) {
                start = now - (info.elapsedTime / 1000);
            }

            var name = "";
            var sport = -1;
            var sub = -1;
            try {
                var profile = Activity.getProfileInfo();
                if (profile != null) {
                    if (profile.name != null) { name = profile.name; }
                    if (profile.sport != null) { sport = profile.sport; }
                    if (profile.subSport != null) { sub = profile.subSport; }
                }
            } catch (ex) {
                Application.Storage.setValue(KEY_ERR, "profile: " + ex.getErrorMessage());
            }
            save({ "start" => start, "seen" => now, "name" => name, "sport" => sport, "sub" => sub });
            return true;
        } catch (ex) {
            Application.Storage.setValue(KEY_ERR, "activity: " + ex.getErrorMessage());
            return false;
        }
    }

    // The capture whose recording started within MATCH_WINDOW_SEC of `startEpoch`
    // (the finished activity's start), or null.
    static function findFor(startEpoch as Number) as Dictionary? {
        var caps = list();
        var best = null;
        var bestGap = MATCH_WINDOW_SEC + 1;
        for (var i = 0; i < caps.size(); i++) {
            var gap = ((caps[i]["start"] as Number) - startEpoch).abs();
            if (gap < bestGap) {
                bestGap = gap;
                best = caps[i];
            }
        }
        return best;
    }

    // Short label (<= 5 chars, A-Z0-9 only: the pixel fonts have no other glyphs).
    static function heroFor(cap as Dictionary) as String {
        var sub = cap["sub"];
        if (sub == Activity.SUB_SPORT_STRENGTH_TRAINING) { return "LIFT"; }
        if (sub == Activity.SUB_SPORT_HIIT) { return "HIIT"; }
        if (sub == Activity.SUB_SPORT_YOGA) { return "YOGA"; }
        if (sub == Activity.SUB_SPORT_PILATES) { return "PILAT"; }
        if (sub == Activity.SUB_SPORT_CARDIO_TRAINING) { return "GYM"; }
        if (sub == Activity.SUB_SPORT_ELLIPTICAL) { return "ELLIP"; }
        if (sub == Activity.SUB_SPORT_STAIR_CLIMBING) { return "STAIR"; }
        if (sub == Activity.SUB_SPORT_TREADMILL) { return "TREAD"; }
        if (sub == Activity.SUB_SPORT_INDOOR_CYCLING) { return "SPIN"; }
        if (sub == Activity.SUB_SPORT_INDOOR_ROWING) { return "ROW"; }
        if (sub == Activity.SUB_SPORT_FLEXIBILITY_TRAINING) { return "STRCH"; }
        if (sub == Activity.SUB_SPORT_BREATHING) { return "ZEN"; }
        return fromName(cap["name"] as String);
    }

    // Free text for the LLM prompt: the profile name the user gave it.
    static function descFor(cap as Dictionary) as String {
        var name = cap["name"] as String;
        return name.length() > 0 ? name + " workout" : "a workout";
    }

    // Diagnostics line: what the last background tick captured.
    static function summary() as String {
        var caps = list();
        if (caps.size() == 0) {
            var err = Application.Storage.getValue(KEY_ERR);
            return err == null ? "prof: none captured yet" : "prof: " + err;
        }
        var c = caps[caps.size() - 1];
        var ago = (Time.now().value() - (c["seen"] as Number)) / 60;
        return "prof: " + c["name"] + " s" + c["sport"] + "/" + c["sub"] + " " + ago + "m ago";
    }

    private static function fromName(name as String) as String {
        var allowed = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var up = name.toUpper();
        var out = "";
        for (var i = 0; i < up.length() && out.length() < 5; i++) {
            var ch = up.substring(i, i + 1);
            if (allowed.find(ch) != null) {
                out = out + ch;
            }
        }
        return out.length() > 0 ? out : "TRAIN";
    }

    private static function list() as Array<Dictionary> {
        var stored = Application.Storage.getValue(KEY);
        return stored instanceof Array ? stored as Array<Dictionary> : ([] as Array<Dictionary>);
    }

    // One entry per recording (same start), newest last, capped.
    private static function save(entry as Dictionary) as Void {
        var caps = list();
        var replaced = false;
        for (var i = 0; i < caps.size(); i++) {
            if (((caps[i]["start"] as Number) - (entry["start"] as Number)).abs() <= 60) {
                caps[i] = entry;
                replaced = true;
            }
        }
        if (!replaced) {
            caps.add(entry);
        }
        while (caps.size() > MAX_KEPT) {
            caps.remove(caps[0]);
        }
        Application.Storage.setValue(KEY, caps);
    }
}
