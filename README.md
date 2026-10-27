# 再再启程 资源编辑（OneWayTicketsMod）

《再再启程 One Way Tickets》的单机 Mod，基于 **BepInEx 5.4.21（Mono x64）**。
全部功能都是直接读写游戏自己的运行时对象，不扫描内存、不写死地址，游戏小更新通常只需重新编译。

游戏内按 **F1** 打开面板（可改键），面板是覆盖层，不修改游戏资源文件。

## 功能

| 页签 | 能做什么 |
|---|---|
| 概览 | 一键满资源、补建未获得的营地币、解锁全部营地币、实时状态总览 |
| 营地资源 | 里程券 / 食物 / 建材 / 珍宝 / 秘能逐项改，支持批量 +1万 / 拉满 |
| 局内 | 局内金币、车辆耐久与上限、无限耐久、车速直接设值与锁定 |
| 旅人 | 不晕车（补「油门焊死」词缀）、解除「操作中」6 人上限、一键上工位、自动补位、伙伴休息所解锁未见过的角色 |
| 设备 | 免疫点燃 / 机械故障 / 腐蚀 / 缠绕 / 打湿，一键清除异常 |
| 奇物 | 局内自由增删奇物（数据表全部 195 种都能加，可搜索、可批量移除） |
| 设置 | 改开关键、满资源目标值、系统字体、隐藏安全点奇物墙、工位格子常显 |

功能细节与每个版本的变更原因见 [`share/Mod修改内容说明.md`](share/Mod修改内容说明.md)。

## 安装

1. 完全退出游戏。
2. 安装 [BepInEx 5.4.21 x64](https://github.com/BepInEx/BepInEx/releases)（Mono 版，不是 IL2CPP 版）。
3. 把 `OneWayTicketsMod.dll` 放到 `BepInEx/plugins/OneWayTicketsMod/`。
4. 启动游戏，进主菜单后按 F1，能看到面板即为成功。

日志在 `BepInEx/LogOutput.log`，出问题时先看它。

## 自己编译

需要 .NET SDK 6+。改 `src/OneWayTicketsMod/OneWayTicketsMod.csproj` 里的 `GameDir` 指向你的游戏安装目录：

```xml
<GameDir>你的游戏目录</GameDir>
<ManagedDir>$(GameDir)\One Way Tickets_Data\Managed</ManagedDir>
```

然后：

```bash
cd src/OneWayTicketsMod
dotnet build
```

编译产物在 `bin/Debug/OneWayTicketsMod.dll`。

## 目录结构

```
src/OneWayTicketsMod/     Mod 工程
  Game/                   业务层：从游戏对象读 / 往游戏对象写，不引用 UI
  Patches/                Harmony 补丁，每个文件只解决一件事
  UI/                     IMGUI 面板（PanelTheme / PanelWidgets 为通用套件）
docs/                     反编译代码索引 + 各模块实现说明
share/                    面向玩家的功能说明与更新记录
scripts/                  部署与打包脚本
```

代码里的 `Decompiled/...` 引用指向本地反编译产物，**不入库**（见 `.gitignore`），
需要时用 ilspycmd 自己反编译生成，命令见 [`docs/反编译代码索引文档.md`](docs/反编译代码索引文档.md)。

## 注意

- 仅供单机自娱，**请勿用于联机或传播修改后的存档**。
- 改数值本身就是作弊，建议先备份游戏目录下的 `SaveData` 文件夹。
- 免费分享，禁止盗卖。
- 与游戏官方无关；因使用本 Mod 造成的存档损坏、成就异常等后果自负。
