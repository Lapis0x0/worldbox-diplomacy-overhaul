# 把橄榄冠按宝石所在的那一列左右镜像成对称的：left 用左半边、right 用右半边。
# 中轴那一列复制成两列，图标变成偶数宽，正好落在 28 格画布的正中
import sys
from PIL import Image
src, out, side = sys.argv[1], sys.argv[2], sys.argv[3]
AXIS = 13  # 宝石中心所在的列
im = Image.open(src).convert("RGBA")
px = im.load()
w, h = im.size
res = Image.new("RGBA", (w, h), (0, 0, 0, 0))
rp = res.load()
for y in range(h):
    for d in range(0, AXIS + 1):
        a = AXIS - d if side == "left" else AXIS + d
        if a < 0 or a >= w: continue
        c = px[a, y]
        # 左半边落在 13-d，右半边落在 14+d
        rp[AXIS - d, y] = c
        if AXIS + 1 + d < w: rp[AXIS + 1 + d, y] = c
res.save(out)
