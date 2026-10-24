// =============================================================================
// TeamService.cs — 局内旅人（操作中 / 休息区）
//
// 数据来源：World.Characters.CharacterMgr（Mission 里挂名 "CharacterMgr"）
//   · characters                      —— 前排「操作中」旅人（上限 6）
//   · backup.backup_characters        —— 后备队「休息区」旅人（上限 6）
//
// 关于「休息区旅人也能上工位」：
//   工位分配入口 Cubicle_Helper.SetCubicle 会先用 Character_Helper.GetAllCharacters()
//   判断这个人是不是自己人，而 GetAllCharacters 只返回 characters（前排）。
//   所以后备队旅人点了工位也没反应。
//   本 Mod 用 Harmony 给 GetAllCharacters 挂后置补丁（见 Patches/CharacterHelperPatch），
//   在开关打开时把后备队成员追加到「副本」里返回——不动原列表，
//   因此不会像 CE 版那样把同一个人写进两个列表、存档再读档后变成两份。
// =============================================================================

using System.Collections.Generic;
using Commons;
using World.Characters;

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
        /// <summary>「休息区旅人可上工位」总开关，Harmony 补丁每帧读它。</summary>
        public static bool RestToFrontEnabled;

        public static CharacterMgr Mgr => SingletonAccess.Mgr<CharacterMgr>("CharacterMgr");

        public static bool Ready => Mgr != null;

        public static int FrontCount => Mgr?.characters?.Count ?? 0;

        public static int BackupCount => Mgr?.backup?.backup_characters?.Count ?? 0;

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

            string name = "旅人";
            try
            {
                if (c.desc != null)
                {
                    var localized = Localization_Utility.get_localization(c.desc.name);
                    if (!string.IsNullOrEmpty(localized)) name = localized;
                }
            }
            catch
            {
                // 本地化表未就绪时保留占位名
            }

            string state = "正常";
            try
            {
                if (c.state != null) state = c.state.GetType().Name;
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
    }
}
