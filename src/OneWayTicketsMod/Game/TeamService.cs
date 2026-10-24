// =============================================================================
// TeamService.cs — 局内旅人（操作中 / 休息区）
//
// 数据来源：World.Characters.CharacterMgr（Mission 里挂名 "CharacterMgr"）
//   · characters                      —— 前排「操作中」旅人（上限 6）
//   · backup.backup_characters        —— 后备队「休息区」旅人（上限 6）
//
// 关于「休息区旅人也能上工位」（v0.2.2 换实现）：
//   工位分配入口 Cubicle_Helper.SetCubicle 会先用 Character_Helper.GetAllCharacters()
//   判断这个人是不是自己人，而 GetAllCharacters 只返回 characters（前排）。
//   所以后备队旅人点了工位也没反应；v0.2.1 那种「绕开判定直接 set_worker」的做法
//   实测在游戏里看不到效果（人不在前排，设备不认他、UI 也不更新）。
//   现在改成**真的把人换到前排**，用的是游戏自己的两个方法
//   （CharacterMgr.cs:249 TakeCharacterToBackup / :273 TakeCharacterToFront）：
//     · 前排有空位 → TakeCharacterToFront(c)；
//     · 前排已满 6 人 → 先把一个「没在岗、也没占工位」的前排旅人换下去，再把 c 换上来。
//   这样全程只有一份，不会像 CE 版那样同一个人写进两个列表、读档后变成两份。
// =============================================================================

using System;
using System.Collections.Generic;
using Commons;
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

        /// <summary>前排上限。游戏在 AddCharacter / TakeCharacterToFront 里写死 6。</summary>
        public const int FrontLimit = 6;

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
        /// 把休息区旅人换到前排（操作中）。前排满了会自动换下一个。
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

            try
            {
                if (mgr.characters.Count < FrontLimit)
                {
                    mgr.TakeCharacterToFront(c);
                    note = $"{NameOf(c)} 已换到操作中";
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
                note = $"{name} 已回到休息区";
                return true;
            }
            catch (Exception ex)
            {
                note = "换人失败：" + ex.Message;
                return false;
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
