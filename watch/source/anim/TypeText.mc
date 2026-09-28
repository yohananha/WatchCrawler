import Toybox.Graphics;
import Toybox.Lang;

// Silkscreen typewriter effect for commentary/reward-punchline lines, with a
// blinking block cursor and an optional trailing "hot" word drawn in red
// (used for the reward punchline's last word, e.g. "...NEVER.").
class TypeText {
    static function draw(dc as Dc, text as String, font, cx as Number, y as Number,
                          color as Number, tSinceStart as Float, rate as Float, hotWord as String?) as Void {
        if (tSinceStart < 0) {
            return;
        }
        var n = (tSinceStart * rate).toNumber();
        if (n > text.length()) {
            n = text.length();
        }
        var shown = text.substring(0, n);

        var hotStart = -1;
        if (hotWord != null && shown.length() >= hotWord.length()) {
            var tail = shown.length() - hotWord.length();
            if (shown.substring(tail, shown.length()).equals(hotWord)) {
                hotStart = tail;
            }
        }

        var showCursor = (n < text.length()) || (((tSinceStart * 3).toNumber() % 2) == 0);
        var cursorW = showCursor ? dc.getTextWidthInPixels("_", font) : 0;
        var totalW = dc.getTextWidthInPixels(shown, font) + cursorW;
        var x = cx - totalW / 2;

        if (hotStart >= 0) {
            var pre = shown.substring(0, hotStart);
            var hot = shown.substring(hotStart, shown.length());
            dc.setColor(color, Graphics.COLOR_TRANSPARENT);
            dc.drawText(x, y, font, pre, Graphics.TEXT_JUSTIFY_LEFT);
            x += dc.getTextWidthInPixels(pre, font);
            dc.setColor(Palette.RED[0], Graphics.COLOR_TRANSPARENT);
            dc.drawText(x, y, font, hot, Graphics.TEXT_JUSTIFY_LEFT);
            x += dc.getTextWidthInPixels(hot, font);
        } else {
            dc.setColor(color, Graphics.COLOR_TRANSPARENT);
            dc.drawText(x, y, font, shown, Graphics.TEXT_JUSTIFY_LEFT);
            x += dc.getTextWidthInPixels(shown, font);
        }

        if (showCursor) {
            dc.setColor(color, Graphics.COLOR_TRANSPARENT);
            dc.drawText(x, y, font, "_", Graphics.TEXT_JUSTIFY_LEFT);
        }
    }

    static function typingDuration(text as String, rate as Float) as Float {
        return text.length() / rate;
    }
}
