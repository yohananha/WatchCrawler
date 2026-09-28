import Toybox.Lang;

// Formats the one hard-number line the watch always has, regardless of
// whether the achievement text came from the LLM or the local TextBank.
class StatLine {

    // distanceMeters/durationSec come straight off UserProfile.UserActivity.
    static function forActivity(distanceMeters as Number, durationSec as Number) as String {
        var distanceKm = distanceMeters / 1000.0;
        if (distanceKm >= 1.0) {
            return formatKm(distanceKm) + " KM . " + formatPace(distanceKm, durationSec) + "/KM";
        }
        return formatDuration(durationSec);
    }

    static function formatKm(km as Float) as String {
        return km.format("%.1f");
    }

    static function formatDuration(sec as Number) as String {
        var h = sec / 3600;
        var m = (sec % 3600) / 60;
        var s = sec % 60;
        if (h > 0) {
            return h.format("%d") + ":" + m.format("%02d") + ":" + s.format("%02d");
        }
        return m.format("%d") + ":" + s.format("%02d");
    }

    static function formatPace(distanceKm as Float, durationSec as Number) as String {
        if (distanceKm <= 0) {
            return "--:--";
        }
        var paceSecPerKm = durationSec / distanceKm;
        var m = (paceSecPerKm / 60).toNumber();
        var s = (paceSecPerKm - m * 60).toNumber();
        return m.format("%d") + ":" + s.format("%02d");
    }
}
