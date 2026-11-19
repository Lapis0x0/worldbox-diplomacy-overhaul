"""量草图里一格的真实像素块大小：对每个块边长找最好的起点，按网格还原再铺回原尺寸，打印和草图的平均色差。
格子切得越碎色差自然越小，所以看的不是最小值，而是曲线上明显的低谷（比两边都低一截的那个）。
把低谷的块边长写进 pixelize_grid.py 的名字后面（如 vd_situation@29）。
用法: fit_curve.py 草图.png 行 列 第几格（从 0 起，从左到右、从上到下） [最小边长 最大边长]"""
import sys
import numpy as np
from PIL import Image

src, rows, cols, k = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4])
lo, hi = (float(sys.argv[5]), float(sys.argv[6])) if len(sys.argv) > 6 else (16.0, 34.0)
im = np.asarray(Image.open(src).convert("RGB")).astype(np.float32)
H, W = im.shape[:2]
bg = np.median(np.concatenate([im[:8].reshape(-1, 3), im[-8:].reshape(-1, 3)]), axis=0)
r, c = divmod(k, cols)
cell = im[r * H // rows:(r + 1) * H // rows, c * W // cols:(c + 1) * W // cols]
mask = np.abs(cell - bg).sum(2) > 40
ys, xs = np.nonzero(mask)
y0, y1, x0, x1 = ys.min() - 4, ys.max() + 5, xs.min() - 4, xs.max() + 5
box, bm = cell[y0:y1, x0:x1], mask[y0:y1, x0:x1]
h, w = bm.shape
Y, X = np.mgrid[0:h, 0:w]


def err(p, phx, phy):
    bi, bj = np.floor((X - phx) / p).astype(int), np.floor((Y - phy) / p).astype(int)
    cx = np.clip((phx + (np.arange(bi.max() + 2) + 0.5) * p).astype(int), 0, w - 1)
    cy = np.clip((phy + (np.arange(bj.max() + 2) + 0.5) * p).astype(int), 0, h - 1)
    on, col = bm[cy[:, None], cx[None, :]], box[cy[:, None], cx[None, :]]
    lay = on[bj.clip(0), bi.clip(0)] & (bi >= 0) & (bj >= 0)
    out = np.where(lay[..., None], col[bj.clip(0), bi.clip(0)], bg)
    return np.abs(out - box).sum(2).mean(), int(on.any(0).sum()), int(on.any(1).sum())


# 起点先每 2 像素试一次，再在最好的附近逐像素细找
for p in np.arange(lo, hi + 0.01, 0.5):
    coarse = min(((a, b) for a in np.arange(0, p, 2.0) for b in np.arange(0, p, 2.0)), key=lambda ab: err(p, *ab)[0])
    best = min(((a, b) for a in np.arange(coarse[0] - 1, coarse[0] + 2) for b in np.arange(coarse[1] - 1, coarse[1] + 2)),
               key=lambda ab: err(p, *ab)[0])
    e, nx, ny = err(p, *best)
    print(f"{p:5.1f}  {e:6.2f}  {nx}x{ny}  " + "#" * int(max(0, 60 - e)), flush=True)
