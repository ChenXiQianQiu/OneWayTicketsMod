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
//     「油门焊死」词缀的 EnterTeam() 就是 owner.isImmuneToStun = true
//     （顺带 wheel_tractive_force_role += wheel_tf，这是词缀本身的增益）。
//
// 为什么 v0.2.1 改成「加词缀」而不是拦方法：
//   v0.2.0 用 Harmony 前置补丁跳过 ApplyShockForce，实测无效。
//   ApplyShockForce 是从 CharacterMgr.tick 的循环里直接调用的，
//   Mono 的 JIT 会把它内联进 tick，补丁只改到方法入口，内联后的调用点看不到补丁。
//   改数据（isImmuneToStun）不经过调用点，不受内联影响，所以稳定生效。
//
// 实现：
//   · 开关打开 → 给每个旅人补一份「油门焊死」词缀（已有就不重复加），
//     Init / Start / EnterTeam 走一遍，于是 isImmuneToStun = true；
//   · 开关关闭 → 只移除本 Mod 加的那份并 ExitTeam 还原，
//     旅人自己本来就有的词缀不动；
//   · 0.5 秒轮询兜底：新入队、换人、读档后自动补齐，玩家不用重新开关。
// =============================================================================

using System;
using System.Collections.Generic;
using AutoCodes;
using UnityEngine;
using World.Characters;
using World.Characters.CharacterProperties;
using World.Characters.CharacterStates;

namespace OneWayTicketsMod.Game
{
    internal static class CharacterGuardService
    {
        /// <summary>「旅人不会晕车」总开关。</summary>
        public static bool NoComaEnabled;

        /// <summary>「油门焊死」词缀在 properties 表里的 id（运行时解析，找不到就跳过）。</summary>
        private const string ImmunePropertyScript = "CP_Reckless_Driving";

        /// <summary>本 Mod 亲手加进去的词缀归属者，关闭开关时只回收这些。</summary>
        private static readonly HashSet<Character> Injected = new HashSet<Character>();

        /// <summary>上次同步时看到的 CharacterMgr，换局（引用变化）时用来清理旧引用。</summary>
        private static CharacterMgr _lastMgr;

        private static uint _propertyId;
        private static bool _resolveTried;
        private static string _resolveNote = "尚未开始解析";
        private static float _nextSync;

        /// <summary>面板上显示的一行诊断，方便排查「开关开了但没生效」。</summary>
        public static string Diagnose()
        {
            if (_propertyId == 0)
                return $"词缀未就绪（{_resolveNote}），暂用直接置位兜底";

            int have = 0;
            foreach (var c in All())
                if (c != null && FindProperty(c) != null) have++;

            return $"id={_propertyId}，队伍里 {have} 人已有，本次新补 {Injected.Count} 人";
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
            WakeInSafeArea();
        }

        /// <summary>
        /// 安全区里的「一直晕」修复。
        ///
        /// 反编译依据：Decompiled/World/World.Characters/CharacterMgr.cs:59-72
        ///     foreach (Character character in characters)
        ///     {
        ///         character.ApplyShockForce(shockForce);        // ← 安全区里照样调
        ///         if (!Singleton<WorldContext>.instance.in_safe_area)
        ///             character.tick();                          // ← 安全区里不调
        ///     }
        /// 也就是说：进安全区之后，冲击判定还在跑，但推进状态倒计时的 tick 停了。
        /// 只要在安全区里被震进 Coma，就永远走不到 state.end()，会一直晕着，
        /// 而且安全区没有战斗，玩家也没法把他弄醒。
        /// 这里在安全区里直接把昏迷状态清掉（和 Character.EnterSafeArea 完全一样的收尾方式）。
        /// </summary>
        private static void WakeInSafeArea()
        {
            var ctx = WorldService.Ctx;
            if (ctx == null || !ctx.in_safe_area) return;

            foreach (var c in All())
            {
                if (c == null || !(c.state is Coma)) continue;

                c.state.end();
                c.state = null;
                Plugin.LogInfo($"安全区：已唤醒昏迷的旅人 {c.desc?.id}（安全区不跑 character.tick，昏迷不会自己结束）");
            }
        }

        // ------------------------------------------------------------ 内部实现

        private static void Sync()
        {
            var mgr = TeamService.Mgr;

            if (!ReferenceEquals(mgr, _lastMgr))
            {
                // 换局 / 重读存档：旧 Character 对象已经废弃，
                // 再去调它的 ExitTeam 会往新一局的 BattleContext 上减牵引力，所以只清引用。
                Injected.Clear();
                _lastMgr = mgr;
            }

            if (!NoComaEnabled)
            {
                if (Injected.Count == 0) return;
                foreach (var c in new List<Character>(Injected)) RemoveProperty(c);
                Injected.Clear();

                // 没赶上补词缀、只被临时打开免疫位的旅人也一并还原。
                // 游戏里只有「油门焊死」会把 isImmuneToStun 置 true（全量 grep 确认），
                // 所以没这个词缀的人设回 false 是安全的。
                foreach (var c in All())
                {
                    if (c == null || FindProperty(c) != null) continue;
                    c.isImmuneToStun = false;
                }
                return;
            }

            if (mgr == null) return;

            if (!ResolvePropertyId())
            {
                // properties 表还没就绪（局外 / 刚进场景）：
                // 先直接把免疫位打开保证功能可用，等表就绪了下一轮再补真词缀。
                foreach (var c in All())
                    if (c != null) c.isImmuneToStun = true;
                return;
            }

            foreach (var c in All()) EnsureProperty(c);
        }

        /// <summary>
        /// 在 properties 表里找「油门焊死」。表是运行时从 Excel 资源加载的，
        /// 局外可能还没就绪，所以失败时不置 _resolveTried，下一轮再试。
        /// </summary>
        private static bool ResolvePropertyId()
        {
            if (_resolveTried) return _propertyId != 0;
            _resolveTried = true;

            try
            {
                var records = propertiess.records;
                if (records == null)
                {
                    _resolveNote = "properties 表返回 null";
                    _resolveTried = false;
                    return false;
                }

                foreach (var pair in records)
                {
                    var record = pair.Value;
                    if (record != null && record.script == ImmunePropertyScript)
                    {
                        _propertyId = record.id;
                        _resolveNote = "ok";
                        Plugin.LogInfo($"已找到「油门焊死」词缀，id={_propertyId}");
                        return true;
                    }
                }

                _resolveNote = $"表里没有 {ImmunePropertyScript}";
                Plugin.LogWarn($"properties 表里没有 {ImmunePropertyScript}，改用直接置 isImmuneToStun 的兜底方案");
            }
            catch (Exception ex)
            {
                _resolveNote = "读表异常：" + ex.Message;
                _resolveTried = false;
            }

            return _propertyId != 0;
        }

        /// <summary>
        /// 按词缀脚本名找「油门焊死」。
        /// 这里不比对 id：表还没解析出来时 id 是 0，用脚本名更稳。
        /// </summary>
        private static CharacterProperty FindProperty(Character c)
        {
            var list = c?.character_properties;
            if (list == null) return null;

            foreach (var property in list)
                if (property?.desc != null && property.desc.script == ImmunePropertyScript)
                    return property;

            return null;
        }

        /// <summary>确保这个人身上有「油门焊死」，并且免疫位是开的。</summary>
        private static void EnsureProperty(Character c)
        {
            if (c == null) return;

            var existing = FindProperty(c);
            if (existing != null)
            {
                // 本来就有（游戏随机给的），只保证免疫位开着，不重复加词缀。
                c.isImmuneToStun = true;
                return;
            }

            try
            {
                var property = CharacterUtility.GetProperty(c, _propertyId);
                if (property == null) return;

                // 和 Character.Init / CharacterMgr.AddCharacter 的顺序保持一致
                property.Init();
                property.Start();
                c.character_properties.Add(property);
                property.EnterTeam();

                // EnterTeam 里就会置 true，这里再写一次是兜底
                c.isImmuneToStun = true;
                Injected.Add(c);
            }
            catch (Exception ex)
            {
                Plugin.LogWarn($"给旅人补「油门焊死」词缀失败：{ex.Message}");
            }
        }

        /// <summary>回收本 Mod 加的词缀，并让 EnterTeam 加的牵引力一起还回去。</summary>
        private static void RemoveProperty(Character c)
        {
            if (c == null) return;

            var property = FindProperty(c);
            if (property == null) return;

            try
            {
                property.ExitTeam();
                c.character_properties.Remove(property);
                c.isImmuneToStun = false;
            }
            catch (Exception ex)
            {
                Plugin.LogWarn($"移除「油门焊死」词缀失败：{ex.Message}");
            }
        }
    }
}
