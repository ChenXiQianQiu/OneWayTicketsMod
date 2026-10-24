#!/usr/bin/env bash
# =============================================================================
# deploy.sh — 编译并把 Mod 复制到游戏 BepInEx/plugins 目录
#
# 用法（仓库根目录执行）：
#   bash scripts/deploy.sh
#
# 注意：游戏运行时 OneWayTicketsMod.dll 会被占用，复制会 Permission denied。
# 必须先把游戏完全退出，再跑本脚本。
# =============================================================================

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/src/OneWayTicketsMod"
GAME="H:/Steam/steamapps/common/再再启程 One Way Tickets"
DEST="$GAME/BepInEx/plugins/OneWayTicketsMod"

echo "== 编译 =="
(cd "$PROJ" && dotnet build -v q)

echo "== 复制 =="
mkdir -p "$DEST"
if ! cp "$PROJ/bin/Debug/OneWayTicketsMod.dll" "$DEST/"; then
    echo "复制失败：游戏可能还在运行，请先完全退出游戏再重试。" >&2
    exit 1
fi

echo "== 完成 =="
echo "版本：$(cat "$PROJ/version.txt")"
ls -la "$DEST"
