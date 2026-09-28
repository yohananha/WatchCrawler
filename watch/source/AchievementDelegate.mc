import Toybox.Lang;
import Toybox.WatchUi;

// Tap/back/select all dismiss the current achievement and show the next
// pending one, or exit back to the diagnostic/home view.
class AchievementDelegate extends WatchUi.BehaviorDelegate {

    function initialize() {
        BehaviorDelegate.initialize();
    }

    function onSelect() as Boolean {
        return advance();
    }

    function onBack() as Boolean {
        return advance();
    }

    function onTap(evt as WatchUi.ClickEvent) as Boolean {
        return advance();
    }

    private function advance() as Boolean {
        var next = PendingQueue.popNext();
        if (next != null) {
            WatchUi.switchToView(new AchievementView(next), new AchievementDelegate(), WatchUi.SLIDE_LEFT);
        } else {
            WatchUi.popView(WatchUi.SLIDE_RIGHT);
        }
        return true;
    }
}
