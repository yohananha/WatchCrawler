import Toybox.Graphics;
import Toybox.Lang;
import Toybox.WatchUi;

// Shown instead of DiagnosticView once Phase 1's debug scaffolding is
// retired: a plain scrollable-by-select list of the last 10 achievements
// (PendingQueue.history()), tier-coloured. Selecting one replays it.
class HallOfShameView extends WatchUi.View {

    private var _items as Array<Dictionary>;
    private var _index as Number = 0;

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
    }

    function onShow() as Void {
    }

    function onUpdate(dc as Dc) as Void {
        dc.setColor(Graphics.COLOR_WHITE, Graphics.COLOR_BLACK);
        dc.clear();

        var w = dc.getWidth();
        var h = dc.getHeight();

        if (_items.size() == 0) {
            dc.drawText(w / 2, h / 2, Graphics.FONT_SMALL, "No achievements yet",
                Graphics.TEXT_JUSTIFY_CENTER | Graphics.TEXT_JUSTIFY_VCENTER);
            return;
        }

        dc.setColor(Graphics.COLOR_LT_GRAY, Graphics.COLOR_BLACK);
        dc.drawText(w / 2, h * 0.14, Graphics.FONT_XTINY,
            "HALL OF SHAME " + (_index + 1) + "/" + _items.size(),
            Graphics.TEXT_JUSTIFY_CENTER);

        var item = _items[_index];
        var tier = item.hasKey("tier") ? item["tier"] as String : "common";
        dc.setColor(colorFor(tier), Graphics.COLOR_BLACK);
        dc.drawText(w / 2, h * 0.36, Graphics.FONT_TINY, tier.toUpper(),
            Graphics.TEXT_JUSTIFY_CENTER);

        dc.setColor(Graphics.COLOR_WHITE, Graphics.COLOR_BLACK);
        var title = item.hasKey("title") ? item["title"] as String : "";
        dc.drawText(w / 2, h * 0.48, Graphics.FONT_SMALL, title, Graphics.TEXT_JUSTIFY_CENTER);

        var stat = item.hasKey("stat") ? item["stat"] as String : "";
        dc.drawText(w / 2, h * 0.62, Graphics.FONT_MEDIUM, stat, Graphics.TEXT_JUSTIFY_CENTER);

        dc.setColor(Graphics.COLOR_DK_GRAY, Graphics.COLOR_BLACK);
        dc.drawText(w / 2, h * 0.84, Graphics.FONT_XTINY, "UP/DOWN browse * SELECT replay",
            Graphics.TEXT_JUSTIFY_CENTER);
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

    private function colorFor(tier as String) as Graphics.ColorValue {
        if (tier.equals("cursed")) { return Graphics.COLOR_RED; }
        if (tier.equals("rare")) { return Graphics.COLOR_BLUE; }
        if (tier.equals("epic")) { return Graphics.COLOR_PURPLE; }
        if (tier.equals("legendary")) { return Graphics.COLOR_YELLOW; }
        return Graphics.COLOR_LT_GRAY;
    }
}
