#!/bin/bash
# 重新生成整合度四档的图标，输出到 GameResources/ui/Icons/
# 草图 draft.png 是 GPT Image 2.5 画的一行四个（积怨、离心、一体、归附），参考图是原版 iconLoyalty 等，提示词见 prompt.txt
# 按草图自身的像素网格还原成 28x28，不再手改
set -euo pipefail
cd "$(dirname "$0")"
out=../../GameResources/ui/Icons
py=${PYTHON:-python3}
$py ../pixelize_grid.py draft.png 1 4 "$out/vd_integ_" grudge drift unity loyal
