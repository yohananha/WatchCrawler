import Toybox.Application;
import Toybox.Lang;
import Toybox.Time;
import Toybox.Time.Gregorian;

// Credit warnings from the server ("Mana Reserves Low" / "Out of Mana"):
// every server answer (achievement, settings poll) carries `notice`, a small
// achievement-shaped dict when the user's API credit is low or empty, null
// when it's fine. The latest one is kept here, and the background service
// shows it as a "SYSTEM MESSAGE" at most once a day, outside quiet hours.
(:background)
class SystemNotice {
    private static const KEY = "sysNotice";
    private static const SHOWN_KEY = "sysNoticeDay";

    // From a server response: remember the notice, or forget it when credit is fine again.
    static function store(data as Dictionary) as Void {
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
