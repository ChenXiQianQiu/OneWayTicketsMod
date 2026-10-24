// =============================================================================
// TabTeam.cs — 「旅人」页签：休息区旅人上工位 + 旅人不会晕车
//
// 两件事：
//   1) 休息区旅人上工位：开关打开后，后备队（休息区）的旅人会被
//      Character_Helper.GetAllCharacters() 一并返回，于是 Cubicle_Helper.SetCubicle
//      的前置判断能通过，玩家点选休息区旅人再点工位即可上工。
//      旅人始终留在后备队列表里，不进前排，所以存档不会写两份。
//   2) 旅人不会晕车：Harmony 拦掉 Character.ApplyShockForce，
//      车辆颠簸 / 设备受击的冲击不再让旅人进入 Coma（昏迷）。
//      详见 Game/CharacterGuardService.cs 与 Patches/CharacterShockPatch.cs。
// =============================================================================

using OneWayTicketsMod.Game;
using UnityEngine;
using static OneWayTicketsMod.UI.PanelWidgets;

namespace OneWayTicketsMod.UI
{
    internal sealed partial class MainPanel
    {
        private void DrawTeamTab()
        {
            BeginCard("旅人不会晕车");
            bool noComa = ToggleChip(Plugin.NoComa.Value, "免疫冲击昏迷（车再颠也不会晕倒）");
            if (noComa != Plugin.NoComa.Value)
            {
                Plugin.NoComa.Value = noComa;
                Notify(noComa ? "已开启：旅人不会晕车了" : "已关闭：恢复游戏原本的颠簸昏迷", 3.5f);
            }

            int coma = CharacterGuardService.ComaCount();
            GUILayout.BeginHorizontal();
            Caption("当前昏迷", 60f);
            GUILayout.Label(coma > 0 ? $"{coma} 人" : "0 人",
                coma > 0 ? PanelTheme.Value : PanelTheme.Sub, GUILayout.Width(60f));
            if (Btn("全部唤醒", 76f, PanelTheme.Mini, 20f))
            {
                int n = CharacterGuardService.WakeAll();
                Notify(n > 0 ? $"已唤醒 {n} 名旅人" : "当前没有昏迷的旅人。");
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            Hint("游戏里「状态」显示 Coma 就是昏迷，来源只有一处：车辆纵向加速度超过旅人抗性后按概率触发。");
            Hint("这个开关会把这个入口整个跳过，所以颠簸、设备受击震动都不会再让人晕倒；关闭后立刻恢复原样。");
            Hint("「全部唤醒」只清掉当前的昏迷状态，不改旅人属性，也不影响其它状态（如亢奋）。");
            EndCard();

            BeginCard("休息区旅人也能上工位");
            bool on = ToggleChip(Plugin.RestCharacterEnabled.Value, "允许后备队（休息区）的旅人上工位");
            if (on != Plugin.RestCharacterEnabled.Value)
            {
                Plugin.RestCharacterEnabled.Value = on;
                Notify(on ? "已开启：休息区旅人现在可以点工位了" : "已关闭", 3.5f);
            }

            Hint("开启后：先点一下休息区里的旅人（会被高亮选中），再点工位即可上工。");
            Hint("和 CE 版不同，这里不把人塞进前排列表，因此读档后不会出现「同一个人两份」的问题。");
            Hint("副作用：需要「全队」判定的逻辑（唤醒 AED、部分增益）也会把休息区旅人算进来，通常更合理。");
            EndCard();

            if (!TeamService.Ready)
            {
                BeginCard("旅人列表");
                Info("当前不在探索场景，读不到旅人数据。");
                EndCard();
                return;
            }

            BeginCard($"旅人列表（操作中 {TeamService.FrontCount} · 休息区 {TeamService.BackupCount}）");
            _teamScroll = BeginList(_teamScroll, 240f);
            foreach (var entry in TeamService.Collect())
            {
                GUILayout.BeginHorizontal(PanelTheme.Row, GUILayout.Height(21f));

                Color saved = GUI.contentColor;
                GUI.contentColor = entry.InFront ? PanelTheme.TxtOk : PanelTheme.TxtWeak;
                GUILayout.Label(entry.InFront ? "操作中" : "休息区", PanelTheme.Sub, GUILayout.Width(54f));
                GUI.contentColor = saved;

                GUILayout.Label(entry.Name, PanelTheme.Sub, GUILayout.Width(120f));

                GUI.contentColor = entry.Working ? PanelTheme.TxtAccent : PanelTheme.TxtWeak;
                GUILayout.Label(entry.Working ? "在岗" : "空闲", PanelTheme.Sub, GUILayout.Width(40f));
                GUI.contentColor = saved;

                GUI.contentColor = entry.StateName == "正常" ? PanelTheme.TxtWeak : PanelTheme.TxtWarn;
                GUILayout.Label(entry.StateName, PanelTheme.Sub, GUILayout.Width(80f));
                GUI.contentColor = saved;

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            EndList();
            Hint("「状态」不是「正常」说明该旅人正在昏迷 / 眩晕等，这种状态下点了工位也不会响应。");
            EndCard();
        }
    }
}
