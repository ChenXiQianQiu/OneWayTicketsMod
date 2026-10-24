// =============================================================================
// CubicleSetPatch.cs — 让「休息区（后备队）旅人」也能被分配到工位
//
// 反编译依据：
//   · Decompiled/World/World.Helpers/Cubicle_Helper.cs:65-79
//     SetCubicle 的前置判断是
//         Character_Helper.GetAllCharacters().Contains(selectCharacter) && ...
//   · Decompiled/World/World.Helpers/Character_Helper.cs:14-18
//     GetAllCharacters() 只返回 mgr.characters（前排「操作中」的 6 人），
//     所以后备队成员点工位永远进不去。
//
// 为什么 v0.2.1 改成直接接管 SetCubicle：
//   v0.2.0 给 GetAllCharacters 挂后置补丁返回合并列表，实测无效。
//   GetAllCharacters 只有三行，Mono 的 JIT 把它内联进了 SetCubicle，
//   补丁改的是方法入口，内联后的调用点看不到。
//   SetCubicle 是被 UI 事件委托调用的（点击工位），委托调用不会被内联，
//   而且方法体够大不会被内联进别处，所以补丁挂在这里一定执行。
//
// 做法：前置补丁在开关打开时完全接管：
//   把「是不是自己人」的判定从「只认前排」放宽成「前排 + 后备队」，
//   其余逻辑（清空旧工位、绑定 worker、置在岗、取消选中与高亮）照抄原实现。
//   旅人始终留在原来的列表里，不进前排，所以存档不会写两份。
// =============================================================================

using HarmonyLib;
using OneWayTicketsMod.Game;
using World.Characters;
using World.Cubicles;
using World.Helpers;

namespace OneWayTicketsMod.Patches
{
    [HarmonyPatch(typeof(Cubicle_Helper), nameof(Cubicle_Helper.SetCubicle))]
    internal static class CubicleSetPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(BasicCubicle bc)
        {
            // 开关关着就走游戏原逻辑，行为完全不变
            if (!TeamService.RestToFrontEnabled) return true;

            Assign(bc);
            return false;
        }

        /// <summary>等价于 Cubicle_Helper.SetCubicle 的原实现，只放宽了人员判定。</summary>
        private static void Assign(BasicCubicle bc)
        {
            var character = Cubicle_Helper.GetSelectCharacter();

            if (character == null || bc == null) return;
            if (character.state != null) return;
            if (!IsTeamMember(character)) return;
            // 原逻辑：设备工位还要过一遍 CanWork
            if (bc is DeviceCubicle deviceCubicle
                && deviceCubicle.device != null
                && !character.CanWork(deviceCubicle.device)) return;

            // 目标工位先清空
            Cubicle_Helper.EmptyCubicle(bc);

            // 这个人原来占着的工位也要清掉，否则一个旅人会挂在两个工位上
            var map = Cubicle_Helper.character_cubicle;
            if (map.TryGetValue(character, out var old) && old != null)
                Cubicle_Helper.EmptyCubicle(old);

            map[character] = bc;

            var mgr = TeamService.Mgr;
            if (mgr == null) return;

            // 以下照抄 Cubicle_Helper.set_cubicle 的实现
            bc.set_worker(character);
            mgr.SetCharacterWorking(character, true);
            mgr.CancelSelectCharacter();
            character.EnterCubicle(bc);

            // 原 SetCubicle 末尾的收尾：取消选中 + 取消工位高亮
            Cubicle_Helper.SetSelectCharacter(null);
        }

        /// <summary>前排或后备队都算自己人。</summary>
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
