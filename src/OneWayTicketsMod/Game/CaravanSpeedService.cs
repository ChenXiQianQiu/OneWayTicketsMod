// =============================================================================
// CaravanSpeedService.cs — 车辆速度：读 / 直接设置 / 锁定
//
// 反编译依据：
//   · Decompiled/World/World/WorldContext.cs:25
//     车速就是 WorldContext.caravan_velocity（Vector2，x 为前进方向）。
//   · Decompiled/World/World.Caravans/CaravanMgr.cs:77
//     每个物理帧调一次 CaravanMover.move()，由它按牵引力 / 阻力 / 坡度
//     重新算出 caravan_velocity，再按 velocity.x 推进 caravan_pos。
//   · Decompiled/World/World.Caravans.Mover/CaravanMover_Run.cs:44-66
//     地面行驶分支：velocity += acc/120，然后 pos.x += velocity.x/120。
//   · Decompiled/World/World/WorldContext.cs:198-202
//     游戏自带的「最低车速」只在 ctx_tick_start 里抬下限（Config.is_load_caravan_speed_low_limit），
//     没有上限概念；所以这里用「锁定」把速度钉死，比改下限更直接。
//
// 实现：
//   · 直接设置 = 往 caravan_velocity.x 写一个值，下一帧物理就会按它推进；
//   · 锁定 = Harmony 在 CaravanMover.move 之后把 x 写回锁定值（见 Patches/CaravanSpeedPatch.cs），
//     只在「地面行驶（Run）」时生效，跳跃 / 滞空 / 安全区不干预，避免影响落地判定。
// =============================================================================

using UnityEngine;
using World.Caravans;

namespace OneWayTicketsMod.Game
{
    internal static class CaravanSpeedService
    {
        /// <summary>锁定车速开关（只在本局有效，重开一局自动复位）。</summary>
        public static bool LockEnabled;

        /// <summary>锁定的目标速度。</summary>
        public static float LockTarget;

        /// <summary>本次探索观察到的最高车速，供「取最高」按钮使用。</summary>
        public static float ObservedMax;

        public static bool TryGetSpeed(out float speed)
        {
            speed = 0f;
            var ctx = WorldService.Ctx;
            if (ctx == null) return false;
            speed = ctx.caravan_velocity.x;
            return true;
        }

        /// <summary>直接写车速（负数按 0 处理）。</summary>
        public static bool SetSpeed(float speed)
        {
            var ctx = WorldService.Ctx;
            if (ctx == null) return false;
            ctx.caravan_velocity.x = Mathf.Max(0f, speed);
            return true;
        }

        public static void ResetMax() => ObservedMax = 0f;

        /// <summary>每帧调用：记录本次探索的最高车速。</summary>
        public static void Tick()
        {
            var ctx = WorldService.Ctx;
            if (ctx == null) return;

            float v = ctx.caravan_velocity.x;
            if (v > ObservedMax) ObservedMax = v;
        }

        /// <summary>
        /// 由 CaravanMover.move 的后置补丁调用：把车速钉回锁定值。
        /// 只在 Run 状态下生效——跳跃途中改水平速度会打乱落地判定。
        /// </summary>
        public static void ApplyLock()
        {
            if (!LockEnabled) return;

            var ctx = WorldService.Ctx;
            if (ctx == null) return;
            if (CaravanMover.move_type != EN_caravan_move_type.Run) return;

            ctx.caravan_velocity.x = LockTarget;
        }
    }
}
