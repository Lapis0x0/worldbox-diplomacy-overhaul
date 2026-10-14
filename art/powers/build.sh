#!/bin/bash
# 重新生成神力按钮图标，输出到 GameResources/ui/Icons/
# draft.png：GPT Image 2.5 先按 prompt_round1.txt 画了两组，挑出 6 个后按 prompt_smaller.txt 照原样画小（原稿超出 28 格），3x2 排列
# draft_army.png：按 prompt_army.txt 画的 2x2，用上排的两个（一顶头盔加箭头、三顶头盔加箭头）
# 按草图自身的像素网格还原成 28x28，不再手改；大叛乱保留原版 iconFireSkull
set -euo pipefail
cd "$(dirname "$0")"
out=../../GameResources/ui/Icons
py=${PYTHON:-python3}
$py ../pixelize_grid.py draft.png 2 3 "$out/vd_power_" make_vassal incite_independence release_city annex transfer_city mandate
$py ../pixelize_grid.py draft_army.png 2 2 "$out/vd_power_" command_army order_kingdom_attack - -
