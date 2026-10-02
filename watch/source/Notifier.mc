import Toybox.Application;
import Toybox.Background;
import Toybox.Lang;
import Toybox.System;

// Fires a system notification when the device supports it (API 5.1+:
// fenix 7/7X/8/9, epix Gen2/Pro, Forerunner 165/170/255/265/570/955/965/970,
// Instinct 3, Venu 3/4/X1, vivoactive 5/6 - see the plan). Falls back to the
// old-style "open app?" wake prompt everywhere else.
//
// Tapping the "Claim reward" action launches the app, which opens directly on
// the pending achievement (AchievementApp.getInitialView pops PendingQueue).
//
// Every path returns a short status string instead of failing silently, so
// Phase 0 can show on-screen what actually happened (see DiagnosticDelegate).
(:background)
class Notifier {

    static function notify(title as String, subtitle as String, body as String) as String {
        if (Toybox has :Notifications) {
            return notifyViaNotifications(title, subtitle, body);
        } else {
            return notifyViaWake(title);
        }
    }

    static function notifyTest() as String {
        return notify("LEGENDARY ACHIEVEMENT", "Phase 0 Test", "If you can see this with sound/vibration, Notifications works on this device.");
    }

    static function notifyAchievement(a as Dictionary) as String {
        var tierUpper = (a["tier"] as String);
        tierUpper = tierUpper.toUpper();
        var title = "system_notice".equals(a["eventType"]) ? "SYSTEM MESSAGE" : tierUpper + " ACHIEVEMENT";
        return notify(title, a["title"] as String, a["text"] as String);
    }

    private static function notifyViaNotifications(title as String, subtitle as String, body as String) as String {
        try {
            // Deferred reference: Toybox.Notifications only exists on devices
            // that have it, and Monkey C only lets us reference it inside
            // this guard.
            var Notifications = Toybox.Notifications;
            // Actions MUST carry :data (not :id) and the notification needs a
            // top-level :data - without them the system treats it as an
            // actionless notification and only offers Back. Selecting the
            // action launches the app, and getInitialView() then shows the
            // pending achievement straight away.
            var options = {
                :body => body,
                :data => "achievement",
                :actions => [{ :label => "Claim reward", :data => "claim" }],
            };
            Notifications.showNotification(title, subtitle, options);
            return "showNotification() called OK";
        } catch (ex instanceof Lang.Exception) {
            return "showNotification() THREW: " + ex.getErrorMessage();
        }
    }

    private static function notifyViaWake(title as String) as String {
        if (Toybox has :Background) {
            try {
                Background.requestApplicationWake("Achievement unlocked?" as Application.PersistableType);
                return "requestApplicationWake() called OK (no Notifications support)";
            } catch (ex instanceof Lang.Exception) {
                return "requestApplicationWake() THREW: " + ex.getErrorMessage();
            }
        }
        return "No Notifications or Background support";
    }
}
