#!/bin/bash
# 共和国的金橄榄冠：draft_gold_small.png 左上那格，按左半边镜像成对称的，替换王国窗口里的皇冠，输出到 GameResources/ui/Icons/
set -euo pipefail
cd "$(dirname "$0")"
py=${PYTHON:-python3}
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
$py ../pixelize_grid.py draft_gold_small.png 2 2 "$tmp/" wreath - - - >/dev/null
$py mirror.py "$tmp/wreath.png" ../../GameResources/ui/Icons/vd_gov_wreath.png left
