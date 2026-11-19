"""模型把王冠画成了 5 格宽、2 格高，三个尖只高出一格，游戏里看不出起伏。
只重画王冠这一块：一行冠带，中间的尖最高，三个尖比冠带高出一到两格，用原版皇冠的金色，描边一格宽，其余像素不动。
用法: crown.py 图标.png（原地改写）"""
import sys
from PIL import Image

path = sys.argv[1]
im = Image.open(path).convert("RGBA")
O = (71, 38, 17, 255)                                    # 和图标其余部分一样的深褐描边
L, M, D, S = (255, 229, 99, 255), (253, 199, 21, 255), (253, 177, 0, 255), (255, 132, 0, 255)
# 王冠：x 19..23，y 4..6，冠带只有一行；左上受光、右下背光
gold = {
    (21, 4): L,
    (19, 5): L, (21, 5): M, (23, 5): D,
    (19, 6): L, (20, 6): M, (21, 6): M, (22, 6): D, (23, 6): S,
}
# 先清掉原来王冠和它的描边（冠带下面那一行是头发的描边，不动）
for y in range(1, 7):
    for x in range(17, 26):
        im.putpixel((x, y), (0, 0, 0, 0))
for (x, y), c in gold.items():
    im.putpixel((x, y), c)
for (x, y) in gold:
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            q = (x + dx, y + dy)
            if q not in gold and im.getpixel(q)[3] == 0:
                im.putpixel(q, O)
im.save(path)
