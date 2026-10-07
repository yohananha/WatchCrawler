import Toybox.Application;
import Toybox.Communications;
import Toybox.Lang;
import Toybox.PersistedContent;
import Toybox.System;
import Toybox.Time;

// The watch's regular check-in with its server, on the background timer.
// Every answer carries the day-event tuning (quiet hours, daily cap...) and
// the API-credit notice (see SystemNotice).
//
// Developer builds (dev.jungle) poll POST <serverUrl>/trigger-test/consume
// every cycle, so a test achievement armed from a browser (the server's own
// "/" page) or curl fires within one background cycle (~5 min) - Connect IQ
// has no server-to-watch push for a sideloaded app. User builds have no test
// trigger: they GET /day-settings, at most every few hours, so the server
// (which sleeps when idle) isn't woken all day. See BackgroundService.
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

        if (shouldSkip()) {
            finish(false, null);
            return;
        }

        var url = Config.serverUrl();
        if (url == null) {
            BgStatus.setTrigger("no serverUrl (" + Config.describe() + ")");
            finish(false, null);
            return;
        }

        var headers = { "X-Device-Id" => Config.deviceId() };
        var key = Config.sharedKey();
        if (key != null) {
            headers.put("X-Watch-Key", key as String);
        }
        WatchErr.addTo(headers);
        BgStatus.setTrigger("sending key=" + (key == null ? "none" : "set"));

        var options = {
            :method => requestMethod(),
            :headers => headers,
            :responseType => Communications.HTTP_RESPONSE_CONTENT_TYPE_JSON,
        };

        try {
            Communications.makeWebRequest((url as String) + endpoint(), {}, options, method(:onResponse));
        } catch (ex) {
            BgStatus.setTrigger("threw: " + ex.getErrorMessage());
            WatchErr.record("poll-threw", -1);
            finish(false, null);
        }
    }

    function onResponse(responseCode as Number, data as Null or Dictionary or String or PersistedContent.Iterator) as Void {
        if (responseCode == 200 && data instanceof Dictionary) {
            Application.Storage.setValue("settingsPolledAt", Time.now().value());
            WatchErr.clear();
            SystemNotice.store(data as Dictionary);
        } else {
            WatchErr.record("poll", responseCode);
            if (responseCode == 401) {
                SystemNotice.storeBadKey();
            }
        }
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

    // ---- build-specific behaviour ------------------------------------------

    // Server said the test trigger is off: don't wake it every cycle. Cleared
    // whenever the app is opened (AchievementApp.onStart).
    (:dev)
    private function shouldSkip() as Boolean {
        var skipUntil = Application.Storage.getValue("triggerSkipUntil");
        if (skipUntil instanceof Number && Time.now().value() < skipUntil) {
            BgStatus.setTrigger("skipped (server has test trigger off)");
            return true;
        }
        return false;
    }

    (:dev)
    private function endpoint() as String {
        return "/trigger-test/consume";
    }

    (:dev)
    private function requestMethod() as Communications.HttpRequestMethod {
        return Communications.HTTP_REQUEST_METHOD_POST;
    }

    // Settings and credit notices change slowly: check a few times a day.
    (:user)
    private function shouldSkip() as Boolean {
        var last = Application.Storage.getValue("settingsPolledAt");
        return last instanceof Number && Time.now().value() - (last as Number) < SETTINGS_EVERY_SEC;
    }

    (:user)
    private const SETTINGS_EVERY_SEC = 3 * 3600;

    (:user)
    private function endpoint() as String {
        return "/day-settings";
    }

    (:user)
    private function requestMethod() as Communications.HttpRequestMethod {
        return Communications.HTTP_REQUEST_METHOD_GET;
    }

    private function finish(armed as Boolean, kind as String?) as Void {
        var cb = _callback;
        _callback = null;
        if (cb != null) {
            cb.invoke(armed, kind);
        }
    }
}
