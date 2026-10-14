"""AI 草图缩出来的衰落、崩溃：颜色贴到原版皇冠调色板（压暗版），中间宝石换成原版图案。
用法: fix_ai.py 输入.png 输出.png 压暗比例 色调R,G,B 宝石亮度 宝石x,y [fx]"""
import sys
import numpy as np
from PIL import Image

src, dst, k, tone, gem_dark = sys.argv[1], sys.argv[2], float(sys.argv[3]), tuple(map(int, sys.argv[4].split(","))), float(sys.argv[5])
GOLD = [(255, 255, 255), (255, 252, 238), (255, 229, 99), (255, 177, 0), (255, 132, 0), (183, 64, 7), (130, 54, 18)]
GEM = [(253, 132, 255), (195, 0, 198), (120, 0, 122)]
fade = lambda c: tuple(int(v * (1 - k) + t * k) for v, t in zip(c, tone))
dark = lambda c: tuple(int(v * gem_dark) for v in c)
gold = [fade(c) for c in GOLD]
gem = [dark(c) for c in GEM]
a = np.asarray(Image.open(src).convert("RGBA")).astype(int)
N = a.shape[0]


def is_gem(c):
    r, g, b = c
    return b > g + 12 and r > g + 20


GX, GY = map(int, sys.argv[6].split(","))
KEEP_FX = len(sys.argv) > 7 and sys.argv[7] == "fx"


def is_fx(c):
    """保留原色的像素：绿叶；加 fx 时还有火（饱和的红橙黄）和烟（灰）"""
    r, g, b = c
    if g > r + 10: return True  # 绿色（叶子）保留原色
    if not KEEP_FX: return False
    fire = r > 190 and r - b > 110
    smoke = max(c) - min(c) < 24 and max(c) < 140
    return fire or smoke


out = a.copy()
near_gem = lambda x, y: GX - 2 <= x <= GX + 3 and GY - 2 <= y <= GY + 3
for y in range(N):
    for x in range(N):
        if not a[y, x, 3]: continue
        c = tuple(a[y, x, :3])
        if is_fx(c) or (KEEP_FX and is_gem(c) and not near_gem(x, y)): continue
        if near_gem(x, y) and is_gem(c):
            out[y, x, :3] = gold[1]  # 先抹掉 AI 画的宝石，铺成宝石底下的浅金
            continue
        # 宝石区域以外，只有鲜艳的品红（两侧的小宝石）才用宝石色，暗紫的阴影归到金色
        vivid = c[0] > 140 and c[2] > 110 and c[1] < 90
        out[y, x, :3] = min(gem if vivid else gold, key=lambda p: sum((u - v) ** 2 for u, v in zip(p, c)))

# 原版的菱形宝石图案（0 白 1 粉 2 品红 3 深紫），左上角在 (GX-1, GY-1)
pattern = {(0, 0): 1, (1, 0): 2, (-1, 1): 1, (0, 1): 0, (1, 1): 3, (2, 1): 2,
           (-1, 2): 2, (0, 2): 3, (1, 2): 1, (2, 2): 3, (0, 3): 2, (1, 3): 3}
cols = [(255, 255, 255)] + GEM
for (dx, dy), i in pattern.items():
    out[GY - 1 + dy, GX + dx] = (*(dark(cols[i]) if i else fade(cols[0])), 255)
Image.fromarray(out.astype(np.uint8)).save(dst)
