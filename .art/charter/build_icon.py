"""行标题上的宪章图标：直接用 GPT Image 草图（draft_charter.png 左上的金卷轴 + 金锁），
裁下来、扣掉灰背景，缩到 4 倍于显示大小（显示 14x15，图 56x60），高分屏上看到的就是草图原样。
用法: build_icon.py draft_charter.png 输出.png"""
import sys
from collections import deque
import numpy as np
from PIL import Image

im = Image.open(sys.argv[1]).convert("RGB")
a = np.asarray(im).astype(int)
bg = np.array([128, 128, 128])
cell = a[0:1000, 0:1024]
fg = np.abs(cell - bg).sum(2) > 40
ys, xs = np.where(fg)
x0, y0, x1, y1 = xs.min(), ys.min(), xs.max() + 1, ys.max() + 1
crop = a[y0:y1, x0:x1]
mask = np.abs(crop - bg).sum(2) > 40
# 只扣外边连通进来的灰，图标里面的灰保留
h, w = mask.shape
outside = np.zeros_like(mask)
q = deque((y, x) for y in range(h) for x in (0, w - 1) if not mask[y, x])
q.extend((y, x) for x in range(w) for y in (0, h - 1) if not mask[y, x])
for y, x in q: outside[y, x] = True
while q:
    y, x = q.popleft()
    for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
        if 0 <= ny < h and 0 <= nx < w and not mask[ny, nx] and not outside[ny, nx]:
            outside[ny, nx] = True; q.append((ny, nx))
rgba = np.dstack([crop, np.where(outside, 0, 255)]).astype(np.uint8)
Image.fromarray(rgba, "RGBA").resize((56, 60), Image.BOX).save(sys.argv[2])
