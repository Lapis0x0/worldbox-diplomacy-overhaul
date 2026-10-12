#!/bin/bash
# 从 CHANGELOG.md 取出某个版本的那一节（不含 "## 版本" 标题行），用作 GitHub Release 和创意工坊的更新说明
set -euo pipefail
cd "$(dirname "$0")/.."
version="$1"
awk -v v="## $version" '
    $0 == v { found = 1; next }
    found && /^## / { exit }
    found { print }
' CHANGELOG.md | sed -e '/./,$!d'
