"""把生图草图里的图标缩成 28x28 像素图。用法: pixelize.py 草图.png 行 列 输出前缀 [名字...] [--unit 像素]
默认整张草图统一缩放，让最大的图标占满；--unit 指定草图里一个像素格的边长，用来对齐原版图标的大小"""
import sys
import numpy as np
from PIL import Image

src, rows, cols, out = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4]
names = sys.argv[5:]
unit = None
if "--unit" in names:
    i = names.index("--unit")
    unit = float(names[i + 1])
    names = names[:i] + names[i + 2:]
SIZE, FIT, COLORS = 28, 27, 24
im = np.asarray(Image.open(src).convert("RGB")).astype(int)
bg = np.median(np.concatenate([im[:8].reshape(-1, 3), im[-8:].reshape(-1, 3)]), axis=0)
H, W = im.shape[:2]
boxes = []
for k in range(rows * cols):
    r, c = divmod(k, cols)
    cell = im[r * H // rows:(r + 1) * H // rows, c * W // cols:(c + 1) * W // cols]
    ys, xs = np.nonzero(np.abs(cell - bg).sum(axis=2) > 40)
    boxes.append(max(ys.max() - ys.min(), xs.max() - xs.min()) + 1)
SIDE = int(unit * FIT) if unit else max(boxes)
for k in range(rows * cols):
    r, c = divmod(k, cols)
    cell = im[r * H // rows:(r + 1) * H // rows, c * W // cols:(c + 1) * W // cols]
    mask = np.abs(cell - bg).sum(axis=2) > 40
    ys, xs = np.nonzero(mask)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    side = SIDE
    cy, cx = (y0 + y1) // 2, (x0 + x1) // 2
    # 正方形裁切，居中
    rgba = np.zeros((side, side, 4), np.uint8)
    sy0, sx0 = cy - side // 2, cx - side // 2
    for yy in range(side):
        py = sy0 + yy
        if not 0 <= py < cell.shape[0]: continue
        for_x = np.arange(sx0, sx0 + side)
        ok = (for_x >= 0) & (for_x < cell.shape[1])
        rgba[yy, ok, :3] = cell[py, for_x[ok]]
        rgba[yy, ok, 3] = mask[py, for_x[ok]] * 255
    tile = Image.fromarray(rgba)
    # 每个目标像素取对应块里的颜色均值（只算前景），前景占一半以上才保留
    small = np.zeros((FIT, FIT, 4), np.uint8)
    a = rgba.astype(float)
    for i in range(FIT):
        for j in range(FIT):
            blk = a[i * side // FIT:(i + 1) * side // FIT, j * side // FIT:(j + 1) * side // FIT].reshape(-1, 4)
            fg = blk[blk[:, 3] > 0]
            if len(fg) >= len(blk) * 0.45:
                # 取众数色而不是均值，避免边界混色
                q = (fg[:, :3] // 16).astype(int)
                key = q[:, 0] * 256 + q[:, 1] * 16 + q[:, 2]
                vals, cnt = np.unique(key, return_counts=True)
                top = vals[cnt.argmax()]
                small[i, j, :3] = fg[key == top, :3].mean(axis=0)
                small[i, j, 3] = 255
    s = Image.fromarray(small)
    rgb = s.convert("RGB").quantize(COLORS, method=Image.Quantize.MEDIANCUT).convert("RGB")
    s = Image.merge("RGBA", (*rgb.split(), s.split()[3]))
    canvas = Image.new("RGBA", (SIZE, SIZE))
    canvas.alpha_composite(s, ((SIZE - FIT) // 2, (SIZE - FIT) // 2))
    name = names[k] if k < len(names) else str(k)
    canvas.save(f"{out}{name}.png")
    print("saved", f"{out}{name}.png")
