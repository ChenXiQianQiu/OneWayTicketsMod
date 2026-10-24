// =============================================================================
// TabTeam.cs — 「旅人」页签：休息区旅人上工位 + 旅人不会晕车
//
// 两件事：
//   1) 休息区旅人上工位（v0.2.2 换实现）：开关打开后，点「安排」时会先把这个人
//      **真的换到前排（操作中）**——前排满 6 人就自动把一个空闲的人换下去——
//      再走游戏自己的工位落地流程。列表里也可以直接点「上阵 / 换下」手动换人。
//   2) 旅人不会晕车：给每个旅人补一份游戏自带的「油门焊死」词缀，
//      该词缀的 EnterTeam 会把 isImmuneToStun 置 true，冲击不再让人进入 Coma（昏迷）。
//      详见 Game/CharacterGuardService.cs。
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

            BeginCard("休息区旅人也能上工位");
            bool on = ToggleChip(Plugin.RestCharacterEnabled.Value, "休息区的旅人会先换到「操作中」，再上工位");
            if (on != Plugin.RestCharacterEnabled.Value)
            {
                Plugin.RestCharacterEnabled.Value = on;
                Notify(on ? "已开启：休息区旅人现在可以安排工位了" : "已关闭", 3.5f);
            }

            Hint("游戏只认前排（操作中）旅人，所以本功能会真的把人换到前排，不再只是「挂个名」。");
            Hint($"用法一：列表里点「选」挑人 → 到「工位分配」点「安排」，会自动换到前排再上工位。");
            Hint(TeamService.UnlockFrontLimit
                ? "用法二：列表里点「上阵」直接换到操作中；已解除人数上限，不会再把人换下去。"
                : $"用法二：列表里直接点「上阵」把人换到操作中；操作中满 {TeamService.FrontLimit} 人时，会自动把一个空闲的人换到休息区。");
            EndCard();

            if (!TeamService.Ready)
            {
                BeginCard("旅人列表");
                Info("当前不在探索场景，读不到旅人数据。");
                EndCard();
                return;
            }

            BeginCard($"旅人列表（操作中 {TeamService.FrontCount} · 休息区 {TeamService.BackupCount}）");
            Hint("「上阵」= 换到操作中（能上工位）；「换下」= 回休息区；「选」= 挑他去做下面的工位分配。");
            _teamScroll = BeginList(_teamScroll, 170f);
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

                    bool picked = ReferenceEquals(_assignTarget, entry.Character);
                    if (Btn(picked ? "已选" : "选", 40f, PanelTheme.Mini, 18f))
                        _assignTarget = picked ? null : entry.Character;
                }

                GUILayout.EndHorizontal();
            }
            EndList();
            Hint("「状态」不是「正常」说明该旅人正在昏迷 / 眩晕等，这种状态下安排工位会被拒绝。");
            EndCard();

            DrawCubicleAssignCard();
        }

        /// <summary>
        /// 工位分配：把选中旅人绑到工位上。
        /// 休息区的人会先被换到前排（操作中），再走游戏的 set_worker /
        /// SetCharacterWorking / EnterCubicle 落地流程。
        /// </summary>
        private void DrawCubicleAssignCard()
        {
            var cubicles = CubicleService.All();
            int empty = 0;
            foreach (var bc in cubicles) if (bc != null && bc.worker == null) empty++;

            BeginCard($"工位分配（共 {cubicles.Count} 个 · 空 {empty} 个）");

            GUILayout.BeginHorizontal();
            Caption("已选旅人", 70f);
            GUILayout.Label(_assignTarget != null ? TeamService.NameOf(_assignTarget) : "未选择（先到上面列表点「选」）",
                _assignTarget != null ? PanelTheme.Value : PanelTheme.Sub, GUILayout.Width(190f));
            if (_assignTarget != null && Btn("取消选择", 70f, PanelTheme.Mini, 18f))
                _assignTarget = null;

            // 一键上工：把所有空工位用「操作中」里空闲的人填满
            if (Btn("一键上工", 70f, PanelTheme.Mini, 18f))
            {
                int n = CubicleService.AutoAssignAll(out var msg);
                Notify(msg ?? (n > 0 ? $"已安排 {n} 个工位" : "没有可以安排的组合。"), 4f);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (cubicles.Count == 0)
            {
                Info("当前场景没有可分配的工位。");
                EndCard();
                return;
            }

            _cubicleScroll = BeginList(_cubicleScroll, 150f);
            foreach (var cubicle in cubicles)
            {
                if (cubicle == null) continue;

                GUILayout.BeginHorizontal(PanelTheme.Row, GUILayout.Height(21f));
                GUILayout.Label(CubicleService.LabelOf(cubicle), PanelTheme.Sub, GUILayout.Width(170f));

                Color saved = GUI.contentColor;
                GUI.contentColor = cubicle.worker != null ? PanelTheme.TxtAccent : PanelTheme.TxtWeak;
                GUILayout.Label(cubicle.worker != null ? TeamService.NameOf(cubicle.worker) : "空闲",
                    PanelTheme.Sub, GUILayout.Width(90f));
                GUI.contentColor = saved;

                // 同上：不用 FlexibleSpace，按钮紧跟内容，避免被滚动区裁到看不见
                GUILayout.Space(8f);

                if (Btn("安排", 46f, PanelTheme.Mini, 18f))
                {
                    bool ok = CubicleService.Assign(_assignTarget, cubicle, out var msg);
                    Notify(msg ?? (ok ? "已安排" : "安排失败"), ok ? 3.5f : 4f);
                }
                if (Btn("撤下", 46f, PanelTheme.Mini, 18f))
                {
                    bool ok = CubicleService.Clear(cubicle, out var msg);
                    Notify(msg ?? (ok ? "已撤下" : "撤下失败"), 3f);
                }

                GUILayout.EndHorizontal();
            }
            EndList();

            Hint("「安排」= 把上面选中的旅人放到这个工位（休息区的人会先自动换到操作中）；「撤下」= 让这个工位的人离开。");
            Hint("「一键上工」= 把每个空工位都塞上一个人：优先挑操作中里状态正常、没在岗、并且能操作这台设备的人。");
            EndCard();
        }
    }
}
