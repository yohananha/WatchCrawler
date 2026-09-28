import Toybox.Graphics;
import Toybox.Lang;
import Toybox.Math;

// A one-shot outward particle burst (the "gold burst" beats in the design
// spec). Visible only during [0, dur] relative to its own start.
class Burst {
    static function draw(dc as Dc, cx as Number, cy as Number, palette as Array<Number>,
                          n as Number, reach as Number, t as Float, dur as Float) as Void {
        if (t < 0 || t > dur) {
            return;
        }
        var e = Easing.outCubic(Easing.clamp01(t / dur));
        for (var i = 0; i < n; i++) {
            var angle = (i.toFloat() / n) * 2 * Math.PI + (i % 2) * 0.15;
            var r = e * reach * (0.7 + ((i * 37) % 10) / 30.0);
            var s = 4 + (i % 3) * 2;
            var x = cx + (Math.cos(angle) * r).toNumber() - s / 2;
            var y = cy + (Math.sin(angle) * r).toNumber() - s / 2;
            dc.setColor(palette[i % palette.size()], Graphics.COLOR_TRANSPARENT);
            dc.fillRectangle(x, y, s, s);
        }
    }
}
