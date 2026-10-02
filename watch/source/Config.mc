import Toybox.Application;
import Toybox.Lang;
import Toybox.Math;
import Toybox.System;
import Toybox.Time;

// serverUrl/sharedKey. Application.Properties is the source of truth, but
// it is not reliably readable from the background process: on a real watch
// the background job reported "no serverUrl set" while the foreground app
// (same install) reached the server fine. Storage does work in the
// background (BgStatus depends on it), so the foreground copies the values
// into Storage at launch (mirrorToStorage) and the background falls back to
// that copy. Open the app once after installing to populate it.
(:background)
class Config {
    static var lastBakedError as String? = null;
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

    // Sent as X-Device-Id on every request. A hosted server (one server for
    // everyone) keys the trial/licence and the "do not repeat" history on it;
    // a personal server ignores it. The watch's own id when it has one, else a
    // random id made once and kept in Storage (older simulators return null).
    static function deviceId() as String {
        try {
            var id = System.getDeviceSettings().uniqueIdentifier;
            if (id instanceof String && (id as String).length() > 0) {
                return id as String;
            }
        } catch (ex) {
        }
        var saved = Application.Storage.getValue(STORE_DEVICE_ID);
        if (saved instanceof String) {
            return saved as String;
        }
        var made = "gen-" + Time.now().value().toString() + "-" + Math.rand().toString();
        Application.Storage.setValue(STORE_DEVICE_ID, made);
        return made;
    }
    private static const STORE_DEVICE_ID = "cfgDeviceId";

    // For diagnostics: which sources have a value, without revealing it.
    static function describe() as String {
        return "props:" + (fromProperties("serverUrl") == null ? "-" : "url")
            + " store:" + (fromStorage(STORE_URL) == null ? "-" : "url")
            + " baked:" + (fromBaked(Rez.Strings.CfgServerUrl) == null ? "-" : "url")
            + (lastBakedError == null ? "" : " err:" + lastBakedError);
    }

    private static function read(propKey as String, storeKey as String) as String? {
        var v = fromProperties(propKey);
        if (v != null) {
            return v;
        }
        var s = fromStorage(storeKey);
        if (s != null) {
            return s;
        }
        return fromBaked(propKey == "serverUrl" ? Rez.Strings.CfgServerUrl : Rez.Strings.CfgSharedKey);
    }

    // Compile-time value from resources/strings (bypasses persisted
    // Properties, which can hold an empty value from an older install).
    private static function fromBaked(id) as String? {
        try {
            return nonEmpty(Application.loadResource(id));
        } catch (ex) {
            lastBakedError = ex.getErrorMessage();
            return null;
        }
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
