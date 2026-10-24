// =============================================================================
// TabDevice.cs — 「设备」页签：车上设备免疫异常状态 + 设备状态一览
//
// 反编译依据：
//   · Decompiled/World/World.Devices/Device.cs:21-30   DeviceState 枚举（异常种类）
//   · Decompiled/World/World.Devices/Device.cs:795     applied_outlier 第一行就是
//                                                      `if (state == immue) return;`
//   · Decompiled/World/World.Devices/Device.cs:392     CheckEmergency 每 tick 扣 immu_countdown
//   · Decompiled/World/World.Devices/DeviceMgr.cs:22   slots_device（车上所有设备槽位）
//
// v0.2.2 起免疫改成「轮询钉住 immue 状态」（Harmony 在本作不执行），
// 只免疫「异常状态」，普通掉血仍走 Device.Hurt —— 那部分交给「局内」页的无限耐久。
// =============================================================================

using OneWayTicketsMod.Game;
using UnityEngine;
using static OneWayTicketsMod.UI.PanelWidgets;

namespace OneWayTicketsMod.UI
{
    internal sealed partial class MainPanel
    {
        private void DrawDeviceTab()
        {
            BeginCard("设备免疫异常状态");
            bool immune = ToggleChip(Plugin.DeviceImmune.Value,
                "设备不会被点燃 / 机械故障 / 腐蚀 / 缠绕 / 打湿");
            if (immune != Plugin.DeviceImmune.Value)
            {
                Plugin.DeviceImmune.Value = immune;
                Notify(immune ? "已开启：车上设备不会再进异常状态" : "已关闭：恢复游戏原本的异常状态", 3.5f);
            }

            int abnormal = DeviceGuardService.AbnormalCount();
            int immuneCount = DeviceGuardService.ImmuneCount();

            GUILayout.BeginHorizontal();
            Caption("异常设备", 60f);
            GUILayout.Label(abnormal > 0 ? $"{abnormal} 台" : "0 台",
                abnormal > 0 ? PanelTheme.Value : PanelTheme.Sub, GUILayout.Width(56f));

            Caption("免疫中", 50f);
            Color savedColor = GUI.contentColor;
            GUI.contentColor = immuneCount > 0 ? PanelTheme.TxtOk : PanelTheme.TxtWeak;
            GUILayout.Label($"{immuneCount} 台", PanelTheme.Sub, GUILayout.Width(56f));
            GUI.contentColor = savedColor;

            if (Btn("全部清除", 76f, PanelTheme.Mini, 20f))
            {
                int n = DeviceGuardService.ClearAll();
                Notify(n > 0 ? $"已清除 {n} 台设备的异常状态" : "当前没有异常状态的设备。");
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            Hint("异常状态指的是设备面板上的燃烧、机械故障、腐蚀、缠绕、潮湿这五种。");
            Hint("开启后每 0.2 秒把设备钉在游戏的「免疫」状态上——游戏自己判定异常的第一行就是「免疫则跳过」，所以五种异常都进不来。");
            Hint("「免疫中」就是被钉住的状态，属正常现象；关掉开关会立刻恢复成「正常」。");
            Hint("只免疫异常，普通掉血仍会掉（要免掉血请开「局内」页的无限耐久）。");
            EndCard();

            if (!DeviceGuardService.Ready)
            {
                BeginCard("设备列表");
                Info("当前不在探索场景，读不到设备数据。");
                EndCard();
                return;
            }

            var devices = DeviceGuardService.Collect();
            BeginCard($"设备列表（共 {devices.Count} 台 · 异常 {abnormal} 台）");

            if (devices.Count == 0)
            {
                Info("车上还没有装设备。");
                EndCard();
                return;
            }

            _deviceScroll = BeginList(_deviceScroll, 260f);
            foreach (var e in devices)
            {
                GUILayout.BeginHorizontal(PanelTheme.Row, GUILayout.Height(21f));

                Color saved = GUI.contentColor;
                GUI.contentColor = e.Alive ? PanelTheme.TxtWeak : PanelTheme.TxtBad;
                GUILayout.Label(e.Slot ?? "-", PanelTheme.Sub, GUILayout.Width(84f));

                GUI.contentColor = e.Alive ? PanelTheme.TxtMain : PanelTheme.TxtBad;
                GUILayout.Label(e.Name, PanelTheme.Sub, GUILayout.Width(150f));

                GUI.contentColor = PanelTheme.TxtWeak;
                GUILayout.Label($"{e.Hp}/{e.HpMax}", PanelTheme.Sub, GUILayout.Width(84f));

                GUI.contentColor = e.Abnormal ? PanelTheme.TxtWarn
                    : e.State == World.Devices.Device.DeviceState.immue ? PanelTheme.TxtOk
                    : PanelTheme.TxtWeak;
                GUILayout.Label(StateName(e.State), PanelTheme.Sub, GUILayout.Width(80f));
                GUI.contentColor = saved;

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            EndList();

            Hint("「状态」不是「正常 / 免疫中」就说明设备正处在异常中，会掉输出或持续掉血。");
            EndCard();
        }

        /// <summary>DeviceState 枚举 → 中文。名字对齐游戏内设备面板的说法。</summary>
        private static string StateName(World.Devices.Device.DeviceState state)
        {
            switch (state)
            {
                case World.Devices.Device.DeviceState.normal: return "正常";
                case World.Devices.Device.DeviceState.immue: return "免疫中";
                case World.Devices.Device.DeviceState.fired: return "燃烧";
                case World.Devices.Device.DeviceState.stupor: return "机械故障";
                case World.Devices.Device.DeviceState.acid: return "腐蚀";
                case World.Devices.Device.DeviceState.bind: return "缠绕";
                case World.Devices.Device.DeviceState.wet: return "潮湿";
                default: return state.ToString();
            }
        }
    }
}
