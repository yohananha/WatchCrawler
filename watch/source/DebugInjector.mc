import Toybox.Lang;
import Toybox.System;
import Toybox.WatchUi;

// Shared by DiagnosticDelegate (manual button/tap trigger) and, for
// unattended self-verification when nobody can click the simulator,
// DiagnosticView's one-shot startup probe. Builds a fake core event of the
// given tier and resolves it through the exact same
// PendingQueue + AchievementResolver + Notifier path a real background
// detection would use - including the Phase 2 server round trip when a
// server URL is configured, so this doubles as a server connectivity test
// (the :test tier's "Token Wasted Successfully" joke bypasses the server
// entirely for a pure plumbing check - see ActivityDetector.buildFakeCore).
//
// A singleton instance, not static functions, for the same reason as
// AchievementResolver: its callback needs a `self` to bind to.
class DebugInjector {
    private static var _instance as DebugInjector?;

    static function get() as DebugInjector {
        if (_instance == null) {
            _instance = new DebugInjector();
        }
        return _instance as DebugInjector;
    }

    function initialize() {
    }

    function inject(tier as Symbol) as Void {
        var core = ActivityDetector.buildFakeCore(tier);
        AchievementResolver.get().resolve(core, method(:onResolved));
    }

    function onResolved(achievement as Dictionary) as Void {
        PendingQueue.push(achievement, true);
        var result = Notifier.notifyAchievement(achievement);
        Diag.status = (achievement["tier"] as String) + " queued: " + result;
        System.println("[DIAG] " + Diag.status);
        WatchUi.requestUpdate();
    }
}
