"""用 Atlas Cloud 上的 GPT Image 2.5 生成图标草图（复制自 figure-to-ppt skill）。

usage:
  python gen_image.py --prompt p.txt --out fig2_v1 [--refs style1.jpg draft.png ...] [--size 2048x2048]
                      [--quality high] [--tier sunburst|flare] [--n 2]

  --refs given  -> edit endpoint (up to 16 reference images: style references, or the draft being edited)
  no --refs     -> text-to-image endpoint
Writes <out>.png (or <out>_0.png, <out>_1.png ... when n > 1).

Key: ATLASCLOUD_API_KEY in ~/.env (never printed). Size: each edge 256-3840 px, a multiple of 16, at most
3840x2160 pixels in total, aspect ratio within 3:1. Jobs are asynchronous: submit, then poll.
"""
import argparse
import base64
import json
import os
import sys
import time

import requests
from dotenv import load_dotenv

load_dotenv(os.path.expanduser("~/.env"))
KEY = os.environ["ATLASCLOUD_API_KEY"]
H = {"Authorization": f"Bearer {KEY}", "Content-Type": "application/json"}
BASE = "https://api.atlascloud.ai/api/v1/model"

ap = argparse.ArgumentParser()
ap.add_argument("--prompt", required=True, help="path to the prompt text")
ap.add_argument("--out", required=True, help="output path prefix")
ap.add_argument("--refs", nargs="*", default=[])
ap.add_argument("--size", default="2048x2048")
ap.add_argument("--quality", default="high", help="low | medium | high | xhigh | max")
ap.add_argument("--tier", default="sunburst", help="sunburst (larger) | flare")
ap.add_argument("--n", type=int, default=2)
a = ap.parse_args()

body = {"prompt": open(a.prompt).read(), "quality": a.quality, "size": a.size, "output_format": "png", "n": a.n}
if a.refs:
    body["model"] = f"openai/gpt-image-2.5-{a.tier}/edit"
    body["images"] = []
    for p in a.refs:
        mime = "image/jpeg" if p.lower().endswith((".jpg", ".jpeg")) else "image/png"
        body["images"].append(f"data:{mime};base64," + base64.b64encode(open(p, "rb").read()).decode())
else:
    body["model"] = f"openai/gpt-image-2.5-{a.tier}/text-to-image"

r = requests.post(f"{BASE}/generateImage", headers=H, json=body, timeout=120)
j = r.json()
if r.status_code != 200 or "data" not in j:
    sys.exit(f"submit failed: {r.status_code} {json.dumps(j)[:800]}")
pid = j["data"]["id"]
print("submitted", pid, flush=True)
t0 = time.time()
while True:
    time.sleep(8)
    d = requests.get(f"{BASE}/prediction/{pid}", headers=H, timeout=60).json().get("data", {})
    if d.get("status") in ("completed", "succeeded"):
        break
    if d.get("status") == "failed" or time.time() - t0 > 900:
        sys.exit(f"failed: {json.dumps(d)[:800]}")
outs = d.get("outputs") or []
for i, url in enumerate(outs):
    fn = f"{a.out}_{i}.png" if len(outs) > 1 else f"{a.out}.png"
    open(fn, "wb").write(requests.get(url, timeout=120).content)
    print("saved", fn)
print(f"{int(time.time() - t0)} s, price {d.get('price', '?')}")
