#!/bin/bash
# 从更新日志取出某个版本的那一节（不含 "## 版本" 标题行），用作 GitHub Release 和创意工坊的更新说明
# 用法：tools/release-notes.sh <版本> [更新日志，默认 CHANGELOG.md]
set -euo pipefail
cd "$(dirname "$0")/.."
version="$1"
file="${2:-CHANGELOG.md}"
awk -v v="## $version" '
    $0 == v { found = 1; next }
    found && /^## / { exit }
    found { print }
' "$file" | sed -e '/./,$!d'
