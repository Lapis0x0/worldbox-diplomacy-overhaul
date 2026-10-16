#!/bin/bash
# 编译 Code/ 并通知正在运行的游戏热重载（开发用）
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"

# 每次用不同的程序集名，避免和已载入的旧版本冲突
name="DiplomacyOverhaul_hot_$(date +%s)"
out="bin/hot"
rm -rf "$out"
if ! build=$(dotnet build -nologo -v q -p:AssemblyName="$name" -o "$out" 2>&1); then
  echo "$build" | grep -E " error " | sort -u >&2
  echo "✗ 编译失败，没有通知游戏" >&2
  exit 1
fi

log="$HOME/Library/Logs/mkarpenko/WorldBox/Player.log"
before=$(wc -l < "$log" 2>/dev/null || echo 0)
echo "$PWD/$out/$name.dll" > bin/reload.signal.tmp
mv bin/reload.signal.tmp bin/reload.signal
echo "已通知游戏重载 $name"

# 等游戏真正载入新版本再退出：重载会清空正在执行的开发命令，紧接着跑 tools/dev.sh 不能和它撞上
pgrep -qf "worldbox.app/Contents/MacOS/WorldBox" || exit 0
for _ in $(seq 60); do
  new=$(tail -n +$((before + 1)) "$log")
  if grep -qF "hot reloaded $name.dll" <<< "$new"; then echo "✓ 重载完成"; exit 0; fi
  if grep -qF "hot reload failed" <<< "$new"; then
    echo "✗ 重载失败，见 $log" >&2; exit 1
  fi
  sleep 0.5
done
echo "✗ 30 秒内游戏没有完成重载" >&2
exit 1
