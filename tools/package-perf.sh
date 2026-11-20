#!/bin/bash
# 打独立的性能优化模组（性能大修）的发布包：bin/dist/PerformanceOverhaul-v<版本>.zip，解压出来是 PerformanceOverhaul/ 文件夹，直接放进 worldbox/Mods/
# 外壳在 packages/performance-overhaul/，优化代码取外交大修 Code/Perf/ 顶层的文件（不含 Dev/ 开发工具）
set -euo pipefail
cd "$(dirname "$0")/.."

src="packages/performance-overhaul"
version=$(python3 -c 'import json; print(json.load(open("'"$src"'/mod.json", encoding="utf-8"))["version"])')
out="${1:-bin/dist}"
name="PerformanceOverhaul"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

dst="$stage/$name"
mkdir -p "$dst/Code/Perf" "$out"
cp "$src/mod.json" "$src/default_config.json" LICENSE "$dst/"
[ -f "$src/preview.png" ] && cp "$src/preview.png" "$dst/"
cp -R "$src/Locales" "$dst/"
cp "$src"/Code/*.cs "$dst/Code/"
cp Code/Perf/*.cs "$dst/Code/Perf/"
find "$stage" -name '.DS_Store' -delete

zip_path="$(cd "$out" && pwd)/$name-v$version.zip"
rm -f "$zip_path"
(cd "$stage" && zip -qr "$zip_path" "$name")
echo "$zip_path"
