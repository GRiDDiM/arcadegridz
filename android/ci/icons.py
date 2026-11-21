"""Writes launcher icons (square + round) from the official ArcadeGridz favicon."""
import os, sys
from PIL import Image, ImageDraw

src, res = sys.argv[1], sys.argv[2]
im = Image.open(src).convert("RGBA")
for density, size in {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}.items():
    d = os.path.join(res, f"mipmap-{density}")
    os.makedirs(d, exist_ok=True)
    bg = Image.new("RGBA", (size, size), (7, 5, 26, 255))
    icon = im.resize((int(size * 0.86), int(size * 0.86)), Image.LANCZOS)
    bg.alpha_composite(icon, ((size - icon.width) // 2, (size - icon.height) // 2))
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=size // 5, fill=255)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(bg, (0, 0), mask)
    out.save(os.path.join(d, "ic_launcher.png"))
print("icons written")
