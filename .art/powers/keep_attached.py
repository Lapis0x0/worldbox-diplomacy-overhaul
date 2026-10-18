"""只保留和图标最大一块（主体）相连的像素，去掉零散的碎块，再居中放回 28x28。不缩放、不改色。
用法: keep_attached.py 图标.png"""
import sys
from PIL import Image

path = sys.argv[1]
im = Image.open(path).convert("RGBA")
px = im.load()
W, H = im.size
seen, parts = set(), []
for y in range(H):
    for x in range(W):
        if px[x, y][3] == 0 or (x, y) in seen: continue
        part, stack = [], [(x, y)]
        seen.add((x, y))
        while stack:
            cx, cy = stack.pop()
            part.append((cx, cy))
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    nx, ny = cx + dx, cy + dy
                    if 0 <= nx < W and 0 <= ny < H and (nx, ny) not in seen and px[nx, ny][3]:
                        seen.add((nx, ny))
                        stack.append((nx, ny))
        parts.append(part)
main = max(parts, key=len)
kept = Image.new("RGBA", (W, H), (0, 0, 0, 0))
for x, y in main: kept.putpixel((x, y), px[x, y])
icon = kept.crop(kept.getbbox())
out = Image.new("RGBA", (28, 28), (0, 0, 0, 0))
out.paste(icon, ((28 - icon.width) // 2, (28 - icon.height) // 2))
out.save(path)
print(f"{path}: {icon.width}x{icon.height}，去掉 {len(parts) - 1} 块碎片")
