#!/bin/bash
# 反编译原版程序集到固定目录并打印路径；程序集没变就直接用上次的结果（游戏更新后自动重做）
# 用法：tools/decomp.sh            打印反编译目录
#       grep -rn xxx "$(tools/decomp.sh)"
set -euo pipefail
dll="$HOME/Library/Application Support/Steam/steamapps/common/worldbox/worldbox.app/Contents/Resources/Data/Managed/Assembly-CSharp.dll"
out="$HOME/Library/Caches/worldbox-decomp"
stamp="$out/.dll-sha"

sha=$(shasum "$dll" | cut -d' ' -f1)
if [ -f "$stamp" ] && [ "$(cat "$stamp")" = "$sha" ]; then
  echo "$out"
  exit 0
fi

echo "程序集有变化，重新反编译（约二十秒）" >&2
rm -rf "$out.tmp"
DOTNET_ROLL_FORWARD=Major PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH" DOTNET_ROOT="$HOME/.dotnet" \
  ilspycmd -p -o "$out.tmp" "$dll" >/dev/null
echo "$sha" > "$out.tmp/.dll-sha"
rm -rf "$out"
mv "$out.tmp" "$out"
echo "$out"
