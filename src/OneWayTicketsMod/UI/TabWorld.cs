// =============================================================================
// TabWorld.cs — 「局内」页签：探索中的金币与车辆耐久
//
// 数据来源：World.BackPack.BackPackMgr.coin_count、World.WorldContext.caravan_hp/_max
// 这些值只在本局有效，回营地结算后才写进存档。
// =============================================================================

using OneWayTicketsMod.Game;
using UnityEngine;
using static OneWayTicketsMod.UI.PanelWidgets;

namespace OneWayTicketsMod.UI
{
    internal sealed partial class MainPanel
    {
        private void DrawWorldTab()
        {
            if (!WorldService.InWorld)
            {
                BeginCard("局内数据");
                Info("当前不在探索场景。");
                Hint("开始一局探索后再打开本页；局外（营地 / 菜单）时这些对象还不存在。");
                EndCard();
                return;
            }

            // ------------------------------------------------------------ 局内金币
            BeginCard("局内金币");
            bool hasCoin = WorldService.TryGetCoin(out var coin);
            KV("当前", hasCoin ? coin.ToString("N0") : "未就绪", 60f);

            GUILayout.BeginHorizontal();
            Caption("设为", 60f);
            string control = "world_coin";
            if (GUI.GetNameOfFocusedControl() != control) _missionCoinBuf = coin.ToString();
            GUI.SetNextControlName(control);
            _missionCoinBuf = GUILayout.TextField(_missionCoinBuf ?? string.Empty, PanelTheme.Field,
                GUILayout.Width(96f), GUILayout.Height(20f));
            if (Btn("应用", 42f, PanelTheme.Mini, 20f))
            {
                if (int.TryParse(_missionCoinBuf, out var value))
                {
                    WorldService.SetCoin(value);
                    Notify($"局内金币 → {value:N0}");
                }
                else
                {
                    Notify("请输入整数。");
                }
            }
            if (Btn("+1万", 46f, PanelTheme.Mini, 20f))
            {
                WorldService.SetCoin(coin + 10000);
                Notify("局内金币 +10,000");
            }
            if (Btn("拉满", 44f, PanelTheme.Mini, 20f))
            {
                WorldService.SetCoin(Plugin.MaxValue.Value);
                Notify("局内金币已拉满");
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Hint("金币是背包管理器上的一个整数，改完立刻生效（背包 UI 下一次刷新就会变）。");
            EndCard();

            // ------------------------------------------------------------ 车辆耐久
            BeginCard("车辆耐久");
            bool hasCar = WorldService.TryGetCaravan(out var hp, out var hpMax);
            KV("当前", hasCar ? $"{hp:N0} / {hpMax:N0}" : "未就绪", 60f);

            GUILayout.BeginHorizontal();
            Caption("耐久", 60f);
            string ctrlHp = "caravan_hp";
            if (GUI.GetNameOfFocusedControl() != ctrlHp) _hpBuf = hp.ToString();
            GUI.SetNextControlName(ctrlHp);
            _hpBuf = GUILayout.TextField(_hpBuf ?? string.Empty, PanelTheme.Field,
                GUILayout.Width(96f), GUILayout.Height(20f));
            if (Btn("应用", 42f, PanelTheme.Mini, 20f))
            {
                if (int.TryParse(_hpBuf, out var value))
                {
                    WorldService.SetCaravanHp(value);
                    Notify($"车辆耐久 → {value:N0}");
                }
                else
                {
                    Notify("请输入整数。");
                }
            }
            if (Btn("回满", 44f, PanelTheme.Mini, 20f))
            {
                WorldService.RepairFull();
                Notify("车辆已回满");
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            Caption("耐久上限", 60f);
            string ctrlMax = "caravan_hp_max";
            if (GUI.GetNameOfFocusedControl() != ctrlMax) _hpMaxBuf = hpMax.ToString();
            GUI.SetNextControlName(ctrlMax);
            _hpMaxBuf = GUILayout.TextField(_hpMaxBuf ?? string.Empty, PanelTheme.Field,
                GUILayout.Width(96f), GUILayout.Height(20f));
            if (Btn("应用", 42f, PanelTheme.Mini, 20f))
            {
                if (int.TryParse(_hpMaxBuf, out var value))
                {
                    WorldService.SetCaravanHpMax(value);
                    Notify($"耐久上限 → {value:N0}");
                }
                else
                {
                    Notify("请输入整数。");
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            Divider();

            bool infinite = ToggleChip(WorldService.InfiniteDurability, "无限耐久（每 0.2 秒把耐久补回上限）");
            if (infinite != WorldService.InfiniteDurability)
            {
                WorldService.InfiniteDurability = infinite;
                Notify(infinite ? "无限耐久已开启" : "无限耐久已关闭");
            }
            Hint("无限耐久在面板关闭后依然生效；退出本局探索时记得关掉，否则新一局也会继续生效。");
            Hint("耐久上限在换车 / 新开一局时会被车辆数据覆盖，属正常现象。");
            EndCard();
        }
    }
}
