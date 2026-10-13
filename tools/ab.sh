#!/bin/bash
# A/B 对比一项优化：读基准档后交替关、开测量若干轮，打印两组的平均帧耗时。
# 用法：tools/ab.sh <开关名|all> [轮数=4] [每轮秒数=12] [速度=x5]
# 世界在不断变化，两组交替穿插测量才可比
set -euo pipefail
cd "$(dirname "$0")/.."
opt="$1"; rounds="${2:-4}"; secs="${3:-12}"; speed="${4:-x5}"
cmds=("load bin/bench/saves/tamriel_910" "camera reset" "speed $speed" "pause off" "wait 8")
for i in $(seq "$rounds"); do
  # 每轮交换先后顺序，抵消世界随时间的漂移
  if (( i % 2 )); then order="off on"; else order="on off"; fi
  for s in $order; do cmds+=("opt $opt $s" "wait 2" "measure ${s}_$i $secs"); done
done
# 结束时恢复开启（默认状态）
cmds+=("opt $opt on")
out=$(tools/dev.sh "${cmds[@]}")
echo "$out" | grep "@result"
echo "$out" | python3 -c '
import sys, re, statistics as st
g = {"off": [], "on": []}
a = {"off": [], "on": []}
for line in sys.stdin:
    m = re.search(r"@result (on|off)_\d+ .*? avg=([\d.]+)ms", line)
    if m: g[m.group(1)].append(float(m.group(2)))
    m2 = re.search(r"@result (on|off)_\d+ .*? alloc=([\d.]+)KB/frame", line)
    if m2: a[m2.group(1)].append(float(m2.group(2)))
off, on = st.mean(g["off"]), st.mean(g["on"])
print(f"off {off:.2f}ms ({1000/off:.1f} fps)  on {on:.2f}ms ({1000/on:.1f} fps)  diff {on-off:+.2f}ms ({(off-on)/off*100:+.1f}%)")
if a["off"] and a["on"]:
    ao, an = st.mean(a["off"]), st.mean(a["on"])
    print(f"alloc off {ao:.0f}KB/frame  on {an:.0f}KB/frame  diff {(an-ao)/ao*100:+.0f}%")
'
