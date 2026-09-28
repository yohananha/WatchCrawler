import Toybox.Graphics;
import Toybox.Lang;
import Toybox.WatchUi;

// Ported from the user's Claude Design mockup ("Hall of Shame v2"): pixel
// fonts (matching the achievement popup) with every line fit against the
// circle's REAL chord width at its own y via ChordFit - not TextWrap's
// fixed-maxWidth approximation, which is what clipped "Personal Record,
// Allegedly" on the real device earlier. Position/tier at top, title
// auto-fit in the middle band, stat below it, control hints at the bottom.
//
// Font sizes are coarser than the mockup's (32/28/24/20/18/16 continuous
// vs our discrete Px16/24/30/38/46/56 bitmap fonts, generated per-size
// since Connect IQ can't scale text) - close in spirit, not pixel-identical.
class HallOfShameView extends WatchUi.View {
    private const SAFE_RADIUS = 196; // matches the mockup's tuned value against the dot ring

    private var _items as Array<Dictionary>;
    private var _index as Number = 0;

    private var _fPx16, _fPx24, _fPx30, _fPx38, _fSk18, _fSk22;

    function initialize() {
        View.initialize();
        _items = PendingQueue.history();
        // Most recent first.
        var reversed = [] as Array<Dictionary>;
        for (var i = _items.size() - 1; i >= 0; i--) {
            reversed.add(_items[i]);
        }
        _items = reversed;
    }

    function onLayout(dc as Dc) as Void {
        _fPx16 = WatchUi.loadResource(Rez.Fonts.Px16);
        _fPx24 = WatchUi.loadResource(Rez.Fonts.Px24);
        _fPx30 = WatchUi.loadResource(Rez.Fonts.Px30);
        _fPx38 = WatchUi.loadResource(Rez.Fonts.Px38);
        _fSk18 = WatchUi.loadResource(Rez.Fonts.Sk18);
        _fSk22 = WatchUi.loadResource(Rez.Fonts.Sk22);
    }

    function onShow() as Void {
    }

    function onUpdate(dc as Dc) as Void {
        dc.setColor(Graphics.COLOR_WHITE, Graphics.COLOR_BLACK);
        dc.clear();

        var cx = dc.getWidth() / 2;
        var cy = dc.getHeight() / 2;

        if (_items.size() == 0) {
            drawEmptyState(dc, cx, cy);
            return;
        }

        var item = _items[_index];
        var tier = item.hasKey("tier") ? item["tier"] as String : "common";
        var pair = Palette.tierColorPair(Baseline.tierFromName(tier));

        drawShadowedLine(dc, "" + (_index + 1) + "/" + _items.size(), cx, 48, _fPx16, [0x9A9AA4, 0x000000]);
        drawShadowedLine(dc, tier.toUpper(), cx, 78, _fPx24, pair);

        var title = item.hasKey("title") ? item["title"] as String : "";
        var fit = ChordFit.fitBlock(dc, title.toUpper(), 114, 272, [_fPx38, _fPx30, _fPx24, _fPx16], cy, SAFE_RADIUS);
        var titleFont = fit["font"];
        var lines = fit["lines"] as Array<Dictionary>;
        for (var i = 0; i < lines.size(); i++) {
            var l = lines[i];
            drawShadowedLine(dc, l["text"] as String, cx, l["top"] as Number, titleFont, pair);
        }

        var stat = item.hasKey("stat") ? item["stat"] as String : "";
        var statFont = ChordFit.fitLine(dc, stat, 286, [_fSk22, _fSk18], cy, SAFE_RADIUS);
        dc.setColor(0xECECF2, Graphics.COLOR_TRANSPARENT);
        dc.drawText(cx, 286, statFont, stat, Graphics.TEXT_JUSTIFY_CENTER);

        drawHints(dc, cx, cy, ["SELECT replay", "MENU diagnostics"], 0xB4B4BE);
    }

    function onHide() as Void {
    }

    function next() as Void {
        if (_items.size() == 0) { return; }
        _index = (_index + 1) % _items.size();
        WatchUi.requestUpdate();
    }

    function previous() as Void {
        if (_items.size() == 0) { return; }
        _index = (_index - 1 + _items.size()) % _items.size();
        WatchUi.requestUpdate();
    }

    function selected() as Dictionary? {
        if (_items.size() == 0) { return null; }
        return _items[_index];
    }

    private function drawEmptyState(dc as Dc, cx as Number, cy as Number) as Void {
        var fit = ChordFit.fitBlock(dc, "NO ACHIEVEMENTS YET", 140, 290, [_fPx38, _fPx30, _fPx24, _fPx16], cy, SAFE_RADIUS);
        var font = fit["font"];
        var lines = fit["lines"] as Array<Dictionary>;
        var grey = [0xD4D4DC, 0x56565F];
        for (var i = 0; i < lines.size(); i++) {
            var l = lines[i];
            drawShadowedLine(dc, l["text"] as String, cx, l["top"] as Number, font, grey);
        }
        drawHints(dc, cx, cy, ["MENU diagnostics"], 0xB4B4BE);
    }

    private function drawHints(dc as Dc, cx as Number, cy as Number, hints as Array<String>, color as Number) as Void {
        var y = 330;
        for (var i = 0; i < hints.size(); i++) {
            var font = ChordFit.fitLine(dc, hints[i], y, [_fSk18], cy, SAFE_RADIUS);
            dc.setColor(color, Graphics.COLOR_TRANSPARENT);
            dc.drawText(cx, y, font, hints[i], Graphics.TEXT_JUSTIFY_CENTER);
            y += 28;
        }
    }

    // colorPair: [light, dark] - light is the fill, dark is a 2px drop
    // shadow (matches the achievement popup's pixel-art look).
    private function drawShadowedLine(dc as Dc, text as String, cx as Number, y as Number, font, colorPair as Array<Number>) as Void {
        dc.setColor(colorPair[1], Graphics.COLOR_TRANSPARENT);
        dc.drawText(cx + 1, y + 2, font, text, Graphics.TEXT_JUSTIFY_CENTER);
        dc.setColor(colorPair[0], Graphics.COLOR_TRANSPARENT);
        dc.drawText(cx, y, font, text, Graphics.TEXT_JUSTIFY_CENTER);
    }
}
