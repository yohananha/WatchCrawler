#!/usr/bin/env python3
"""Regenerates every pixel font in resources/fonts from its source TTF in tools/fonts.

Usage: regen_fonts.py [out_dir]     (default: resources/fonts)

The character set is CHARS below - add a character there, run this, and every size gets it.
pxNN = Press Start 2P (uppercase only: the watch uppercases text drawn with it), skNN = Silkscreen.
server/Tests/WatchGlyphTests.cs reads the generated .fnt files, so the server's allowlist
(AchievementGenerator.WatchGlyphs) and the watch's built-in lines are checked against them.
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WATCH = os.path.dirname(HERE)

# Printable ASCII, plus a few accented letters that turn up in English text (cafe, naive, fiance).
PUNCT = " !\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"
DIGITS = "0123456789"
UPPER = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
LOWER = UPPER.lower()
ACCENTS_UPPER = "ÉÈÏ"
ACCENTS_LOWER = ACCENTS_UPPER.lower()

FONTS = {
    # Lowercase accents too: Monkey C's toUpper() may leave them lowercase.
    "px": ("PressStart2P-Regular.ttf", [16, 24, 30, 38, 46, 56], PUNCT + DIGITS + UPPER + ACCENTS_UPPER + ACCENTS_LOWER),
    "sk": ("Silkscreen-Regular.ttf", [18, 22], PUNCT + DIGITS + UPPER + LOWER + ACCENTS_UPPER + ACCENTS_LOWER),
}


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(WATCH, "resources", "fonts")
    os.makedirs(out_dir, exist_ok=True)
    for prefix, (ttf, sizes, chars) in FONTS.items():
        chars = "".join(sorted(set(chars), key=ord))
        for size in sizes:
            subprocess.run([sys.executable, os.path.join(HERE, "make_bmfont.py"), os.path.join(HERE, "fonts", ttf),
                            os.path.join(out_dir, f"{prefix}{size}"), str(size), chars], check=True)


if __name__ == "__main__":
    main()
