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
//   · 锁定 = Harmony 在 CaravanMover_Run.do 之后把速度写回锁定值
//     （见 Patches/CaravanSpeedPatch.cs），只在「地面行驶（Run）」时生效。
//
// v0.2.1 修正：原来补丁挂在 CaravanMover.move 上，实测无效。
//   move() 方法体只有一行反射调用，Mono 的 JIT 会把它内联进 CaravanMgr.tick，
//   补丁改的是方法入口，内联后的调用点根本走不到。
//   而 move() 内部是用反射 GetMethod("do").Invoke 调 CaravanMover_Run.do 的，
//   反射调用无法被内联，所以把补丁点挪到 do 上就一定生效。
// =============================================================================

using UnityEngine;
using World;
using World.Caravans;
using World.Helpers;

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

        /// <summary>
        /// 把速度写进 WorldContext。
        /// CaravanMover_Run.do 末尾会把 velocity 重设成「沿坡面方向、模长不变」的向量
        /// （caravan_velocity = sign(vx) * magnitude * (cos, sin)），
        /// 所以这里不能只写 x，否则下个物理帧会被按 cos(坡度) 缩掉一截。
        /// 正确做法是反推模长 = 目标 x / cos(坡度)，再按坡面方向重建整个向量。
        /// </summary>
        public static void WriteVelocity(WorldContext ctx, float target)
        {
            if (ctx == null) return;

            float rad = 0f;
            try
            {
                Road_Info_Helper.try_get_leap_rad(ctx.caravan_pos.x, out rad);
            }
            catch
            {
                // 路况数据未就绪时按平地处理
                rad = 0f;
            }

            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            // 近乎垂直的坡面上 cos 趋近 0，限制一下避免把模长放大到离谱的值
            if (cos < 0.05f) cos = 0.05f;

            ctx.caravan_velocity = (target / cos) * new Vector2(cos, sin);
        }

        /// <summary>直接写车速（负数按 0 处理）。</summary>
        public static bool SetSpeed(float speed)
        {
            var ctx = WorldService.Ctx;
            if (ctx == null) return false;
            WriteVelocity(ctx, Mathf.Max(0f, speed));
            return true;
        }

        public static void ResetMax() => ObservedMax = 0f;

        /// <summary>每帧调用：记录最高车速，并在锁定时把速度钉回去。</summary>
        public static void Tick()
        {
            var ctx = WorldService.Ctx;
            if (ctx == null) return;

            float v = ctx.caravan_velocity.x;
            if (v > ObservedMax) ObservedMax = v;

            // 兜底：不依赖 Harmony 补丁，Update 里也钉一次。
            // 物理是 120Hz、Update 一般 60Hz，两次物理 tick 之间只写一次，
            // 中间那帧会按阻力掉一点点，所以面板上可能看到 23.4 / 23.3 这种抖动，
            // 属于正常现象（补丁生效时则完全不动）。
            if (LockEnabled && CaravanMover.move_type == EN_caravan_move_type.Run)
                WriteVelocity(ctx, LockTarget);
        }

        /// <summary>
        /// 由 CaravanMover_Run.do 的后置补丁调用：把车速钉回锁定值。
        /// 只在地面行驶时才有这个补丁点（跳跃走 CaravanMover_Jump，不挂补丁），
        /// 所以这里再判断一次 move_type 只是防御性的。
        /// </summary>
        public static void ApplyLock()
        {
            if (!LockEnabled) return;

            var ctx = WorldService.Ctx;
            if (ctx == null) return;
            if (CaravanMover.move_type != EN_caravan_move_type.Run) return;

            WriteVelocity(ctx, LockTarget);
        }
    }
}
