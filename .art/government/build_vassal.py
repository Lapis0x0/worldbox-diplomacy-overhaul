"""附庸家族的三个政体图标，都比对应的独立政体小一号：
公国是三尖小皇冠，自治领是小号议事厅，行省是带城墙的城门。附庸身份由副标题里的宗主名表明，图标不另加标记。
小皇冠取自 draft_vassal_chain.png 左上，去掉挂在底下的锁链（试过用锁链当附庸标记，像项链，没用），
金色贴回原版皇冠的调色板，中间换成原版的宝石图案；
城门取自 draft_vassal.png 左下，去掉插在城楼上的旗（试过用宗主旗当标记，看不出意思）；
议事厅照共和国的那座缩小重画。
用法: build_vassal.py 草图缩成的公国.png 草图缩成的行省.png 输出目录"""
import os
import sys
import numpy as np
from PIL import Image

from build_republic import STONE, draw, stamp

N = 28
duchy_src = np.asarray(Image.open(sys.argv[1]).convert("RGBA")).copy()
province_src = np.asarray(Image.open(sys.argv[2]).convert("RGBA")).copy()
out = sys.argv[3]

def centered(img):
    """把图案挪到 28x28 画布正中"""
    ys, xs = np.nonzero(img[..., 3])
    dx = (N - 1 - xs.max() - xs.min()) // 2
    dy = (N - 1 - ys.max() - ys.min()) // 2
    res = np.zeros_like(img)
    stamp(res, img[ys.min():ys.max() + 1, xs.min():xs.max() + 1], xs.min() + dx, ys.min() + dy)
    return res


# 原版皇冠的金色和宝石色
GOLD = [(255, 255, 255), (255, 252, 238), (255, 229, 99), (255, 177, 0), (255, 132, 0), (183, 64, 7), (130, 54, 18)]
GEM = [(253, 132, 255), (195, 0, 198), (120, 0, 122)]


def snap(c):
    vivid = c[0] > 140 and c[2] > 110 and c[1] < 90
    return min(GEM if vivid else GOLD, key=lambda p: sum((u - v) ** 2 for u, v in zip(p, c)))


def above_chain(img, keep_last):
    """草图里锁链垂在物体下方，中间和物体之间有一行空隙；取空隙以上的部分。
    keep_last: 空隙上面那一行也要（行省的底边），其中锁链的金色像素改成相邻的轮廓色"""
    ys, _ = np.nonzero(img[..., 3])
    gap = next(y for y in range(ys.min(), N) if not img[y, 11:17, 3].any())
    res = img.copy()
    res[gap - (0 if keep_last else 1):] = 0
    if keep_last:
        row = res[gap - 1]
        for x in range(N):
            r, g, b = (int(v) for v in row[x, :3])
            if row[x, 3] and r > 200 and g > 150 and b < 90:
                row[x] = row[x - 1]
    return res


# 公国
crown = above_chain(duchy_src, False)
for y in range(N):
    for x in range(N):
        if crown[y, x, 3]:
            crown[y, x, :3] = snap(tuple(int(v) for v in crown[y, x, :3]))
# 草图里正面宝石的位置，贴原版皇冠的菱形宝石（0 白 1 粉 2 品红 3 深紫），两侧小宝石用品红
GEM_PATTERN = {(0, 0): 1, (1, 0): 2, (-1, 1): 1, (0, 1): 0, (1, 1): 3, (2, 1): 2,
               (-1, 2): 2, (0, 2): 3, (1, 2): 1, (2, 2): 3, (0, 3): 2, (1, 3): 3}
GEM_COLORS = [(255, 255, 255)] + GEM
for (dx, dy), i in GEM_PATTERN.items():
    crown[9 + dy, 12 + dx] = (*GEM_COLORS[i], 255)
for x, y in [(6, 10), (19, 10)]:
    crown[y, x] = (*GEM[1], 255)
duchy = centered(crown)

# 自治领：15 列宽的议事厅（共和国那座是 17 列），没有屋顶红旗
SMALL_HALL = [
    "......OHO......",
    "....OHHLMMO....",
    "..OHHLLMMMMDO..",
    "OHHLLLMMMMMMDSO",
    "OHHHHHHHHHHHHMO",
    "OMMMMMMMMMMMMSO",
    ".OHHVHHVHHVHHO.",
    ".OLDVLDVLDVLDO.",
    ".OLDVLDVLDVLDO.",
    ".OLDVLDVLDVLDO.",
    ".OLDVLDVLDVLDO.",
    "OHHHHHHHHHHHHHO",
    "OHLLLLLLLLLLLMO",
    "OOOOOOOOOOOOOOO",
]
dominion = np.zeros((N, N, 4), np.uint8)
stamp(dominion, draw(SMALL_HALL, STONE), 0, 0)
dominion = centered(dominion)

# 行省：城楼顶在第 9 行（第 13 列以左）和第 11 行（第 14 列以右，再往上是旗），城楼上残留的旗尖金色去掉
gate = np.zeros_like(province_src)
for y in range(N):
    for x in range(N):
        if province_src[y, x, 3] and (y >= 11 or (x <= 13 and y >= 9)):
            r, g, b = (int(v) for v in province_src[y, x, :3])
            if y <= 12 and x >= 14 and r > 200 and g > 150 and b < 90: continue
            gate[y, x] = province_src[y, x]
province = centered(gate)

for name, img in [("duchy", duchy), ("dominion", dominion), ("province", province)]:
    Image.fromarray(img).save(os.path.join(out, f"vd_gov_{name}.png"))
