#!/bin/bash
# 长跑测试：让游戏自己跑很多年，抓测试时跑几十秒碰不到的问题（几年才开一次的议会、国家灭亡那一刻正好有箭在飞……）。
# 读基准档，把最大的国家扩成大帝国、制造战争和国家灭亡，高速跑若干游戏年，每段之间巡检一次对象之间的关系。
# 跑完汇报：日志里的异常、熔断停用的系统、关系巡检的违规、最卡的帧和超过一帧的定期结算。
# 用法：tools/soak.sh [游戏年数=30] [速度=x20]
# 游戏要开着（tools/game.sh start）
set -euo pipefail
cd "$(dirname "$0")/.."
years="${1:-30}"
speed="${2:-x20}"
log="$HOME/Library/Logs/mkarpenko/WorldBox/Player.log"
out=bin/dev/soak.txt
log_start=$(($(wc -l < "$log") + 1))
export DEV_TIMEOUT=600

year() { tools/dev.sh "year" | awk '/^@year/ { print $2 }'; }

tools/dev.sh "load bin/bench/saves/tamriel_910" "speed $speed" "pause off" "wait 10" "bench stress 6" > "$out"
start=$(year)
target=$((start + years))
echo "从 $start 年跑到 $target 年（$speed）…" >&2
now=$start
while [ "$now" -lt "$target" ]; do
  # 每段测 30 秒帧时间，再巡检一次
  tools/dev.sh "measure soak 30" "check" "year" >> "$out"
  now=$(year)
  echo "  $now 年" >&2
done
tools/dev.sh "pause on" "safety" "check" >> "$out"

echo
echo "== 跑了 $start → $now 年"
echo
errors=$(tail -n +"$log_start" "$log" | grep -E "Exception|^\[外交大修\].*(出错|停用)" | grep -cv "^  at " || true)
if [ "$errors" -gt 0 ]; then
  echo "✗ 日志里有 $errors 条异常："
  tail -n +"$log_start" "$log" | grep -E "Exception|^\[外交大修\].*(出错|停用)" | cut -c1-160 | sort | uniq -c | sort -rn | head -15
else
  echo "✓ 日志里没有异常"
fi
off=$(grep "Vassal=" "$out" | tail -1 | tr ' ' '\n' | grep "=off" || true)
if [ -n "$off" ]; then echo "✗ 熔断停用的系统：$(echo $off)"; else echo "✓ 没有系统熔断"; fi
violations=$(grep "^@check" "$out" | awk '{ s += $2 } END { print s + 0 }')
if [ "$violations" -gt 0 ]; then
  echo "✗ 关系巡检累计 $violations 条违规："
  grep "^@violation" "$out" | sort | uniq -c | sort -rn | head -15
else
  echo "✓ 关系巡检没有违规"
fi
echo
echo "== 最卡的帧（每段 30 秒）"
grep "^@result soak" "$out" | sed -E 's/.*avg=([0-9.]+ms).*p99=([0-9.]+).*max=([0-9]+).*spikes=([^ ]*).*/  平均 \1  p99 \2ms  最大 \3ms  卡顿 \4/; s/.*avg=([0-9.]+ms).*p99=([0-9.]+).*max=([0-9]+).*/  平均 \1  p99 \2ms  最大 \3ms/'
spikes=$(grep "^@spike" "$out" | sort -k3 -t' ' -rn | head -10 || true)
if [ -n "$spikes" ]; then
  echo
  echo "== 超过一帧的定期结算"
  grep "^@spike" "$out" | awk '{ n[$2]++; if ($3 + 0 > m[$2]) m[$2] = $3 + 0 } END { for (k in n) printf "  %-24s %d 次，最长 %.1fms\n", k, n[k], m[k] }' | sort
fi
echo
echo "完整输出：$out"
