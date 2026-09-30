import Toybox.Application;
import Toybox.Lang;
import Toybox.Time;

// The watch has no way to tell anyone when something goes wrong on it, so
// the last failure (network code, where it happened, how many times) is kept
// in Storage and sent to the server as an X-Watch-Error header on the next
// request that gets through. The server forwards it to the developer's error
// reports (unless the user opted out) and the watch clears it on HTTP 200.
(:background)
class WatchErr {
    private static const KEY = "watchErr";

    // Normal life, not a failure: the phone is out of range / Bluetooth off.
    private static const IGNORED = [-104, -2];

    static function record(where as String, code as Number) as Void {
        if (IGNORED.indexOf(code) >= 0) {
            return;
        }
        var prev = Application.Storage.getValue(KEY);
        var n = 1;
        if (prev instanceof Dictionary && prev["code"] == code && prev["where"] == where) {
            n = (prev["n"] as Number) + 1;
        }
        Application.Storage.setValue(KEY, { "code" => code, "where" => where, "n" => n, "t" => Time.now().value() });
    }

    // e.g. "-400 achievement x3 12m-ago build 5c3978f", or null when there is nothing to report.
    static function header() as String? {
        var e = Application.Storage.getValue(KEY);
        if (!(e instanceof Dictionary)) {
            return null;
        }
        var ago = (Time.now().value() - (e["t"] as Number)) / 60;
        return e["code"] + " " + e["where"] + " x" + e["n"] + " " + ago + "m-ago build " + BuildInfo.STAMP;
    }

    static function addTo(headers as Dictionary) as Void {
        var h = header();
        if (h != null) {
            headers.put("X-Watch-Error", h as String);
        }
    }

    static function clear() as Void {
        Application.Storage.deleteValue(KEY);
    }
}
