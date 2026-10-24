# 模块说明 · Mod 工程与 UI 面板

## 1. 目录结构

```
再再启程/
├── AGENTS.md                       # 项目约定（必读）
├── src/OneWayTicketsMod/           # Mod 工程（本仓库唯一的 Mod）
│   ├── OneWayTicketsMod.csproj
│   ├── version.txt                 # 0.2.6，改版本必须同步 Plugin.Version
│   ├── Plugin.cs                   # BepInEx 入口：配置 + 补丁 + 宿主装配
│   ├── Game/                       # 业务层（不依赖 UI）
│   │   ├── SingletonAccess.cs      # 反射读 m_instance，绝不 new
│   │   ├── CampCoinService.cs      # 营地币读写 + HUD 刷新
│   │   ├── WorldService.cs         # 局内金币 / 车辆耐久 / 无限耐久
│   │   ├── CaravanSpeedService.cs  # 车速读取 / 直接设置 / 锁定
│   │   ├── TeamService.cs          # 旅人列表 + 解除操作中人数上限
│   │   ├── CubicleViewService.cs   # 工位格子常显（修进战斗后工位消失）
│   │   ├── CharacterGuardService.cs# 旅人不会晕车 + 全部唤醒
│   │   └── DeviceGuardService.cs   # 设备免疫异常 + 清除异常 + 设备列表
│   ├── Patches/                    # 一个补丁只解决一件事（本作不执行，见旅人模块说明第 3 节）
│   │   ├── CharacterShockPatch.cs  # 旅人不会晕车（ApplyShockForce 前置）
│   │   ├── DeviceOutlierPatch.cs   # 设备免疫异常（applied_outlier 前置，TargetMethod 反射）
│   │   └── CaravanSpeedPatch.cs    # 锁定车速（CaravanMover.move 后置）
│   └── UI/
│       ├── PanelTheme.cs           # 配色 / 圆角九宫格纹理 / 系统中文字体 / 全套 GUIStyle
│       ├── PanelWidgets.cs         # 卡片、KV 行、按钮、输入框、限高滚动
│       ├── ModPanel.cs             # 窗口壳：标题栏 / 竖排导航 / 缩放 / 拖动 / 按键捕获
│       ├── PanelHost.cs            # 常驻 MonoBehaviour：按键轮询 + OnGUI 转发 + 每帧 Tick
│       ├── UiInputGuard.cs         # 面板可见时禁用游戏 UI 输入模块（防点击穿透）
│       ├── MainPanel.cs            # 面板外壳 + 概览页
│       ├── TabCamp.cs              # 营地资源页
│       ├── TabWorld.cs             # 局内页（金币 / 耐久 / 车速）
│       ├── TabTeam.cs              # 旅人页（不晕车 / 解除操作中上限 / 列表）
│       ├── TabDevice.cs            # 设备页（免疫异常 / 设备列表）
│       └── TabSettings.cs          # 设置页
├── docs/                           # 反编译索引 + 各模块说明
├── share/Mod修改内容说明.md         # 面向玩家/分享包的功能说明（必须手动维护）
├── _work/                          # CE 时代的生成脚本与离线测试 + 验证用小工具
│   └── screen_tool.py              # 截图 / SendInput 点击，纯 ctypes，不依赖 Pillow
│   └── winrect.py                  # 枚举窗口矩形 / 把游戏窗口切到前台
├── 再再启程 资源编辑表.CT          # CE 表（已被本 Mod 取代，留档）
└── Decompiled/                     # 反编译产物，不提交
```

## 2. 编译与部署

```bash
cd src/OneWayTicketsMod
dotnet build -v q                 # → bin/Debug/OneWayTicketsMod.dll

# 部署（AGENTS.md 要求：编译完必须同步到游戏目录）
cp bin/Debug/OneWayTicketsMod.dll \
   "H:/Steam/steamapps/common/再再启程 One Way Tickets/BepInEx/plugins/OneWayTicketsMod/"
```

工程关键点：

| 项 | 值 | 原因 |
|---|---|---|
| `TargetFramework` | `netstandard2.1` | Unity 2022.3 的 Mono 能直接加载，且不需要装 .NET Framework 4.x 目标包 |
| `LangVersion` | `10.0` | 面板模板用了 file-scoped namespace（C# 10 语法） |
| `AppendTargetFrameworkToOutputPath` | `false` | 产物直接落在 `bin/Debug/`，方便复制 |
| 引用 | `BepInEx` / `0Harmony` / `UnityEngine(.CoreModule/.IMGUIModule/.InputLegacyModule/.TextRenderingModule/.UI)` / `Commons` `World` `Camp` `Foundations` `AutoCodes` | 全部 `Private=false`，不复制进插件目录 |
| `GameDir` 属性 | csproj 顶部一行 | 换机器只改这一行 |

## 3. 面板套件

面板模板来自 `unity-imgui-mod-panel` 技能，直接复制后只改了三处：

1. 命名空间 `ModPanelKit` → `OneWayTicketsMod.UI`；
2. `PanelTheme.Value` 关掉 `wordWrap`（数值放在定宽行里，换行会撑高行高）；
3. `PanelHost` 增加 `OnTick` 回调（承载「无限耐久」「HUD 脏刷新」「点击拦截」这类常驻逻辑）。

页签用 `partial class MainPanel` 拆到 `Tab*.cs`，每个文件都在 500 行以内。

## 4. 踩过的坑（本作特有）

### 4.1 点击穿透

IMGUI 画在所有 Canvas 之上，但它不参与游戏的 `EventSystem`，
所以点面板按钮时底下的 UGUI 按钮也会被点到。

处理：`UiInputGuard.Sync(panelVisible, enabled)` 每帧检查，
面板可见就把 `EventSystem.current` 上的 `BaseInputModule` 置 `enabled = false`，关闭时恢复。
切场景后 `EventSystem` 会换实例，所以要「发现换了就先还旧的、再拦新的」。
设置页里给了开关（默认开），万一某个场景禁用输入模块会卡住，关掉即可。

### 4.2 光标

面板打开期间每帧强制 `Cursor.visible = true`，关闭时还原游戏原本的值。
（`ModPanelBase.UpdateCursor`）

### 4.3 OnGUI 里必须吞异常

任何异常都会打断 `GUILayout.Begin/End` 配对，之后整帧布局全乱（日志刷 `Mismatched GUILayout`）。
`PanelHost.OnGUI` 整体 try/catch，异常只记日志。

### 4.4 数据读取不能放在每帧热路径

`AutoCodes.*.records` 走 `ExcelAnalyzer.init`（内部 `Assembly.Load` + 反射），
`OnGUI` 一帧要画 2~4 遍，直接调会明显掉帧。
营地币这边做了名字缓存、id 表缓存、0.25 秒 TTL 的列表缓存（写入时立刻失效）。

### 4.5 输入框被每帧覆盖

行内输入框的规则：**只有没有焦点时才把游戏数值同步进缓冲**。

```csharp
if (GUI.GetNameOfFocusedControl() != control) _buf = value.ToString();
GUI.SetNextControlName(control);
_buf = GUILayout.TextField(_buf, ...);
```

### 4.6 单例不要用 `.instance`

`Foundations.Singleton<T>.instance` 在字段为空时会 `new T()`，
Mod 在菜单界面访问就会造出假单例。统一走 `SingletonAccess.Get<T>()` 读 `m_instance`，
拿不到就显示「未就绪」。

## 5. 验证方法

游戏是 2560×1440 无边框全屏。`_work/screen_tool.py` 把整套动作都做成了命令行，
只用 `ctypes`（不依赖 Pillow / pyautogui），也绕开了 PowerShell 对 `Add-Type` 的安全限制：

```bash
P="C:/Users/chen/.workbuddy-ai/binaries/python/versions/3.13.12/python.exe"
$P _work/screen_tool.py shot /tmp/a.png             # 全屏截图
$P _work/screen_tool.py shot /tmp/a.png 0 0 860 680 # 只截左上角（1:1，方便量坐标）
$P _work/screen_tool.py click 60 200                # SendInput 绝对坐标点击
$P _work/screen_tool.py key 112                     # 112 = VK_F1
```

流程：

1. 启动游戏（**必须用 `run_in_background`**，否则 shell 退出会把游戏进程一起带走）；
2. 等约 25 秒到主菜单，先点一下画面空白处让窗口拿到焦点（不点的话 F1 收不到）；
3. `key 112` 开关面板；点左侧导航切页签；
4. `shot ... 0 0 860 680` 截图看面板。

已验证（v0.2.0）：4 个补丁全部挂载成功（见 `BepInEx/LogOutput.log`）、
面板 6 个页签都能正常渲染、新增的「旅人 / 设备 / 车辆速度」三块卡片布局正确。
「车辆速度」卡片在不进局时会被 `InWorld` 判断挡住，验证时临时把该判断改成 `false`
截了一张图确认布局，**验证完必须改回来**（别忘了重新编译部署）。

## 6. 版本管理

- 版本号写在 `src/OneWayTicketsMod/version.txt` 与 `Plugin.Version` 两处，必须同步；
- 规则见 `AGENTS.md`：`主.次.补`，每格 0~9，逐格进位；
- 改了功能就要同步更新 `share/Mod修改内容说明.md` 的「当前包含的 Mod」与「更新记录」。
