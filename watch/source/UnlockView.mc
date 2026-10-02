import Toybox.Graphics;
import Toybox.Lang;
import Toybox.WatchUi;

// MENU from the Hall of Shame (user builds): where this watch stands with a
// hosted server - on trial, unlocked until when, or out of mana - and the
// unlock code + page to type it into on a phone. The code is the one thing
// a user needs from the watch to pay, so it is always one button away, not
// only in the once-a-day System message. On a personal server (no licence
// info ever received) it just says so.
class UnlockView extends WatchUi.View {
    private var _fPx24, _fPx38, _fSk18, _fSk22;

    function initialize() {
        View.initialize();
    }

    function onLayout(dc as Dc) as Void {
        _fPx24 = WatchUi.loadResource(Rez.Fonts.Px24);
        _fPx38 = WatchUi.loadResource(Rez.Fonts.Px38);
        _fSk18 = WatchUi.loadResource(Rez.Fonts.Sk18);
        _fSk22 = WatchUi.loadResource(Rez.Fonts.Sk22);
    }

    function onUpdate(dc as Dc) as Void {
        dc.setColor(Graphics.COLOR_WHITE, Graphics.COLOR_BLACK);
        dc.clear();
        var cx = dc.getWidth() / 2;
        var cy = dc.getHeight() / 2;

        var l = SystemNotice.license();
        if (l == null) {
            dc.setColor(Graphics.COLOR_LT_GRAY, Graphics.COLOR_TRANSPARENT);
            dc.drawText(cx, cy - 30, _fPx24, "PERSONAL SERVER", Graphics.TEXT_JUSTIFY_CENTER);
            dc.drawText(cx, cy + 4, _fSk18, "No unlock needed.", Graphics.TEXT_JUSTIFY_CENTER);
            dc.drawText(cx, cy + 28, _fSk18, "Build " + BuildInfo.STAMP, Graphics.TEXT_JUSTIFY_CENTER);
            return;
        }

        var state = l["state"] instanceof String ? l["state"] as String : "";
        var headline;
        var detail;
        if ("licensed".equals(state) || "capped".equals(state) && l["licensedUntil"] != null) {
            headline = "MANA RESTORED";
            detail = "until " + l["licensedUntil"];
        } else if ("trial".equals(state) || "capped".equals(state)) {
            headline = "FREE TRIAL";
            var d = l["daysLeft"];
            detail = d instanceof Number ? (d <= 1 ? "last day" : d + " days left") : "";
        } else if ("budget".equals(state)) {
            headline = "SYSTEM RESTING";
            detail = "AI paused today";
        } else {
            headline = "OUT OF MANA";
            detail = "unlock to restore AI";
        }

        var top = cy - 92;
        dc.setColor(Palette.tierColorPair(Baseline.tierFromName("out".equals(state) || "expired".equals(state) ? "cursed" : "rare"))[0], Graphics.COLOR_TRANSPARENT);
        dc.drawText(cx, top, _fPx24, headline, Graphics.TEXT_JUSTIFY_CENTER);
        dc.setColor(Graphics.COLOR_LT_GRAY, Graphics.COLOR_TRANSPARENT);
        dc.drawText(cx, top + 30, _fSk18, detail, Graphics.TEXT_JUSTIFY_CENTER);

        dc.setColor(Graphics.COLOR_LT_GRAY, Graphics.COLOR_TRANSPARENT);
        dc.drawText(cx, cy - 22, _fSk18, "YOUR CODE", Graphics.TEXT_JUSTIFY_CENTER);
        dc.setColor(Graphics.COLOR_WHITE, Graphics.COLOR_TRANSPARENT);
        dc.drawText(cx, cy, _fPx38, l["code"] as String, Graphics.TEXT_JUSTIFY_CENTER);

        dc.setColor(Graphics.COLOR_LT_GRAY, Graphics.COLOR_TRANSPARENT);
        dc.drawText(cx, cy + 54, _fSk18, "enter it at", Graphics.TEXT_JUSTIFY_CENTER);
        dc.setColor(Graphics.COLOR_WHITE, Graphics.COLOR_TRANSPARENT);
        dc.drawText(cx, cy + 76, _fSk22, l["url"] instanceof String ? l["url"] as String : "", Graphics.TEXT_JUSTIFY_CENTER);
    }
}

class UnlockDelegate extends WatchUi.BehaviorDelegate {
    function initialize() {
        BehaviorDelegate.initialize();
    }

    function onBack() as Boolean {
        WatchUi.popView(WatchUi.SLIDE_RIGHT);
        return true;
    }

    function onSelect() as Boolean {
        return onBack();
    }
}
