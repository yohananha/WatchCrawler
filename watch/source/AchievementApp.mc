import Toybox.Application;
import Toybox.Background;
import Toybox.Lang;
import Toybox.System;
import Toybox.Time;
import Toybox.WatchUi;

// Hall of Shame is the real home screen (nothing pending -> your achievement
// history). DiagnosticView (capability probes + the debug tier injector)
// is reachable from there via MENU - kept for ongoing testing, not the
// default anymore now that Phases 1/2 are working end to end.
class AchievementApp extends Application.AppBase {

    function initialize() {
        AppBase.initialize();
    }

    function onStart(state as Dictionary?) as Void {
        Application.Storage.deleteValue("triggerSkipUntil"); // re-check the server's test-trigger flag
        Config.mirrorToStorage(); // background can't reliably read Properties - see Config
        // Background checks run every 5 minutes. Deliberately NOT calling
        // UserProfile.getUserActivityHistory() here (first-run priming is
        // folded into ActivityDetector.checkForNewActivity() instead, which
        // only ever runs from the background timer): in the simulator, with
        // zero activity FIT files present, that call is a VM-level crash
        // ("Illegal Access (Out of Bounds)") that Monkey C try/catch cannot
        // intercept, and it happened to sit before this registration call -
        // silently breaking background detection on every launch. Needs
        // re-verifying on a real watch, which actually has FIT files.
        if (!(Background.getTemporalEventRegisteredTime() instanceof Moment)) {
            Background.registerForTemporalEvent(new Time.Duration(5 * 60));
        }
    }

    function onStop(state as Dictionary?) as Void {
    }

    function getInitialView() as [Views] or [Views, InputDelegates] {
        // Garmin's Notifications sample registers here (foreground only);
        // the watch may not offer a notification's actions until the app has
        // a message callback. Deferred reference, same as Notifier: the
        // module only exists on API 5.1+ devices.
        if (Toybox has :Notifications) {
            var Notifications = Toybox.Notifications;
            Notifications.registerForNotificationMessages(method(:onNotification));
        }
        if (!PendingQueue.isEmpty()) {
            var next = PendingQueue.popNext();
            return [ new AchievementView(next as Dictionary), new AchievementDelegate() ];
        }
        var hof = new HallOfShameView();
        return [ hof, new HallOfShameDelegate(hof) ];
    }

    // "Claim reward" selected (type 2 = NOTIFICATION_MESSAGE_TYPE_SELECTED)
    // while the app is running: show the pending achievement. Skipped when
    // one is already on screen - e.g. getInitialView() just opened it because
    // the action launched the app, and the queued message arrives right after.
    function onNotification(message) as Void {
        if (message.type != 2 || PendingQueue.isEmpty()
            || WatchUi.getCurrentView()[0] instanceof AchievementView) {
            return;
        }
        var next = PendingQueue.popNext();
        WatchUi.pushView(new AchievementView(next as Dictionary), new AchievementDelegate(), WatchUi.SLIDE_LEFT);
    }

    // Called when a background temporal event fires.
    function getServiceDelegate() as [ServiceDelegate] {
        return [ new BackgroundService() ];
    }

    // Called when the background process finishes; result is whatever
    // BackgroundService.onTemporalEvent() returned via Background.exit(...).
    function onBackgroundData(data as Application.PropertyValueType) as Void {
        WatchUi.requestUpdate();
    }
}

function getApp() as AchievementApp {
    return Application.getApp() as AchievementApp;
}
