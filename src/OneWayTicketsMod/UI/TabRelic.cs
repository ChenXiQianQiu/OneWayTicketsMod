// =============================================================================
// TabRelic.cs — 「奇物」页签：局内自由编辑奇物
//
// 上半：本局持有的奇物（按 id 聚合），每个可单独移除；
// 下半：数据表里的全部奇物，搜索后随时加进本局，加多少个都行。
//
// 业务都在 Game/RelicEditService.cs（那里的文件头写清了反编译依据），
// 这里只负责把列表画出来、把按钮接到方法上。
// =============================================================================

using OneWayTicketsMod.Game;
using UnityEngine;
using static OneWayTicketsMod.UI.PanelWidgets;

namespace OneWayTicketsMod.UI
{
    internal sealed partial class MainPanel
    {
        private void DrawRelicTab()
        {
            if (!RelicEditService.Ready)
            {
                BeginCard("奇物编辑");
                Info("当前不在局内，读不到奇物管理器。");
                Hint("奇物只在一局探索里存在：进局后（车上或安全点都行）再回来看本页。");
                EndCard();
                return;
            }

            DrawOwnedRelics();
            DrawRelicCatalog();
        }

        // ---------------------------------------------------------------- 本局持有

        private void DrawOwnedRelics()
        {
            var owned = RelicEditService.Owned();

            BeginCard($"本局奇物（{owned.Count} 种 · {RelicEditService.Summary()}）");

            GUILayout.BeginHorizontal();
            if (Btn("刷新", 46f, PanelTheme.Mini, 20f)) Notify("已刷新奇物列表。");
            if (Btn("全部移除", 76f, PanelTheme.ButtonDanger, 20f))
            {
                string result = RelicEditService.ClearAll();
                Notify(result, 4f);
            }
            GUILayout.Space(8f);
            Hint("移除走游戏自己的 RemoveRelic，会连带清掉奇物挂在战斗上的效果。");
            GUILayout.EndHorizontal();

            Divider();

            if (owned.Count == 0)
            {
                Info("本局还没有奇物。在下面「添加奇物」里挑几个吧。");
                EndCard();
                return;
            }

            _relicOwnedScroll = BeginList(_relicOwnedScroll, 190f);
            foreach (var e in owned)
            {
                GUILayout.BeginHorizontal(PanelTheme.Row, GUILayout.Height(21f));

                Color saved = GUI.contentColor;
                GUI.contentColor = RankColor(e.Rank);
                GUILayout.Label(e.Name, PanelTheme.Sub, GUILayout.Width(132f));

                GUI.contentColor = PanelTheme.TxtWeak;
                GUILayout.Label("#" + e.Id, PanelTheme.Sub, GUILayout.Width(52f));

                GUI.contentColor = e.Count > 1 ? PanelTheme.TxtAccent : PanelTheme.TxtWeak;
                GUILayout.Label("×" + e.Count, PanelTheme.Sub, GUILayout.Width(34f));

                GUI.contentColor = PanelTheme.TxtWeak;
                GUILayout.Label(Short(e.Desc, 34), PanelTheme.Sub, GUILayout.Width(170f));
                GUI.contentColor = saved;

                // 不用 FlexibleSpace 右对齐：页面外层滚动区的可见宽度比布局宽度窄，
                // 被推到最右的按钮会落到可视区外，看不见也点不到（营地页踩过同一个坑）。
                if (Btn("移除", 44f, PanelTheme.Mini, 19f))
                {
                    Notify(RelicEditService.RemoveOne(e.Id), 4f);
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();
            }
            EndList();
            EndCard();
        }

        // ---------------------------------------------------------------- 添加

        private void DrawRelicCatalog()
        {
            var list = RelicEditService.Search(_relicSearch);

            BeginCard("添加奇物");

            GUILayout.BeginHorizontal();
            Caption("搜索", 34f);
            GUI.SetNextControlName("relic_search");
            _relicSearch = GUILayout.TextField(_relicSearch ?? string.Empty, PanelTheme.Field,
                GUILayout.Width(150f), GUILayout.Height(20f));
            if (Btn("清空", 42f, PanelTheme.Mini, 20f)) _relicSearch = string.Empty;
            GUILayout.Space(10f);
            GUILayout.Label($"共 {list.Count} 条", PanelTheme.Hint, GUILayout.Width(70f));
            GUILayout.EndHorizontal();

            Divider();

            _relicScroll = BeginList(_relicScroll, 250f);
            if (list.Count == 0)
            {
                Hint("没有匹配的奇物（数据表可能还没加载好，或关键词太窄）。");
            }
            else
            {
                foreach (var e in list)
                {
                    GUILayout.BeginHorizontal(PanelTheme.Row, GUILayout.Height(21f));

                    Color saved = GUI.contentColor;
                    GUI.contentColor = RankColor(e.Rank);
                    GUILayout.Label(e.Name, PanelTheme.Sub, GUILayout.Width(132f));

                    GUI.contentColor = PanelTheme.TxtWeak;
                    GUILayout.Label("#" + e.Id, PanelTheme.Sub, GUILayout.Width(52f));

                    GUI.contentColor = PanelTheme.TxtWeak;
                    GUILayout.Label(Short(e.Desc, 40), PanelTheme.Sub, GUILayout.Width(200f));
                    GUI.contentColor = saved;

                    if (Btn("+1", 34f, PanelTheme.Mini, 19f))
                    {
                        Notify(RelicEditService.Add(e.Id, 1), 3.5f);
                        GUILayout.EndHorizontal();
                        break;
                    }
                    if (Btn("+3", 34f, PanelTheme.Mini, 19f))
                    {
                        Notify(RelicEditService.Add(e.Id, 3), 3.5f);
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
            }
            EndList();

            Hint("添加不消耗奇物池、不改图鉴，也不会顺带解锁别的奇物；存档时会跟着 relic_list 一起存下来。");
            EndCard();
        }

        // ---------------------------------------------------------------- 工具

        /// <summary>稀有度配色：越高越亮，0 视为普通。</summary>
        private static Color RankColor(int rank)
        {
            if (rank >= 3) return PanelTheme.TxtAccent;
            if (rank == 2) return PanelTheme.TxtOk;
            if (rank == 1) return PanelTheme.TxtSub;
            return PanelTheme.TxtWeak;
        }

        /// <summary>描述太长会把行撑高，按字符数截断（IMGUI 的中文宽度按字算即可）。</summary>
        private static string Short(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string flat = text.Replace("\r", " ").Replace("\n", " ");
            return flat.Length <= max ? flat : flat.Substring(0, max) + "…";
        }
    }
}
