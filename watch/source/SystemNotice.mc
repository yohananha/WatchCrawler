import Toybox.Application;
import Toybox.Lang;
import Toybox.Time;
import Toybox.Time.Gregorian;

// System messages from the server: every server answer (achievement,
// settings poll) carries `notice`, a small achievement-shaped dict, or null
// when there is nothing to say. A personal server sends "Mana Reserves Low"
// / "Out of Mana" about the user's own API credit; a hosted server sends
// "Trial Mana Fading" / "Out of Mana" about the trial/licence, and also
// `license` (state, unlock code, unlock URL) which the Unlock screen shows.
// The latest notice is kept here and the background service shows it as a
// "SYSTEM MESSAGE" at most once a day, outside quiet hours.
(:background)
class SystemNotice {
    private static const KEY = "sysNotice";
    private static const SHOWN_KEY = "sysNoticeDay";
    private static const LICENSE_KEY = "license";

    // From a server response: remember the notice, or forget it when all is fine again.
    static function store(data as Dictionary) as Void {
        if (data.hasKey("license")) {
            var l = data["license"];
            if (l instanceof Dictionary && l["code"] instanceof String) {
                Application.Storage.setValue(LICENSE_KEY, {
                    "state" => l["state"], "code" => l["code"], "url" => l["url"],
                    "daysLeft" => l["daysLeft"], "licensedUntil" => l["licensedUntil"],
                });
            }
        }
        if (!data.hasKey("notice")) {
            return; // older server: no opinion
        }
        var n = data["notice"];
        if (n instanceof Dictionary && n["title"] instanceof String) {
            Application.Storage.setValue(KEY, {
                "state" => n["state"], "title" => n["title"], "text" => n["text"], "reward" => n["reward"],
            });
        } else {
            Application.Storage.deleteValue(KEY);
        }
    }

    // What a hosted server last said about this watch's licence (null on a personal server).
    static function license() as Dictionary? {
        var l = Application.Storage.getValue(LICENSE_KEY);
        return l instanceof Dictionary ? l as Dictionary : null;
    }

    // A ready-to-queue achievement dict if a notice is due now, else null. Marks it shown for today.
    static function takeDue() as Dictionary? {
        var n = Application.Storage.getValue(KEY);
        if (!(n instanceof Dictionary)) {
            return null;
        }
        var info = Gregorian.info(Time.now(), Time.FORMAT_SHORT);
        var day = info.year * 10000 + info.month * 100 + info.day;
        if (Application.Storage.getValue(SHOWN_KEY) == day || DayEvents.isQuiet(info.hour)) {
            return null;
        }
        Application.Storage.setValue(SHOWN_KEY, day);
        var out = "out".equals(n["state"]);
        return {
            "eventType" => "system_notice",
            "hero" => "MANA",
            "title" => n["title"],
            "stat" => out ? "0 MANA" : "LOW MANA",
            "text" => n["text"],
            "reward" => n["reward"],
            "tier" => "cursed",
            "sound" => "fail",
        };
    }
}
