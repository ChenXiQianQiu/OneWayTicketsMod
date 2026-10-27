#!/usr/bin/env bash
# =============================================================================
# package.sh — 打一个能直接发给别人的分享包
#
# 用法（仓库根目录执行）：
#   bash scripts/package.sh
#
# 产物（dist/ 下，不入库）：
#   再再启程Mod_v<版本>_含BepInEx.zip  —— 解压到游戏根目录即用（含 BepInEx 5.4.21 x64）
#   再再启程Mod_v<版本>_仅Mod.zip      —— 只含 Mod 本体，对方需自备 BepInEx 5.4.21
#
# 说明：
#   · 版本号取自 src/OneWayTicketsMod/version.txt，文件名与包内说明都跟着变；
#   · BepInEx 从本地原始安装包取（不含 config / cache / 日志），对方拿到的是干净配置；
#   · 压缩走 .NET 的 ZipFile + UTF-8 标志位，避免中文文件名在 Windows 自带解压里变乱码。
# =============================================================================

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/src/OneWayTicketsMod"
BEPINEX_SRC="E:/project/.tools/Unity/Modding/BepInEx/BepInEx_x64_5.4.21.0"
DIST="$ROOT/dist"
STAGE="$DIST/_stage"

VERSION="$(tr -d '[:space:]' < "$PROJ/version.txt")"
FOLDER="再再启程Mod_v$VERSION"

if [ ! -f "$BEPINEX_SRC/winhttp.dll" ]; then
    echo "找不到 BepInEx 原始安装包：$BEPINEX_SRC" >&2
    echo "请确认路径，或在脚本里改 BEPINEX_SRC。" >&2
    exit 1
fi

echo "== 编译 =="
(cd "$PROJ" && dotnet build -v q)

DLL="$PROJ/bin/Debug/OneWayTicketsMod.dll"
[ -f "$DLL" ] || { echo "编译产物不存在：$DLL" >&2; exit 1; }

echo "== 准备目录 =="
rm -rf "$STAGE"
mkdir -p "$STAGE"

# ---------------------------------------------------------------- 安装说明
# $1 = 输出路径；$2 = yes 表示包里自带 BepInEx
write_readme() {
    local dest="$1"
    local with_bepinex="$2"

    {
        # UTF-8 BOM：保证 Windows 记事本直接打开不乱码
        printf '\xEF\xBB\xBF'
        printf '再再启程 One Way Tickets · 资源编辑 Mod\n'
        printf '=========================================\n\n'
        printf '版本：v%s\n' "$VERSION"
        printf '适用：Steam 版《再再启程 One Way Tickets》（Unity 2022.3 / Mono 后端）\n'
        printf '打开面板：进游戏后按 F1（可在面板「设置」页改键）\n\n'

        if [ "$with_bepinex" = "yes" ]; then
            cat <<'TXT'
安装步骤（本包已自带 BepInEx 5.4.21 x64）
------------------------------------------
1. 先完全退出游戏（Steam 里也要确认进程已经结束）。
2. 把本压缩包里的全部内容解压到游戏根目录，也就是有「One Way Tickets.exe」的那个文件夹。
   解压后应该长这样：

     Onewaytickets\
       ├─ One Way Tickets.exe
       ├─ winhttp.dll                 ← 本包提供
       ├─ doorstop_config.ini         ← 本包提供
       └─ BepInEx\
            ├─ core\                  ← 本包提供
            └─ plugins\
                 └─ OneWayTicketsMod\OneWayTicketsMod.dll   ← 本 Mod

   如果之前已经装过 BepInEx，只把 BepInEx\plugins\OneWayTicketsMod 这个文件夹复制过去就行，
   不要覆盖自己原来的 BepInEx 配置。
3. 启动游戏，进到主菜单后按 F1，能看到面板就成功了。

TXT
        else
            cat <<'TXT'
安装步骤（本包只有 Mod 本体）
------------------------------------------
前提：你已经装好了 BepInEx 5.4.21 x64（Mono 版）。

1. 先完全退出游戏。
2. 把本包里的 BepInEx\plugins\OneWayTicketsMod 整个文件夹复制到游戏根目录的
   BepInEx\plugins 下，最终路径是：

     <游戏根目录>\BepInEx\plugins\OneWayTicketsMod\OneWayTicketsMod.dll

3. 启动游戏，进到主菜单后按 F1，能看到面板就成功了。

TXT
        fi

        cat <<'TXT'
功能一览
--------
按 F1 打开面板，页签从左到右：
  · 概览：一键满资源、补建未获得的营地币、解锁全部营地币
  · 营地资源：里程券 / 食物 / 建材 / 珍宝 / 秘能逐项改，或全部 +1万 / 拉满
  · 局内：金币、车辆耐久与上限、无限耐久、车速（直接设值 / 锁定）
  · 旅人：不晕车、解除「操作中」6 人上限、一键上工位、自动补位、
          伙伴休息所（点亮「没见过」的角色、把特殊伙伴直接收下）
  · 设备：免疫点燃 / 机械故障 / 腐蚀 / 缠绕 / 打湿，一键清除异常
  · 奇物：局内自由增删奇物（全部 195 种都能加，可搜索、可批量移除）
  · 设置：改开关键、满资源目标值、系统字体、隐藏安全点奇物墙、工位格子常显

常见问题
--------
1. 按 F1 没反应
   先确认 BepInEx 本身有没有装上：看游戏根目录有没有 BepInEx\LogOutput.log。
   没有这个文件说明 BepInEx 没生效（见下面「BepInEx 没加载」）。

2. BepInEx 没加载
   · 确认 winhttp.dll、doorstop_config.ini、BepInEx 文件夹三者都和游戏的 exe 在同一个目录；
   · 确认 doorstop_config.ini 里 enabled=true；
   · 游戏是 64 位版本，必须用 BepInEx x64（不是 x86）。

3. 日志在哪
   游戏根目录 BepInEx\LogOutput.log，出错时把最后几十行发出来就行。

4. 杀毒软件报毒
   winhttp.dll 是 BepInEx 的注入器，会被部分杀软误报，需要自己加信任。
   不放心可以只装「仅 Mod」包，用你自己下载的 BepInEx。

5. 会不会坏存档
   这个 Mod 只读写游戏自己的存档对象，不改存档格式。但改数值本身就是作弊，
   建议先备份存档：游戏根目录下的 SaveData 文件夹。
   另外它是单机 Mod，别拿改过的存档去联机或到处传播。

6. 卸载
   删掉 BepInEx\plugins\OneWayTicketsMod 文件夹即可；
   完整包想彻底还原，再把 winhttp.dll、doorstop_config.ini、BepInEx 文件夹一起删掉。
TXT
    } > "$dest"

    # 转成 CRLF，Windows 记事本打开才正常换行
    sed -i 's/$/\r/' "$dest"
}

# ---------------------------------------------------------------- 仅 Mod 包
echo "== 组装：仅 Mod =="
ONLY="$STAGE/only/$FOLDER"
mkdir -p "$ONLY/BepInEx/plugins/OneWayTicketsMod"
cp "$DLL" "$ONLY/BepInEx/plugins/OneWayTicketsMod/"
write_readme "$ONLY/安装说明.txt" no

# ---------------------------------------------------------------- 完整包
echo "== 组装：含 BepInEx =="
FULL="$STAGE/full/$FOLDER"
mkdir -p "$FULL/BepInEx/plugins/OneWayTicketsMod"
cp -r "$BEPINEX_SRC/BepInEx/core" "$FULL/BepInEx/"
cp "$BEPINEX_SRC/winhttp.dll" "$FULL/"
cp "$BEPINEX_SRC/doorstop_config.ini" "$FULL/"
cp "$DLL" "$FULL/BepInEx/plugins/OneWayTicketsMod/"
write_readme "$FULL/安装说明.txt" yes

# ---------------------------------------------------------------- 压缩
echo "== 压缩 =="
mkdir -p "$DIST"
zip_dir() {
    local wsrc wdst
    wsrc="$(cygpath -w "$1")"
    wdst="$(cygpath -w "$2")"
    powershell -NoProfile -Command \
        "Add-Type -AssemblyName System.IO.Compression.FileSystem; \
         if (Test-Path '$wdst') { Remove-Item -LiteralPath '$wdst' -Force }; \
         [System.IO.Compression.ZipFile]::CreateFromDirectory('$wsrc', '$wdst', \
            [System.IO.Compression.CompressionLevel]::Optimal, \$true, [System.Text.Encoding]::UTF8)"
}

FULL_ZIP="$DIST/再再启程Mod_v${VERSION}_含BepInEx.zip"
ONLY_ZIP="$DIST/再再启程Mod_v${VERSION}_仅Mod.zip"
# 先清掉 dist 里其它版本的包，避免发错
rm -f "$DIST"/再再启程Mod_v*_含BepInEx.zip "$DIST"/再再启程Mod_v*_仅Mod.zip
zip_dir "$STAGE/full/$FOLDER" "$FULL_ZIP"
zip_dir "$STAGE/only/$FOLDER" "$ONLY_ZIP"

rm -rf "$STAGE"

echo "== 完成 =="
echo "版本：$VERSION"
ls -lh "$DIST"/*.zip
