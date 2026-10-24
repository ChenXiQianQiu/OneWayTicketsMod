// =============================================================================
// WorldService.cs — 局内（探索）数据
//
// 数据来源：
//   · World.BackPack.BackPackMgr.coin_count   —— 局内金币（Mission 里挂名 "BackPack"）
//   · World.WorldContext.caravan_hp / _max    —— 车队耐久
//
// 局内数据只在本局有效：回到营地结算时会写进存档的营地资源，
// 所以这里改的数值不会直接改存档（和 CE 表的行为一致）。
// =============================================================================

using System;
using UnityEngine;
using World;
using World.BackPack;

namespace OneWayTicketsMod.Game
{
    internal static class WorldService
    {
        /// <summary>无限耐久开关（常驻逻辑，面板关闭也生效）。</summary>
        public static bool InfiniteDurability;

        private static float _nextRepair;

        public static BackPackMgr BackPack => SingletonAccess.Mgr<BackPackMgr>("BackPack");

        public static WorldContext Ctx => SingletonAccess.Get<WorldContext>();

        /// <summary>是否处于局内（探索场景）。</summary>
        public static bool InWorld => BackPack != null || Ctx != null;

        // ---------------------------------------------------------------- 局内金币

        public static bool TryGetCoin(out int value)
        {
            value = 0;
            var mgr = BackPack;
            if (mgr == null) return false;
            value = mgr.coin_count;
            return true;
        }

        public static bool SetCoin(int value)
        {
            var mgr = BackPack;
            if (mgr == null) return false;
            mgr.coin_count = Mathf.Max(0, value);
            return true;
        }

        // ---------------------------------------------------------------- 车辆耐久

        public static bool TryGetCaravan(out int hp, out int hpMax)
        {
            hp = hpMax = 0;
            var ctx = Ctx;
            if (ctx == null) return false;
            hp = ctx.caravan_hp;
            hpMax = ctx.caravan_hp_max;
            return true;
        }

        public static bool SetCaravanHp(int value)
        {
            var ctx = Ctx;
            if (ctx == null) return false;
            ctx.caravan_hp = Mathf.Clamp(value, 0, Mathf.Max(1, ctx.caravan_hp_max));
            return true;
        }

        public static bool SetCaravanHpMax(int value)
        {
            var ctx = Ctx;
            if (ctx == null) return false;
            ctx.caravan_hp_max = Mathf.Max(1, value);
            if (ctx.caravan_hp > ctx.caravan_hp_max) ctx.caravan_hp = ctx.caravan_hp_max;
            return true;
        }

        /// <summary>把当前耐久直接回满。</summary>
        public static bool RepairFull()
        {
            var ctx = Ctx;
            if (ctx == null) return false;
            ctx.caravan_hp = Mathf.Max(1, ctx.caravan_hp_max);
            return true;
        }

        /// <summary>
        /// 每帧调用：无限耐久开启时把耐久钉在上限。
        /// 用 0.2 秒节流，避免每帧写字段触发不必要的 UI 刷新。
        /// </summary>
        public static void Tick()
        {
            if (!InfiniteDurability) return;
            if (Time.unscaledTime < _nextRepair) return;
            _nextRepair = Time.unscaledTime + 0.2f;

            var ctx = Ctx;
            if (ctx == null) return;
            int max = Mathf.Max(1, ctx.caravan_hp_max);
            if (ctx.caravan_hp < max) ctx.caravan_hp = max;
        }
    }
}
