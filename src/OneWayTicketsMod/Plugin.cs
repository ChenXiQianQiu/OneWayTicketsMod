// =============================================================================
// Plugin.cs — BepInEx 入口
//
// 职责：读配置 → 挂 Harmony 补丁 → 创建常驻宿主（PanelHost）→ 把面板接上去。
// 具体功能都在 Game/ 下，界面都在 UI/ 下，这里只做装配。
// =============================================================================

using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using OneWayTicketsMod.Game;
using OneWayTicketsMod.Patches;
using OneWayTicketsMod.UI;
using UnityEngine;

namespace OneWayTicketsMod
{
    [BepInPlugin(Guid, PluginName, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "onewaytickets.resedit";
        public const string PluginName = "再再启程 资源编辑";
        public const string Version = "0.2.3";

        internal static Plugin Instance { get; private set; }

        /// <summary>给 Game/ 层用的日志出口（BaseUnityPlugin.Logger 是 protected，外部访问不到）。</summary>
        internal static void LogInfo(string msg) => Instance?.Logger.LogInfo(msg);

        internal static void LogWarn(string msg) => Instance?.Logger.LogWarning(msg);

        internal static ConfigEntry<KeyCode> ToggleKey;
        internal static ConfigEntry<float> PanelScale;
        internal static ConfigEntry<int> MaxValue;
        internal static ConfigEntry<bool> RestCharacterEnabled;
        internal static ConfigEntry<bool> NoComa;
        internal static ConfigEntry<bool> DeviceImmune;
        internal static ConfigEntry<bool> UseSystemFont;
        internal static ConfigEntry<bool> ClickGuard;

        private Harmony _harmony;
        private PanelHost _host;

        private void Awake()
        {
            Instance = this;

            ToggleKey = Config.Bind("界面", "打开面板", KeyCode.F1,
                "按这个键打开 / 关闭面板。也可以在面板的「设置」页里改。");
            PanelScale = Config.Bind("界面", "面板缩放", 1f,
                new ConfigDescription("整窗缩放（含文字），0.6 ~ 2.5", new AcceptableValueRange<float>(0.6f, 2.5f)));
            UseSystemFont = Config.Bind("界面", "使用系统中文字体", true,
                "关掉会退回 Unity 内置字体，中文会变细变糊，一般不用关。");
            ClickGuard = Config.Bind("界面", "面板打开时挡住游戏点击", true,
                "面板是 IMGUI 覆盖层，不会自动拦住游戏的 UI 点击。开启后打开面板期间会临时禁用游戏的 UI 输入模块，避免点面板同时点到底下的按钮。");

            MaxValue = Config.Bind("通用", "一键满资源的目标值", 999999,
                new ConfigDescription("「一键满资源」和「满」按钮写入的数值", new AcceptableValueRange<int>(1, 999999999)));

            RestCharacterEnabled = Config.Bind("功能", "休息区旅人也能上工位", false,
                "开启后，休息区（后备队）的旅人也能上工位：安排工位时会先把人换到前排「操作中」，前排满 6 人则自动换下一个空闲的人。改动立即生效，不用重启。");

            NoComa = Config.Bind("功能", "旅人不会晕车", false,
                "开启后给每个旅人补一份「油门焊死」词缀（免疫冲击昏迷），车辆颠簸、设备受击的震动都不再让旅人晕倒。改动立即生效，不用重启。");

            DeviceImmune = Config.Bind("功能", "设备免疫异常状态", false,
                "开启后车上设备不会被点燃 / 机械故障 / 腐蚀 / 缠绕 / 打湿。做法是每 0.2 秒把设备钉在游戏的「免疫」状态上（游戏判定异常的第一行就是免疫则跳过）。只免疫异常状态，普通掉血仍然生效。");

            PanelTheme.Log = msg => Logger.LogInfo(msg);
            PanelTheme.UseSystemFont = UseSystemFont.Value;
            TeamService.RestToFrontEnabled = RestCharacterEnabled.Value;
            CharacterGuardService.SetEnabled(NoComa.Value);
            DeviceGuardService.SetEnabled(DeviceImmune.Value);

            RestCharacterEnabled.SettingChanged += (_, __) =>
            {
                TeamService.RestToFrontEnabled = RestCharacterEnabled.Value;
                Logger.LogInfo($"休息区旅人可上工位：{(RestCharacterEnabled.Value ? "已开启" : "已关闭")}");
            };

            NoComa.SettingChanged += (_, __) =>
            {
                // 走 SetEnabled 而不是直接赋值：切换后要立刻补 / 撤词缀，不能等下一轮轮询
                CharacterGuardService.SetEnabled(NoComa.Value);
                Logger.LogInfo($"旅人不会晕车：{(NoComa.Value ? "已开启" : "已关闭")}");
            };

            DeviceImmune.SettingChanged += (_, __) =>
            {
                DeviceGuardService.SetEnabled(DeviceImmune.Value);
                Logger.LogInfo($"设备免疫异常状态：{(DeviceImmune.Value ? "已开启" : "已关闭")}");
            };

            UseSystemFont.SettingChanged += (_, __) =>
            {
                PanelTheme.UseSystemFont = UseSystemFont.Value;
                PanelTheme.Invalidate();
            };

            _harmony = new Harmony(Guid);

            // 逐个补丁类单独挂载：某个补丁点因游戏更新失效时，
            // 只影响它自己，不会连带其它功能一起不可用。
            PatchAll(typeof(CubicleSetPatch),
                     typeof(CharacterShockPatch),
                     typeof(DeviceOutlierPatch),
                     typeof(CaravanSpeedPatch));

            CreateHost();

            Logger.LogInfo($"{PluginName} v{Version} 已加载，按 {ToggleKey.Value} 打开面板。");
        }

        /// <summary>逐个挂载补丁，单个失败只记录不影响其它。</summary>
        private void PatchAll(params Type[] patchTypes)
        {
            foreach (var type in patchTypes)
            {
                try
                {
                    _harmony.PatchAll(type);
                    Logger.LogInfo($"补丁已挂载：{type.Name}");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"补丁 {type.Name} 挂载失败，对应功能不可用：{ex.Message}");
                }
            }
        }

        private void CreateHost()
        {
            var go = new GameObject("OneWayTicketsMod_Host");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;

            _host = go.AddComponent<PanelHost>();
            _host.Panel = new MainPanel();
            _host.ToggleKey = () => ToggleKey.Value;
            _host.LogError = msg => Logger.LogError(msg);
            // 每帧常驻逻辑：无限耐久 + 车速峰值记录 + 不晕车词缀同步 + 设备免疫轮询 + 面板打开时挡住游戏点击。
            // 挂在宿主上而不是面板里，这样面板关着也照跑。
            _host.OnTick = () =>
            {
                WorldService.Tick();
                CaravanSpeedService.Tick();
                CharacterGuardService.Tick();
                DeviceGuardService.Tick();
                CampCoinService.Tick();
                UiInputGuard.Sync(_host.Panel != null && _host.Panel.Visible, ClickGuard.Value);
            };
        }

        private void OnDestroy()
        {
            UiInputGuard.Restore();

            try
            {
                _harmony?.UnpatchSelf();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("卸载 Harmony 补丁时出错：" + ex.Message);
            }
        }
    }
}
