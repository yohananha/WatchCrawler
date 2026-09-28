import Toybox.Application;
import Toybox.Lang;

// Shared serverUrl/sharedKey reads (Application.Properties, set via Garmin
// Connect Mobile settings - or, for a sideloaded/non-Store app where GCM
// shows "No settings", baked into resources/properties/properties.xml at
// build time instead). Used by both AchievementResolver and TriggerChecker.
class Config {
    static function serverUrl() as String? {
        return str("serverUrl");
    }

    static function sharedKey() as String? {
        return str("sharedKey");
    }

    private static function str(key as String) as String? {
        if (!(Toybox.Application has :Properties)) {
            return null;
        }
        var v = Application.Properties.getValue(key);
        if (v == null || !(v instanceof String) || (v as String).length() == 0) {
            return null;
        }
        return v as String;
    }
}
