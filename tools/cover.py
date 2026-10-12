# 生成封面 preview.png（创意工坊封面、模组列表图标、README 头图）：
# 像素风的王国地图，帝国居中，附庸的边界用帝国的颜色。改了功能就改底部关键词重新生成
# 用法：python3 tools/cover.py [输出路径]，默认覆盖仓库根目录的 preview.png。字体用的是 macOS 自带的
import math, os, random, sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter

random.seed(7)
N = 128          # 像素地图的格数
S = 4            # 放大倍数 → 512
W = N * S

# ---------- 噪声 ----------
def make_noise(cell, seed):
    rnd = random.Random(seed)
    g = {}
    def val(ix, iy):
        if (ix, iy) not in g: g[(ix, iy)] = rnd.random()
        return g[(ix, iy)]
    def f(x, y):
        x /= cell; y /= cell
        ix, iy = int(math.floor(x)), int(math.floor(y))
        fx, fy = x - ix, y - iy
        sx, sy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
        a = val(ix, iy) + (val(ix + 1, iy) - val(ix, iy)) * sx
        b = val(ix, iy + 1) + (val(ix + 1, iy + 1) - val(ix, iy + 1)) * sx
        return a + (b - a) * sy
    return f

n1, n2, n3 = make_noise(16, 1), make_noise(7, 2), make_noise(5, 3)
def fbm(x, y): return n1(x, y) * 0.65 + n2(x, y) * 0.35

# ---------- 陆地 ----------
cx, cy = N * 0.5, N * 0.59
land = [[False] * N for _ in range(N)]
for y in range(N):
    for x in range(N):
        d = math.hypot((x - cx) / 1.05, (y - cy) / 0.92) / (N * 0.5)
        land[y][x] = d + (fbm(x, y) - 0.5) * 0.5 < 0.66

# ---------- 王国 ----------
# (中心, 填充色, 边界色, 宗主) —— 附庸的边界用宗主的颜色，和模组里王国地图的效果一样
EMP_FILL, EMP_BORDER = (196, 62, 52), (120, 22, 22)
kingdoms = [
    dict(c=(60, 78), fill=EMP_FILL, border=EMP_BORDER),                       # 0 帝国
    dict(c=(36, 66), fill=(224, 156, 60), border=EMP_BORDER),                 # 1 附庸
    dict(c=(50, 106), fill=(214, 196, 76), border=EMP_BORDER),                # 2 附庸
    dict(c=(82, 104), fill=(120, 168, 74), border=EMP_BORDER),                # 3 附庸
    dict(c=(32, 92), fill=(176, 104, 168), border=EMP_BORDER),                # 4 附庸
    dict(c=(98, 72), fill=(70, 118, 196), border=(28, 56, 120)),              # 5 独立的共和国（交战中）
    dict(c=(64, 50), fill=(88, 170, 170), border=(30, 92, 96)),               # 6 独立
]
weights = [1.6, 0.95, 1.0, 1.0, 0.85, 1.3, 0.9]
owner = [[-1] * N for _ in range(N)]
for y in range(N):
    for x in range(N):
        if not land[y][x]: continue
        best, bd = -1, 1e9
        for i, k in enumerate(kingdoms):
            kx, ky = k["c"]
            d = math.hypot(x - kx, y - ky) / weights[i] + (n3(x + i * 31, y) - 0.5) * 9
            if d < bd: bd, best = d, i
        owner[y][x] = best

for _ in range(3):
    new = [row[:] for row in owner]
    for y in range(1, N - 1):
        for x in range(1, N - 1):
            if owner[y][x] < 0: continue
            cnt = {}
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    o = owner[y + dy][x + dx]
                    if o >= 0: cnt[o] = cnt.get(o, 0) + 1
            best = max(cnt, key=cnt.get)
            if cnt[best] >= 5: new[y][x] = best
    owner = new

def shade(c, f): return tuple(max(0, min(255, int(v * f))) for v in c)

img = Image.new("RGB", (N, N))
px = img.load()
for y in range(N):
    for x in range(N):
        if not land[y][x]:
            # 海：离岸近的浅
            near = any(0 <= x + dx < N and 0 <= y + dy < N and land[y + dy][x + dx]
                       for dx in range(-2, 3) for dy in range(-2, 3))
            base = (52, 104, 156) if near else (34, 72, 120)
            px[x, y] = shade(base, 0.94 + 0.12 * n2(x * 3, y * 3))
            continue
        o = owner[y][x]
        k = kingdoms[o]
        edge = False
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if not (0 <= nx < N and 0 <= ny < N) or not land[ny][nx] or owner[ny][nx] != o:
                edge = True
        if edge:
            px[x, y] = k["border"]
        else:
            px[x, y] = shade(k["fill"], 0.9 + 0.16 * n2(x * 2 + o * 50, y * 2))

big = img.resize((W, W), Image.NEAREST)
bpx = big.load()

def stamp_big(pattern, colors, gx, gy, unit):
    # gx, gy：地图格子坐标；unit：每个图标像素占多少屏幕像素
    for j, row in enumerate(pattern):
        for i, ch in enumerate(row):
            if ch not in colors: continue
            for yy in range(unit):
                for xx in range(unit):
                    bpx[int(gx * S) + i * unit + xx, int(gy * S) + j * unit + yy] = colors[ch]

K = (34, 18, 10)
# 帝国首都的皇冠
stamp_big([".k...k...k.",
           "kgk.kgk.kgk",
           "kggkgggkggk",
           "kgggggggggk",
           "kgrgggggrgk",
           "kgggggggggk",
           "kkkkkkkkkkk"], {"k": K, "g": (255, 208, 70), "r": (210, 40, 40)}, 53, 72, 5)
# 军旗
def flag(gx, gy, c):
    stamp_big(["kkkkkkkkk",
               "kwwwwwwwk",
               "kwcccccwk",
               "kwcccccwk",
               "kwcccccwk",
               "kwwwwwwwk",
               "kkkkkkkkk",
               "k........",
               "k........",
               "k........"],
              {"k": K, "c": c, "w": (250, 246, 232)}, gx, gy, 3)
flag(84, 62, EMP_FILL)
flag(86, 80, EMP_FILL)
flag(98, 58, (70, 118, 196))
# 交战处的交叉剑
stamp_big(["s.....s", ".s...s.", "..s.s..", "...s...", "..s.s..", "hh...hh", "hh...hh"],
          {"s": (240, 240, 245), "h": (130, 84, 40)}, 91, 70, 4)

# ---------- 文字 ----------
d = ImageDraw.Draw(big, "RGBA")
# 上下两条暗带
for y in range(0, 150):
    a = int(225 * (1 - y / 150) ** 1.2)
    d.line([(0, y), (W, y)], fill=(12, 14, 26, a))
for y in range(W - 92, W):
    a = int(230 * ((y - (W - 92)) / 92) ** 0.9)
    d.line([(0, y), (W, y)], fill=(12, 14, 26, a))

zh = ImageFont.truetype("/System/Library/Fonts/STHeiti Medium.ttc", 92)
en = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial Black.ttf", 27)
tag = ImageFont.truetype("/System/Library/Fonts/STHeiti Medium.ttc", 30)

def text_c(y, s, font, fill, outline=(20, 10, 6), ow=4, spacing=0):
    bbox = d.textbbox((0, 0), s, font=font)
    w = bbox[2] - bbox[0]
    x = (W - w) / 2 - bbox[0]
    d.text((x + 3, y + 5), s, font=font, fill=(0, 0, 0, 150))
    d.text((x, y), s, font=font, fill=fill, stroke_width=ow, stroke_fill=outline)

text_c(10, "外交大修", zh, (255, 214, 120))
text_c(110, "DIPLOMACY OVERHAUL", en, (245, 238, 220), ow=3)
words = ["附庸", "政体", "王朝", "军队"]
ws = [d.textbbox((0, 0), w, font=tag)[2] for w in words]
gap = 46
x = (W - sum(ws) - gap * (len(words) - 1)) / 2
for i, w in enumerate(words):
    d.text((x, W - 58), w, font=tag, fill=(245, 238, 220), stroke_width=3, stroke_fill=(20, 10, 6))
    if i < len(words) - 1:
        cxd = x + ws[i] + gap / 2
        d.rectangle([cxd - 3, W - 40, cxd + 3, W - 34], fill=(255, 208, 70))
    x += ws[i] + gap

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "preview.png")
big.save(out)
print(os.path.abspath(out))
