#!/bin/bash
# 发版前的性能基准：读基准档跑一会儿，在原规模和扩成大帝国后各测一遍议会、好感、定期结算、议会窗口等重函数（见 Code/Perf/Dev/BenchDev.cs），
# 跑几轮取每项最快的一次，和基线比较。基线和结果都在 bin/bench/perf/（和机器有关，不进仓库）。
# 同时数运行期间游戏日志里的异常，应该是 0。
# 用法：tools/perf-check.sh [轮数=2]          测一遍并和基线比较
#       tools/perf-check.sh --save [轮数=2]   测一遍并存为新基线
# 游戏要开着（tools/game.sh start）
set -euo pipefail
cd "$(dirname "$0")/.."
save=0
if [ "${1:-}" = "--save" ]; then save=1; shift; fi
rounds="${1:-2}"
dir=bin/bench/perf
mkdir -p "$dir"
raw="$dir/raw.txt"
: > "$raw"
log="$HOME/Library/Logs/mkarpenko/WorldBox/Player.log"
log_start=$(($(wc -l < "$log") + 1))

for i in $(seq "$rounds"); do
  echo "第 $i/$rounds 轮…" >&2
  # 第一遍只为让各函数都编译过，结果不要；重新读档后在同一个暂停的世界上测，每轮测的是同一个状态
  tools/dev.sh "load bin/bench/saves/tamriel_910" "pause on" "bench run" "bench stress 6" "bench run" \
    "load bin/bench/saves/tamriel_910" "pause on" "end measure" \
    "bench run" "bench stress 6" "bench run" > "$dir/round.txt"
  awk '/^> end measure/ { on = 1 } !on { next } /^> bench stress/ { scale = "stress" } /^> end measure/ { scale = "base" }
       /^@bench / { print scale "." $2, $3 }
       /^@scale / { s[scale] = $0 }
       END { for (k in s) print "#", k, s[k] }' "$dir/round.txt" >> "$raw"
done

# 每项取各轮最快的
awk '/^#/ { if (!seen[$2]++) print; next } { if (!($1 in v) || $2 < v[$1]) v[$1] = $2 } END { for (k in v) print k, v[k] }' "$raw" | sort > "$dir/latest.txt"

if [ "$save" = 1 ]; then
  cp "$dir/latest.txt" "$dir/baseline.txt"
  echo "已存为基线：$dir/baseline.txt"
fi

grep '^#' "$dir/latest.txt" | sed 's/^# //'
echo
# 读档等原版自己的提示不算；模组隔离下来的异常以 [外交大修] 开头
errors=$(tail -n +"$log_start" "$log" | grep -E "Exception|^\[外交大修\].*出错" | grep -cv "^  at " || true)
if [ "$errors" -gt 0 ]; then
  echo "✗ 运行期间日志里有 $errors 条异常："
  tail -n +"$log_start" "$log" | grep -E "Exception|^\[外交大修\].*出错" | sort | uniq -c | sort -rn | head -10
else
  echo "运行期间没有异常"
fi
echo
# 标记：比基线慢一倍以上（且多出 1ms 以上），或者超出预算。预算按多久发生一次定：
# 定期结算和起草一年一次，50ms；点开窗口 100ms；其余会被频繁调用，16ms（一帧）
awk -v has_base="$([ -f "$dir/baseline.txt" ] && echo 1 || echo 0)" '
  FNR == NR { if ($1 != "#") b[$1] = $2; next }
  $1 == "#" { next }
  {
    base = ($1 in b) ? b[$1] : ""
    flag = ""
    if (base != "" && $2 > base * 2 && $2 - base > 1) flag = "慢了"
    budget = $1 ~ /\.ui\./ ? 100 : $1 ~ /\.clock\.|\.parl\.draft/ ? 50 : 16
    if ($2 > budget) flag = (flag == "" ? "" : flag "，") "超预算 " budget "ms"
    if (flag != "") bad++
    printf "%-32s %10s %10.2f %8s  %s\n", $1, (base == "" ? "-" : sprintf("%.2f", base)), $2, (base == "" || base == 0 ? "" : sprintf("x%.2f", $2 / base)), flag
  }
  END { printf "\n%d 项需要看一下（毫秒；基线 / 本次 / 倍数）\n", bad }
' "$( [ -f "$dir/baseline.txt" ] && echo "$dir/baseline.txt" || echo /dev/null )" "$dir/latest.txt"
