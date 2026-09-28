import Toybox.Graphics;
import Toybox.Lang;
import Toybox.Math;

// The dot ring around the screen edge. Behaviour depends on the scene phase:
// 0 (Unlock) fills clockwise from 12 o'clock, 1 (Record) is a chasing comet
// used as a countdown/progress feel, 2 (Reward) twinkles. Simplified from the
// design spec: binary lit/unlit per dot rather than smooth alpha fades (kept
// the visual read - chunky pixel dots - without needing alpha blending math
// verified without a screen to look at).
class Ring {
    static function draw(dc as Dc, cx as Number, cy as Number, radius as Number, dotSize as Number,
                          count as Number, color as Number, phase as Number, t as Float) as Void {
        dc.setColor(color, Graphics.COLOR_TRANSPARENT);
        for (var i = 0; i < count; i++) {
            var angle = (i.toFloat() / count) * 2 * Math.PI - Math.PI / 2;
            var lit = false;

            if (phase == 0) {
                var fill = Easing.outCubic(Easing.clamp01(t / 0.9));
                lit = (i.toFloat() / count) < fill;
            } else if (phase == 1) {
                var d = ((i - (t * 14).toNumber()) % count + count) % count;
                lit = d < 8;
            } else {
                var s = Math.sin(t * 6 + i * 2.1);
                lit = s > 0.2;
            }

            if (!lit) {
                continue;
            }
            var x = cx + (Math.cos(angle) * radius).toNumber() - dotSize / 2;
            var y = cy + (Math.sin(angle) * radius).toNumber() - dotSize / 2;
            dc.fillRectangle(x, y, dotSize, dotSize);
        }
    }
}
