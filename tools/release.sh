#!/bin/bash
# 发版：检查无误后给 mod.json 里的版本打 tag 并推送，GitHub Actions 随后建 Release 并附上发布包。
# 编译检查要用本机游戏目录里的程序集，所以放在这里跑，不放在 CI
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"

fail() { echo "✗ $*" >&2; exit 1; }

version=$(python3 -c 'import json; print(json.load(open("mod.json", encoding="utf-8"))["version"])')
tag="v$version"
echo "版本 $version"

[ "$(git rev-parse --abbrev-ref HEAD)" = "main" ] || fail "不在 main 分支"
[ -z "$(git status --porcelain)" ] || fail "工作区有未提交的改动"
git rev-parse -q --verify "refs/tags/$tag" >/dev/null && fail "本地已有 tag $tag"
git ls-remote --exit-code --tags origin "$tag" >/dev/null 2>&1 && fail "远端已有 tag $tag"
grep -qx "## $version" CHANGELOG.md || fail "CHANGELOG.md 里没有 ## $version 这一节"
[ -n "$(tools/release-notes.sh "$version")" ] || fail "CHANGELOG.md 里 $version 这一节是空的"

python3 - <<'PY' || fail "JSON 检查没通过"
import json, sys
for f in ["mod.json", "default_config.json", "Locales/ch.json", "Locales/cz.json", "Locales/en.json"]:
    json.load(open(f, encoding="utf-8"))
ch = json.load(open("Locales/ch.json", encoding="utf-8"))
cz = json.load(open("Locales/cz.json", encoding="utf-8"))
en = json.load(open("Locales/en.json", encoding="utf-8"))
if ch != cz:
    sys.exit("ch.json 和 cz.json 内容不一致")
missing = sorted(set(ch) ^ set(en))
if missing:
    sys.exit("中英文 key 不一致: " + ", ".join(missing))
PY
echo "✓ JSON"

build=$(dotnet build -nologo -v q 2>&1 || true)
echo "$build" | grep -q "Build succeeded" || { echo "$build" | grep -E " error " >&2; fail "编译没通过"; }
echo "✓ 编译"

zip=$(tools/package.sh)
echo "✓ 发布包 $zip"
unzip -l "$zip" | tail -1

# 一次推送的提交多、带图片时，HTTPS 默认缓冲区不够，GitHub 会回 HTTP 400
push() { git -c http.postBuffer=524288000 push origin "$@"; }
push main
git tag -a "$tag" -m "版本 $version"
push "$tag"
echo "✓ 已推送 $tag，GitHub Actions 正在建 Release：https://github.com/Lapis0x0/worldbox-diplomacy-overhaul/releases/tag/$tag"
