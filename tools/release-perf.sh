#!/bin/bash
# 性能大修发版：检查无误后给 packages/performance-overhaul/mod.json 里的版本打 tag perf-v<版本> 并推送，
# GitHub Actions（release-perf.yml）随后建 Release 并附上发布包。和外交大修的 v<版本> 互不相干
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"

fail() { echo "✗ $*" >&2; exit 1; }

src=packages/performance-overhaul
version=$(python3 -c 'import json, sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["version"])' "$src/mod.json")
tag="perf-v$version"
echo "性能大修 版本 $version"

[ "$(git rev-parse --abbrev-ref HEAD)" = "main" ] || fail "不在 main 分支"
[ -z "$(git status --porcelain)" ] || fail "工作区有未提交的改动"
git rev-parse -q --verify "refs/tags/$tag" >/dev/null && fail "本地已有 tag $tag"
git ls-remote --exit-code --tags origin "$tag" >/dev/null 2>&1 && fail "远端已有 tag $tag"
grep -qx "## $version" "$src/CHANGELOG.md" || fail "$src/CHANGELOG.md 里没有 ## $version 这一节"
[ -n "$(tools/release-notes.sh "$version" "$src/CHANGELOG.md")" ] || fail "$src/CHANGELOG.md 里 $version 这一节是空的"

python3 - "$src" <<'PY' || fail "JSON 检查没通过"
import json, sys
src = sys.argv[1]
for f in ["mod.json", "default_config.json"]:
    json.load(open(f"{src}/{f}", encoding="utf-8"))
loc = {l: json.load(open(f"{src}/Locales/{l}.json", encoding="utf-8")) for l in ["ch", "cz", "en", "br"]}
if loc["ch"] != loc["cz"]:
    sys.exit("ch.json 和 cz.json 内容不一致")
for l in ["en", "br"]:
    missing = sorted(set(loc["ch"]) ^ set(loc[l]))
    if missing:
        sys.exit(f"ch.json 和 {l}.json 的 key 不一致: " + ", ".join(missing))
PY
echo "✓ JSON"

build=$(dotnet build "$src/PerformanceOverhaul.csproj" -nologo -v q 2>&1 || true)
echo "$build" | grep -q "Build succeeded" || { echo "$build" | grep -E " error " >&2; fail "编译没通过"; }
echo "✓ 编译"

zip=$(tools/package-perf.sh)
echo "✓ 发布包 $zip"
unzip -l "$zip" | tail -1

push() { git -c http.postBuffer=524288000 push origin "$@"; }
push main
git tag -a "$tag" -m "性能大修 $version"
push "$tag"
echo "✓ 已推送 $tag，GitHub Actions 正在建 Release：https://github.com/Lapis0x0/worldbox-diplomacy-overhaul/releases/tag/$tag"
