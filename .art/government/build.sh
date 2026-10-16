#!/bin/bash
# 重新生成政体图标，输出到 GameResources/ui/Icons/
set -euo pipefail
cd "$(dirname "$0")"
out=../../GameResources/ui/Icons
py=${PYTHON:-python3}
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
$py build_republic.py "$out"
# 公国的小皇冠按 26px 一格缩，和原版皇冠差不多宽；行省按 30px
$py ../pixelize.py draft_vassal_chain.png 2 2 "$tmp/d_" duchy b c d --unit 26 >/dev/null
$py ../pixelize.py draft_vassal.png 2 2 "$tmp/p_" a b province d --unit 30 >/dev/null
$py build_vassal.py "$tmp/d_duchy.png" "$tmp/p_province.png" "$out"
echo "已生成 $out/vd_gov_*.png"
