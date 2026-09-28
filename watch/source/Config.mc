import Toybox.Application;
import Toybox.Lang;

// serverUrl/sharedKey. Application.Properties is the source of truth, but
// it is not reliably readable from the background process: on a real watch
// the background job reported "no serverUrl set" while the foreground app
// (same install) reached the server fine. Storage does work in the
// background (BgStatus depends on it), so the foreground copies the values
// into Storage at launch (mirrorToStorage) and the background falls back to
// that copy. Open the app once after installing to populate it.
(:background)
class Config {
    private static const STORE_URL = "cfgServerUrl";
    private static const STORE_KEY = "cfgSharedKey";

    static function serverUrl() as String? {
        return read("serverUrl", STORE_URL);
    }

    static function sharedKey() as String? {
        return read("sharedKey", STORE_KEY);
    }

    // Call from the FOREGROUND (AchievementApp.onStart).
    static function mirrorToStorage() as Void {
        mirror("serverUrl", STORE_URL);
        mirror("sharedKey", STORE_KEY);
    }

    // For diagnostics: which sources have a value, without revealing it.
    static function describe() as String {
        return "props:" + (fromProperties("serverUrl") == null ? "-" : "url")
            + " store:" + (fromStorage(STORE_URL) == null ? "-" : "url");
    }

    private static function read(propKey as String, storeKey as String) as String? {
        var v = fromProperties(propKey);
        if (v != null) {
            return v;
        }
        return fromStorage(storeKey);
    }

    private static function mirror(propKey as String, storeKey as String) as Void {
        var v = fromProperties(propKey);
        if (v != null) {
            Application.Storage.setValue(storeKey, v);
        }
    }

    private static function fromProperties(key as String) as String? {
        try {
            if (!(Toybox.Application has :Properties)) {
                return null;
            }
            return nonEmpty(Application.Properties.getValue(key));
        } catch (ex) {
            return null;
        }
    }

    private static function fromStorage(key as String) as String? {
        return nonEmpty(Application.Storage.getValue(key));
    }

    private static function nonEmpty(v) as String? {
        if (v == null || !(v instanceof String) || (v as String).length() == 0) {
            return null;
        }
        return v as String;
    }
}
