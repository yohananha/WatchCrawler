import Toybox.Graphics;
import Toybox.Lang;

// Greedy word-wrap against a fixed max width. The design spec wraps to the
// circle's actual chord width at each line's y; this uses one fixed width
// instead (content sits near vertical centre where the chord is close to
// full width anyway) - a deliberate simplification to keep the layout code
// predictable without a screen to visually check the exact wrap points on.
class TextWrap {
    static function wrap(dc as Dc, text as String, font, maxWidth as Number) as Array<String> {
        var words = splitWords(text);
        var lines = [] as Array<String>;
        var line = "";

        for (var i = 0; i < words.size(); i++) {
            var word = words[i];
            var candidate = line.length() == 0 ? word : (line + " " + word);
            if (dc.getTextWidthInPixels(candidate, font) > maxWidth && line.length() > 0) {
                lines.add(line);
                line = word;
            } else {
                line = candidate;
            }
        }
        if (line.length() > 0) {
            lines.add(line);
        }
        if (lines.size() == 0) {
            lines.add("");
        }
        return lines;
    }

    // Public: reused by ChordFit for its own word-by-word packing.
    static function splitWords(text as String) as Array<String> {
        var words = [] as Array<String>;
        var current = "";
        for (var i = 0; i < text.length(); i++) {
            var ch = text.substring(i, i + 1);
            if (ch.equals(" ")) {
                if (current.length() > 0) {
                    words.add(current);
                    current = "";
                }
            } else {
                current += ch;
            }
        }
        if (current.length() > 0) {
            words.add(current);
        }
        return words;
    }
}
