#!/bin/bash
# 重新生成宪章的两张图：行标题上的卷轴图标、现行选项的金包角按钮
# draft_charter.png 是 GPT Image 2.5 的草图，用左上那格（金卷轴 + 金锁），直接缩小，不还原像素网格
# 按钮取原版 special_buttonRedGodly（先用 ../extract_icons.py 抽出来），四角原样保留，中间拉伸
set -euo pipefail
cd "$(dirname "$0")"
py=${PYTHON:-python3}
godly=${GODLY:?先用 ../extract_icons.py 抽出 special_buttonRedGodly，把路径传给 GODLY}
$py build_icon.py draft_charter.png ../../GameResources/ui/Icons/vd_charter.png
$py build_button.py "$godly" ../../GameResources/ui/special/vd_charter_button.png
echo "已生成 vd_charter.png、vd_charter_button.png"
