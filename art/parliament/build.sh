#!/bin/bash
# 重新生成议会图标，输出到 GameResources/ui/Icons/
# draft.png 是第一轮草图，选了左上那格的议席半圆；draft_small.png 是让模型照它画小的一轮，用第三格
set -euo pipefail
cd "$(dirname "$0")"
out=../../GameResources/ui/Icons
py=${PYTHON:-python3}
$py ../pixelize_grid.py draft_small.png 2 2 "$out/" - - vd_parliament - >/dev/null
echo "已生成 $out/vd_parliament.png"
