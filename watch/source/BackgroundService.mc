import Toybox.Background;
import Toybox.Lang;
import Toybox.System;

// Runs on Garmin's background timer (registered in AchievementApp.onStart).
// Order matters: the remote test trigger is checked FIRST, then real
// activity detection. If detection ever fails (it reads the FIT activity
// history, which has crashed the background process in the simulator), it
// can no longer stop a remote test from firing. Either path resolves the
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

    function initialize() {
        ServiceDelegate.initialize();
    }

    function onTemporalEvent() as Void {
        BgStatus.mark("start");
        TriggerChecker.get().checkAndConsume(method(:onTriggerChecked));
    }

    function onTriggerChecked(armed as Boolean) as Void {
        if (armed) {
            BgStatus.mark("trigger armed -> resolving");
            // See ActivityDetector.buildFakeCore: fixed values, tier forced
            // to :legendary. Its baselineMean/Std are null, so the server
            // computes its own tier for the TEXT (probably :common) - a
            // cosmetic mismatch with our local :legendary styling/sound,
            // fine for a "does the round trip work" test.
            var test = ActivityDetector.buildFakeCore(:legendary);
            AchievementResolver.get().resolve(test, method(:onResolved));
            return;
        }

        BgStatus.mark("trigger not armed -> detecting");
        var core = ActivityDetector.detectCoreEvent();
        if (core == null) {
            core = IdleDayChecker.checkForIdleDay();
            if (core != null) {
                BgStatus.mark("idle day -> resolving");
                AchievementResolver.get().resolve(core, method(:onResolved));
                return;
            }
            BgStatus.mark("done: no new activity");
            Background.exit(false);
            return;
        }

        BgStatus.mark("new activity -> resolving");
        AchievementResolver.get().resolve(core, method(:onResolved));
    }

    function onResolved(achievement as Dictionary) as Void {
        if (achievement.hasKey("hero") && achievement["hero"] != "IDLE") {
            IdleDayChecker.noteAchievement(); // the idle roast itself doesn't count as an achievement
        }
        PendingQueue.push(achievement);
        var result = Notifier.notifyAchievement(achievement);
        BgStatus.mark("done: notified (" + result + ")");
        Background.exit(true);
    }
}
