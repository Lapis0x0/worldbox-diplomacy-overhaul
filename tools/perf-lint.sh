#!/bin/bash
# 列出改动里新加的热入口和昂贵调用，规则从 docs/dev/cost.md 的「新增热入口」「昂贵调用」两张表读。
# 只提醒不判断：每一处都要按 cost.md 确认它被调得多频繁、乘起来多贵。
# 用法：tools/perf-lint.sh [基准]，默认和 HEAD 比（包括未提交、未跟踪的文件）；发版前可以和上个版本比，如 tools/perf-lint.sh v0.10.0
set -euo pipefail
cd "$(dirname "$0")/.."
base="${1:-HEAD}"

{
  git diff -U0 "$base" -- 'Code/*.cs' ':!Code/Perf/Dev/*'
  # 未跟踪的新文件整份算新增
  git ls-files --others --exclude-standard -- 'Code/*.cs' ':!Code/Perf/Dev/*' | while read -r f; do
    git diff -U0 --no-index /dev/null "$f" || true
  done
} | awk -v rules=docs/dev/cost.md '
BEGIN {
  while ((getline row < rules) > 0) {
    if (row ~ /^## /) kind = row ~ /^## 新增热入口/ ? "热入口" : row ~ /^## 昂贵调用/ ? "昂贵" : ""
    if (kind == "" || row !~ /^\| `/) continue
    rest = substr(row, 4)
    pat = substr(rest, 1, index(rest, "`") - 1)
    split(row, cols, "|")
    note = cols[3]
    gsub(/^ +| +$/, "", note)
    n++; pats[n] = pat; kinds[n] = kind; notes[n] = note
  }
  if (n == 0) { print "✗ 没从 " rules " 读到规则" > "/dev/stderr"; exit 1 }
}
/^\+\+\+ / { file = $2; sub(/^b\//, "", file); next }
/^@@/ { split($3, h, ","); ln = substr(h[1], 2) + 0; next }
/^\+/ {
  text = substr($0, 2)
  code = text
  sub(/\/\/.*/, "", code)
  for (i = 1; i <= n; i++) {
    if (index(code, pats[i]) == 0) continue
    gsub(/^[ \t]+/, "", text)
    printf "%s:%d  [%s] %s — %s\n    %s\n", file, ln, kinds[i], pats[i], notes[i], text
    found[kinds[i]]++
  }
  ln++
}
END {
  if (found["热入口"] + found["昂贵"] == 0) print "没有新加的热入口或昂贵调用"
  else printf "\n新热入口 %d 处，昂贵调用 %d 处。逐处按 docs/dev/cost.md 确认频率和增长方式\n", found["热入口"], found["昂贵"]
}'
