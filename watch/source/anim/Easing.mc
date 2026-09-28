import Toybox.Lang;
import Toybox.Math;

class Easing {
    static function clamp01(v as Float) as Float {
        if (v < 0.0) { return 0.0; }
        if (v > 1.0) { return 1.0; }
        return v;
    }

    // Overshoots past 1 then settles - the "pop in" bounce.
    static function outBack(p as Float) as Float {
        var c1 = 1.70158;
        var c3 = c1 + 1.0;
        var x = p - 1.0;
        return 1.0 + c3 * x * x * x + c1 * x * x;
    }

    static function outCubic(p as Float) as Float {
        var x = 1.0 - p;
        return 1.0 - x * x * x;
    }
}
