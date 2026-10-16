#!/bin/bash
# 打发布包：bin/dist/DiplomacyOverhaul-v<版本>.zip（放在 bin 下，NML 上传创意工坊时不会带上），解压出来是 DiplomacyOverhaul/ 文件夹，直接放进 worldbox/Mods/
# NML 在游戏启动时编译 Code/ 下的源码，所以包里放源码，只带玩家需要的文件
set -euo pipefail
cd "$(dirname "$0")/.."

version=$(python3 -c 'import json; print(json.load(open("mod.json", encoding="utf-8"))["version"])')
out="${1:-bin/dist}"
name="DiplomacyOverhaul"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

mkdir -p "$stage/$name" "$out"
cp mod.json default_config.json preview.png LICENSE "$stage/$name/"
cp -R Code Locales GameResources "$stage/$name/"
find "$stage" -name '.DS_Store' -delete

zip_path="$(cd "$out" && pwd)/$name-v$version.zip"
rm -f "$zip_path"
(cd "$stage" && zip -qr "$zip_path" "$name")
echo "$zip_path"
