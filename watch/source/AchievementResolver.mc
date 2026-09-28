import Toybox.Application;
import Toybox.Communications;
import Toybox.Lang;
import Toybox.PersistedContent;
import Toybox.System;

// Turns a "core" event (hero/stat/tier/sound + raw numbers, no text yet -
// see ActivityDetector) into a complete achievement dict, via the Phase 2
// LLM server when a server URL is configured and reachable, falling back to
// the local TextBank (Phase 1) on any failure: no URL set, no network, no
// phone, wrong key, timeout, bad response.
//
// A singleton instance (not static functions) because Communications.
// makeWebRequest's callback must be a bound Method, and method(:symbol)
// needs a `self` to bind to - static/module functions don't have one.
//
// Single-flight: only one resolution in progress at a time, which is fine
// here - the background service only ever detects/resolves one activity at
// a time. A second resolve() call while one is in flight would clobber the
// first's callback, so callers should not overlap calls.
(:background)
class AchievementResolver {
    private static var _instance as AchievementResolver?;

    static function get() as AchievementResolver {
        if (_instance == null) {
            _instance = new AchievementResolver();
        }
        return _instance as AchievementResolver;
    }

    private var _core as Dictionary?;
    private var _callback as Method?;

    function initialize() {
    }

    function resolve(core as Dictionary, callback as Method) as Void {
        if (core.hasKey("isComplete") && core["isComplete"] == true) {
            callback.invoke(core);
            return;
        }

        _core = core;
        _callback = callback;

        var url = Config.serverUrl();
        System.println("[SRV] serverUrl=" + (url == null ? "(null)" : url));
        if (url == null) {
            finishWithFallback();
            return;
        }

        var body = {
            "type" => "activity_completed",
            "value" => core["km"],
            "unit" => "km",
            "baselineMean" => core["baselineMean"],
            "baselineStd" => core["baselineStd"],
            "higherIsBetter" => true,
        };
        if (core.hasKey("eventType")) {
            // Non-activity event (e.g. no_achievement_today): no distance value.
            body = {
                "type" => core["eventType"],
                "higherIsBetter" => core.hasKey("higherIsBetter") ? core["higherIsBetter"] : true,
                "details" => core["details"],
            };
            if (core.hasKey("value")) {
                body.put("value", core["value"]);
                body.put("unit", core["unit"]);
                body.put("baselineMean", core["baselineMean"]);
                body.put("baselineStd", core["baselineStd"]);
            }
        }

        var headers = { "Content-Type" => Communications.REQUEST_CONTENT_TYPE_JSON };
        var key = Config.sharedKey();
        if (key != null) {
            headers.put("X-Watch-Key", key as String);
        }

        var options = {
            :method => Communications.HTTP_REQUEST_METHOD_POST,
            :headers => headers,
            :responseType => Communications.HTTP_RESPONSE_CONTENT_TYPE_JSON,
        };

        try {
            Communications.makeWebRequest((url as String) + "/achievement", body, options, method(:onResponse));
        } catch (ex) {
            System.println("[SRV] makeWebRequest threw: " + ex.getErrorMessage());
            finishWithFallback();
        }
    }

    function onResponse(responseCode as Number, data as Null or Dictionary or String or PersistedContent.Iterator) as Void {
        if (_callback == null) {
            return; // Stray/late callback after a fallback already fired.
        }
        if (responseCode == 200 && data instanceof Dictionary && data.hasKey("title")) {
            System.println("[SRV] got achievement from server");
            finishWithServerText(data as Dictionary);
        } else {
            System.println("[SRV] request failed, code=" + responseCode);
            finishWithFallback();
        }
    }

    private function finishWithServerText(data as Dictionary) as Void {
        var core = _core as Dictionary;
        var result = {
            "hero" => core["hero"],
            "title" => data["title"],
            "stat" => core["stat"],
            "text" => data["text"],
            "reward" => data["reward"],
            "tier" => core["tier"],
            "sound" => core["sound"],
        };
        if (core.hasKey("eventType")) { result.put("eventType", core["eventType"]); }
        finish(result);
    }

    private function finishWithFallback() as Void {
        var core = _core as Dictionary;
        var tier = Baseline.tierFromName(core["tier"] as String);
        var words = core.hasKey("eventType") ? TextBank.pickEvent(core["eventType"] as String, core["tier"] as String) : TextBank.pick(tier, core["km"] as Float, core["durationSec"] as Number,
            core["pace"] as String, core["hero"] as String);
        var result = {
            "hero" => core["hero"],
            "title" => words[0],
            "stat" => core["stat"],
            "text" => words[1],
            "reward" => words[2],
            "tier" => core["tier"],
            "sound" => core["sound"],
        };
        if (core.hasKey("eventType")) { result.put("eventType", core["eventType"]); }
        finish(result);
    }

    private function finish(achievement as Dictionary) as Void {
        var cb = _callback;
        _core = null;
        _callback = null;
        if (cb != null) {
            cb.invoke(achievement);
        }
    }

}
