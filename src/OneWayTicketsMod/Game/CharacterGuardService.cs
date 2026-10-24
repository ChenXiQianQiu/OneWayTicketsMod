// =============================================================================
// CharacterGuardService.cs — 旅人免疫「晕车」（冲击昏迷）
//
// 反编译依据：
//   · Decompiled/World/World.Characters/CharacterMgr.cs:61-72
//     CharacterMgr.tick 每个物理帧用车辆的纵向加速度算出 shockForce，
//     再对前排每个旅人调用 Character.ApplyShockForce(shockForce)。
//     也就是说「车越颠 → shockForce 越大 → 越容易晕」，这就是玩家说的晕车。
//   · Decompiled/World/World.Characters/Character.cs:221-246
//     shockForce 超过 desc.coma_g 阈值后按概率写入 state = new Coma(...)，
//     昏迷时长最长 900 tick。整个游戏里只有这一处会 new Coma（已全量搜索确认），
//     所以拦掉 ApplyShockForce 就等于「不会晕车」。
//   · Decompiled/World/World.Characters.CharacterProperties/CP_Step_Up.cs:24-33
//     旅人特性「挺身而出」也走同一个入口施加冲击，开关打开后同样免疫。
//
// 实现：Harmony 前置补丁跳过 ApplyShockForce（见 Patches/CharacterShockPatch.cs）。
//   不动车辆物理、不改数据表，关掉开关立刻恢复原状。
// =============================================================================

using System.Collections.Generic;
using World.Characters;
using World.Characters.CharacterStates;

namespace OneWayTicketsMod.Game
{
    internal static class CharacterGuardService
    {
        /// <summary>「旅人不会晕车」总开关，Harmony 补丁每次冲击都读它。</summary>
        public static bool NoComaEnabled;

        /// <summary>前排 + 休息区的全部旅人。可能含 null 项。</summary>
        public static IEnumerable<Character> All()
        {
            var mgr = TeamService.Mgr;
            if (mgr == null) yield break;

            if (mgr.characters != null)
                foreach (var c in mgr.characters) yield return c;

            var backup = mgr.backup?.backup_characters;
            if (backup != null)
                foreach (var c in backup) yield return c;
        }

        /// <summary>当前处于昏迷状态的旅人数（前排 + 休息区）。</summary>
        public static int ComaCount()
        {
            int n = 0;
            foreach (var c in All())
                if (c != null && c.state is Coma) n++;
            return n;
        }

        /// <summary>
        /// 立刻唤醒所有昏迷旅人。
        /// 走和游戏自然结束昏迷一样的路径：先 state.end() 再置空，
        /// end() 会写 coma_free_ticks（3.5 秒免疫），所以不会马上又晕回去。
        /// </summary>
        public static int WakeAll()
        {
            int n = 0;
            foreach (var c in All())
            {
                if (c == null || !(c.state is Coma)) continue;
                c.state.end();
                c.state = null;
                n++;
            }
            return n;
        }
    }
}
