import Toybox.Graphics;
import Toybox.Lang;

// 9x9 pixel-art icons per tier (star for legendary/epic, diamond for rare,
// skull for cursed, nothing for common - deliberately underwhelming), drawn
// as filled squares so they scale by choosing the cell size. `pair` is
// [light, dark]: the dark shade is a one-cell-offset drop shadow, same look
// as the pixel text.
class TierIcon {
    private static const STAR = [
        "....1....",
        "...111...",
        "111111111",
        ".1111111.",
        "..11111..",
        "..11111..",
        ".111.111.",
        ".11...11.",
        "1.......1",
    ];
    private static const DIAMOND = [
        "....1....",
        "...111...",
        "..11111..",
        ".1111111.",
        "111111111",
        ".1111111.",
        "..11111..",
        "...111...",
        "....1....",
    ];
    private static const SKULL = [
        ".1111111.",
        "111111111",
        "11.111.11",
        "11.111.11",
        "111111111",
        ".1111111.",
        "..1.1.1..",
        "..1.1.1..",
        ".........",
    ];

    static const GRID = 9;

    // Whether this tier has an icon at all.
    static function hasIcon(tier as Symbol) as Boolean {
        return tier != :common;
    }

    // Draws the icon horizontally centred on cx, top edge at `top`.
    static function draw(dc as Dc, tier as Symbol, cx as Number, top as Number, cell as Number, pair as Array<Number>) as Void {
        if (!hasIcon(tier) || cell < 1) {
            return;
        }
        var grid = STAR;
        if (tier == :rare) { grid = DIAMOND; }
        else if (tier == :cursed) { grid = SKULL; }

        var left = cx - (GRID * cell) / 2;
        var shadow = cell > 3 ? cell / 2 : 1;
        paint(dc, grid, left + shadow, top + shadow, cell, pair[1]);
        paint(dc, grid, left, top, cell, pair[0]);
    }

    private static function paint(dc as Dc, grid as Array<String>, left as Number, top as Number, cell as Number, color as Number) as Void {
        dc.setColor(color, Graphics.COLOR_TRANSPARENT);
        for (var r = 0; r < GRID; r++) {
            var row = grid[r];
            for (var c = 0; c < GRID; c++) {
                if (row.substring(c, c + 1).equals("1")) {
                    dc.fillRectangle(left + c * cell, top + r * cell, cell, cell);
                }
            }
        }
    }
}
