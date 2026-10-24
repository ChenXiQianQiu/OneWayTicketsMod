// =============================================================================
// CaravanSpeedPatch.cs — 锁定车速
//
// 反编译依据（Decompiled/World/World.Caravans/CaravanMgr.cs:77）：
//   每个物理帧 CaravanMgr.tick 调一次 CaravanMover.move()，
//   由它算完 velocity 并按 velocity.x 推进 pos（CaravanMover_Run.cs:44-66）。
//
// 做法：后置补丁在 move() 算完之后把 velocity.x 写回锁定值。
//   · 放后置是因为前置写的值会被 move() 内部的加速度重新算掉；
//   · 只在 Run 状态生效（见 CaravanSpeedService.ApplyLock），跳跃途中不干预。
// =============================================================================

using HarmonyLib;
using OneWayTicketsMod.Game;
using World.Caravans;

namespace OneWayTicketsMod.Patches
{
    [HarmonyPatch(typeof(CaravanMover), nameof(CaravanMover.move))]
    internal static class CaravanSpeedPatch
    {
        [HarmonyPostfix]
        private static void Postfix() => CaravanSpeedService.ApplyLock();
    }
}
