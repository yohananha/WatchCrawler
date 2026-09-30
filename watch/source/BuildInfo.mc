import Toybox.Lang;

// Overwritten in the gitignored copy by tools/build_personal.sh with the git hash + build time,
// so the diagnostics screen (and error reports, via WatchErr) show which build is actually installed.
(:background)
class BuildInfo {
    static const STAMP = "dev";
}
