import Toybox.Background;
import Toybox.Lang;
import Toybox.System;

// Runs every 5 minutes (registered in AchievementApp.onStart). First checks
// for a new completed activity; if there isn't one, checks whether a remote
// test was armed from the server's browser page (TriggerChecker). Either
// path resolves the achievement text via AchievementResolver (Phase 2 LLM
// server, falling back to the local TextBank), queues it for the foreground
// view, and notifies. The resolve step is async (a network call), so this
// doesn't call Background.exit() until onResolved runs - that's expected:
// Garmin's background execution model waits for it, within its time budget.
(:background)
class BackgroundService extends System.ServiceDelegate {

    function initialize() {
        ServiceDelegate.initialize();
    }

    function onTemporalEvent() as Void {
        var core = ActivityDetector.detectCoreEvent();
        if (core != null) {
            AchievementResolver.get().resolve(core, method(:onResolved));
            return;
        }

        TriggerChecker.get().checkAndConsume(method(:onTriggerChecked));
    }

    function onTriggerChecked(armed as Boolean) as Void {
        if (!armed) {
            Background.exit(false);
            return;
        }
        // See ActivityDetector.buildFakeCore: fixed values, tier forced to
        // :legendary for a satisfying test notification. Its baselineMean/
        // Std are null, so the server independently computes its own tier
        // (probably :common, since it has no baseline either) for the TEXT
        // it generates - a cosmetic mismatch with our local :legendary
        // styling/sound that's fine for a "does the round trip work" test.
        var core = ActivityDetector.buildFakeCore(:legendary);
        AchievementResolver.get().resolve(core, method(:onResolved));
    }

    function onResolved(achievement as Dictionary) as Void {
        PendingQueue.push(achievement);
        var result = Notifier.notifyAchievement(achievement);
        System.println("[BG] new " + (achievement["tier"] as String) + " achievement queued, " + result);
        Background.exit(true);
    }
}
