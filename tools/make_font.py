#!/usr/bin/env python3
"""Generates content/gfx/font.png + font.json (bitmap font atlas + metrics).

Run from repo root:  python3 tools/make_font.py
"""
import json, os
from PIL import Image, ImageFont, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SIZE = 44
EXTRA = "★●▲▼♥…’‘“”–—×°"
CHARS = [chr(c) for c in range(32, 127)] + list(EXTRA)
FONT_PATH = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"

font = ImageFont.truetype(FONT_PATH, SIZE)
W, H = 1024, 1024
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
glyphs = {}
x, y, rowh = 1, 1, 0
for ch in CHARS:
    bb = font.getbbox(ch)
    gw, gh = bb[2] - bb[0], bb[3] - bb[1]
    # bold glyphs can overhang their advance; pad so letters never overprint
    adv = max(font.getlength(ch), gw + 3)
    if gw <= 0 or gh <= 0:
        glyphs[ch] = {"x": 0, "y": 0, "w": 0, "h": 0, "adv": adv, "ox": 0, "oy": 0}
        continue
    if x + gw + 1 > W:
        x = 1
        y += rowh + 1
        rowh = 0
    d.text((x - bb[0], y - bb[1]), ch, font=font, fill=(255, 255, 255, 255))
    glyphs[ch] = {"x": x, "y": y, "w": gw, "h": gh, "adv": adv, "ox": bb[0], "oy": bb[1]}
    x += gw + 1
    rowh = max(rowh, gh)

img = img.crop((0, 0, W, min(H, y + rowh + 2)))
os.makedirs(os.path.join(ROOT, "content/gfx"), exist_ok=True)
img.save(os.path.join(ROOT, "content/gfx/font.png"))
meta = {"texW": img.width, "texH": img.height, "size": SIZE,
        "lineHeight": int(SIZE * 1.18), "chars": glyphs}
with open(os.path.join(ROOT, "content/gfx/font.json"), "w") as f:
    json.dump(meta, f)
print(f"font.png {img.width}x{img.height}, {len(glyphs)} glyphs")
