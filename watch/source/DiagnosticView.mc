import Toybox.Attention;
import Toybox.Graphics;
import Toybox.Lang;
import Toybox.System;
import Toybox.UserProfile;
import Toybox.WatchUi;

// Phase 0/1 debug home screen: capability probes + a status line, plus
// (via DiagnosticDelegate) manual triggers to inject fake achievements
// through the real PendingQueue/Notifier pipeline.
//
// Deliberately no auto-firing timer here - an earlier version fired a test
// notification automatically on every view-show, which looped forever on a
// real watch once tapping the notification kept reopening the app. Every
// notification now only ever fires from an explicit button/tap.
class DiagnosticView extends WatchUi.View {

    function initialize() {
        View.initialize();
    }

    // (:debug) so this self-test is stripped from a release build (-r).
    // Guarded to run at most once (onLayout runs once per view instance,
    // but a static flag makes that explicit rather than assumed): fires a
    // single fake legendary achievement through the real pipeline so the
    // whole chain - injector, queue, notify, view render, sound/vibe - can
    // be confirmed from the monkeydo log alone, with nobody clicking buttons.
    (:debug)
    private static var _selfTested as Boolean = false;

    function onLayout(dc as Dc) as Void {
        System.println("[DIAG] Notif:" + yn(hasSymbol(:Notifications)) + " Bg:" + yn(hasSymbol(:Background))
            + " ActMon:" + yn(hasSymbol(:ActivityMonitor)) + " Hist:" + yn(Toybox.UserProfile has :getUserActivityHistory)
            + " Comm:" + yn(hasSymbol(:Communications)) + " Tone:" + yn(Toybox.Attention has :playTone));

        selfTestOnce();
    }

    (:debug)
    private function selfTestOnce() as Void {
        if (_selfTested) {
            return;
        }
        _selfTested = true;
        Diag.status = "resolving...";
        DebugInjector.get().inject(:legendary);
    }

    function onShow() as Void {
    }

    function onUpdate(dc as Dc) as Void {
        dc.setColor(Graphics.COLOR_WHITE, Graphics.COLOR_BLACK);
        dc.clear();

        var w = dc.getWidth();
        var h = dc.getHeight();
        var lines = capabilityLines();
        var font = Graphics.FONT_XTINY;
        var lineHeight = dc.getFontHeight(font) + 2;

        // Centre the whole block vertically so it can't run off the top/bottom
        // of a round screen (which is narrower than the middle at every y
        // except the equator).
        var y = h / 2 - (lines.size() * lineHeight) / 2;
        for (var i = 0; i < lines.size(); i++) {
            dc.drawText(w / 2, y, font, lines[i], Graphics.TEXT_JUSTIFY_CENTER);
            y += lineHeight;
        }
    }

    function onHide() as Void {
    }

    // One line per capability probe, kept short so it survives the circular
    // screen's shrinking chord width near the top/bottom.
    private function capabilityLines() as Array<String> {
        var lines = ["PHASE 0/1"] as Array<String>;

        lines.add("Notif:" + yn(hasSymbol(:Notifications)) + " Bg:" + yn(hasSymbol(:Background)));
        lines.add("ActMon:" + yn(hasSymbol(:ActivityMonitor)) + " Hist:" + yn(Toybox.UserProfile has :getUserActivityHistory));
        lines.add("Comm:" + yn(hasSymbol(:Communications)) + " Tone:" + yn(Toybox.Attention has :playTone));

        lines.add("");
        lines.add("MENU=inject SELECT=view");
        lines.add(Diag.status);

        return lines;
    }

    private function yn(b as Boolean) as String {
        return b ? "Y" : "N";
    }

    private function hasSymbol(sym as Symbol) as Boolean {
        return Toybox has sym;
    }
}
