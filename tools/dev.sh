#!/bin/bash
# 给正在运行的游戏发开发命令（见 Code/Dev/Dev.cs），等执行完后打印这次的输出。
# 用法：tools/dev.sh "load bin/bench/saves/tamriel_910" "speed x5" "measure base 30"
# 相对路径按仓库根目录解析
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
dir="bin/dev"
mkdir -p "$dir"
touch "$dir/out.log"
token="t$(date +%s)$RANDOM"
start=$(wc -l < "$dir/out.log")
{
  for c in "$@"; do
    if [[ "$c" == load\ * && "${c#load }" != /* ]]; then c="load $root/${c#load }"; fi
    echo "$c"
  done
  echo "end $token"
} > "$dir/cmd.tmp"
mv "$dir/cmd.tmp" "$dir/cmd.txt"

timeout="${DEV_TIMEOUT:-900}"
waited=0
until grep -q "@done $token" "$dir/out.log"; do
  if ! pgrep -qf "worldbox.app/Contents/MacOS/WorldBox"; then echo "✗ 游戏没有在运行" >&2; exit 1; fi
  if [ "$waited" -ge "$timeout" ]; then echo "✗ 超时，最后的输出：" >&2; tail -n +$((start + 1)) "$dir/out.log" >&2; exit 1; fi
  sleep 1; waited=$((waited + 1))
done
tail -n +$((start + 1)) "$dir/out.log" | grep -v "@done $token"
