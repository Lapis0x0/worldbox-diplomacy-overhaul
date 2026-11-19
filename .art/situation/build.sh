#!/bin/bash
# 重新生成局势图标，输出到 GameResources/ui/Icons/
# draft_ideas.png 是让模型自由发挥的一轮（prompt_ideas.txt），选了上排中间背对背的两人；
# draft.png 是让模型照它重画到 24 格宽的一轮（prompt.txt），用左上那格。
# 自动量出的块边长不准，用 ../fit_curve.py 看误差曲线的低谷定为 29.5；王冠画得太扁，crown.py 只重画王冠
set -euo pipefail
cd "$(dirname "$0")"
out=../../GameResources/ui/Icons
py=${PYTHON:-python3}
$py ../pixelize_grid.py draft.png 2 2 "$out/" vd_situation@29.5 - - - >/dev/null
$py crown.py "$out/vd_situation.png"
echo "已生成 $out/vd_situation.png"
