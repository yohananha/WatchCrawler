import Toybox.Application;
import Toybox.Lang;
import Toybox.Time;

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

    // Waiting items expire, so opening the app never replays a pile of stale
    // events: real ones after 24 h, debug injects after 30 min, and at most
    // MAX_PENDING wait at once (the oldest are dropped). Items saved by older
    // builds have no timestamp and count as expired.
    private static const MAX_PENDING = 3;
    private static const TTL_SEC = 24 * 3600;
    private static const TTL_TEST_SEC = 30 * 60;

    // isTest: debug-injected item (short expiry).
    static function push(achievement as Dictionary, isTest as Boolean) as Void {
        achievement.put("queuedAt", Time.now().value());
        achievement.put("isTest", isTest);
        var queue = current();
        queue.add(achievement);
        Application.Storage.setValue(QUEUE_KEY, prune(queue));
    }

    static function isEmpty() as Boolean {
        return current().size() == 0;
    }

    // The stored queue with expired/excess items removed (and saved back).
    private static function current() as Array<Dictionary> {
        var stored = Application.Storage.getValue(QUEUE_KEY);
        if (!(stored instanceof Array)) {
            return [] as Array<Dictionary>;
        }
        var pruned = prune(stored as Array<Dictionary>);
        if (pruned.size() != (stored as Array).size()) {
            Application.Storage.setValue(QUEUE_KEY, pruned);
        }
        return pruned;
    }

    private static function prune(queue as Array<Dictionary>) as Array<Dictionary> {
        var now = Time.now().value();
        var kept = [] as Array<Dictionary>;
        for (var i = 0; i < queue.size(); i++) {
            var item = queue[i];
            var at = item.hasKey("queuedAt") ? item["queuedAt"] : null;
            if (!(at instanceof Number)) {
                continue;
            }
            var ttl = (item.hasKey("isTest") && item["isTest"] == true) ? TTL_TEST_SEC : TTL_SEC;
            if (now - (at as Number) <= ttl) {
                kept.add(item);
            }
        }
        while (kept.size() > MAX_PENDING) {
            kept.remove(kept[0]);
        }
        return kept;
    }

    // Removes and returns the oldest pending achievement, recording it into
    // history. Returns null if the queue is empty.
    static function popNext() as Dictionary? {
        var arr = current();
        if (arr.size() == 0) {
            return null;
        }
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
