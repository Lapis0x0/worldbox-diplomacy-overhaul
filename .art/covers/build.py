"""封面 preview.png（创意工坊封面、模组列表图标、README 头图）：GPT 出的 1254x1254 原图
→ 抹掉外圈的蓝色像素圆角框 → 四边各裁 30 → 512x512 → 量化成 256 色压缩。
原图只留本地（.art 下的图片不入库）。用法：python .art/covers/build.py，两张封面一起重做"""
import numpy as np
from PIL import Image, ImageFilter

COVERS = [
    (".art/covers/diplomacy_source.png", "preview.png"),
    (".art/covers/performance_source.png", "packages/performance-overhaul/preview.png"),
]


def build(pSrc, pDst):
    src = Image.open(pSrc).convert("RGB")
    a = np.array(src).astype(int)
    H, W, _ = a.shape
    r, b = a[..., 0], a[..., 2]

    # 边框是一圈 (80,130,190) 上下的蓝，四角是台阶状的圆弧，往里伸进约 60 像素。
    # 只在靠边的带子和四个角里找，避开中间画面里的蓝（海、旗帜）
    yy, xx = np.mgrid[0:H, 0:W]
    dx, dy = np.minimum(xx, W - 1 - xx), np.minimum(yy, H - 1 - yy)
    frame = (b - r > 40) & (b > 70) & (r < 120) & ((np.minimum(dx, dy) < 40) | ((dx < 170) & (dy < 170)))
    mask = Image.fromarray((frame * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(7))  # 连同边缘的过渡色

    # 底色几乎是平的，只有很轻的暗角：框的位置填上去掉框以后模糊出来的底色
    bg_color = np.median(a[60:120, 60:120].reshape(-1, 3), axis=0)
    bg = a.copy()
    bg[np.array(mask) > 0] = bg_color
    out = Image.composite(Image.fromarray(bg.astype(np.uint8)).filter(ImageFilter.GaussianBlur(12)), src, mask)

    m = 30
    out = out.crop((m, m, W - m, H - m)).resize((512, 512), Image.LANCZOS)
    out = out.quantize(colors=256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    out.save(pDst, optimize=True)

    # 四条边上不应该留下框的蓝
    edge = np.array(out.convert("RGB")).astype(int)
    strips = np.concatenate([edge[:4].reshape(-1, 3), edge[-4:].reshape(-1, 3), edge[:, :4].reshape(-1, 3), edge[:, -4:].reshape(-1, 3)])
    print(pDst, "边上最亮", strips.max(0).tolist())


for src, dst in COVERS:
    build(src, dst)
