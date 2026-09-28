import Toybox.Lang;
import Toybox.WatchUi;

class HallOfShameDelegate extends WatchUi.BehaviorDelegate {

    private var _view as HallOfShameView;

    function initialize(view as HallOfShameView) {
        BehaviorDelegate.initialize();
        _view = view;
    }

    function onNextPage() as Boolean {
        _view.next();
        return true;
    }

    function onPreviousPage() as Boolean {
        _view.previous();
        return true;
    }

    function onSelect() as Boolean {
        var item = _view.selected();
        if (item != null) {
            WatchUi.pushView(new AchievementView(item), new AchievementDelegate(), WatchUi.SLIDE_LEFT);
        }
        return true;
    }

    // Debug/diagnostics screen (capability probes, tier injector) is a menu
    // away now that it's no longer the default home screen.
    function onMenu() as Boolean {
        WatchUi.pushView(new DiagnosticView(), new DiagnosticDelegate(), WatchUi.SLIDE_LEFT);
        return true;
    }
}
