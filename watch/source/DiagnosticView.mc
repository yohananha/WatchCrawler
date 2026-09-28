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
    private var _font;

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
        _font = WatchUi.loadResource(Rez.Fonts.Sk18);
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
        var font = _font;
        var lineHeight = dc.getFontHeight(font) + 2;
        var safeRadius = 196; // same tuned value Hall of Shame uses

        // Wrap every logical line to the circle's real width at its own y
        // (a long BG/trig line used to run off the round screen). Two
        // passes: the chord width depends on where the block starts, and
        // the start depends on how many lines wrapping produces.
        var logical = capabilityLines();
        var startY = 70;
        var flat = [] as Array<String>;
        for (var pass = 0; pass < 2; pass++) {
            flat = [] as Array<String>;
            var y = startY;
            for (var i = 0; i < logical.size(); i++) {
                if (logical[i].length() == 0) {
                    flat.add("");
                    y += lineHeight;
                    continue;
                }
                var wrapped = ChordFit.wrapFlow(dc, logical[i], font, y, lineHeight, h / 2, safeRadius);
                for (var j = 0; j < wrapped.size(); j++) {
                    flat.add(wrapped[j]);
                    y += lineHeight;
                }
            }
            startY = h / 2 - (flat.size() * lineHeight) / 2;
            if (startY < 40) { startY = 40; }
        }

        var y2 = startY;
        for (var k = 0; k < flat.size(); k++) {
            dc.drawText(w / 2, y2, font, flat[k], Graphics.TEXT_JUSTIFY_CENTER);
            y2 += lineHeight;
        }
    }

    function onHide() as Void {
    }

    // One line per capability probe, kept short so it survives the circular
    // screen's shrinking chord width near the top/bottom.
    private function capabilityLines() as Array<String> {
        var lines = [] as Array<String>;

        // One flowing line instead of three rows - wrapFlow breaks it to fit.
        lines.add("Notif:" + yn(hasSymbol(:Notifications)) + " Bg:" + yn(hasSymbol(:Background))
            + " ActMon:" + yn(hasSymbol(:ActivityMonitor)) + " Hist:" + yn(Toybox.UserProfile has :getUserActivityHistory)
            + " Comm:" + yn(hasSymbol(:Communications)) + " Tone:" + yn(Toybox.Attention has :playTone));
        lines.add(BgStatus.summary());
        lines.add(BgStatus.triggerSummary());
        lines.add("MENU=inject SELECT=view/back");
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
