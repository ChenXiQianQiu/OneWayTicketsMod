// =============================================================================
// CaravanSpeedPatch.cs — 锁定车速
//
// 反编译依据：
//   · Decompiled/World/World.Caravans/CaravanMgr.cs:77
//     每个物理帧 CaravanMgr.tick 调一次 CaravanMover.move()。
//   · Decompiled/World/World.Caravans/CaravanMover.cs:38-41
//     move() 的方法体只有一行：
//         m_assembly.GetType($"World.Caravans.Mover.CaravanMover_{m_move_type}")
//                   .GetMethod("do")?.Invoke(null, null);
//   · Decompiled/World/World.Caravans.Mover/CaravanMover_Run.cs:44-66
//     地面行驶分支：velocity += acc/120，然后把速度重设成沿坡面的向量，
//     最后 pos.x += velocity.x / 120。
//
// 为什么补丁点挂在 CaravanMover_Run.do 而不是 CaravanMover.move（v0.2.1 修正）：
//   v0.2.0 挂在 move() 上，实测锁定无效。move() 只有一行，Mono 的 JIT 会把它
//   内联进 CaravanMgr.tick，Harmony 改的是方法入口，内联后的调用点绕过了补丁。
//   而 move() 是用反射 GetMethod("do").Invoke 调 do 的，反射调用不会被内联，
//   所以补丁挂在 do 上一定能被执行到。
//
// 做法：后置补丁在 do() 算完并把车推进完之后，把速度按锁定值重建
//   （见 CaravanSpeedService.WriteVelocity，要按坡度反推模长，不能只写 x）。
//   跳跃 / 滞空 / 安全区分别走 CaravanMover_Jump / _Flying / _None，这里不干预。
// =============================================================================

using HarmonyLib;
using OneWayTicketsMod.Game;
using World.Caravans.Mover;

namespace OneWayTicketsMod.Patches
{
    // do 是 C# 关键字，nameof 用不了，只能写字符串（IL 里的方法名就是 "do"）
    [HarmonyPatch(typeof(CaravanMover_Run), "do")]
    internal static class CaravanSpeedPatch
    {
        private static bool _logged;

        [HarmonyPostfix]
        private static void Postfix()
        {
            // 本作实测这个补丁点不会被执行（同机制的其它补丁也一样），
            // 真正生效的是 CaravanSpeedService.Tick 里的每帧兜底。
            // 这里保留补丁 + 首帧日志：万一以后 Harmony 能用了，锁定会更精确，
            // 日志也能第一时间告诉我们补丁活过来了。
            if (!_logged)
            {
                _logged = true;
                Plugin.LogInfo("CaravanMover_Run.do 后置补丁已开始执行（Harmony 在本作可用）");
            }

            CaravanSpeedService.ApplyLock();
        }
    }
}
