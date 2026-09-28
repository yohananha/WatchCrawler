import Toybox.Application;
import Toybox.Lang;

// Storage-backed queue of achievements waiting to be shown in the
// foreground, plus a short rolling history (the future Hall of Shame).
(:background)
class PendingQueue {
    private static const MAX_HISTORY = 10;

    // "v2": bumped once, deliberately, when the achievement dict shape
    // changed (added "hero") - so any stale queue/history from an older
    // build (different dict shape) is invisible instead of crashing the
    // next render. Bump again if the shape changes again.
    private static const QUEUE_KEY = "pendingQueueV2";
    private static const HISTORY_KEY = "achievementHistoryV2";

    static function push(achievement as Dictionary) as Void {
        var queue = Application.Storage.getValue(QUEUE_KEY);
        if (queue == null) {
            queue = [] as Array<Dictionary>;
        }
        (queue as Array<Dictionary>).add(achievement);
        Application.Storage.setValue(QUEUE_KEY, queue);
    }

    static function isEmpty() as Boolean {
        var queue = Application.Storage.getValue(QUEUE_KEY);
        return queue == null || (queue as Array).size() == 0;
    }

    // Removes and returns the oldest pending achievement, recording it into
    // history. Returns null if the queue is empty.
    static function popNext() as Dictionary? {
        var queue = Application.Storage.getValue(QUEUE_KEY);
        if (queue == null || (queue as Array).size() == 0) {
            return null;
        }
        var arr = queue as Array<Dictionary>;
        var next = arr[0];
        arr.remove(arr[0]);
        Application.Storage.setValue(QUEUE_KEY, arr);
        addToHistory(next);
        return next;
    }

    static function history() as Array<Dictionary> {
        var h = Application.Storage.getValue(HISTORY_KEY);
        return h == null ? ([] as Array<Dictionary>) : (h as Array<Dictionary>);
    }

    private static function addToHistory(achievement as Dictionary) as Void {
        var h = history();
        h.add(achievement);
        while (h.size() > MAX_HISTORY) {
            h.remove(h[0]);
        }
        Application.Storage.setValue(HISTORY_KEY, h);
    }
}
