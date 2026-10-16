"""共和国家族的三个政体图标：同一座古典议事厅，商人共和国右下加金币，军事共和国右下加头盔。
议事厅照 AI 草图（draft_republic.png，左上）的造型和配色在 28x28 网格上重画，草图直接缩小柱子会不齐。
头盔取自原版 iconArmyList，金币用原版皇冠的金色调色板。
用法: build_republic.py 输出目录"""
import os
import sys
import numpy as np
from PIL import Image

N = 28
here = os.path.dirname(os.path.abspath(__file__))


def draw(art, pal):
    a = np.zeros((len(art), len(art[0]), 4), np.uint8)
    for y, row in enumerate(art):
        for x, ch in enumerate(row):
            if ch != ".": a[y, x] = (*pal[ch], 255)
    return a


def stamp(dst, patch, x0, y0):
    for y in range(patch.shape[0]):
        for x in range(patch.shape[1]):
            if patch[y, x, 3]: dst[y0 + y, x0 + x] = patch[y, x]


STONE = {"H": (252, 246, 223), "L": (237, 214, 178), "M": (220, 191, 149), "D": (179, 142, 106), "S": (153, 115, 86),
         "O": (87, 55, 34), "V": (55, 40, 30), "P": (106, 59, 36), "R": (214, 64, 52), "r": (163, 25, 27)}
# 17 列宽，中线是第 8 列；放在 x=5、y=3
HALL = [
    "........PRRR.....",
    "........PRrRR....",
    "........PrRr.....",
    "........P........",
    ".......OHO.......",
    ".....OHHLMMO.....",
    "...OHHLLMMMMDO...",
    ".OHHLLLMMMMMMDSO.",
    "OHHHHHHHHHHHHHHMO",
    "OMMMMMMMMMMMMMMSO",
    "..OHHVHHVHHVHHO..",
    "..OLDVLDVLDVLDO..",
    "..OLDVLDVLDVLDO..",
    "..OLDVLDVLDVLDO..",
    "..OLDVLDVLDVLDO..",
    "..OLDVLDVLDVLDO..",
    ".OHHHHHHHHHHHHHO.",
    "OHLLLLLLLLLLLLLMO",
    "OMMMMMMMMMMMMMMSO",
    "OOOOOOOOOOOOOOOOO",
]
GOLD = {"O": (130, 54, 18), "g": (255, 132, 0), "G": (255, 177, 0), "Y": (255, 229, 99), "W": (255, 255, 255)}
COIN = [
    "..OOOOO..",
    ".OYYYYGO.",
    "OYWYYGGgO",
    "OYYgggGgO",
    "OYYgOgGgO",
    "OYYgggGgO",
    "OGYYGGGgO",
    ".OgggggO.",
    "..OOOOO..",
]


def hall():
    a = np.zeros((N, N, 4), np.uint8)
    stamp(a, draw(HALL, STONE), 5, 3)
    return a


if __name__ == "__main__":
    out = sys.argv[1]
    helmet = np.asarray(Image.open(os.path.join(here, "iconArmyList.png")).convert("RGBA"))[7:16, 17:24].copy()
    republic = hall()
    merchant = hall()
    stamp(merchant, draw(COIN, GOLD), 17, 15)
    military = hall()
    stamp(military, helmet, 18, 15)
    for name, img in [("republic", republic), ("merchant_republic", merchant), ("military_republic", military)]:
        Image.fromarray(img).save(os.path.join(out, f"vd_gov_{name}.png"))
