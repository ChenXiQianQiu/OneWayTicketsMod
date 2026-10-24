// =============================================================================
// CharacterGuardService.cs — 旅人免疫「晕车」（冲击昏迷）
//
// 反编译依据：
//   · Decompiled/World/World.Characters/CharacterMgr.cs:61-72
//     CharacterMgr.tick 每个物理帧用车辆的纵向加速度算出 shockForce，
//     再对前排每个旅人调用 Character.ApplyShockForce(shockForce)。
//     也就是说「车越颠 → shockForce 越大 → 越容易晕」，这就是玩家说的晕车。
//   · Decompiled/World/World.Characters/Character.cs:221-246
//     ApplyShockForce 第一行就是
//         if (isImmuneToStun || state != null || coma_free_ticks > 0) return;
//     后面按 desc.coma_g 阈值判定，超了就 state = new Coma(...)。
//     整个游戏里只有这一处会 new Coma（已全量 grep 确认）。
//   · Decompiled/World/World.Characters.CharacterProperties/CP_Reckless_Driving.cs:16-20
//     「油门焊死」词缀的 EnterTeam() 除了置 isImmuneToStun = true，
//     还会 wheel_tractive_force_role += wheel_tf。
//
// 为什么 v0.2.3 起不再「补词缀」，改成纯轮询置 isImmuneToStun：
//   v0.2.1/v0.2.2 的做法是给每个旅人塞一份「油门焊死」词缀，好处是能进存档。
//   但这个词缀的 EnterTeam() 里除了免疫，还会加牵引力
//   （CP_Reckless_Driving.cs:16-20）。
//   一旦同一个人身上挂了两份（存档里已有一份 + 进场景后又补一份），
//   牵引力就会翻倍；词缀越多车越猛，车速被推到极高之后
//   CaravanMover_Run.do 里的 `caravan_pos += velocity/120` 会把车送出地图，
//   相机跟着 caravan_pos 走（LensMgr.calc_camera_pos 直接读 caravan_pos.x），
//   表现就是「UI 还在、画面全黑」。
//   现在改成每 0.5 秒直接把 isImmuneToStun 置 true：
//   不写存档、不加牵引力、不碰任何游戏数据，效果完全一样（ApplyShockForce 第一行就 return）。
//   顺带做一次「重复词缀清理」：发现同一人身上有多份「油门焊死」就只留一份，
//   把历史版本可能留下的重复数据修掉。
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using World.Characters;
using World.Characters.CharacterStates;

namespace OneWayTicketsMod.Game
{
    internal static class CharacterGuardService
    {
        /// <summary>「旅人不会晕车」总开关。</summary>
        public static bool NoComaEnabled;

        /// <summary>「油门焊死」词缀的脚本名，用来识别/清理重复数据。</summary>
        private const string ImmunePropertyScript = "CP_Reckless_Driving";

        /// <summary>上次同步时看到的 CharacterMgr，换局（引用变化）时重置去重检查。</summary>
        private static CharacterMgr _lastMgr;

        /// <summary>换局后是否已经做过一次重复词缀清理。</summary>
        private static bool _dupChecked;

        /// <summary>已经报告过重复词缀的旅人，避免刷屏。</summary>
        private static readonly HashSet<uint> ReportedDup = new HashSet<uint>();

        /// <summary>本 Mod 清理掉的重复词缀数量（面板显示用）。</summary>
        public static int TrimmedCount { get; private set; }

        private static float _nextSync;

        /// <summary>面板上显示的一行诊断，方便排查「开关开了但没生效」。</summary>
        public static string Diagnose()
        {
            int total = 0, hasProperty = 0;
            foreach (var c in All())
            {
                if (c == null) continue;
                total++;
                if (CountProperty(c) > 0) hasProperty++;
            }

            string extra = TrimmedCount > 0 ? $"，已清理重复词缀 {TrimmedCount} 份" : string.Empty;
            return $"免疫位每 0.5 秒置位：队伍 {total} 人，自带「油门焊死」{hasProperty} 人{extra}";
        }

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

        /// <summary>开关切换时立刻同步一次，不用等下一轮轮询。</summary>
        public static void SetEnabled(bool on)
        {
            NoComaEnabled = on;
            _nextSync = 0f;
            Sync();
        }

        /// <summary>每帧调用（内部 0.5 秒节流）。</summary>
        public static void Tick()
        {
            if (Time.unscaledTime < _nextSync) return;
            _nextSync = Time.unscaledTime + 0.5f;
            Sync();
        }

        // ------------------------------------------------------------ 内部实现

        private static void Sync()
        {
            var mgr = TeamService.Mgr;
            if (mgr == null) return;

            if (!ReferenceEquals(mgr, _lastMgr))
            {
                // 换局 / 重读存档：旧 Character 对象已经废弃，只清引用
                _lastMgr = mgr;
                _dupChecked = false;
                ReportedDup.Clear();
            }

            foreach (var c in All())
            {
                if (c == null) continue;

                // 重复词缀只在换局后查一次（属性表不会自己长出第二份）
                if (!_dupChecked) TrimDuplicateProperty(c);

                // 身上自带「油门焊死」的人，免疫位本来就该是开的，
                // 关开关时不要把它关掉（那是游戏自己给的）。
                c.isImmuneToStun = NoComaEnabled || CountProperty(c) > 0;
            }

            _dupChecked = true;
        }

        /// <summary>数一下这个人身上有几份「油门焊死」。</summary>
        private static int CountProperty(Character c)
        {
            var list = c?.character_properties;
            if (list == null) return 0;

            int n = 0;
            foreach (var p in list)
                if (p?.desc != null && p.desc.script == ImmunePropertyScript) n++;
            return n;
        }

        /// <summary>
        /// 同一个人身上挂多份「油门焊死」时只保留一份。
        /// 每份的 EnterTeam 都会加一次 wheel_tractive_force_role，
        /// 多出来的那份会让牵引力凭空变大，是本 Mod 旧版本可能留下的隐患。
        /// </summary>
        private static void TrimDuplicateProperty(Character c)
        {
            var list = c?.character_properties;
            if (list == null) return;

            int seen = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var p = list[i];
                if (p?.desc == null || p.desc.script != ImmunePropertyScript) continue;

                seen++;
                if (seen == 1) continue; // 留一份

                try
                {
                    p.ExitTeam();
                    list.RemoveAt(i);
                    TrimmedCount++;

                    uint id = c.desc?.id ?? 0u;
                    if (ReportedDup.Add(id))
                        Plugin.LogWarn($"旅人 {id} 身上有多份「油门焊死」，已移除多余的（避免牵引力叠加把车速顶飞）。");
                }
                catch
                {
                    // 单个属性移除失败不影响其它
                }
            }
        }
    }
}
