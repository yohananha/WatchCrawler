import Toybox.Application;
import Toybox.Lang;
import Toybox.System;
import Toybox.Time;

// Heartbeat for the background process: it runs where nobody can see it,
// and on a real watch there's no console, so "does it run at all, and how
// far does it get?" was unanswerable (a remote test trigger sat armed
// while the server logs showed the watch never polled). Each stage
// overwrites the last, so if the process dies mid-way the stage shown is
// the last one it reached. Displayed on DiagnosticView.
(:background)
class BgStatus {
    private static const KEY = "bgStatus";

    static function mark(stage as String) as Void {
        var s = Application.Storage.getValue(KEY);
        var count = 0;
        if (s != null && s instanceof Dictionary && (s as Dictionary).hasKey("count")) {
            count = (s as Dictionary)["count"] as Number;
        }
        if (stage.equals("start")) {
            count += 1;
        }
        Application.Storage.setValue(KEY, { "t" => Time.now().value(), "stage" => stage, "count" => count });
        System.println("[BG] stage=" + stage + " run=" + count);
    }

    // Why the last remote-trigger poll returned "not armed": no URL/key set,
    // request threw, or the HTTP/Communications code that came back.
    static function setTrigger(detail as String) as Void {
        Application.Storage.setValue("bgTrigger", detail);
        System.println("[BG] trigger: " + detail);
    }

    static function triggerSummary() as String {
        var s = Application.Storage.getValue("bgTrigger");
        return s == null ? "trig: -" : ("trig: " + s);
    }

    static function summary() as String {
        var s = Application.Storage.getValue(KEY);
        if (s == null || !(s instanceof Dictionary)) {
            return "BG: never ran";
        }
        var d = s as Dictionary;
        var ago = (Time.now().value() - (d["t"] as Number)) / 60;
        return "BG#" + d["count"] + " " + ago + "m ago: " + d["stage"];
    }
}
