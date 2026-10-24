// =============================================================================
// TabTeam.cs — 「旅人」页签：旅人不会晕车 + 解除操作中人数上限 + 旅人列表
//
// v0.2.5 起去掉了两块：
//   · 「休息区旅人也能上工位」开关 —— 用户实测完全没效果；
//   · 「工位分配」卡片（安排 / 撤下 / 一键上工）—— 会把旅人塞进「已经不在车上」的旧设备工位，
//     而且解除上限后全员都在「操作中」，游戏自己的点选流程就够了，不需要 Mod 再插一手。
// 现在本页只剩三件事：
//   1) 旅人不会晕车：给每个旅人补一份游戏自带的「油门焊死」词缀，
//      该词缀的 EnterTeam 会把 isImmuneToStun 置 true，冲击不再让人进入 Coma（昏迷）。
//      详见 Game/CharacterGuardService.cs。
//   2) 解除操作中人数上限：把 CharacterMgr 写死的 6 人上限绕开，整支队伍都待在操作中。
//      详见 Game/TeamService.cs。
//   3) 旅人列表：显示操作中 / 休息区、在岗状态、异常状态，可以手动「上阵 / 换下」。
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
            bool noComa = ToggleChip(Plugin.NoComa.Value, "给全体旅人补「油门焊死」词缀（免疫冲击昏迷）");
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

            KV("词缀", CharacterGuardService.Diagnose(), 60f);
            Hint("做法是给每位旅人补一份游戏自带的「油门焊死」词缀（效果就是免疫冲击昏迷，附带少量车轮牵引力）。");
            Hint("换人 / 读档后 0.5 秒内自动补齐；关掉开关只撤本 Mod 加的那份，旅人自带的词缀不动。");
            EndCard();

            BeginCard("解除操作中人数上限");
            bool unlock = ToggleChip(Plugin.UnlockFrontLimit.Value,
                $"操作中不再限制 {TeamService.FrontLimit} 人，休息区的人会自动换上来");
            if (unlock != Plugin.UnlockFrontLimit.Value)
            {
                Plugin.UnlockFrontLimit.Value = unlock;
                Notify(unlock
                    ? $"已开启：操作中最多可以站 {TeamService.RosterLimit} 人"
                    : "已关闭：操作中恢复 6 人上限", 3.5f);
            }

            GUILayout.BeginHorizontal();
            Caption("操作中", 60f);
            GUILayout.Label($"{TeamService.FrontCount} / {TeamService.EffectiveFrontLimit} 人",
                PanelTheme.Value, GUILayout.Width(90f));
            if (Btn("全部上阵", 76f, PanelTheme.Mini, 20f))
            {
                int n = TeamService.PromoteAll(out var msg);
                Notify(msg ?? (n > 0 ? $"已换 {n} 人" : "没有需要换的人。"), 4f);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            Hint("游戏把「操作中」写死成 6 人：CharacterMgr.AddCharacter / TakeCharacterToFront 都判 characters.Count < 6。");
            Hint("开启后整支队伍（最多 12 人）都待在操作中，于是游戏自己的「点人头像 → 点工位」对所有人都能生效，不依赖任何补丁。");
            Hint("读档 / 换局后会自动重新拉满；在列表里点「换下」的人会留在休息区，不会被反复拉上来。");
            EndCard();

            if (!TeamService.Ready)
            {
                BeginCard("旅人列表");
                Info("当前不在探索场景，读不到旅人数据。");
                EndCard();
                return;
            }

            BeginCard($"旅人列表（操作中 {TeamService.FrontCount} · 休息区 {TeamService.BackupCount}）");
            Hint("「上阵」= 换到操作中（能上工位）；「换下」= 回休息区。工位请用游戏自己的方式安排（点人头像 → 点工位）。");
            _teamScroll = BeginList(_teamScroll, 200f);
            foreach (var entry in TeamService.Collect())
            {
                GUILayout.BeginHorizontal(PanelTheme.Row, GUILayout.Height(21f));

                Color saved = GUI.contentColor;
                GUI.contentColor = entry.InFront ? PanelTheme.TxtOk : PanelTheme.TxtWeak;
                GUILayout.Label(entry.InFront ? "操作中" : "休息区", PanelTheme.Sub, GUILayout.Width(54f));
                GUI.contentColor = saved;

                GUILayout.Label(entry.Name, PanelTheme.Sub, GUILayout.Width(110f));

                GUI.contentColor = entry.Working ? PanelTheme.TxtAccent : PanelTheme.TxtWeak;
                GUILayout.Label(entry.Working ? "在岗" : "空闲", PanelTheme.Sub, GUILayout.Width(40f));
                GUI.contentColor = saved;

                GUI.contentColor = entry.StateName == "正常" ? PanelTheme.TxtWeak : PanelTheme.TxtWarn;
                GUILayout.Label(entry.StateName, PanelTheme.Sub, GUILayout.Width(70f));
                GUI.contentColor = saved;

                // 注意：这里不能用 FlexibleSpace 把按钮推到右边。
                // 页面外层是滚动区（ModPanel 的 BeginScrollView 用的是游戏自带的
                // verticalScrollbar 皮肤，比可见区域宽），行内容一旦按 FlexibleSpace
                // 撑满就会顶到可见区域外面，按钮点不到（v0.2.1 的「撤下」就是这样消失的）。
                // 所以按钮紧跟在状态后面左对齐。
                GUILayout.Space(8f);

                if (entry.Character != null)
                {
                    // 上阵 / 换下：直接挪列表，走的是游戏自己的 TakeCharacterToFront / ToBackup
                    if (!entry.InFront)
                    {
                        if (Btn("上阵", 40f, PanelTheme.Mini, 18f))
                        {
                            bool ok = TeamService.SendToFront(entry.Character, out var msg);
                            Notify(msg ?? (ok ? "已换到操作中" : "换人失败"), ok ? 3.5f : 4f);
                        }
                    }
                    else
                    {
                        if (Btn("换下", 40f, PanelTheme.Mini, 18f))
                        {
                            bool ok = TeamService.SendToBackup(entry.Character, out var msg);
                            Notify(msg ?? (ok ? "已换到休息区" : "换人失败"), ok ? 3.5f : 4f);
                        }
                    }
                }

                GUILayout.EndHorizontal();
            }
            EndList();
            Hint("「状态」不是「正常」说明该旅人正在昏迷 / 眩晕等，这种状态下游戏不允许他上工位。");
            EndCard();
        }
    }
}
