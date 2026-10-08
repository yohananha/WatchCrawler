import Toybox.Application;
import Toybox.Background;
import Toybox.Lang;
import Toybox.System;

// Runs on Garmin's background timer (registered in AchievementApp.onStart).
// Order matters: real activity detection runs FIRST, before any network call.
// It used to wait for the server poll's answer, and with the phone link down
// (HTTP -104) a whole strength workout went by with no achievement, not even
// the watch's own lines - the runs stopped at "start". The
// poll (remote test trigger / day settings) now only runs when nothing was
// detected. An event is also saved as "stranded" before its network call and
// cleared once it is shown; if the run dies mid-request, the next run shows
// it with local text instead of losing it. Either path resolves the
// achievement text via AchievementResolver (LLM server, falling back to the
// local TextBank), queues it for the foreground view, and notifies. Those
// are async network calls, so Background.exit() is only called from
// onResolved / when there's nothing to do - Garmin's background model
// waits for it, within its time budget.
//
// Every stage is recorded via BgStatus so a real watch (no console) can
// show how far the last run got.
(:background)
class BackgroundService extends System.ServiceDelegate {

    // True when this run was started by the server's test trigger (short queue expiry).
    private var _isTest as Boolean = false;

    // An event whose run died before it was shown (see header).
    private static const STRANDED = "strandedEvent";

    function initialize() {
        ServiceDelegate.initialize();
    }

    function onTemporalEvent() as Void {
        BgStatus.mark("start");
        // Mid-workout: remember which profile is recording, then stay completely
        // silent (no notifications, no network) until it is saved/discarded.
        if (ProfileCapture.captureIfRecording()) {
            BgStatus.mark("done: recording in progress (profile captured)");
            Background.exit(false);
            return;
        }
        var stranded = Application.Storage.getValue(STRANDED);
        if (stranded instanceof Dictionary) {
            BgStatus.mark("stranded event -> local text");
            AchievementResolver.get().resolveLocal(stranded as Dictionary, method(:onResolved));
            return;
        }

        BgStatus.mark("detecting");
        var core = ActivityDetector.detectCoreEvent();
        if (core != null) {
            BgStatus.mark("new activity -> resolving");
            resolveSaved(core);
            return;
        }
        BgStatus.mark("no new activity -> polling");
        TriggerChecker.get().checkAndConsume(method(:onTriggerChecked));
    }

    function onTriggerChecked(armed as Boolean, kind as String?) as Void {
        if (armed && fireTestTrigger(kind)) {
            return;
        }

        var core = DayEvents.checkForDayEvent();
        if (core != null) {
            BgStatus.mark("day event " + core["eventType"] + " -> resolving");
            resolveSaved(core);
            return;
        }
        // Nothing happened: a good moment for a pending system message (API credit, refused key).
        var notice = SystemNotice.takeDue();
        if (notice != null) {
            BgStatus.mark("system notice -> notifying");
            onResolved(notice as Dictionary);
            return;
        }
        BgStatus.mark("done: no new activity");
        Background.exit(false);
    }

    // Saved first, so a run that dies waiting on the server doesn't lose the event.
    private function resolveSaved(core as Dictionary) as Void {
        Application.Storage.setValue(STRANDED, core);
        AchievementResolver.get().resolve(core, method(:onResolved));
    }

    // Remote test trigger: developer builds only. Returns true when it took over this run.
    (:dev)
    private function fireTestTrigger(kind as String?) as Boolean {
        _isTest = true;
        BgStatus.mark("trigger armed (" + kind + ") -> resolving");
        // See ActivityDetector.buildFakeCore: fixed values, tier forced
        // to :legendary. Its baselineMean/Std are null, so the server
        // computes its own tier for the TEXT (probably :common) - a
        // cosmetic mismatch with our local :legendary styling/sound,
        // fine for a "does the round trip work" test.
        var test = ActivityDetector.fakeCoreForKind(kind);
        AchievementResolver.get().resolve(test, method(:onResolved));
        return true;
    }

    // User builds have no fake achievements at all: even if a server claimed a
    // test was armed, it is ignored and the run continues with real detection.
    (:user)
    private function fireTestTrigger(kind as String?) as Boolean {
        return false;
    }

    function onResolved(achievement as Dictionary) as Void {
        Application.Storage.deleteValue(STRANDED);
        if (!achievement.hasKey("eventType")) {
            DayEvents.noteAchievement(); // the idle roast itself doesn't count as an achievement
        }
        DayEvents.noteSent();
        PendingQueue.push(achievement, _isTest);
        var result = Notifier.notifyAchievement(achievement);
        BgStatus.mark("done: notified (" + result + ")");
        Background.exit(true);
    }
}
