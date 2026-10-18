#!/bin/bash
# 重新生成神力按钮图标，输出到 GameResources/ui/Icons/
# draft.png：GPT Image 2.5 先按 prompt_round1.txt 画了两组，挑出 6 个后按 prompt_smaller.txt 照原样画小（原稿超出 28 格），3x2 排列
# draft_army.png：按 prompt_army.txt 画的 2x2，用上排的两个（一顶头盔加箭头、三顶头盔加箭头）
# draft_release.png：按 prompt_release.txt 画的 2x2，用左上的提线木偶架（线剪断了）；只留挂在木架上的半截线，掉下去的断头去掉；还原后比旁边的图标高，删掉断线中间重复的四行
# 按草图自身的像素网格还原成 28x28，不再手改；大叛乱保留原版 iconFireSkull
set -euo pipefail
cd "$(dirname "$0")"
out=../../GameResources/ui/Icons
py=${PYTHON:-python3}
$py ../pixelize_grid.py draft.png 2 3 "$out/vd_power_" make_vassal incite_independence release_city annex transfer_city mandate
$py ../pixelize_grid.py draft_army.png 2 2 "$out/vd_power_" command_army order_kingdom_attack - -
$py ../pixelize_grid.py draft_release.png 2 2 "$out/vd_power_" release_army - - -
$py trim_rows.py "$out/vd_power_release_army.png" 16,20,24,26
$py keep_attached.py "$out/vd_power_release_army.png"
