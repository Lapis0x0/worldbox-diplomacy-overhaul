#!/bin/bash
# 重新生成王朝周期四个图标，输出到 GameResources/ui/Icons/
# 新生：原版皇冠逐像素叠加新芽；鼎盛、衰落、崩溃：AI 草图缩成 28x28，再贴原版调色板和宝石
# 草图来自 GPT Image 2.5（编辑接口，参考图是原版图标）：draft.png 的衰落取第二轮、崩溃取第一轮（prompt_round*.txt），
# draft_prime.png 是鼎盛的四个候选（prompt_prime.txt），用左下的桂冠，按草图像素格 29px 缩放，和原版皇冠一样大
set -euo pipefail
cd "$(dirname "$0")"
out=../../GameResources/ui/Icons
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
py=${PYTHON:-python3}
$py build_crown.py iconCrown.png "$out"
$py ../pixelize.py draft.png 2 2 "$tmp/" renewal prime decline collapse >/dev/null
$py ../pixelize.py draft_prime.png 2 2 "$tmp/p_" a b laurel c --unit 29 >/dev/null
$py ../fix_ai.py "$tmp/p_laurel.png" "$out/vd_cycle_prime.png" 0 0,0,0 1 13,14
$py ../fix_ai.py "$tmp/decline.png" "$out/vd_cycle_decline.png" 0.3 120,96,60 0.6 10,15
$py ../fix_ai.py "$tmp/collapse.png" "$out/vd_cycle_collapse.png" 0.55 100,86,70 0.5 13,20 fx
echo "已生成 $out/vd_cycle_*.png"
