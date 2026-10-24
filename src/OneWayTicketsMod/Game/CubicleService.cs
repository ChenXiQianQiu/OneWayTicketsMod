// =============================================================================
// CubicleService.cs — 工位分配（绕开游戏的点选流程）
//
// 反编译依据：
//   · Decompiled/World/World.Cubicles/CubiclesMgr.cs:15
//     public List<BasicCubicle> cubicles —— 本局所有工位。
//   · Decompiled/World/World.Cubicles/BasicCubicle.cs:9,26-33
//     public Character worker / public void set_worker(Character) —— 都是 public。
//   · Decompiled/World/World.Helpers/Cubicle_Helper.cs:59-88
//     character_cubicle 字典、EmptyCubicle、set_cubicle 的原始流程。
//   · Decompiled/World/World.Cubicles/DeviceCubicle.cs:9
//     device 字段，用来判断 CanWork。
//
// 为什么需要它：
//   v0.2.0 用 Harmony 改 Character_Helper.GetAllCharacters，无效；
//   v0.2.1 改成接管 Cubicle_Helper.SetCubicle，实测补丁也完全不执行
//   （同一套机制的 CaravanMover_Run.do 计数补丁跑了 0 次）；
//   v0.2.1 末期的「绕开判定直接 set_worker」在游戏里同样看不到效果
//   ——人还留在休息区，设备拿不到他的加成，底部 UI 也不会亮「在岗」。
//   所以 v0.2.2 起改成「先把人换到前排（操作中），再走游戏的落地流程」，
//   见 TeamService.SendToFront：前排有空位就直接换，满 6 人就把一个空闲的人换下去。
//   本类负责第二步：清工位 → 登记 character_cubicle → set_worker → SetCharacterWorking → EnterCubicle，
//   和游戏自己的 Cubicle_Helper.set_cubicle 一模一样。
// =============================================================================

using System.Collections.Generic;
using System.Text.RegularExpressions;
using Commons;
using World.Characters;
using World.Cubicles;
using World.Helpers;

namespace OneWayTicketsMod.Game
{
    internal static class CubicleService
    {
        private static readonly Regex TagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);

        public static CubiclesMgr Mgr => SingletonAccess.Mgr<CubiclesMgr>("CubiclesMgr");

        public static bool Ready => Mgr != null;

        public static List<BasicCubicle> All()
        {
            var mgr = Mgr;
            if (mgr?.cubicles == null) return new List<BasicCubicle>();
            return mgr.cubicles;
        }

        /// <summary>工位显示名：数据表里的 name 是本地化 key，取完要剥掉富文本标签。</summary>
        public static string NameOf(BasicCubicle bc)
        {
            if (bc?.desc == null) return "工位";
            try
            {
                var text = Localization_Utility.get_localization(bc.desc.name);
                if (!string.IsNullOrEmpty(text))
                {
                    text = TagRegex.Replace(text, string.Empty).Trim();
                    if (text.Length > 0) return text;
                }
            }
            catch
            {
                // 本地化表未就绪时退回 id
            }
            return $"工位 {bc.desc.id}";
        }

        /// <summary>
        /// 工位列表里的完整标签：带上设备名，方便和游戏里的设备对上号
        /// （游戏里同一台设备可能有 2~3 个工位，光看「工位 1」分不清是哪台）。
        /// </summary>
        public static string LabelOf(BasicCubicle bc)
        {
            string cub = NameOf(bc);
            string dev = DeviceNameOf(bc);
            return string.IsNullOrEmpty(dev) ? cub : $"{dev} · {cub}";
        }

        private static string DeviceNameOf(BasicCubicle bc)
        {
            try
            {
                if (!(bc is DeviceCubicle deviceCubicle)) return null;
                var desc = deviceCubicle.device?.desc;
                if (desc == null) return null;

                var text = Localization_Utility.get_localization(desc.name);
                if (!string.IsNullOrEmpty(text)) text = TagRegex.Replace(text, string.Empty).Trim();
                if (string.IsNullOrEmpty(text)) text = desc.name;
                return string.IsNullOrEmpty(text) ? null : text;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 把旅人安排到工位。休息区的人会先被换到前排（操作中），再上工位。
        /// </summary>
        public static bool Assign(Character c, BasicCubicle bc, out string message)
        {
            message = null;

            if (c == null) { message = "请先在旅人列表里选一个人。"; return false; }
            if (bc == null) { message = "工位不存在。"; return false; }
            if (c.state != null) { message = "这个旅人当前状态异常（昏迷 / 亢奋），不能上工位。"; return false; }
            if (!IsTeamMember(c)) { message = "这个旅人已经不在队伍里了。"; return false; }

            if (bc is DeviceCubicle deviceCubicle
                && deviceCubicle.device != null
                && !c.CanWork(deviceCubicle.device))
            {
                message = "这个旅人不能操作该设备（词缀 / 技能限制）。";
                return false;
            }

            var mgr = TeamService.Mgr;
            if (mgr == null) { message = "读不到旅人管理器。"; return false; }

            // 关键一步：先确保这个人在前排。
            // 游戏里「能不能上工位」只看 Cubicle_Helper.SetCubicle 那道判定（只认 mgr.characters），
            // 而设备真正吃到的加成、底部 UI 的「在岗」高亮、存档里的工位归属也都围绕前排旅人，
            // 所以绕开判定直接 set_worker 是没用的（v0.2.1 实测），必须真的把人换到前排。
            string frontNote = null;
            if (!TeamService.IsInFront(c))
            {
                // v0.2.4：解除操作中人数上限后，不再要求先打开「休息区旅人也能上工位」，
                // 因为「全员在操作中」本身就是那个开关想要的效果。
                if (!TeamService.RestToFrontEnabled && !TeamService.UnlockFrontLimit)
                {
                    message = "这个旅人在休息区。先打开「休息区旅人也能上工位」或「解除操作中人数上限」，或在列表里点「上阵」。";
                    return false;
                }

                if (!TeamService.SendToFront(c, out frontNote))
                {
                    message = frontNote ?? "换到操作中失败。";
                    return false;
                }
            }

            try
            {
                // 目标工位先清空（可能坐着别人）
                Cubicle_Helper.EmptyCubicle(bc);

                // 这个人原来占着的工位也要清掉，否则一个旅人会挂在两个工位上
                var map = Cubicle_Helper.character_cubicle;
                if (map.TryGetValue(c, out var old) && old != null && !ReferenceEquals(old, bc))
                    Cubicle_Helper.EmptyCubicle(old);

                map[c] = bc;

                // 照抄 Cubicle_Helper.set_cubicle 的落地流程
                bc.set_worker(c);
                mgr.SetCharacterWorking(c, true);
                c.EnterCubicle(bc);

                string target = NameOf(bc);
                message = string.IsNullOrEmpty(frontNote)
                    ? $"{TeamService.NameOf(c)} → {target}"
                    : $"{frontNote} → {target}";
                return true;
            }
            catch (System.Exception ex)
            {
                message = "安排失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>把工位上的人撤下来。</summary>
        public static bool Clear(BasicCubicle bc, out string message)
        {
            message = null;
            if (bc == null) { message = "工位不存在。"; return false; }
            if (bc.worker == null) { message = "这个工位本来就是空的。"; return false; }

            try
            {
                var name = TeamService.NameOf(bc.worker);
                Cubicle_Helper.EmptyCubicle(bc);
                message = $"{name} 已离开 {NameOf(bc)}";
                return true;
            }
            catch (System.Exception ex)
            {
                message = "撤下失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 一键上工：把所有空工位填满。
        /// 候选人是「操作中」里状态正常、没在岗、没占工位、并且 CanWork 这台设备的人；
        /// 每人最多安排一次，找不到合适的人就把这个工位空着。
        /// 用的是和 Assign 一样的落地流程（set_worker / SetCharacterWorking / EnterCubicle）。
        /// </summary>
        public static int AutoAssignAll(out string message)
        {
            message = null;

            var mgr = TeamService.Mgr;
            if (mgr?.characters == null) { message = "读不到旅人管理器（当前不在探索场景）。"; return 0; }

            var cubicles = All();
            if (cubicles.Count == 0) { message = "当前场景没有可分配的工位。"; return 0; }

            // 已经在岗 / 已经占着工位的人不再参与分配
            var used = new HashSet<Character>();
            foreach (var c in mgr.characters)
            {
                if (c == null) continue;
                if (c.is_working || Cubicle_Helper.GetCubicle(c) != null) used.Add(c);
            }

            int assigned = 0;
            foreach (var bc in cubicles)
            {
                if (bc == null || bc.worker != null) continue;

                var target = PickWorker(mgr, bc, used);
                if (target == null) continue;

                try
                {
                    Cubicle_Helper.EmptyCubicle(bc);
                    Cubicle_Helper.character_cubicle[target] = bc;
                    bc.set_worker(target);
                    mgr.SetCharacterWorking(target, true);
                    target.EnterCubicle(bc);
                    used.Add(target);
                    assigned++;
                }
                catch (System.Exception ex)
                {
                    Plugin.LogWarn($"自动安排 {LabelOf(bc)} 失败：{ex.Message}");
                }
            }

            message = assigned > 0
                ? $"已自动安排 {assigned} 个工位"
                : "没有可以安排的组合（操作中的人要么在岗、要么不能操作这些设备）。";
            return assigned;
        }

        /// <summary>挑一个能操作这个工位的人：状态正常、没被用过、CanWork 通过。</summary>
        private static Character PickWorker(CharacterMgr mgr, BasicCubicle bc, HashSet<Character> used)
        {
            var device = (bc as DeviceCubicle)?.device;

            foreach (var c in mgr.characters)
            {
                if (c == null || used.Contains(c)) continue;
                if (c.state != null) continue;
                if (device != null && !c.CanWork(device)) continue;
                return c;
            }
            return null;
        }

        private static bool IsTeamMember(Character c)
        {
            var mgr = TeamService.Mgr;
            if (mgr == null) return false;

            if (mgr.characters != null && mgr.characters.Contains(c)) return true;

            var backup = mgr.backup?.backup_characters;
            return backup != null && backup.Contains(c);
        }
    }
}
