// =============================================================================
// CampCoinService.cs — 局外（营地）资源：CommonContext.camp_coins
//
// 数据来源：Commons.CommonContext（Foundations.Singleton<CommonContext>）
//   · Dictionary<int,int> camp_coins          —— 真正持有的数量
//   · HashSet<int> unlocked_camp_coins       —— 是否解锁（解锁了才会在营地 UI 里出现）
//
// 关键差异（CE 版踩过的坑，这里天然没有）：
//   「解锁」只是把 id 放进 unlocked 集合，不会在 camp_coins 里建条目；
//   没有条目的币在 UI 上显示为 0 / ???。所以这里补条目 = 往字典里塞一个 0。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AutoCodes;
using Camp;
using Commons;
using UnityEngine;

namespace OneWayTicketsMod.Game
{
    /// <summary>营地币的一行数据。</summary>
    internal sealed class CampCoinEntry
    {
        public int Id;
        public string Name;
        public int Value;
        public bool Unlocked;
        public bool HasEntry;
    }

    internal static class CampCoinService
    {
        private static readonly Regex TagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);

        /// <summary>数据表读不到时的兜底名称（与游戏内中文一致）。</summary>
        private static readonly Dictionary<int, string> FallbackNames = new Dictionary<int, string>
        {
            { 6100, "里程券" },
            { 6111, "食物" },
            { 6112, "建材" },
            { 6113, "珍宝" },
            { 6114, "秘能" },
        };

        public static CommonContext Ctx => SingletonAccess.Get<CommonContext>();

        public static bool Ready => Ctx != null;

        // 名称 / id 表 / 列表都缓存：
        // AutoCodes.camp_coins.records 每次访问都会走 ExcelAnalyzer.init（内部 Assembly.Load），
        // OnGUI 一帧要画好几遍，不缓存会明显卡帧。
        private static readonly Dictionary<int, string> NameCache = new Dictionary<int, string>();
        private static List<int> _allIds;
        private static List<CampCoinEntry> _listCache;
        private static bool _listCacheAll;
        private static float _listCacheAt = -1f;
        private const float ListCacheSeconds = 0.25f;

        /// <summary>写入数据后调用，让下次读取重新取。</summary>
        public static void Invalidate()
        {
            _listCache = null;
            _listCacheAt = -1f;
            _hudDirty = true;
        }

        // ---------------------------------------------------------------- 营地 HUD 刷新
        //
        // 营地左上角那排资源数字不是每帧刷新的，只在 Camp.Coins.CoinArea.call()
        // 被调用时重读（游戏里由车库 / 角色 / 装置等操作触发）。
        // 我们直接改字典不会触发它，所以标脏 + 下一帧统一刷一次，
        // 避免批量写入时反复重载图标资源。

        private static bool _hudDirty;

        public static void MarkHudDirty() => _hudDirty = true;

        /// <summary>每帧由宿主调用；有改动就刷新一次营地 HUD。</summary>
        public static void Tick()
        {
            if (!_hudDirty) return;
            _hudDirty = false;
            RefreshCampHud();
        }

        /// <summary>让营地左上角的资源栏重新读一遍 CommonContext.camp_coins。</summary>
        public static void RefreshCampHud()
        {
            try
            {
                var root = CampSceneRoot.instance;
                if (root != null && root.coinArea != null) root.coinArea.call();
            }
            catch
            {
                // 不在营地场景 / 对象已销毁：忽略
            }
        }

        /// <summary>取当前应当展示的营地币列表（已持有 ∪ 已解锁 ∪ 已知数据表条目）。</summary>
        public static List<CampCoinEntry> Collect(bool includeAllRecords)
        {
            if (_listCache != null && _listCacheAll == includeAllRecords
                && Time.unscaledTime - _listCacheAt < ListCacheSeconds)
                return _listCache;

            var result = new List<CampCoinEntry>();
            var ctx = Ctx;
            if (ctx == null) return result;

            var ids = new SortedSet<int>();

            if (ctx.camp_coins != null)
                foreach (var kv in ctx.camp_coins) ids.Add(kv.Key);

            if (ctx.unlocked_camp_coins != null)
                foreach (var id in ctx.unlocked_camp_coins) ids.Add(id);

            // 只认识已持有 / 已解锁的币，避免把数据表里上百个测试条目全铺出来
            if (includeAllRecords)
                foreach (var id in AllRecordIds()) ids.Add(id);

            foreach (var id in ids)
            {
                int value = 0;
                bool has = ctx.camp_coins != null && ctx.camp_coins.TryGetValue(id, out value);
                result.Add(new CampCoinEntry
                {
                    Id = id,
                    Name = NameOf(id),
                    Value = has ? value : 0,
                    Unlocked = ctx.unlocked_camp_coins != null && ctx.unlocked_camp_coins.Contains(id),
                    HasEntry = has,
                });
            }

            _listCache = result;
            _listCacheAll = includeAllRecords;
            _listCacheAt = Time.unscaledTime;
            return result;
        }

        /// <summary>数据表里登记过的全部营地币 id。</summary>
        public static List<int> AllRecordIds()
        {
            if (_allIds != null) return _allIds;

            var list = new List<int>();
            try
            {
                var records = camp_coins.records;
                if (records == null) return list;
                foreach (var key in records.Keys)
                    if (int.TryParse(key, out var id)) list.Add(id);
                list.Sort();
                _allIds = list;
            }
            catch
            {
                // 数据表还没初始化，返回空表即可，不影响其它功能
            }

            return list;
        }

        /// <summary>营地币 id → 中文名。优先走游戏本地化表，失败再用兜底表。</summary>
        public static string NameOf(int id)
        {
            if (NameCache.TryGetValue(id, out var cached)) return cached;

            try
            {
                var records = camp_coins.records;
                if (records != null && records.TryGetValue(id.ToString(), out var record) && record != null)
                {
                    string localized = Localization_Utility.get_localization(record.name);
                    if (!string.IsNullOrEmpty(localized))
                    {
                        string name = StripTags(localized);
                        NameCache[id] = name;   // 只有真正从数据表取到时才缓存，避免把兜底名钉死
                        return name;
                    }
                }
            }
            catch
            {
                // 忽略：走兜底
            }

            return FallbackNames.TryGetValue(id, out var fallback) ? fallback : "营地币 " + id;
        }

        /// <summary>写入指定数量；条目不存在时先补一条 0 再写。</summary>
        public static bool Set(int id, int value)
        {
            var ctx = Ctx;
            if (ctx == null || ctx.camp_coins == null) return false;

            if (value < 0) value = 0;

            if (!ctx.camp_coins.ContainsKey(id))
            {
                ctx.camp_coins[id] = 0;
                // 既然玩家手动给了数值，就顺手解锁，否则营地 UI 里根本不显示这一项
                ctx.unlocked_camp_coins?.Add(id);
            }

            ctx.camp_coins[id] = value;
            Invalidate();
            return true;
        }

        public static bool Add(int id, int delta)
        {
            var ctx = Ctx;
            if (ctx == null || ctx.camp_coins == null) return false;

            int current = ctx.camp_coins.TryGetValue(id, out var v) ? v : 0;
            return Set(id, (int)Math.Min(int.MaxValue, (long)current + delta));
        }

        /// <summary>把所有已解锁的营地币一次性设成同一个值。</summary>
        public static int FillAll(int value)
        {
            var ctx = Ctx;
            if (ctx == null || ctx.camp_coins == null) return 0;

            int count = 0;
            var ids = new SortedSet<int>();
            if (ctx.unlocked_camp_coins != null)
                foreach (var id in ctx.unlocked_camp_coins) ids.Add(id);
            foreach (var id in ctx.camp_coins.Keys) ids.Add(id);

            foreach (var id in ids)
            {
                if (Set(id, value)) count++;
            }

            Invalidate();
            return count;
        }

        /// <summary>已解锁但字典里还没有条目的币，补一条 0（对应 CE 版的「补建未获得的营地币」）。</summary>
        public static int FillMissing()
        {
            var ctx = Ctx;
            if (ctx == null || ctx.camp_coins == null || ctx.unlocked_camp_coins == null) return 0;

            int count = 0;
            foreach (var id in ctx.unlocked_camp_coins)
            {
                if (ctx.camp_coins.ContainsKey(id)) continue;
                ctx.camp_coins[id] = 0;
                count++;
            }

            Invalidate();
            return count;
        }

        /// <summary>把数据表里所有营地币 id 加进「已解锁」，让它们在营地里可见可花。</summary>
        public static int UnlockAll()
        {
            var ctx = Ctx;
            if (ctx == null || ctx.unlocked_camp_coins == null) return 0;

            int count = 0;
            foreach (var id in AllRecordIds())
            {
                if (ctx.unlocked_camp_coins.Add(id)) count++;
            }

            Invalidate();
            return count;
        }

        /// <summary>IMGUI 不解析富文本标签，名字里的 &lt;sprite&gt; / &lt;color&gt; 必须先剥掉。</summary>
        private static string StripTags(string text)
            => string.IsNullOrEmpty(text) ? text : TagRegex.Replace(text, string.Empty).Trim();
    }
}
