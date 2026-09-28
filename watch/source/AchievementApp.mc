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
        if (!PendingQueue.isEmpty()) {
            var next = PendingQueue.popNext();
            return [ new AchievementView(next as Dictionary), new AchievementDelegate() ];
        }
        var hof = new HallOfShameView();
        return [ hof, new HallOfShameDelegate(hof) ];
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
