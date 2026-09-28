import Toybox.Graphics;
import Toybox.Lang;
import Toybox.System;
import Toybox.Timer;
import Toybox.WatchUi;

// The pixel-art popup: three scenes (Unlock / Record / Reward), each a
// vertical stack of pixel-font text that "waves in" character by character,
// plus a dot ring and particle bursts. Ported from the approved design spec
// with a few deliberate simplifications (see comments below and the plan
// file) made because this had to be built and verified without anyone able
// to look at the actual screen - noted so they're easy to revisit:
//   - No cross-fade/"wave out": each scene fully replaces the last on cut.
//   - Ring dots are binary lit/unlit, not alpha-faded.
//   - Text wraps to a fixed width, not the circle's exact per-line chord.
//   - Tap/back dismiss the whole achievement rather than skipping scenes.
class AchievementView extends WatchUi.View {

    private var _a as Dictionary;
    private var _tier as Symbol;
    private var _palette as Array<Number>;

    private var _scene as Number = 0;
    private var _cachedScene as Number = -1;
    private var _sceneStartMs as Number = 0;
    private var _timer as Timer.Timer?;

    private var _recordDuration as Float = 4.5;
    private var _rewardDuration as Float = 3.5;
    private var _commentaryLines as Array<String> = [];
    private var _rewardMainLines as Array<String> = [];
    private var _rewardPunch as String?;

    private var _fPx14, _fPx20, _fPx26, _fPx32, _fPx40, _fPx48, _fSk18;

    function initialize(achievement as Dictionary) {
        View.initialize();
        _a = achievement;
        _tier = tierFromString(stringField("tier", "common"));
        _palette = Palette.forTier(_tier);
    }

    function onLayout(dc as Dc) as Void {
        _fPx14 = WatchUi.loadResource(Rez.Fonts.Px14);
        _fPx20 = WatchUi.loadResource(Rez.Fonts.Px20);
        _fPx26 = WatchUi.loadResource(Rez.Fonts.Px26);
        _fPx32 = WatchUi.loadResource(Rez.Fonts.Px32);
        _fPx40 = WatchUi.loadResource(Rez.Fonts.Px40);
        _fPx48 = WatchUi.loadResource(Rez.Fonts.Px48);
        _fSk18 = WatchUi.loadResource(Rez.Fonts.Sk18);
    }

    function onShow() as Void {
        System.println("[ACH] showing " + stringField("tier", "?") + ": " + stringField("title", "?") + " | " + stringField("stat", "?"));
        _scene = 0;
        _cachedScene = -1;
        _sceneStartMs = System.getTimer();
        Fanfare.play(stringField("sound", "chime"));
        Fanfare.vibrate(_tier);

        _timer = new Timer.Timer();
        _timer.start(method(:onTick), 40, true); // ~25fps
    }

    function onTick() as Void {
        WatchUi.requestUpdate();
    }

    function onUpdate(dc as Dc) as Void {
        dc.setColor(Graphics.COLOR_WHITE, Graphics.COLOR_BLACK);
        dc.clear();

        if (_scene != _cachedScene) {
            recomputeSceneLayout(dc);
            _cachedScene = _scene;
        }

        var now = System.getTimer();
        var elapsed = (now - _sceneStartMs) / 1000.0;
        var duration = sceneDuration();

        if (elapsed > duration && _scene < 2) {
            _scene += 1;
            _sceneStartMs = now;
            elapsed = 0.0;
            recomputeSceneLayout(dc);
            _cachedScene = _scene;
            if (_scene == 2) {
                Fanfare.confirm();
            }
        }

        var cx = dc.getWidth() / 2;
        var cy = dc.getHeight() / 2;
        var ringColor = Palette.ringColor(_tier);
        var ringRadius = (dc.getWidth() / 2) - 22;

        if (_scene == 0) {
            Ring.draw(dc, cx, cy, ringRadius, 8, 40, ringColor, 0, elapsed);
            drawUnlock(dc, cx, elapsed);
        } else if (_scene == 1) {
            Ring.draw(dc, cx, cy, ringRadius, 8, 40, ringColor, 1, elapsed);
            drawRecord(dc, cx, elapsed);
        } else {
            Ring.draw(dc, cx, cy, ringRadius, 8, 40, ringColor, 2, elapsed);
            drawReward(dc, cx, elapsed);
        }
    }

    function onHide() as Void {
        if (_timer != null) {
            _timer.stop();
            _timer = null;
        }
    }

    private function sceneDuration() as Float {
        if (_scene == 0) { return 4.2; }
        if (_scene == 1) { return _recordDuration; }
        return _rewardDuration;
    }

    private function recomputeSceneLayout(dc as Dc) as Void {
        if (_scene == 1) {
            var text = stringField("text", "");
            _commentaryLines = TextWrap.wrap(dc, text, _fSk18, 380);
            var typingSec = TypeText.typingDuration(text, 26.0);
            var readBuffer = 1.2 + 0.02 * text.length();
            var d = 1.6 + typingSec + readBuffer;
            if (d < 4.5) { d = 4.5; }
            if (d > 10.0) { d = 10.0; }
            _recordDuration = d;
        } else if (_scene == 2) {
            var reward = stringField("reward", "");
            var splitIdx = reward.find(". ");
            var mainText = reward;
            _rewardPunch = null;
            if (splitIdx != null) {
                mainText = reward.substring(0, (splitIdx as Number) + 1);
                _rewardPunch = trimLeft(reward.substring((splitIdx as Number) + 1, reward.length())).toUpper();
            }
            _rewardMainLines = TextWrap.wrap(dc, mainText.toUpper(), _fPx20, 380);

            if (_rewardPunch != null) {
                var typingSec2 = TypeText.typingDuration(_rewardPunch as String, 20.0);
                var d2 = 1.6 + typingSec2 + 1.5;
                if (d2 < 4.0) { d2 = 4.0; }
                _rewardDuration = d2;
            } else {
                _rewardDuration = 3.5;
            }
        }
    }

    private function drawUnlock(dc as Dc, cx as Number, t as Float) as Void {
        var headline2 = _tier == :cursed ? "UNLOCKED?" : "UNLOCKED";
        Wave.draw(dc, "ACHIEVEMENT", _fPx20, cx, 128, _palette, t - 0.3, 0.04, 0.0);
        Wave.draw(dc, headline2, _fPx26, cx, 166, _palette, t - 0.6, 0.05, 0.0);

        var burstPalette = _tier == :cursed ? Palette.RED : Palette.GOLD;
        var burstCount = _tier == :cursed ? 10 : 18;
        var burstReach = _tier == :cursed ? 90 : 160;
        Burst.draw(dc, cx, 250, burstPalette, burstCount, burstReach, t - 1.6, 1.0);

        var tierWord = stringField("tier", "common").toUpper();
        var shimmer = _tier == :legendary ? 8.0 : 0.0;
        var tierFont = Wave.fitFont(dc, tierWord, [_fPx40, _fPx32, _fPx26], 380);
        Wave.draw(dc, tierWord, tierFont, cx, 250, Palette.tierWordColor(_tier), t - 1.8, 0.06, shimmer);
    }

    private function drawRecord(dc as Dc, cx as Number, t as Float) as Void {
        var hero = stringField("hero", "ACTIVITY").toUpper();
        var title = stringField("title", "").toUpper();
        var stat = stringField("stat", "").toUpper();

        var heroFont = Wave.fitFont(dc, hero, [_fPx48, _fPx40, _fPx32], 380);
        var titleFont = Wave.fitFont(dc, title, [_fPx20, _fPx14], 380);
        var statFont = Wave.fitFont(dc, stat, [_fPx32, _fPx26, _fPx20], 380);

        Wave.draw(dc, hero, heroFont, cx, 90, _palette, t - 0.1, 0.08, 0.0);
        Wave.draw(dc, title, titleFont, cx, 155, _palette, t - 0.5, 0.03, 0.0);
        Wave.draw(dc, stat, statFont, cx, 195, _palette, t - 0.9, 0.04, 0.0);
        Burst.draw(dc, cx, 210, _palette, 12, 110, t - 1.3, 0.8);

        var y = 250;
        var typeStart = 1.6;
        var rate = 26.0;
        var cursorColor = _palette[2 % _palette.size()];
        var cumulativeChars = 0;
        for (var i = 0; i < _commentaryLines.size(); i++) {
            var line = _commentaryLines[i];
            var lineStart = typeStart + cumulativeChars / rate;
            TypeText.draw(dc, line, _fSk18, cx, y, cursorColor, t - lineStart, rate, null);
            cumulativeChars += line.length() + 1;
            y += 26;
        }
    }

    private function drawReward(dc as Dc, cx as Number, t as Float) as Void {
        var heading = _tier == :cursed ? "REWARD?" : "REWARD";
        var headColor = _tier == :cursed ? Palette.RED : Palette.GOLD;
        Wave.draw(dc, heading, _fPx32, cx, 140, headColor, t - 0.1, 0.06, _tier == :legendary ? 8.0 : 0.0);
        Burst.draw(dc, cx, 160, headColor, 14, 140, t - 0.2, 0.9);

        var dividerT = Easing.clamp01((t - 0.6) / 0.3);
        var barW = (Easing.outCubic(dividerT) * 100).toNumber();
        if (barW > 0) {
            dc.setColor(_palette[0], Graphics.COLOR_TRANSPARENT);
            dc.fillRectangle(cx - barW / 2, 178, barW, 3);
        }

        var y = 200;
        for (var i = 0; i < _rewardMainLines.size(); i++) {
            Wave.draw(dc, _rewardMainLines[i], _fPx20, cx, y, _palette, t - (0.8 + i * 0.2), 0.03, 0.0);
            y += 26;
        }

        if (_rewardPunch != null) {
            var hot = lastWord(_rewardPunch as String);
            TypeText.draw(dc, _rewardPunch as String, _fSk18, cx, y + 6, _palette[2 % _palette.size()], t - 1.6, 20.0, hot);
        }
    }

    private function lastWord(s as String) as String {
        var lastSpace = -1;
        for (var i = s.length() - 1; i >= 0; i--) {
            if (s.substring(i, i + 1).equals(" ")) {
                lastSpace = i;
                break;
            }
        }
        if (lastSpace < 0) { return s; }
        return s.substring(lastSpace + 1, s.length());
    }

    // Defensive against a stale achievement dict from a Storage-persisted
    // queue built by an older version of this code (see PendingQueue's
    // QUEUE_KEY comment) - missing/wrong-type fields fall back instead of
    // throwing an "Unexpected Type Error" mid-render.
    private function stringField(key as String, fallback as String) as String {
        if (!_a.hasKey(key)) {
            return fallback;
        }
        var v = _a[key];
        return v instanceof String ? v : fallback;
    }

    private function trimLeft(s as String) as String {
        var start = 0;
        while (start < s.length() && s.substring(start, start + 1).equals(" ")) {
            start += 1;
        }
        return s.substring(start, s.length());
    }

    private function tierFromString(t as String) as Symbol {
        if (t.equals("cursed")) { return :cursed; }
        if (t.equals("rare")) { return :rare; }
        if (t.equals("epic")) { return :epic; }
        if (t.equals("legendary")) { return :legendary; }
        return :common;
    }
}
