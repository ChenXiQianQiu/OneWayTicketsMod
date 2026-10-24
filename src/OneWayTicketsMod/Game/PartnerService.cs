// =============================================================================
// PartnerService.cs — 营地「伙伴休息所」：把没见过的特殊伙伴变成可见 / 可收
//
// 反编译依据（Camp/Camp.Character/）：
//   · SingleCampCharacterView.update_view_state()（:48-71）
//       joined_once_character_list 不含该 id → view_state = 2（黑色剪影 + unknown_tip，点击被忽略）
//       含该 id 但 friend_character_list 不含 → view_state = 1（正常立绘，可以点开成本面板）
//       两个都含 → view_state = 0（已是伙伴）
//   · CampCharacterRestRoom.UnlockCharacter()（:103-120）
//       玩家点「解锁」→ 扣科技成本 → 写 friend_character_list → 若是 special 角色就解锁
//       GAME_FIRST_FRIEND 成就。也就是说「见过」是游戏设的一道前置门槛。
//   · World/World.Characters/CharacterMgr.AddCharacter()（:130-140）
//       局内招募只会写 joined_once；而且自动写 friend_character_list 时带 !special，
//       所以特殊伙伴只能靠休息所收人。
//
// 本服务只做两件事：
//   1) MarkSeen —— 补写 joined_once_character_list，让休息所里那些黑格子变成可点；
//   2) Recruit —— 直接写 friend_character_list，等同于在休息所点了「解锁」（跳过资源消耗）。
//
// 范围：
//   · MarkSeen 覆盖数据表里的**全部角色**（特殊伙伴 + 普通旅人），因为休息所里两类格子都有，
//     只补特殊伙伴会留下点不开的普通旅人格子。排除教程角色 15000「拾光」（开局就是伙伴）
//     和无立绘的占位条目 15999。
//   · Recruit 只对 special == true 的特殊伙伴生效 —— 普通旅人局内入队时游戏自己会写
//     friend_character_list，没有「花资源收人」这个概念，硬写反而会制造脏数据。
// =============================================================================

using System.Collections.Generic;
using System.Text.RegularExpressions;
using AutoCodes;
using Camp.Character;
using Commons;
using UnityEngine;

namespace OneWayTicketsMod.Game
{
    /// <summary>伙伴休息所里的一格。</summary>
    internal sealed class PartnerEntry
    {
        public int Id;
        public string Name;
        public bool Special;   // role.special：特殊伙伴（休息所里要花资源收的那批）
        public bool Seen;      // joined_once_character_list
        public bool Friend;    // friend_character_list
    }

    internal static class PartnerService
    {
        private static readonly Regex TagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);

        /// <summary>Config 读不到时的教程角色兜底 id（游戏默认值）。</summary>
        private const int TutorialRoleFallback = 15000;

        /// <summary>无立绘、无任何名单引用的占位条目，标记它没有意义。</summary>
        private const int PlaceholderRoleId = 15999;

        public static CommonContext Ctx => SingletonAccess.Get<CommonContext>();

        public static bool Ready => Ctx != null;

        // roles.records 每次访问都走 ExcelAnalyzer.init（内部 Assembly.Load），
        // OnGUI 一帧要画好几遍，所以名称与列表都要缓存。
        private static readonly Dictionary<int, string> NameCache = new Dictionary<int, string>();
        private static List<PartnerEntry> _cache;
        private static float _cacheAt = -1f;
        private const float CacheSeconds = 0.25f;

        public static void Invalidate()
        {
            _cache = null;
            _cacheAt = -1f;
        }

        /// <summary>教程角色 id：开局就在好友名单里，不参与「没见过」的统计。</summary>
        private static int TutorialRoleId()
        {
            try
            {
                return (int)Config.current.tutorial_role_id;
            }
            catch
            {
                return TutorialRoleFallback;
            }
        }

        /// <summary>全部角色（排除教程角色与占位条目），按 id 升序。</summary>
        public static List<PartnerEntry> Collect()
        {
            if (_cache != null && Time.unscaledTime - _cacheAt < CacheSeconds) return _cache;

            var result = new List<PartnerEntry>();
            var ctx = Ctx;
            if (ctx == null) return result;

            int tutorial = TutorialRoleId();
            try
            {
                var records = roles.records;
                if (records == null) return result;

                foreach (var kv in records)
                {
                    if (!int.TryParse(kv.Key, out var id)) continue;
                    var record = kv.Value;
                    if (record == null) continue;
                    if (id == tutorial || id == PlaceholderRoleId) continue;

                    result.Add(new PartnerEntry
                    {
                        Id = id,
                        Name = NameOf(id),
                        Special = record.special,
                        Seen = ctx.joined_once_character_list != null
                               && ctx.joined_once_character_list.Contains(id),
                        Friend = ctx.friend_character_list != null
                                 && ctx.friend_character_list.Contains(id),
                    });
                }
            }
            catch
            {
                // 数据表还没初始化：返回空列表，面板显示「未就绪」即可
                return result;
            }

            result.Sort((a, b) => a.Id.CompareTo(b.Id));
            _cache = result;
            _cacheAt = Time.unscaledTime;
            return result;
        }

        /// <summary>还没见过的角色数量。</summary>
        public static int UnknownCount()
        {
            int n = 0;
            foreach (var p in Collect()) if (!p.Seen) n++;
            return n;
        }

        /// <summary>把所有没见过的角色标记为「已见」，返回本次新增数量。</summary>
        public static int MarkAllSeen()
        {
            int n = 0;
            foreach (var p in Collect())
                if (!p.Seen && MarkSeen(p.Id)) n++;

            if (n > 0)
            {
                Invalidate();
                RefreshCampUi();
            }
            return n;
        }

        /// <summary>把单个角色标记为「已见」。</summary>
        public static bool MarkSeen(int id)
        {
            var ctx = Ctx;
            if (ctx == null || id <= 0) return false;
            if (ctx.joined_once_character_list == null) return false;
            if (ctx.joined_once_character_list.Contains(id)) return false;

            ctx.joined_once_character_list.Add(id);
            Invalidate();
            return true;
        }

        /// <summary>把所有还没收的特殊伙伴直接收为伙伴，返回本次新增数量。</summary>
        public static int RecruitAll()
        {
            int n = 0;
            foreach (var p in Collect())
                if (p.Special && !p.Friend && Recruit(p.Id)) n++;

            if (n > 0) RefreshCampUi();
            return n;
        }

        /// <summary>
        /// 直接收为伙伴（等价于在休息所点「解锁」，但不扣资源）。
        /// 只对 special 角色生效：普通旅人没有「花资源收人」这套流程。
        /// </summary>
        public static bool Recruit(int id)
        {
            var ctx = Ctx;
            if (ctx == null || id <= 0) return false;
            if (ctx.friend_character_list == null) return false;
            if (!IsSpecial(id)) return false;
            if (ctx.friend_character_list.Contains(id)) return false;

            // 收人必然意味着「见过」，两个名单一起补，避免出现 friend 有而 joined_once 没有的脏状态
            if (ctx.joined_once_character_list != null && !ctx.joined_once_character_list.Contains(id))
                ctx.joined_once_character_list.Add(id);

            ctx.friend_character_list.Add(id);
            Invalidate();
            return true;
        }

        /// <summary>该角色是不是「特殊伙伴」（role.special）。</summary>
        public static bool IsSpecial(int id)
        {
            try
            {
                var records = roles.records;
                return records != null && records.TryGetValue(id.ToString(), out var record)
                       && record != null && record.special;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 让营地休息所的界面立刻重算一遍格子状态。
        /// 休息所的 SingleCampCharacterView 只在 CampCharacterRestRoom.UpdateView() 里刷新，
        /// 不主动调的话要等玩家重开面板才看得到变化。
        /// 营地场景里的角色 NPC 是每帧自己判定的（CampCharacterView.Update），不用管。
        /// </summary>
        public static void RefreshCampUi()
        {
            try
            {
                var rooms = Resources.FindObjectsOfTypeAll<CampCharacterRestRoom>();
                if (rooms == null) return;
                foreach (var room in rooms)
                {
                    if (room == null) continue;
                    if (!room.gameObject.scene.IsValid()) continue;   // 排除 prefab 模板
                    room.UpdateView();
                }
            }
            catch
            {
                // 不在营地 / 对象已销毁：忽略
            }
        }

        /// <summary>角色 id → 中文名。走游戏本地化表，失败退回「角色 + id」。</summary>
        public static string NameOf(int id)
        {
            if (NameCache.TryGetValue(id, out var cached)) return cached;

            try
            {
                var records = roles.records;
                if (records != null && records.TryGetValue(id.ToString(), out var record) && record != null)
                {
                    string localized = Localization_Utility.get_localization(record.name);
                    if (!string.IsNullOrEmpty(localized))
                    {
                        string name = StripTags(localized);
                        NameCache[id] = name;
                        return name;
                    }
                }
            }
            catch
            {
                // 忽略：走兜底
            }

            return "角色 " + id;
        }

        /// <summary>IMGUI 不解析富文本标签，名字里的 &lt;sprite&gt; / &lt;color&gt; 必须先剥掉。</summary>
        private static string StripTags(string text)
            => string.IsNullOrEmpty(text) ? text : TagRegex.Replace(text, string.Empty).Trim();
    }
}
