#!/usr/bin/env python3
"""Generates tiny placeholder textures: content/gfx/particle.png, pixel.png.

Run from repo root:  python3 tools/make_gfx.py
"""
import os, math
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
out = os.path.join(ROOT, "content/gfx")
os.makedirs(out, exist_ok=True)

# soft round particle (confetti/sparks)
N = 24
img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
px = img.load()
for yy in range(N):
    for xx in range(N):
        dx, dy = xx - N / 2 + 0.5, yy - N / 2 + 0.5
        r = math.hypot(dx, dy) / (N / 2)
        a = max(0.0, 1.0 - r)
        px[xx, yy] = (255, 255, 255, int(255 * (a * a)))
img.save(os.path.join(out, "particle.png"))

# 4x4 white pixel (stretched for flat UI shapes)
Image.new("RGBA", (4, 4), (255, 255, 255, 255)).save(os.path.join(out, "pixel.png"))
print("particle.png, pixel.png written")
