import Toybox.Lang;

// Phase 0 only: a tiny shared status line so we can see on the watch screen
// (and, via System.println, in the monkeydo log) what input actually reached
// the app and what Notifier did with it, since a silently-failed API call
// looks identical to a button that isn't wired up at all.
class Diag {
    static var status as String = "(no input yet)";
}
