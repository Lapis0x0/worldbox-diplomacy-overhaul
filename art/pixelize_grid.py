"""按草图自身的像素网格还原成 28x28：AI 画的「像素块」大小不一定整齐，切错网格会把相邻两块混色、整体发糊。
这里对每个图标单独量出块的边长和起点（找颜色突变最集中的等距位置，横竖共用一个边长），在每块中间取色，尽量原样保留 AI 画的像素。
和主体分开的小零件（闪光之类）常常没对齐主体的网格，按主体网格取色会缺笔画，所以按它自己的外框单独取色，再放到最近的格子上。
用法: pixelize_grid.py 草图.png 行 列 输出前缀 名字...（名字按从左到右、从上到下的格子顺序）"""
import sys
import numpy as np
from PIL import Image

src, rows, cols, out = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4]
names = sys.argv[5:]
N = 28
im = np.asarray(Image.open(src).convert("RGB")).astype(float)
bg = np.median(np.concatenate([im[:8].reshape(-1, 3), im[-8:].reshape(-1, 3)]), axis=0)
H, W = im.shape[:2]


def phase_score(g, p):
    """块边长为 p 时，最好的起点和对应的得分（等距位置上的平均突变强度，相对整体均值）"""
    best = (0, 0)
    for ph in np.arange(0, p, 0.2):
        idx = np.floor(np.arange(ph, len(g) - 1, p)).astype(int)
        s = g[idx].mean() / g.mean()
        if s > best[0]: best = (s, ph)
    return best


def grid(gx, gy, lo=16, hi=26):
    """像素块是正方形：横竖共用一个边长，起点各自找；返回 (边长, x 起点, y 起点)"""
    best = (0, 0, 0, 0)
    for p in np.arange(lo, hi, 0.1):
        sx, phx = phase_score(gx, p)
        sy, phy = phase_score(gy, p)
        if sx + sy > best[0]: best = (sx + sy, p, phx, phy)
    return best[1:]


def components(mask):
    """4 邻接连通块，返回每块的 (像素数, y0, y1, x0, x1)"""
    lab = np.zeros(mask.shape, np.int32)
    res = []
    for sy, sx in zip(*np.nonzero(mask)):
        if lab[sy, sx]: continue
        lab[sy, sx] = len(res) + 1
        stack, pts = [(sy, sx)], []
        while stack:
            y, x = stack.pop()
            pts.append((y, x))
            for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= ny < mask.shape[0] and 0 <= nx < mask.shape[1] and mask[ny, nx] and not lab[ny, nx]:
                    lab[ny, nx] = len(res) + 1
                    stack.append((ny, nx))
        ys, xs = zip(*pts)
        res.append((len(pts), min(ys), max(ys) + 1, min(xs), max(xs) + 1))
    return res


def cells(n):
    """小零件占几格：取最近的奇数（闪光是对称的十字，偶数格取不到中心那一笔）"""
    k = max(1, round(n))
    if k % 2 == 0: k = k + 1 if n > k else k - 1
    return max(1, k)


def sample(img, m, a0, a1, b0, b1):
    """一块里取中间一半的中位色；前景不到一半算透明"""
    h, w = a1 - a0, b1 - b0
    a0, a1, b0, b1 = int(a0 + h / 4), int(a1 - h / 4) + 1, int(b0 + w / 4), int(b1 - w / 4) + 1
    mm = m[a0:a1, b0:b1]
    if mm.size == 0 or mm.mean() < 0.5: return None
    return np.median(img[a0:a1, b0:b1][mm], axis=0)


for k, name in enumerate(names):
    r, c = divmod(k, cols)
    cell = im[r * H // rows:(r + 1) * H // rows, c * W // cols:(c + 1) * W // cols]
    mask = np.abs(cell - bg).sum(axis=2) > 40
    ys, xs = np.nonzero(mask)
    y0, y1, x0, x1 = ys.min() - 4, ys.max() + 5, xs.min() - 4, xs.max() + 5
    box = cell[y0:y1, x0:x1]
    gx = np.abs(np.diff(box, axis=1)).sum(2).sum(0)
    gy = np.abs(np.diff(box, axis=0)).sum(2).sum(1)
    px, phx, phy = grid(gx, gy)
    py = px
    nx, ny = int((box.shape[1] - phx) // px), int((box.shape[0] - phy) // py)
    M = 3  # 四周多留几格，小零件可能落在主体网格范围外
    small = np.zeros((ny + 2 * M, nx + 2 * M, 4), np.uint8)
    bm = mask[y0:y1, x0:x1]
    for j in range(ny):
        for i in range(nx):
            c = sample(box, bm, phy + j * py, phy + (j + 1) * py, phx + i * px, phx + (i + 1) * px)
            if c is not None: small[M + j, M + i] = (*c, 255)
    parts = components(bm)
    main = max(p[0] for p in parts)
    small_parts = [q for q in parts if q[0] != main and q[0] >= px * py * 0.5]
    # 先清掉主体网格里中心落在小零件外框内的格子（零件跨进相邻格时，主体那一遍会多取出一笔）
    for _, a0, a1, b0, b1 in small_parts:
        for j in range(ny):
            for i in range(nx):
                cy, cx = phy + (j + 0.5) * py, phx + (i + 0.5) * px
                if a0 <= cy < a1 and b0 <= cx < b1: small[M + j, M + i] = 0
    for _, a0, a1, b0, b1 in small_parts:
        # 小零件：按它自己的外框分格，放到离外框左上角最近的格子
        gh, gw = cells((a1 - a0) / py), cells((b1 - b0) / px)
        oy, ox = M + round((a0 - phy) / py), M + round((b0 - phx) / px)
        small[oy:oy + gh, ox:ox + gw] = 0
        for j in range(gh):
            for i in range(gw):
                c = sample(box, bm, a0 + j * (a1 - a0) / gh, a0 + (j + 1) * (a1 - a0) / gh,
                           b0 + i * (b1 - b0) / gw, b0 + (i + 1) * (b1 - b0) / gw)
                if c is not None: small[oy + j, ox + i] = (*c, 255)
    ys2, xs2 = np.nonzero(small[..., 3])
    small = small[ys2.min():ys2.max() + 1, xs2.min():xs2.max() + 1]
    h, w = small.shape[:2]
    if h > N or w > N: sys.exit(f"{name}: {w}x{h} 超过 28")
    canvas = np.zeros((N, N, 4), np.uint8)
    canvas[(N - h) // 2:(N - h) // 2 + h, (N - w) // 2:(N - w) // 2 + w] = small
    Image.fromarray(canvas).save(f"{out}{name}.png")
    print(f"{name}: 块 {px:.1f}x{py:.1f}px, {w}x{h}")
