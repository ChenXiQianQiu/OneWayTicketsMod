// =============================================================================
// RelicEditService.cs — 局内奇物（Relic）自由编辑
//
// 反编译依据（World 程序集，2026-10-27 更新后逐行核对，奇物相关类本次零改动）：
//   1. Decompiled/World/World.Relic/RelicMgr.cs:142
//        public List<Relic> relic_list;            ← 本局持有的奇物，全在这里
//   2. Decompiled/World/World.Relic/RelicMgr.cs:283-291
//        private void add_relic_directly(Relic relic)
//        { relic_list.Add(relic); relic.Get(); foreach (view) view.notify_add_rellic(relic); }
//      这是读档恢复奇物走的路径（RelicPD.cs:46 用 CreateRelicAndAddDirectly），
//      **不扣奇物池、不触发 lock_change、不改图鉴**，正是「自由添加」要的语义。
//      它是 private，所以这里按同样三步自己实现（views 是 Model 基类的 public 字段）。
//   3. Decompiled/World/World.Relic/RelicMgr.cs:351-359
//        public void RemoveRelic(Relic relic) → relic_list.Remove + relic.Drop() + 通知视图
//      Drop() 必须走 —— 奇物在 Get() 里会往 ProjectileMgr / BattleContext 挂事件委托
//      （例：Relics/Relic_Ammo_Hail.cs:16-20），只从 list 里删会让事件残留成幽灵效果。
//   4. Decompiled/World/World.Relic/RelicMgr.cs:188-201、374-384
//        CreateRelic(id) 按 relic.behaviour_script 反射 new 出行为对象，**不碰奇物池**；
//        CreateRelicAndAddDirectly(id) 则会先调 relic_museum.GetRelic 扣 remain_cnt
//        并改其它奇物的 lock_stage —— 所以自由编辑不用它，避免「加一个奇物顺手解锁一片」。
//   5. Decompiled/World/World.Relic/RelicMgrView.cs:40-58
//        视图收到通知就整表重建格子（7 列、最少 3 行），所以增删后安全点奇物墙会立刻同步。
//   6. 存档：Decompiled/World/World.Saves/Save_DS.cs:90-92
//        存档时遍历 relic_list 写 relic_datas，所以这里改完下次存档自动带上，不用手动存。
//
// 不做的事（刻意保持最小副作用）：
//   · 不写 CommonContext.relic_owned（局外图鉴）—— 局内编辑不该改玩家长期进度；
//   · 不动 relic_museum（奇物池剩余数 / 解锁阶段）—— 那是商店与掉落用的。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AutoCodes;
using Commons;
using World.Relic;

namespace OneWayTicketsMod.Game
{
    internal static class RelicEditService
    {
        /// <summary>本局的奇物管理器（Mission 里挂名 "RelicMgr"，只在局内存在）。</summary>
        public static RelicMgr Mgr => SingletonAccess.Mgr<RelicMgr>("RelicMgr");

        public static bool Ready => Mgr != null;

        /// <summary>奇物的一行数据（本局持有 / 全表目录共用）。</summary>
        public sealed class Entry
        {
            public uint Id;
            public string Name;       // 本地化名称（已剥富文本）
            public string Desc;       // 本地化描述（已剥富文本）
            public int Count;         // 本局持有数量（目录里恒为 0）
            public int Rank;          // 稀有度
            public int MaxNum;        // 池子里最多出现几次（仅展示用）
            public string Behaviour;  // 行为脚本名，空 = 纯数据奇物
        }

        private static readonly Regex TagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);

        // relics.records 每次访问都会走 ExcelAnalyzer.init（内部 Assembly.Load），
        // OnGUI 一帧要画好几遍，不缓存会明显卡帧。
        private static List<Entry> _catalog;
        private static readonly Dictionary<uint, Entry> ById = new Dictionary<uint, Entry>();

        // ---------------------------------------------------------------- 目录

        /// <summary>数据表里的全部奇物（按 id 排序）。读不到表时返回空表。</summary>
        public static List<Entry> Catalog()
        {
            EnsureCatalog();
            return _catalog;
        }

        /// <summary>按关键词过滤（名称 / id / 行为脚本名），keyword 为空返回全表。</summary>
        public static List<Entry> Search(string keyword)
        {
            EnsureCatalog();
            string key = (keyword ?? string.Empty).Trim();
            if (key.Length == 0) return _catalog;

            var result = new List<Entry>();
            foreach (var e in _catalog)
            {
                if (e.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0
                    || e.Id.ToString().Contains(key)
                    || (!string.IsNullOrEmpty(e.Behaviour)
                        && e.Behaviour.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0))
                    result.Add(e);
            }
            return result;
        }

        private static void EnsureCatalog()
        {
            if (_catalog != null) return;

            var list = new List<Entry>();
            try
            {
                foreach (var pair in relics.records)
                {
                    var r = pair.Value;
                    if (r == null) continue;

                    var e = new Entry
                    {
                        Id = r.id,
                        Name = Localize(r.name, "奇物 " + r.id),
                        Desc = Localize(r.description, string.Empty),
                        Rank = r.rank,
                        MaxNum = r.max_num,
                        Behaviour = r.behaviour_script ?? string.Empty,
                    };
                    list.Add(e);
                    ById[e.Id] = e;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("读取奇物数据表失败（面板里将没有可添加的条目）：" + ex.Message);
            }

            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            _catalog = list;
        }

        // ---------------------------------------------------------------- 本局持有

        /// <summary>本局持有的奇物，按 id 聚合（同名奇物可能持有多个）。</summary>
        public static List<Entry> Owned()
        {
            var result = new List<Entry>();
            var mgr = Mgr;
            if (mgr?.relic_list == null) return result;

            var counter = new Dictionary<uint, int>();
            var order = new List<uint>();
            foreach (var relic in mgr.relic_list)
            {
                var desc = relic?.desc;
                if (desc == null) continue;
                if (!counter.ContainsKey(desc.id))
                {
                    counter[desc.id] = 0;
                    order.Add(desc.id);
                }
                counter[desc.id]++;
            }

            foreach (var id in order)
            {
                var e = MakeEntry(id);
                e.Count = counter[id];
                result.Add(e);
            }
            return result;
        }

        /// <summary>一行摘要，给面板状态条用（种类数在「奇物」页的列表里能直接数出来）。</summary>
        public static string Summary()
        {
            var mgr = Mgr;
            if (mgr?.relic_list == null) return "不在局内";

            int total = 0;
            foreach (var relic in mgr.relic_list)
            {
                if (relic?.desc != null) total++;
            }
            return total + " 个";
        }

        // ---------------------------------------------------------------- 增删

        /// <summary>添加 count 个指定奇物。返回一句给玩家看的结果。</summary>
        public static string Add(uint id, int count = 1)
        {
            var mgr = Mgr;
            if (mgr == null) return "不在局内，读不到奇物管理器。";

            EnsureCatalog();
            if (!ById.ContainsKey(id)) return $"奇物表里没有 {id} 号奇物。";

            int ok = 0;
            for (int i = 0; i < count; i++)
            {
                if (!AddOne(mgr, id)) break;
                ok++;
            }

            if (ok == 0) return $"添加 {id} 号奇物失败，详见日志。";
            return $"已添加 {NameOf(id)} × {ok}";
        }

        /// <summary>
        /// 添加一个：完全照抄 RelicMgr.add_relic_directly 的三步。
        /// 中途行为脚本报错就回滚，避免 relic_list 里留下「有对象但效果没挂上」的半成品。
        /// </summary>
        private static bool AddOne(RelicMgr mgr, uint id)
        {
            Relic relic;
            try
            {
                relic = mgr.CreateRelic(id.ToString());
            }
            catch (Exception ex)
            {
                Plugin.LogWarn($"创建奇物 {id} 失败：{ex.Message}");
                return false;
            }

            if (relic?.desc == null) return false;

            try
            {
                mgr.relic_list.Add(relic);
                relic.Get();
                foreach (var view in mgr.views) view.notify_add_rellic(relic);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.LogWarn($"添加奇物 {id} 时行为脚本报错（已回滚）：{ex.Message}");
                try { mgr.relic_list.Remove(relic); } catch { }
                try { relic.Drop(); } catch { }
                return false;
            }
        }

        /// <summary>移除一个指定奇物（走游戏自己的 RemoveRelic，含 Drop 清理）。</summary>
        public static string RemoveOne(uint id)
        {
            var mgr = Mgr;
            if (mgr?.relic_list == null) return "不在局内，读不到奇物管理器。";

            var relic = mgr.relic_list.Find(r => r?.desc != null && r.desc.id == id);
            if (relic == null) return $"本局没有 {id} 号奇物。";

            try
            {
                mgr.RemoveRelic(relic);
                return $"已移除 {NameOf(id)}";
            }
            catch (Exception ex)
            {
                Plugin.LogWarn($"移除奇物 {id} 失败：{ex.Message}");
                return "移除失败：" + ex.Message;
            }
        }

        /// <summary>清空本局全部奇物。</summary>
        public static string ClearAll()
        {
            var mgr = Mgr;
            if (mgr?.relic_list == null) return "不在局内，读不到奇物管理器。";
            if (mgr.relic_list.Count == 0) return "本局还没有奇物。";

            var copy = new List<Relic>(mgr.relic_list);
            int ok = 0;
            foreach (var relic in copy)
            {
                try
                {
                    mgr.RemoveRelic(relic);
                    ok++;
                }
                catch (Exception ex)
                {
                    Plugin.LogWarn("清空奇物时出错：" + ex.Message);
                }
            }
            return $"已清空本局 {ok} 个奇物";
        }

        // ---------------------------------------------------------------- 工具

        /// <summary>目录里的条目复制一份（不改动缓存对象）。</summary>
        private static Entry MakeEntry(uint id)
        {
            if (ById.TryGetValue(id, out var src))
            {
                return new Entry
                {
                    Id = src.Id,
                    Name = src.Name,
                    Desc = src.Desc,
                    Rank = src.Rank,
                    MaxNum = src.MaxNum,
                    Behaviour = src.Behaviour,
                };
            }

            // 表里没有（例如旧存档里已被官方删掉的奇物）：用 id 兜底
            return new Entry { Id = id, Name = "奇物 " + id, Desc = string.Empty };
        }

        public static string NameOf(uint id)
        {
            EnsureCatalog();
            return ById.TryGetValue(id, out var e) ? e.Name : "奇物 " + id;
        }

        /// <summary>本地化取值 + 剥富文本标签（面板用的是 IMGUI，标签会原样画出来）。</summary>
        private static string Localize(string key, string fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return fallback;
                var text = Localization_Utility.get_localization(key);
                if (string.IsNullOrEmpty(text)) return fallback;
                return TagRegex.Replace(text, string.Empty).Trim();
            }
            catch
            {
                return fallback;
            }
        }
    }
}
