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

echo "$PWD/$out/$name.dll" > bin/reload.signal.tmp
mv bin/reload.signal.tmp bin/reload.signal
echo "已通知游戏重载 $name"
