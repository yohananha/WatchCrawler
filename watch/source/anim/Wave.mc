import Toybox.Graphics;
import Toybox.Lang;

// Draws text centred at (cx, y) with a per-character drop-in-and-bounce
// animation (the design spec's "wave in"), cycling through a colour palette,
// plus a small drop shadow for the pixel-art look. Scenes fully replace their
// content on transition (see AchievementView), so there's no "wave out" -
// only entry.
class Wave {
    // tSinceStart: seconds since this whole line should start appearing
    // (negative = not yet, draws nothing). stagger: seconds between each
    // character's own start. shimmerHz>0 cycles the palette index over time
    // (used for the legendary tier word).
    static function draw(dc as Dc, text as String, font, cx as Number, y as Number,
                          palette as Array<Number>, tSinceStart as Float, stagger as Float, shimmerHz as Float) as Void {
        if (tSinceStart < 0 || text.length() == 0) {
            return;
        }
        var fullWidth = dc.getTextWidthInPixels(text, font);
        var x = cx - fullWidth / 2;
        var shimmerStep = shimmerHz > 0.0 ? (tSinceStart * shimmerHz).toNumber() : 0;

        for (var i = 0; i < text.length(); i++) {
            var ch = text.substring(i, i + 1);
            var w = dc.getTextWidthInPixels(ch, font);
            var localT = tSinceStart - i * stagger;
            if (localT >= 0) {
                var pin = Easing.clamp01(localT / 0.35);
                var e = Easing.outBack(pin);
                var dropPx = ((1.0 - e) * 26).toNumber();
                var color = palette[(i + shimmerStep) % palette.size()];

                dc.setColor(Palette.SHADOW, Graphics.COLOR_TRANSPARENT);
                dc.drawText(x + 2, y + dropPx + 2, font, ch, Graphics.TEXT_JUSTIFY_LEFT);
                dc.setColor(color, Graphics.COLOR_TRANSPARENT);
                dc.drawText(x, y + dropPx, font, ch, Graphics.TEXT_JUSTIFY_LEFT);
            }
            x += w;
        }
    }

    // Total time (seconds) until the last character has finished popping in.
    static function settleTime(text as String, stagger as Float) as Float {
        return (text.length() - 1) * stagger + 0.35;
    }

    // Press Start 2P is ~monospace at roughly 1 char-width per point size, so
    // a fixed font size overflows the 454px screen for anything longer than
    // ~12-14 characters (found the hard way: "5.0 KM . 4:40/KM" at size 32
    // needs ~560px). Picks the largest font from `candidates` (ordered
    // biggest first) whose rendered width fits maxWidth, falling back to the
    // smallest if even that doesn't fit.
    static function fitFont(dc as Dc, text as String, candidates as Array, maxWidth as Number) {
        for (var i = 0; i < candidates.size(); i++) {
            if (dc.getTextWidthInPixels(text, candidates[i]) <= maxWidth) {
                return candidates[i];
            }
        }
        return candidates[candidates.size() - 1];
    }
}
