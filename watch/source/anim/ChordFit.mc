import Toybox.Graphics;
import Toybox.Lang;
import Toybox.Math;

// Ports the real per-line chord-width auto-fit algorithm from the user's
// Claude Design mockup ("Hall of Shame v2") - the actual fix for round-
// screen text clipping, more precise than TextWrap's fixed-maxWidth
// approximation: for a given vertical band, tries each font (biggest
// first) and each line count, measuring every candidate line against the
// circle's ACTUAL chord width at that line's own y (narrower near the top/
// bottom, wider at the equator), and returns the biggest font/fewest-lines
// combo that fits without clipping.
class ChordFit {

    // fonts: candidates, biggest first. Returns a Dictionary:
    // {"lines" => Array<{"text","top"}>, "font" => the chosen font}.
    static function fitBlock(dc as Dc, text as String, top as Number, bottom as Number,
                              fonts as Array, cy as Number, safeRadius as Number) as Dictionary {
        var words = TextWrap.splitWords(text);
        if (words.size() == 0) {
            return { "lines" => [], "font" => fonts[fonts.size() - 1] };
        }

        for (var fi = 0; fi < fonts.size(); fi++) {
            var font = fonts[fi];
            var fontH = dc.getFontHeight(font);
            var lineHeight = fontH + 4;
            var maxLines = ((bottom - top) / lineHeight).toNumber();
            if (maxLines < 1) {
                maxLines = 1;
            }

            for (var n = 1; n <= maxLines; n++) {
                var start = ((top + bottom) / 2 - (n * lineHeight) / 2).toNumber();
                var tops = [] as Array<Number>;
                for (var i = 0; i < n; i++) {
                    tops.add(start + i * lineHeight);
                }

                var packed = packWords(dc, words, font, tops, fontH, cy, safeRadius);
                if (packed != null) {
                    var lines = [] as Array<Dictionary>;
                    var packedLines = packed as Array<String>;
                    for (var li = 0; li < packedLines.size(); li++) {
                        lines.add({ "text" => packedLines[li], "top" => tops[li] });
                    }
                    return { "lines" => lines, "font" => font };
                }
            }
        }

        // Nothing fit cleanly (pathological input) - smallest font, one
        // line, may still clip at the very edges. Better than crashing.
        return { "lines" => [{ "text" => text, "top" => top }], "font" => fonts[fonts.size() - 1] };
    }

    // Single-line variant (position/tier/stat/hint text): biggest font from
    // `fonts` whose full measured width fits the chord at `y`.
    static function fitLine(dc as Dc, text as String, y as Number, fonts as Array, cy as Number, safeRadius as Number) {
        for (var i = 0; i < fonts.size(); i++) {
            var font = fonts[i];
            var h = dc.getFontHeight(font);
            var w = chordWidth(y, y + h, cy, safeRadius);
            if (dc.getTextWidthInPixels(text, font) <= w) {
                return font;
            }
        }
        return fonts[fonts.size() - 1];
    }

    // Greedy-packs `words` into `tops.size()` lines, one per given top,
    // using each line's own chord width as its cap - returns null if the
    // words don't fit into exactly that many lines.
    private static function packWords(dc as Dc, words as Array<String>, font, tops as Array<Number>,
                                       fontH as Number, cy as Number, safeRadius as Number) as Array<String>? {
        var out = [] as Array<String>;
        var wi = 0;

        for (var si = 0; si < tops.size(); si++) {
            var maxW = chordWidth(tops[si], tops[si] + fontH, cy, safeRadius);
            var line = "";
            while (wi < words.size()) {
                var candidate = line.length() == 0 ? words[wi] : (line + " " + words[wi]);
                if (dc.getTextWidthInPixels(candidate, font) <= maxW) {
                    line = candidate;
                    wi += 1;
                } else {
                    break;
                }
            }
            if (line.length() == 0) {
                return null; // This line count/size can't even fit one word - try more lines or a smaller font.
            }
            out.add(line);
        }

        return wi == words.size() ? out : null; // Only a match if every word got placed.
    }

    private static function chordWidth(y0 as Number, y1 as Number, cy as Number, safeRadius as Number) as Number {
        var dy = (y0 - cy).abs();
        var dy1 = (y1 - cy).abs();
        if (dy1 > dy) {
            dy = dy1;
        }
        if (dy >= safeRadius) {
            return 0;
        }
        return (2 * Math.sqrt(safeRadius * safeRadius - dy * dy)).toNumber();
    }
}
