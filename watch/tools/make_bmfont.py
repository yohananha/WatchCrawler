#!/usr/bin/env python3
"""Rasterizes a TTF into an AngelCode BMFont .fnt + .png pair Connect IQ's
monkeyc can compile (it requires this bitmap-font format, not raw .ttf -
confirmed empirically against SDK 9.2.0; see watch/source/*.mc font comments).

Usage: make_bmfont.py <ttf_path> <out_basename> <px_size> <chars>

Writes <out_basename>.png (a single-row RGBA atlas, one cell per glyph) and
<out_basename>.fnt (AngelCode text format: info/common/page/chars/char lines).
Connect IQ needs page id=0 and a "chnl=15" per glyph; row height = size.
"""
import os
import sys
from PIL import Image, ImageDraw, ImageFont

def main():
    ttf_path, out_base, px_size, chars = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]

    font = ImageFont.truetype(ttf_path, px_size)
    ascent, descent = font.getmetrics()
    line_height = ascent + descent

    # Measure every glyph first so the atlas is exactly wide enough.
    metrics = []
    total_w = 0
    pad = 1
    for ch in chars:
        bbox = font.getbbox(ch)  # (left, top, right, bottom), relative to origin
        if bbox is None:
            w = 0
            bbox = (0, 0, 0, 0)
        w = bbox[2] - bbox[0]
        advance = font.getlength(ch)
        cell_w = max(1, int(round(advance)))
        metrics.append({"ch": ch, "bbox": bbox, "advance": advance, "cell_w": cell_w})
        total_w += cell_w + pad

    atlas_w = max(1, total_w)
    atlas_h = line_height + pad
    img = Image.new("RGBA", (atlas_w, atlas_h), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    base_name = os.path.basename(out_base)
    fnt_lines = []
    fnt_lines.append('info face="{}" size={} bold=0 italic=0 charset="" unicode=1 stretchH=100 smooth=0 aa=0 padding=0,0,0,0 spacing=1,1'.format(base_name, px_size))
    fnt_lines.append('common lineHeight={} base={} scaleW={} scaleH={} pages=1 packed=0'.format(line_height, ascent, atlas_w, atlas_h))
    fnt_lines.append('page id=0 file="{}.png"'.format(base_name))
    fnt_lines.append('chars count={}'.format(len(chars)))

    x = 0
    for m in metrics:
        ch = m["ch"]
        cell_w = m["cell_w"]
        # Draw with the glyph's own origin at (x, ascent) - i.e. draw at
        # (x, 0) using PIL's top-left text box, which already accounts for
        # ascent via the font metrics.
        draw.text((x, 0), ch, font=font, fill=(255, 255, 255, 255))
        code = ord(ch)
        fnt_lines.append(
            'char id={} x={} y=0 width={} height={} xoffset=0 yoffset=0 xadvance={} page=0 chnl=15'.format(
                code, x, cell_w, atlas_h, cell_w
            )
        )
        x += cell_w + pad

    img.save(out_base + ".png")
    with open(out_base + ".fnt", "w", encoding="utf-8") as f:
        f.write("\n".join(fnt_lines) + "\n")

    print("wrote {}.png ({}x{}) and {}.fnt, {} glyphs, lineHeight={}".format(
        out_base, atlas_w, atlas_h, out_base, len(chars), line_height))

if __name__ == "__main__":
    main()
