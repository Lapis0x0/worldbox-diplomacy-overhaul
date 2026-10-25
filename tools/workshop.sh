#!/bin/bash
# 用 SteamCMD 更新创意工坊物品：上传发布包里的文件、封面，更新说明取 CHANGELOG 这个版本的一节。
# 标题、简介、可见性不动，简介在网页上改。
# Steam 账号名放在 bin/steam_user；登录状态由 SteamCMD 缓存，第一次要在终端里手动登录：
#   HOME=~/.steamcmd steamcmd +login <账号名> +quit
# macOS 上 SteamCMD 默认和 Steam 客户端共用 ~/Library/Application Support/Steam，
# 客户端一启动就会清掉 SteamCMD 的登录缓存，所以给它单独的 HOME
set -euo pipefail
cd "$(dirname "$0")/.."

fail() { echo "✗ $*" >&2; exit 1; }

app_id=1206560          # WorldBox
item_id=3817246294      # 外交大修

command -v steamcmd >/dev/null || fail "没装 SteamCMD：brew install --cask steamcmd"
[ -s bin/steam_user ] || fail "bin/steam_user 里没有 Steam 账号名"
user=$(tr -d '[:space:]' < bin/steam_user)
steam_home="$HOME/.steamcmd"
mkdir -p "$steam_home"

version=$(python3 -c 'import json; print(json.load(open("mod.json", encoding="utf-8"))["version"])')
notes=$(tools/release-notes.sh "$version")
[ -n "$notes" ] || fail "CHANGELOG.md 里没有 $version 的说明"
echo "版本 $version"

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
zip=$(tools/package.sh "$stage")
unzip -q "$zip" -d "$stage"
content="$stage/DiplomacyOverhaul"
[ -f "$content/mod.json" ] || fail "发布包里没有 mod.json"

vdf="$stage/item.vdf"
CONTENT="$content" PREVIEW="$content/preview.png" NOTES="$notes" APP="$app_id" ITEM="$item_id" python3 - > "$vdf" <<'PY'
import os
import re
# SteamCMD 解析 VDF 不认反斜杠转义，遇到直引号就当字符串结束：说明里的直引号成对换成弯引号
def curly(s): return re.sub(r'"([^"\n]*)"', '\u201c\\1\u201d', s).replace('"', '\u201d')
def q(s): return '"' + curly(s) + '"'
fields = [("appid", os.environ["APP"]), ("publishedfileid", os.environ["ITEM"]),
          ("contentfolder", os.environ["CONTENT"]), ("previewfile", os.environ["PREVIEW"]),
          ("changenote", os.environ["NOTES"])]
print('"workshopitem"\n{')
for k, v in fields: print(f'\t{q(k)}\t{q(v)}')
print('}')
PY

log="$stage/steamcmd.log"
# 登录状态失效时直接报错，不停在输入密码的提示上
HOME="$steam_home" steamcmd +@NoPromptForPassword 1 +login "$user" +workshop_build_item "$vdf" +quit 2>&1 | tee "$log" | grep -E "Uploading|Success|ERROR|FAILED|Failed|failed" || true
grep -q "Success" "$log" || { tail -20 "$log" >&2; fail "上传没成功"; }
echo "✓ 已更新创意工坊：https://steamcommunity.com/sharedfiles/filedetails/?id=$item_id"
