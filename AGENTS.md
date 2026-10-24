## 基本规则

- 始终使用中文回答；新增注释、运行日志、配置描述、文档和提交说明默认使用中文。
- PowerShell 读取文件时必须显式使用 UTF-8 编码，例如 `Get-Content -Encoding utf8`，避免脚本末尾中文字符串被按本地编码解成乱码。
- 不直接修改反编译源码或游戏本体程序集；`Decompiled/` 只作为阅读和定位 Hook 点的参考。
- 新增或修改 Mod 代码统一放在 `src/`，辅助脚本放在 `scripts/`，说明文档放在仓库根目录或 `docs/`。
- `.tools/`、`Decompiled/`、游戏本体、BepInEx 安装目录、构建产物、日志和缓存都不得提交。
- 每次修改后检查 `git status --short --ignored`，确认只跟踪本次相关代码、脚本和文档。
- 反编译工具路径在 E:\project\.tools 目录下，如果没有找到有效的反编译相关工具，请让我立刻去下载补充

## 版本与提交

- 每个 Mod 内部的代码按模块拆分到多个 `.cs` 文件（同一工程下的不同文件名），按等语义划分，禁止全部写在一个文件。
- 每个 `.cs` 文件原则上控制在 500 行以内；特殊情况下最多不超过 1000 行，且需要在该文件头部写明拆分原因。
- 每个 Mod 维护自己的版本号，版本文件放在对应 Mod 工程目录内，例如 `src/<ModName>/version.txt`。
- 版本格式使用 `主版本.次版本.补丁版本`，每格最多到 9，补丁超过 9 进位到次版本，次版本超过 9 进位到主版本，例如 `0.1.8 -> 0.1.9 -> 0.2.0`、`0.9.9 -> 1.0.0`。
- 小修小改递增补丁版本；新增功能或兼容性变化递增次版本；大规模重构或存档/配置不兼容变化递增主版本。
- 每次修改运行时插件后，同步更新插件常量、版本文件和必要文档。
- 每次新增、修改或移除 Mod 功能时，必须同步手动维护 `share/Mod修改内容说明.md`：
  - 在“当前包含的 Mod”中更新完整功能说明。
  - 在“更新记录”中追加或补充当日日期条目，说明本次新增、调整、修复或移除的功能。
  - 该文件位于 `share/` 层级并加入 Git 跟踪，不由分享包脚本自动生成。
- 修改完成后提交 Git commit；提交信息使用中文，简短说明本次变化。
- Mod 编译完成后必须同步复制到游戏对应目录下

## 面板与反编译约定

- 绘制控制面板时参考规范：`%USERPROFILE%\.config\opencode\skills\unity-imgui-mod-panel`（同名技能也在 `~/.workbuddy-ai/skills/unity-imgui-mod-panel`）；若绘制的是需要常态共存而非简单开关使用的UI，询问用户是否需要使用 UniverseLib 来优化性能（通常如DPS显示、血量显示等直接在游玩中共存的才需要）
- 反编译尽量采用类似 Cpp2IL 输出 diffable-cs 格式 这种多文件的形式，不要输出单个反编译文件

## 项目概况

- 游戏：**再再启程 One Way Tickets**（Steam 单机），Unity 2022.3.17f1c1，**Mono 后端**（非 IL2CPP）。
- 游戏目录：`H:\Steam\steamapps\common\再再启程 One Way Tickets`
- Mod 加载器：BepInEx 5.4.21.0 x64（Mono 版，已安装在游戏目录；本地安装包在 `E:\project\.tools\Unity\Modding\BepInEx\BepInEx_x64_5.4.21.0`）。
- 当前只有一个 Mod：`src/OneWayTicketsMod`（再再启程 资源编辑），版本见 `src/OneWayTicketsMod/version.txt`。
- 历史产物：`再再启程 资源编辑表.CT` 是 Cheat Engine 表，已被 Mod 取代，仅留档；
  它的生成脚本与离线单测在 `_work/`（`ct_lua.lua` + `build_ct.py` + `test_*.lua`）。

## 项目结构

```
src/OneWayTicketsMod/      Mod 工程（csproj / Plugin.cs / Game/ / Patches/ / UI/ / version.txt）
docs/                      反编译代码索引 + 各模块详细说明
share/Mod修改内容说明.md    面向玩家的功能说明与更新记录（必须手动维护）
_work/                     CE 时代的生成脚本与离线测试（lua/py）
Decompiled/                反编译产物，不入库
再再启程 资源编辑表.CT      CE 表（留档）
```

代码分层约定：

- `Game/`：业务层，只做「从游戏对象读 / 往游戏对象写」，不引用任何 UI 类型。
- `Patches/`：Harmony 补丁，每个补丁文件只解决一件事，并在文件头写清反编译依据（文件:行号）。
- `UI/`：IMGUI 面板。`PanelTheme` / `PanelWidgets` / `ModPanel` / `PanelHost` 是通用套件，
  业务页面用 `partial class MainPanel` 拆到 `Tab*.cs`。

## 开发与调试

编译与部署（改完必须部署，否则游戏里跑的还是旧 dll）：

```bash
cd src/OneWayTicketsMod
dotnet build -v q
cp bin/Debug/OneWayTicketsMod.dll \
   "H:/Steam/steamapps/common/再再启程 One Way Tickets/BepInEx/plugins/OneWayTicketsMod/"
```

运行日志：`H:\Steam\steamapps\common\再再启程 One Way Tickets\BepInEx\LogOutput.log`

打分享包（发给别人用，产物在 `dist/`，不入库）：

```bash
bash scripts/package.sh
# → dist/再再启程Mod_v<版本>_含BepInEx.zip（解压到游戏根目录即用）
# → dist/再再启程Mod_v<版本>_仅Mod.zip（对方自备 BepInEx 5.4.21 x64）
```

重新反编译（需要时）：

```bash
M="H:/Steam/steamapps/common/再再启程 One Way Tickets/再再启程_Data/Managed"
for dll in Commons Camp World Foundations AutoCodes Assembly-CSharp Init; do
  ilspycmd -p -o "Decompiled/$dll" "$M/$dll.dll"
done
```

验证方式（本机可半自动化）：

- 启动游戏 → 等主菜单 → 用 `SendInput` 点击「继续旅程」进营地 → 按 F1 开面板 → 截图裁左上角 820×640 检查。
- 注意：模拟点击要用 `SendInput`，`mouse_event` 会被游戏忽略；
  坐标按 2560×1440 真实分辨率算（截屏预览常被缩放，别照着预览图量坐标）。

## 关键接口速查（详见 docs/反编译代码索引文档.md）

| 需求 | 取法 |
|---|---|
| 局外存档主体 | `SingletonAccess.Get<CommonContext>()`（营地币 `camp_coins`） |
| 局内 Manager | `SingletonAccess.Mgr<BackPackMgr>("BackPack")` / `Mgr<CharacterMgr>("CharacterMgr")` |
| 局内世界状态 | `SingletonAccess.Get<WorldContext>()`（`caravan_hp` / `caravan_hp_max`） |
| 营地场景根 | `CampSceneRoot.instance`（`coinArea.call()` 刷新左上角资源栏） |
| 全局配置与调试开关 | `Commons.Config.current`（`is_player_invincible` / `all_cc_unlock` 等） |
| 本地化 | `Localization_Utility.get_localization(key)`，返回串要先剥掉 `<...>` 富文本标签 |

## 提交

- 每次修改完代码都进行一次提交，提交 commit 标题自定，内容需要包含详细的信息，至少包含： 对应这次修改的用户提出的需求、支持进行这次提交的反编译源码证据位置、每个文件为什么要进行这样的修改;PowerShell 不支持 heredoc 语法，需要用文件形式提交创建.tmp_commit_msg.txt再提交
- 反编译完成后，必须先对反编译的代码进行一次分析，对项目结构、约束、项目约定等做出说明，写入/docs/反编译代码索引文档.md，包含项目文件索引，并对每个重要文件中包含哪些方法，用于实现什么模块等进行详细的说明，后续读取反编译代码了解更多信息后，及时更新这个文档,对于每个模块的详细说明单独写入同级其他文档，且在索引中做好说明
- 如果目前还没看过历史的提交内容，先阅读一遍提交标题以对项目有一个大体了解，按需了解提交正文内容
