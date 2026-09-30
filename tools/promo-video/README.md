# Promo video

A ~50 s vertical (1080x1920, 30 fps) social clip: a hook, three achievements (legendary run, rare
step goal, cursed couch day), the Hall of Shame, and a call to action.

`promo.html` is a canvas replica of the watch popup, ported from `watch/source/AchievementView.mc`,
`HallOfShameView.mc` and `anim/*.mc`. It draws with the watch's own bitmap fonts from
`watch/resources/fonts/`. Every frame is a pure function of time (`renderFrame(t)`), and
`render.mjs` steps through the frames in headless Chromium and pipes them to ffmpeg.

```bash
npm install
npx playwright install chromium
node render.mjs            # -> out/watchcrawler-promo.mp4 and out/poster.png
node render.mjs --serve    # preview with a scrubber at http://127.0.0.1:8765/tools/promo-video/promo.html
```

Lines are wrapped to the circle's width at their own height, not a fixed width as on the watch.
Before encoding, `render.mjs` draws every frame and stops if any glyph lands outside the watch's
dot ring. If your copy is too long, it tells you which line and when.

To change the copy, edit `ACHIEVEMENTS`, `HALL_OF_SHAME`, `HOOK` and `CTA` at the top of
`promo.html`. Pacing is set by `SPEED` and `TYPE_RATE`. The pixel fonts only have `A-Z`, `0-9`,
space and `. , ! ? : % / - * ' _ ( )`. The Silkscreen ones (`sk*`) also have lowercase. Any other
character draws as a space.

The 8-bit background music is synthesised by `sound.mjs` (NES-style square, triangle and noise
voices, no samples). There are no sound effects, because the real app is silent while its text
animates. Change the loudness with `VOLUME` in `sound.mjs`. The audio is also saved as
`out/soundtrack.wav`.
