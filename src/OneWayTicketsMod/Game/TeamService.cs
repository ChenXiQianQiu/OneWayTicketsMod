// =============================================================================
// TeamService.cs — 局内旅人（操作中 / 休息区）
//
// 数据来源：World.Characters.CharacterMgr（Mission 里挂名 "CharacterMgr"）
//   · characters                      —— 前排「操作中」旅人（游戏写死上限 6）
//   · backup.backup_characters        —— 后备队「休息区」旅人（游戏写死上限 6）
//
// 反编译依据：
//   · Decompiled/World/World.Characters/CharacterMgr.cs:139 / :180
//     AddCharacter / AddCharacterByLoad：characters.Count < 6 才进前排，否则进后备。
//   · CharacterMgr.cs:249 TakeCharacterToBackup：backup.Count >= 6 直接 return。
//   · CharacterMgr.cs:273 TakeCharacterToFront ：characters.Count >= 6 直接 return。
//   · CharacterMgr.cs:41 / :73
//     characters / backup 都是 public 字段，视图列表 views 来自
//     Foundations.MVVM.Model.views（public LinkedList<V>），
//     所以「绕过 6 人上限」不需要 Harmony，直接动列表 + 手动通知视图即可。
//   · CharacterMgrView.cs:83 notify_add_character：
//     找不到空槽就 Instantiate 一个新的 CharacterView，所以头像数量本身没有上限。
//
// 关于「休息区旅人也能上工位」（v0.2.2 换实现）：
//   工位分配入口 Cubicle_Helper.SetCubicle 会先用 Character_Helper.GetAllCharacters()
//   判断这个人是不是自己人，而 GetAllCharacters 只返回 characters（前排）。
//   所以后备队旅人点了工位也没反应；v0.2.1 那种「绕开判定直接 set_worker」的做法
//   实测在游戏里看不到效果（人不在前排，设备不认他、UI 也不更新）。
//   现在改成**真的把人换到前排**。
//
// v0.2.4「解除操作中人数上限」：
//   用户实测反馈「游戏里点人 → 点工位」对休息区旅人依旧没反应，
//   于是换个思路：干脆把 6 人上限拿掉，让整支队伍（最多 12 人）都待在「操作中」，
//   这样游戏自己的点选流程（CharacterView.OnPointerClick → SetCubicle）对所有人都成立，
//   不依赖任何补丁。见 PromoteToFront / PromoteAll / Tick。
// =============================================================================

using System;
using System.Collections.Generic;
using Commons;
using UnityEngine;
using World.Characters;
using World.Characters.CharacterStates;
using World.Helpers;

namespace OneWayTicketsMod.Game
{
    internal sealed class CharacterEntry
    {
        public Character Character;
        public string Name;
        public bool InFront;
        public bool Working;
        public string StateName;
    }

    internal static class TeamService
    {
        /// <summary>「休息区旅人可上工位」总开关。</summary>
        public static bool RestToFrontEnabled;

        /// <summary>「解除操作中人数上限」总开关（v0.2.4）。</summary>
        public static bool UnlockFrontLimit;

        /// <summary>游戏写死的前排上限（CharacterMgr 里 6 处都是这个数字）。</summary>
        public const int FrontLimit = 6;

        /// <summary>队伍总人数上限（前排 6 + 后备 6）。解除上限后最多也就是这么多人同时在操作中。</summary>
        public const int RosterLimit = 12;

        /// <summary>当前生效的前排上限（面板显示 / 换人判定都用它）。</summary>
        public static int EffectiveFrontLimit => UnlockFrontLimit ? RosterLimit : FrontLimit;

        /// <summary>玩家手动「换下」的人，自动上阵会跳过他们（换局时清空）。</summary>
        private static readonly HashSet<Character> ManualBackup = new HashSet<Character>();

        private static CharacterMgr _lastMgr;
        private static float _nextSync;

        public static CharacterMgr Mgr => SingletonAccess.Mgr<CharacterMgr>("CharacterMgr");

        public static bool Ready => Mgr != null;

        public static int FrontCount => Mgr?.characters?.Count ?? 0;

        public static int BackupCount => Mgr?.backup?.backup_characters?.Count ?? 0;

        // ---------------------------------------------------------------- 换人

        /// <summary>是否在前排（操作中）。</summary>
        public static bool IsInFront(Character c)
            => c != null && Mgr?.characters != null && Mgr.characters.Contains(c);

        /// <summary>是否在休息区（后备队）。</summary>
        public static bool IsInBackup(Character c)
            => c != null && Mgr?.backup?.backup_characters != null && Mgr.backup.backup_characters.Contains(c);

        /// <summary>
        /// 把休息区旅人换到前排（操作中）。
        /// 没解除上限时前排满 6 人会换下一个空闲的人；解除上限后直接加人，不挤任何人。
        /// 成功返回 true，note 里是给玩家看的说明。
        /// </summary>
        public static bool SendToFront(Character c, out string note)
        {
            note = null;

            var mgr = Mgr;
            if (mgr == null) { note = "读不到旅人管理器。"; return false; }
            if (c == null) { note = "没有选中旅人。"; return false; }
            if (IsInFront(c)) { note = $"{NameOf(c)} 已经在操作中了。"; return true; }

            var backup = mgr.backup?.backup_characters;
            if (backup == null || !backup.Contains(c)) { note = "这个旅人不在队伍里。"; return false; }

            ManualBackup.Remove(c);

            try
            {
                // 前排还有空位 → 走游戏自己的方法（内部会通知视图）
                if (mgr.characters.Count < FrontLimit)
                {
                    mgr.TakeCharacterToFront(c);
                    note = $"{NameOf(c)} 已换到操作中";
                    return true;
                }

                // 解除上限 → 直接加进前排，谁都不用下去
                if (UnlockFrontLimit)
                {
                    PromoteToFront(c);
                    note = $"{NameOf(c)} 已换到操作中（操作中 {FrontCount} 人，已解除 {FrontLimit} 人上限）";
                    return true;
                }

                var swapOut = PickSwapOut(c);
                if (swapOut == null) { note = $"操作中已经满 {FrontLimit} 人，也没有可以换下的人。"; return false; }

                // 游戏的这两个方法各带一道「对面没满」的判定：
                //   TakeCharacterToBackup：要求 backup.Count < 6
                //   TakeCharacterToFront ：要求 backup.Contains(c) && characters.Count < 6
                // 前后排都满（6 + 6）时两个都不满足，所以先把 c 从后备里摘出来，
                // 等换下 swapOut 之后再放回去，让 TakeCharacterToFront 的 Contains 判定能过。
                // 中间这段只在内存里改列表，不通知 UI；方法内部会各自通知，界面不会闪。
                bool backupFull = backup.Count >= FrontLimit;
                if (backupFull) backup.Remove(c);

                mgr.TakeCharacterToBackup(swapOut);

                if (backupFull) backup.Add(c);
                mgr.TakeCharacterToFront(c);

                note = $"{NameOf(c)} 已换到操作中，{NameOf(swapOut)} 去了休息区";
                return true;
            }
            catch (Exception ex)
            {
                note = "换人失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>把前排旅人换回休息区。</summary>
        public static bool SendToBackup(Character c, out string note)
        {
            note = null;

            var mgr = Mgr;
            if (mgr == null) { note = "读不到旅人管理器。"; return false; }
            if (c == null) { note = "没有选中旅人。"; return false; }
            if (IsInBackup(c)) { note = $"{NameOf(c)} 已经在休息区了。"; return true; }
            if (!IsInFront(c)) { note = "这个旅人不在队伍里。"; return false; }

            if (mgr.backup.backup_characters.Count >= FrontLimit)
            {
                note = $"休息区已经满 {FrontLimit} 人，先把休息区的人换到操作中再来。";
                return false;
            }

            try
            {
                var name = NameOf(c);
                // 游戏自己的方法：会先 SetCharacterWorking(false) + EmptyCubicle，再挪列表
                mgr.TakeCharacterToBackup(c);
                // 记一下：这是玩家主动换下去的，解除上限的自动上阵不要再把他拉上来
                ManualBackup.Add(c);
                note = $"{name} 已回到休息区";
                return true;
            }
            catch (Exception ex)
            {
                note = "换人失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 解除上限专用：直接把旅人挪进前排，不挤任何人下去。
        ///
        /// 游戏自带的 TakeCharacterToFront 第一行就是
        ///     if (!backup.backup_characters.Contains(c) || characters.Count >= 6) return;
        /// 所以超过 6 人只能自己动列表。步骤和它完全一致：
        ///   加到 characters → 通知视图加头像 → 从 backup 移除 → 通知视图刷新休息区。
        /// 视图通知必须手动补，否则数据变了但底部那排头像不变（v0.2.1 踩过这个坑）。
        /// </summary>
        private static bool PromoteToFront(Character c)
        {
            var mgr = Mgr;
            if (mgr == null || c == null || mgr.characters == null) return false;

            bool added = false;
            if (!mgr.characters.Contains(c))
            {
                mgr.characters.Add(c);
                added = true;
            }

            bool removed = false;
            var backup = mgr.backup?.backup_characters;
            if (backup != null) removed = backup.Remove(c);

            if (added) NotifyViews(mgr, v => v.notify_add_character(c));
            if (added || removed) NotifyViews(mgr, v => v.notify_backup_character());
            return true;
        }

        /// <summary>把休息区的人全部挪到操作中，返回人数。</summary>
        public static int PromoteAll(out string note)
        {
            var mgr = Mgr;
            if (mgr == null) { note = "读不到旅人管理器（当前不在探索场景）。"; return 0; }

            var backup = mgr.backup?.backup_characters;
            if (backup == null || backup.Count == 0) { note = "休息区现在没有人。"; return 0; }

            // 先拷一份再动列表，避免边遍历边改
            var pending = new List<Character>(backup);
            int moved = 0;
            foreach (var c in pending)
            {
                if (c == null) continue;
                ManualBackup.Remove(c);
                if (PromoteToFront(c)) moved++;
            }

            note = moved > 0
                ? $"已把 {moved} 人换到操作中（现在 {FrontCount} 人）"
                : "没有需要换到操作中的人。";
            return moved;
        }

        /// <summary>
        /// 每 0.5 秒一次的兜底：解除上限时把休息区的人自动换上来。
        /// 覆盖三种情况：读档（游戏只往 6 人里塞）、中途招募、以及上一局留下的后备队。
        /// 玩家手动点过「换下」的人会一直留在休息区，不会被反复拉上来。
        /// </summary>
        public static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextSync) return;
            _nextSync = now + 0.5f;

            var mgr = Mgr;
            if (!ReferenceEquals(mgr, _lastMgr))
            {
                // 换局 / 读档：CharacterMgr 是新建的，旧 Character 引用全部作废
                _lastMgr = mgr;
                ManualBackup.Clear();
            }

            if (mgr == null || !UnlockFrontLimit) return;

            var backup = mgr.backup?.backup_characters;
            if (backup == null || backup.Count == 0) return;

            List<Character> pending = null;
            foreach (var c in backup)
            {
                if (c == null || ManualBackup.Contains(c)) continue;
                if (pending == null) pending = new List<Character>();
                pending.Add(c);
            }
            if (pending == null) return;

            foreach (var c in pending) PromoteToFront(c);
        }

        /// <summary>逐个通知视图；某个视图抛异常不影响其它视图。</summary>
        private static void NotifyViews(CharacterMgr mgr, Action<ICharacterMgrView> action)
        {
            if (mgr?.views == null || mgr.views.Count == 0) return;

            // 拷一份再遍历：视图回调里有可能间接改动 views
            var list = new List<ICharacterMgrView>(mgr.views);
            foreach (var view in list)
            {
                if (view == null) continue;
                try
                {
                    action(view);
                }
                catch (Exception ex)
                {
                    Plugin.LogWarn("通知旅人视图失败：" + ex.Message);
                }
            }
        }

        /// <summary>前排满员时挑一个最合适换下去的人：优先「没在岗、也没占工位」的，其次最后一个。</summary>
        private static Character PickSwapOut(Character incoming)
        {
            var mgr = Mgr;
            if (mgr?.characters == null) return null;

            Character fallback = null;
            for (int i = mgr.characters.Count - 1; i >= 0; i--)
            {
                var c = mgr.characters[i];
                if (c == null || ReferenceEquals(c, incoming)) continue;

                if (fallback == null) fallback = c;

                bool idle = !c.is_working && Cubicle_Helper.GetCubicle(c) == null;
                if (idle) return c;
            }
            return fallback;
        }

        public static List<CharacterEntry> Collect()
        {
            var list = new List<CharacterEntry>();
            var mgr = Mgr;
            if (mgr == null) return list;

            if (mgr.characters != null)
                foreach (var c in mgr.characters)
                    list.Add(Make(c, true));

            if (mgr.backup?.backup_characters != null)
                foreach (var c in mgr.backup.backup_characters)
                    list.Add(Make(c, false));

            return list;
        }

        private static CharacterEntry Make(Character c, bool inFront)
        {
            if (c == null) return new CharacterEntry { Name = "(空)", InFront = inFront };

            string name = NameOf(c);

            string state = "正常";
            try
            {
                // state 为 null 就是正常；目前游戏里只有昏迷与亢奋两种状态类，
                // 其余（万一后续新增）就退回去显示类名，至少不会误报成「正常」。
                if (c.state is Coma) state = "昏迷";
                else if (c.state is Galvanized) state = "亢奋";
                else if (c.state != null) state = c.state.GetType().Name;
            }
            catch
            {
                // 忽略
            }

            return new CharacterEntry
            {
                Character = c,
                Name = name,
                InFront = inFront,
                Working = c.is_working,
                StateName = state,
            };
        }

        /// <summary>旅人显示名（本地化名，取不到就用占位名）。</summary>
        public static string NameOf(Character c)
        {
            if (c == null) return "(空)";

            try
            {
                if (c.desc != null)
                {
                    var localized = Localization_Utility.get_localization(c.desc.name);
                    if (!string.IsNullOrEmpty(localized)) return localized;
                }
            }
            catch
            {
                // 本地化表未就绪时保留占位名
            }

            return "旅人";
        }
    }
}
