import Toybox.Graphics;
import Toybox.Lang;

// Tier colour palettes (0xRRGGBB), condensed from the design spec's [light,dark]
// pairs down to a single fill colour per step (the dark half of each pair is
// reused as the drop-shadow colour everywhere, via Wave.draw).
class Palette {
    // Default look ("Candy" from the design prototype).
    static const CANDY = [0xFF9EC7, 0x9EE7FF, 0xFFE39E, 0xC9A8FF, 0xA8FFC9] as Array<Number>;
    static const GOLD = [0xFFF6B0, 0xFFD83D, 0xFFB020, 0xFFE680] as Array<Number>;
    static const RED = [0xFF4A4A] as Array<Number>;
    static const CYAN_MINT = [0x9EE7FF, 0xA8FFC9] as Array<Number>;
    static const PURPLE_PINK = [0xC9A8FF, 0xFF9EC7] as Array<Number>;
    static const SHADOW = 0x2A1030; // dark plum, used for every drop-shadow

    static function forTier(tier as Symbol) as Array<Number> {
        if (tier == :cursed) { return RED; }
        if (tier == :rare) { return CYAN_MINT; }
        if (tier == :epic) { return PURPLE_PINK; }
        if (tier == :legendary) { return GOLD; }
        return CANDY;
    }

    static function tierWordColor(tier as Symbol) as Array<Number> {
        if (tier == :legendary) { return GOLD; }
        if (tier == :cursed) { return RED; }
        return forTier(tier);
    }

    static function ringColor(tier as Symbol) as Number {
        if (tier == :cursed) { return 0xFF4A4A; }
        if (tier == :rare) { return 0x9EE7FF; }
        if (tier == :epic) { return 0xC9A8FF; }
        if (tier == :legendary) { return 0xFFD83D; }
        return 0xCCCCCC;
    }

    // [light, dark] pairs for a single tier-coloured line + drop shadow
    // (Hall of Shame). Matches the user's Claude Design "Hall of Shame v2"
    // mockup's TIERS palette.
    static function tierColorPair(tier as Symbol) as Array<Number> {
        if (tier == :cursed) { return [0xFF8A8A, 0x7A1F2A]; }
        if (tier == :rare) { return [0x8EEBFF, 0x1F6E85]; }
        if (tier == :epic) { return [0xCDB0FF, 0x4B2A99]; }
        if (tier == :legendary) { return [0xFFE08A, 0x8A6414]; }
        return [0xD4D4DC, 0x56565F];
    }
}
