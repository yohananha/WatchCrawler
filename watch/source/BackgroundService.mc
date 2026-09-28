import Toybox.Background;
import Toybox.Lang;
import Toybox.System;

// Runs every 5 minutes (registered in AchievementApp.onStart). Detects a
// new completed activity, computes tier/stat locally, then resolves the
// achievement text via AchievementResolver (Phase 2 LLM server, falling
// back to the local TextBank), queues it for the foreground view, and
// notifies. The resolve step is async (a network call), so this doesn't
// call Background.exit() until onResolved runs - that's expected: Garmin's
// background execution model waits for it, within its time budget.
(:background)
class BackgroundService extends System.ServiceDelegate {

    function initialize() {
        ServiceDelegate.initialize();
    }

    function onTemporalEvent() as Void {
        var core = ActivityDetector.detectCoreEvent();
        if (core == null) {
            Background.exit(false);
            return;
        }

        AchievementResolver.get().resolve(core, method(:onResolved));
    }

    function onResolved(achievement as Dictionary) as Void {
        PendingQueue.push(achievement);
        var result = Notifier.notifyAchievement(achievement);
        System.println("[BG] new " + (achievement["tier"] as String) + " achievement queued, " + result);
        Background.exit(true);
    }
}
