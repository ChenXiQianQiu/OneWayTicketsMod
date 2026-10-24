// =============================================================================
// CharacterHelperPatch.cs — 让「休息区（后备队）旅人」也能被分配到工位
//
// 反编译依据（Decompiled/World/World.Helpers/Cubicle_Helper.cs:68）：
//   SetCubicle 的前置判断是
//       Character_Helper.GetAllCharacters().Contains(selectCharacter)
//   而 Character_Helper.GetAllCharacters()（同目录 Character_Helper.cs:14）
//   只返回 CharacterMgr.characters（前排「操作中」的 6 人），
//   所以后备队成员点工位永远进不去。
//
// 做法：后置补丁把后备队成员追加到一个「新列表」里返回。
//   注意 __result 就是 mgr.characters 本体，就地 Add 会真的把人塞进前排，
//   那样存档会写两份（CE 版踩过这个坑），所以这里一定先复制。
// =============================================================================

using System.Collections.Generic;
using HarmonyLib;
using OneWayTicketsMod.Game;
using World.Characters;
using World.Helpers;

namespace OneWayTicketsMod.Patches
{
    [HarmonyPatch(typeof(Character_Helper), nameof(Character_Helper.GetAllCharacters))]
    internal static class CharacterHelperPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref List<Character> __result)
        {
            if (!TeamService.RestToFrontEnabled) return;
            if (__result == null) return;

            var mgr = TeamService.Mgr;
            var backup = mgr?.backup?.backup_characters;
            if (backup == null || backup.Count == 0) return;

            List<Character> merged = null;
            foreach (var character in backup)
            {
                if (character == null || __result.Contains(character)) continue;

                if (merged == null)
                {
                    // 只有真要加人时才复制，平时零分配
                    merged = new List<Character>(__result);
                    // 复制后后续判断要基于副本，避免重复追加
                    __result = merged;
                }

                merged.Add(character);
            }
        }
    }
}
