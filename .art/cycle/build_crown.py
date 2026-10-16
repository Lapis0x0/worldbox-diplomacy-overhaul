"""王朝周期的新生：以原版 iconCrown（28x28）为底逐像素叠加一株新芽。
用法: build_cycle.py iconCrown.png 输出目录"""
import sys
import numpy as np
from PIL import Image

crown = np.asarray(Image.open(sys.argv[1]).convert("RGBA")).copy()
out = sys.argv[2]
N = 28


def paint(img, art, pal):
    """art: 28 行字符串，'.' 不动，其他字符按 pal 上色，' ' 擦掉"""
    for y, row in enumerate(art):
        for x, ch in enumerate(row):
            if ch == ".": continue
            img[y, x] = (0, 0, 0, 0) if ch == " " else (*pal[ch], 255)


def grid(*rows):
    rows = list(rows) + ["." * N] * (N - len(rows))
    return [r.ljust(N, ".") for r in rows]


# 新生：原版皇冠 + 中间长出一株两片叶的新芽
green = {"L": (148, 232, 62), "M": (72, 182, 52), "D": (30, 112, 48), "O": (20, 70, 40)}
renewal = crown.copy()
paint(renewal, grid(
    "",
    "................OLL.........",
    ".........OO....OLLLO........",
    "........OLLO..OLLMDO........",
    "........OLMLO.OLMDO.........",
    ".........ODMLOLMDO..........",
    "..........OODMLDO...........",
    "............OML.............",
    "............OML.............",
    "............DML.............",
    "............DML.............",
    "............DML.............",
    "............OMO.............",
), green)

for name, img in [("renewal", renewal)]:
    Image.fromarray(img).save(f"{out}/vd_cycle_{name}.png")
