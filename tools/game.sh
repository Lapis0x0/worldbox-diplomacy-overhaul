#!/bin/bash
# 启动 / 退出 / 重启游戏，等到模组加载完成（开发用）
# 用法：tools/game.sh start|stop|restart|status
set -euo pipefail
log="$HOME/Library/Logs/mkarpenko/WorldBox/Player.log"
proc="worldbox.app/Contents/MacOS/WorldBox"

running() { pgrep -qf "$proc"; }

stop() {
  running || return 0
  pkill -TERM -f "$proc" || true
  for _ in $(seq 30); do running || return 0; sleep 1; done
  pkill -KILL -f "$proc" || true
}

start() {
  running && { echo "游戏已在运行"; return 0; }
  local t0; t0=$(date +%s)
  open "steam://rungameid/1206560"
  for _ in $(seq 180); do
    sleep 1
    # 启动时 Player.log 会被换成新文件，旧文件里的内容不算
    [ -f "$log" ] && [ "$(stat -f %B "$log")" -ge "$t0" ] || continue
    if grep -q "外交大修 loaded" "$log"; then
      # 模组加载后还要等第一张地图生成
      for _ in $(seq 120); do
        sleep 1
        grep -q "\[perf\] ready" "$log" && { echo "游戏已启动"; return 0; }
      done
      echo "✗ 模组已加载，但没等到世界就绪" >&2; return 1
    fi
    if grep -q "error CS" "$log"; then
      echo "✗ 模组编译失败：" >&2; grep "error CS" "$log" | head -20 >&2; return 1
    fi
  done
  echo "✗ 启动超时" >&2; return 1
}

case "${1:-}" in
  start) start ;;
  stop) stop; echo "游戏已退出" ;;
  restart) stop; sleep 2; start ;;
  status) running && echo "运行中" || echo "未运行" ;;
  *) echo "用法：$0 start|stop|restart|status" >&2; exit 2 ;;
esac
