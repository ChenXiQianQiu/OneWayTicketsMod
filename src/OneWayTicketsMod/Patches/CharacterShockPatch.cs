// =============================================================================
// CharacterShockPatch.cs — 旅人不会晕车（免疫冲击昏迷）
//
// 反编译依据（Decompiled/World/World.Characters/Character.cs:221）：
//   ApplyShockForce(float shockForce) 是全游戏唯一会 new Coma 的地方，
//   调用者是 CharacterMgr.tick（同目录 CharacterMgr.cs:66，每物理帧按车辆纵向加速度算冲击力）。
//
// 做法：前置补丁直接返回 false 跳过原方法。不改车辆物理、不改角色数据，
//   关掉开关立即恢复原样。
// =============================================================================

using HarmonyLib;
using OneWayTicketsMod.Game;
using World.Characters;

namespace OneWayTicketsMod.Patches
{
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyShockForce))]
    internal static class CharacterShockPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !CharacterGuardService.NoComaEnabled;
    }
}
