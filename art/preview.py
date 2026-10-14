"""给用户看的图标预览：每个图标放大 8 倍，下面标名字，再给 1 倍、2 倍的实际大小，深色背景接近游戏界面。
用法: preview.py 输出.png 图标.png[:名字] ...（可以混进原版图标做对照）"""
import os
import sys
from PIL import Image, ImageDraw, ImageFont

out, specs = sys.argv[1], sys.argv[2:]
font = ImageFont.truetype("/System/Library/Fonts/PingFang.ttc", 22)
Z, CW = 7, 28 * 7 + 40
sheet = Image.new("RGBA", (40 + len(specs) * CW, 28 * Z + 150), (44, 44, 52, 255))
draw = ImageDraw.Draw(sheet)
for i, spec in enumerate(specs):
    path, _, label = spec.partition(":")
    label = label or os.path.splitext(os.path.basename(path))[0]
    icon = Image.open(path).convert("RGBA")
    x = 20 + i * CW
    sheet.alpha_composite(icon.resize((28 * Z, 28 * Z), Image.NEAREST), (x, 15))
    draw.text((x, 28 * Z + 22), label, font=font, fill="white")
    sheet.alpha_composite(icon, (x + 10, 28 * Z + 84))
    sheet.alpha_composite(icon.resize((56, 56), Image.NEAREST), (x + 60, 28 * Z + 70))
sheet.save(out)
