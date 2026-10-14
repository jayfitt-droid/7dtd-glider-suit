#!/usr/bin/env python3
"""Make the 160x160 inventory icon for modArmorGliderSuit.

The game's own item icons are lit 3D renders on a transparent background, so
this icon is one too: the Blender script (unity/blender/build_assets.py) renders
the mod's real wings, open, to a 640x640 master, which is kept in the repo as
tools/icon_master.png. This script trims the master to its
content, fits it to the icon size with a small margin, and gives it the slightly
punchy contrast the vanilla icons have.
"""

import argparse
import os

from PIL import Image, ImageEnhance, ImageFilter

SIZE = 160
MARGIN = 4


def build(master_path):
    master = Image.open(master_path).convert("RGBA")
    box = master.getchannel("A").getbbox()
    if box is None:
        raise SystemExit("the icon master is empty: " + master_path)
    art = master.crop(box)

    room = SIZE - 2 * MARGIN
    scale = min(room / art.width, room / art.height)
    art = art.resize((max(1, round(art.width * scale)), max(1, round(art.height * scale))), Image.LANCZOS)

    alpha = art.getchannel("A")
    colour = art.convert("RGB")
    colour = ImageEnhance.Contrast(colour).enhance(1.15)
    colour = ImageEnhance.Color(colour).enhance(1.15)
    colour = colour.filter(ImageFilter.UnsharpMask(radius=1.0, percent=60, threshold=2))
    art = colour.convert("RGBA")
    art.putalpha(alpha)

    icon = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    icon.alpha_composite(art, ((SIZE - art.width) // 2, (SIZE - art.height) // 2))
    return icon


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    repo = os.path.dirname(here)
    default_out = os.path.join(
        repo, "Mod", "GliderSuit", "UIAtlases", "ItemIconAtlas", "modArmorGliderSuit.png"
    )
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--master", default=os.path.join(here, "icon_master.png"), help="640x640 render from Blender")
    parser.add_argument("--out", default=default_out, help="output PNG path")
    args = parser.parse_args()
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    build(args.master).save(args.out)
    print("wrote " + args.out)


if __name__ == "__main__":
    main()
