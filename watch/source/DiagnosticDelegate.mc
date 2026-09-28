import Toybox.Lang;
import Toybox.System;
import Toybox.WatchUi;

// Debug-only (used until Phase 3 adds a real settings/menu screen):
// MENU/TAP cycles through the five tiers and injects a fake achievement of
// that tier - through the same PendingQueue + Notifier path a real
// background detection would use, so we can exercise the whole pipeline
// from the simulator without waiting for a real activity or background timer.
// SELECT jumps straight to viewing the next queued achievement.
//
// No auto-firing timer here on purpose: an earlier version of this file
// auto-fired a test notification on every view-show, which looped forever
// on a real watch once the notification tap kept reopening the app.
class DiagnosticDelegate extends WatchUi.BehaviorDelegate {

    private static var _tierIndex as Number = 0;
    // :test is a hardcoded joke achievement (see ActivityDetector.buildFakeAchievement)
    // for instantly confirming the notify->queue->view->sound pipeline works,
    // independent of tier math or the Phase 2 server.
    private static const TIERS = [:common, :rare, :epic, :legendary, :cursed, :test, :idle, :goal, :sit];

    function initialize() {
        BehaviorDelegate.initialize();
    }

    function onMenu() as Boolean {
        return injectFakeAchievement();
    }

    function onTap(evt as WatchUi.ClickEvent) as Boolean {
        return injectFakeAchievement();
    }

    function onSelect() as Boolean {
        var next = PendingQueue.popNext();
        if (next == null) {
            // Back to the Hall of Shame that pushed this view - DiagnosticView
            // is no longer the default home screen, it's reached from there.
            WatchUi.popView(WatchUi.SLIDE_RIGHT);
            return true;
        }
        WatchUi.pushView(new AchievementView(next), new AchievementDelegate(), WatchUi.SLIDE_LEFT);
        return true;
    }

    private function injectFakeAchievement() as Boolean {
        var tier = TIERS[_tierIndex % TIERS.size()];
        _tierIndex += 1;

        Diag.status = "resolving " + tier + "...";
        System.println("[DIAG] " + Diag.status);
        WatchUi.requestUpdate();
        DebugInjector.get().inject(tier);
        return true;
    }
}
