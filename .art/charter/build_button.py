"""宪章按钮：原版 special_buttonRedGodly（30x30 红底金包角）裁成扁长条。
四个角的像素原样保留，中间的行、列照原版九宫格拉伸，上浅下深的分界保留。
用法: build_button.py special_buttonRedGodly.png 输出.png"""
import sys
from PIL import Image

src = Image.open(sys.argv[1]).convert("RGBA")
rows = list(range(0, 7)) + [12, 13, 14, 16, 17] + list(range(22, 30))   # 上 7 行、中 5 行、下 8 行，共 20
cols = list(range(0, 8)) + [8] * 65 + list(range(22, 30))               # 左右各 8 列，中间拉伸，共 81
out = Image.new("RGBA", (len(cols), len(rows)))
for y, sy in enumerate(rows):
    for x, sx in enumerate(cols):
        out.putpixel((x, y), src.getpixel((sx, sy)))
out.save(sys.argv[2])
