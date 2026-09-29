import Toybox.Application;
import Toybox.Communications;
import Toybox.Lang;
import Toybox.PersistedContent;
import Toybox.System;
import Toybox.Time;

// Polls POST <serverUrl>/trigger-test/consume on the background timer, so a
// test achievement armed from a browser (the server's own "/" page) or curl
// fires within one background cycle (~5 min) with no button press needed -
// Connect IQ has no server-to-watch push for a private/sideloaded app, so
// polling on the tick we already have is the only option. See
// BackgroundService for where this is called, only when no real activity
// was detected that cycle.
//
// Singleton instance for the same reason as AchievementResolver: method(:sym)
// needs a `self` to bind to.
(:background)
class TriggerChecker {
    private static var _instance as TriggerChecker?;

    static function get() as TriggerChecker {
        if (_instance == null) {
            _instance = new TriggerChecker();
        }
        return _instance as TriggerChecker;
    }

    private var _callback as Method?;

    function initialize() {
    }

    // callback: method(armed as Boolean, kind as String?) as Void
    function checkAndConsume(callback as Method) as Void {
        _callback = callback;

        // Server said the feature is off: don't wake it every cycle. Cleared
        // whenever the app is opened (AchievementApp.onStart).
        var skipUntil = Application.Storage.getValue("triggerSkipUntil");
        if (skipUntil instanceof Number && Time.now().value() < skipUntil) {
            BgStatus.setTrigger("skipped (server has test trigger off)");
            finish(false, null);
            return;
        }

        var url = Config.serverUrl();
        if (url == null) {
            BgStatus.setTrigger("no serverUrl (" + Config.describe() + ")");
            finish(false, null);
            return;
        }

        var headers = {};
        var key = Config.sharedKey();
        if (key != null) {
            headers.put("X-Watch-Key", key as String);
        }
        BgStatus.setTrigger("sending key=" + (key == null ? "none" : "set"));

        var options = {
            :method => Communications.HTTP_REQUEST_METHOD_POST,
            :headers => headers,
            :responseType => Communications.HTTP_RESPONSE_CONTENT_TYPE_JSON,
        };

        try {
            Communications.makeWebRequest((url as String) + "/trigger-test/consume", {}, options, method(:onResponse));
        } catch (ex) {
            BgStatus.setTrigger("threw: " + ex.getErrorMessage());
            finish(false, null);
        }
    }

    function onResponse(responseCode as Number, data as Null or Dictionary or String or PersistedContent.Iterator) as Void {
        // Day-event tuning rides along on every poll (see server DayEventSettings).
        if (responseCode == 200 && data instanceof Dictionary && data["settings"] instanceof Dictionary) {
            var s = data["settings"] as Dictionary;
            Application.Storage.setValue("dayCfg", {
                "idleHour" => s["idleHour"], "goalHour" => s["goalHour"],
                "quietFrom" => s["quietFrom"], "quietTo" => s["quietTo"], "maxPerDay" => s["maxPerDay"],
            });
        }
        if (responseCode == 200 && data instanceof Dictionary && data["enabled"] == false) {
            Application.Storage.setValue("triggerSkipUntil", Time.now().value() + 6 * 3600);
            BgStatus.setTrigger("HTTP 200 test trigger off on server");
            finish(false, null);
            return;
        }
        if (responseCode == 200 && data instanceof Dictionary && data["wasArmed"] == true) {
            var kind = data["kind"];
            BgStatus.setTrigger("HTTP 200 wasArmed=true kind=" + kind);
            finish(true, kind instanceof String ? kind as String : null);
        } else {
            BgStatus.setTrigger("HTTP " + responseCode + (responseCode == 200 ? " wasArmed=false" : ""));
            finish(false, null);
        }
    }

    private function finish(armed as Boolean, kind as String?) as Void {
        var cb = _callback;
        _callback = null;
        if (cb != null) {
            cb.invoke(armed, kind);
        }
    }
}
