// =============================================================================
// TabTeam.cs — 「旅人」页签：旅人不会晕车 + 解除操作中人数上限 + 工位 + 旅人列表
//
// v0.2.5 起去掉了两块：
//   · 「休息区旅人也能上工位」开关 —— 用户实测完全没效果；
//   · 「工位分配」卡片（安排 / 撤下 / 一键上工）—— 会把旅人塞进「已经不在车上」的旧设备工位，
//     而且解除上限后全员都在「操作中」，游戏自己的点选流程就够了，不需要 Mod 再插一手。
// 现在本页只剩五件事：
//   1) 旅人不会晕车：给每个旅人补一份游戏自带的「油门焊死」词缀，
//      该词缀的 EnterTeam 会把 isImmuneToStun 置 true，冲击不再让人进入 Coma（昏迷）。
//      详见 Game/CharacterGuardService.cs。
//   2) 解除操作中人数上限：把 CharacterMgr 写死的 6 人上限绕开，整支队伍都待在操作中。
//      详见 Game/TeamService.cs。
//   3) 旅人列表：显示操作中 / 休息区、在岗状态、异常状态，可以手动「上阵 / 换下」。
//   4) 工位卡片（v0.3.0）：把「一键上工位」从「设置」页搬回来 —— 它安排的是旅人，
//      放在旅人页才找得到；同时新增「自动补满空工位」开关（Game/CubicleViewService.cs）。
//   5) 伙伴休息所（v0.4.0）：把「没见过」的特殊伙伴标记为已见 / 直接收为伙伴，
//      解开休息所里那些点不开的黑格子。详见 Game/PartnerService.cs。
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
            Hint("给每位旅人补一份「油门焊死」词缀，免疫冲击昏迷；关掉只撤本 Mod 加的那份。");
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

            Hint("游戏把「操作中」写死成 6 人，开启后整队都能站在操作中，工位点选对所有人都生效。");
            EndCard();

            BeginCard("工位（一键上工位 / 自动补位）");
            GUILayout.BeginHorizontal();
            if (Btn("一键上工位", 96f, PanelTheme.ButtonPrimary, 22f))
                Notify(CubicleViewService.FillAll(), 5f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Hint("把操作中里空闲的旅人铺到车上的空工位；自动补位每 2 秒检查一次，不会抢你正在选的人。");

            bool autoFill = ToggleChip(Plugin.AutoFillCubicle.Value, "自动补满空工位（每 2 秒检查一次）");
            if (autoFill != Plugin.AutoFillCubicle.Value)
            {
                Plugin.AutoFillCubicle.Value = autoFill;
                Notify(autoFill ? "已开启：空工位会自己补人" : "已关闭：不再自动安排旅人", 3.5f);
            }
            EndCard();

            DrawPartnerCard();

            if (!TeamService.Ready)
            {
                BeginCard("旅人列表");
                Info("当前不在探索场景，读不到旅人数据。");
                EndCard();
                return;
            }

            BeginCard($"旅人列表（操作中 {TeamService.FrontCount} · 休息区 {TeamService.BackupCount}）");
            Hint("「上阵」= 换到操作中（能上工位），「换下」= 回休息区。");
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
            EndCard();
        }

        /// <summary>
        /// 伙伴休息所：把「没见过」的特殊伙伴变成可点。
        /// 详见 Game/PartnerService.cs 与 docs/模块说明-成就系统.md。
        /// </summary>
        private void DrawPartnerCard()
        {
            BeginCard("伙伴休息所（解锁没见过的伙伴）");

            if (!PartnerService.Ready)
            {
                Info("还没读到存档（CommonContext 未就绪），先进入营地再来。");
                EndCard();
                return;
            }

            var partners = PartnerService.Collect();
            if (partners.Count == 0)
            {
                Info("读不到旅人表，或当前版本没有特殊伙伴数据。");
                EndCard();
                return;
            }

            int unknown = 0, seenOnly = 0, friends = 0, specials = 0;
            foreach (var p in partners)
            {
                if (p.Special) specials++;
                if (p.Friend) friends++;
                else if (p.Seen) seenOnly++;
                else unknown++;
            }

            KV("角色", $"共 {partners.Count} 名（特殊伙伴 {specials} 名）", 60f);
            KV("状态", $"未见过 {unknown} · 已见未收 {seenOnly} · 已是伙伴 {friends}", 60f);

            GUILayout.BeginHorizontal();
            if (Btn("全部标记为已见", 112f, PanelTheme.ButtonPrimary, 22f))
            {
                int n = PartnerService.MarkAllSeen();
                Notify(n > 0
                    ? $"已把 {n} 名没见过的角色标记为已见，回营地打开伙伴休息所即可解锁"
                    : "没有需要标记的角色（都已经见过了）。", 5f);
            }
            if (Btn("特殊伙伴全收下", 112f, PanelTheme.Button, 22f))
            {
                int n = PartnerService.RecruitAll();
                Notify(n > 0
                    ? $"已直接收下 {n} 名特殊伙伴（跳过资源消耗，等于在休息所点了「解锁」）"
                    : "没有需要收的特殊伙伴（都已经在名单里了）。", 5f);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            Hint("黑色剪影 = 没见过的角色，游戏要求先在局内让他入队一次才点得开。");
            Hint("「标记为已见」只解锁可点状态，之后仍要在休息所花资源收人；「收为伙伴」跳过资源消耗。");

            _partnerScroll = BeginList(_partnerScroll, 150f);
            foreach (var p in partners)
            {
                GUILayout.BeginHorizontal(PanelTheme.Row, GUILayout.Height(21f));

                Color saved = GUI.contentColor;
                GUI.contentColor = p.Friend ? PanelTheme.TxtOk : p.Seen ? PanelTheme.TxtAccent : PanelTheme.TxtWarn;
                GUILayout.Label(p.Friend ? "已是伙伴" : p.Seen ? "已见未收" : "未见过", PanelTheme.Sub, GUILayout.Width(58f));
                GUI.contentColor = saved;

                GUILayout.Label(p.Name, PanelTheme.Sub, GUILayout.Width(96f));
                GUILayout.Label(p.Id.ToString(), PanelTheme.Hint, GUILayout.Width(44f));

                GUI.contentColor = p.Special ? PanelTheme.TxtAccent : PanelTheme.TxtWeak;
                GUILayout.Label(p.Special ? "特殊" : "普通", PanelTheme.Sub, GUILayout.Width(34f));
                GUI.contentColor = saved;

                // 按钮紧跟在文字后面左对齐：列表外层是滚动区，用 FlexibleSpace 推到右边会跑出可见区域
                GUILayout.Space(8f);

                if (!p.Seen)
                {
                    if (Btn("标记已见", 62f, PanelTheme.Mini, 18f))
                    {
                        bool ok = PartnerService.MarkSeen(p.Id);
                        Notify(ok ? $"{p.Name} 已标记为已见，回营地就能解锁" : "标记失败：存档对象未就绪", 4f);
                    }
                }
                else if (p.Special && !p.Friend)
                {
                    if (Btn("收为伙伴", 62f, PanelTheme.Mini, 18f))
                    {
                        bool ok = PartnerService.Recruit(p.Id);
                        Notify(ok ? $"{p.Name} 已成为伙伴" : "收录失败：存档对象未就绪", 4f);
                    }
                }

                GUILayout.EndHorizontal();
            }
            EndList();
            EndCard();
        }
    }
}
