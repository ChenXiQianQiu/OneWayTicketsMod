// =============================================================================
// TabTeam.cs — 「旅人」页签：休息区旅人上工位 + 旅人不会晕车
//
// 两件事：
//   1) 休息区旅人上工位：开关打开后，Cubicle_Helper.SetCubicle 的「是不是自己人」
//      判定被放宽成「前排 + 后备队」，玩家点选休息区旅人再点工位即可上工。
//      旅人始终留在后备队列表里，不进前排，所以存档不会写两份。
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

            BeginCard("休息区旅人也能上工位");
            bool on = ToggleChip(Plugin.RestCharacterEnabled.Value, "允许后备队（休息区）的旅人上工位");
            if (on != Plugin.RestCharacterEnabled.Value)
            {
                Plugin.RestCharacterEnabled.Value = on;
                Notify(on ? "已开启：休息区旅人现在可以安排工位了" : "已关闭", 3.5f);
            }

            Hint("用法：在下面的旅人列表里点「选」挑人，再到「工位分配」里点「安排」。");
            Hint("旅人始终留在原来的列表里，不进前排，所以读档后不会出现「同一个人两份」。");
            EndCard();

            if (!TeamService.Ready)
            {
                BeginCard("旅人列表");
                Info("当前不在探索场景，读不到旅人数据。");
                EndCard();
                return;
            }

            BeginCard($"旅人列表（操作中 {TeamService.FrontCount} · 休息区 {TeamService.BackupCount}）");
            Hint("点右边「选」按钮挑一个人，再到下面的「工位分配」里点「安排」即可上工位。");
            _teamScroll = BeginList(_teamScroll, 150f);
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

                bool picked = entry.Character != null && ReferenceEquals(_assignTarget, entry.Character);
                if (entry.Character != null)
                {
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
        /// 工位分配：绕开游戏的点选判定，直接把选中旅人绑到工位上。
        /// 游戏原流程（Cubicle_Helper.SetCubicle）只认前排旅人，所以休息区的
        /// 人在游戏里点工位是没反应的；这里走的是同一套落地逻辑（set_worker /
        /// SetCharacterWorking / EnterCubicle），只是不做那道前排判定。
        /// </summary>
        private void DrawCubicleAssignCard()
        {
            var cubicles = CubicleService.All();
            BeginCard($"工位分配（共 {cubicles.Count} 个）");

            GUILayout.BeginHorizontal();
            Caption("已选旅人", 70f);
            GUILayout.Label(_assignTarget != null ? TeamService.NameOf(_assignTarget) : "未选择（先到上面列表点「选」）",
                _assignTarget != null ? PanelTheme.Value : PanelTheme.Sub, GUILayout.Width(200f));
            if (_assignTarget != null && Btn("取消选择", 70f, PanelTheme.Mini, 18f))
                _assignTarget = null;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (cubicles.Count == 0)
            {
                Info("当前场景没有可分配的工位。");
                EndCard();
                return;
            }

            _cubicleScroll = BeginList(_cubicleScroll, 170f);
            foreach (var cubicle in cubicles)
            {
                if (cubicle == null) continue;

                GUILayout.BeginHorizontal(PanelTheme.Row, GUILayout.Height(21f));
                GUILayout.Label(CubicleService.NameOf(cubicle), PanelTheme.Sub, GUILayout.Width(150f));

                Color saved = GUI.contentColor;
                GUI.contentColor = cubicle.worker != null ? PanelTheme.TxtAccent : PanelTheme.TxtWeak;
                GUILayout.Label(cubicle.worker != null ? TeamService.NameOf(cubicle.worker) : "空闲",
                    PanelTheme.Sub, GUILayout.Width(100f));
                GUI.contentColor = saved;

                GUILayout.FlexibleSpace();

                if (Btn("安排", 46f, PanelTheme.Mini, 18f))
                {
                    bool ok = CubicleService.Assign(_assignTarget, cubicle, out var msg);
                    Notify(msg ?? (ok ? "已安排" : "安排失败"), ok ? 3f : 4f);
                }
                if (Btn("撤下", 46f, PanelTheme.Mini, 18f))
                {
                    bool ok = CubicleService.Clear(cubicle, out var msg);
                    Notify(msg ?? (ok ? "已撤下" : "撤下失败"), 3f);
                }

                GUILayout.EndHorizontal();
            }
            EndList();

            Hint("「安排」= 把上面选中的旅人放到这个工位；「撤下」= 让这个工位的人离开。");
            Hint("休息区旅人同样可以安排，且始终留在休息区列表里，不会写进存档的前排名单。");
            EndCard();
        }
    }
}
