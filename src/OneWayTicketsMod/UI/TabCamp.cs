// =============================================================================
// TabCamp.cs — 「营地资源」页签：CommonContext.camp_coins 逐项编辑
//
// 行结构：名称 | 当前值 | 输入框 | 应用 | +1万 | 满
// 输入框只在「没有焦点」时才跟随游戏数值刷新，否则玩家打字会被每帧覆盖。
// =============================================================================

using OneWayTicketsMod.Game;
using UnityEngine;
using static OneWayTicketsMod.UI.PanelWidgets;

namespace OneWayTicketsMod.UI
{
    internal sealed partial class MainPanel
    {
        private void DrawCampTab()
        {
            if (!CampCoinService.Ready)
            {
                BeginCard("营地资源");
                Info("还没读到营地数据。");
                Hint("请先进入营地（或读档进营地）一次，再回到本面板。局外资源保存在存档里，菜单界面时对象还没创建。");
                EndCard();
                return;
            }

            BeginCard("营地资源");

            GUILayout.BeginHorizontal();
            Caption("搜索", 34f);
            GUI.SetNextControlName("coin_search");
            _coinSearch = GUILayout.TextField(_coinSearch ?? string.Empty, PanelTheme.Field,
                GUILayout.Width(120f), GUILayout.Height(20f));
            if (Btn("清空", 42f, PanelTheme.Mini, 20f)) _coinSearch = string.Empty;
            GUILayout.Space(8f);
            bool showAll = GUILayout.Toggle(_showAllCoins, " 显示数据表里全部营地币", PanelTheme.Toggle, GUILayout.Width(190f));
            if (showAll != _showAllCoins)
            {
                _showAllCoins = showAll;
                _coinBuf.Clear();
            }
            // 不用 FlexibleSpace 右对齐：页面外层滚动区的可见宽度比布局宽度窄，
            // 右对齐的按钮会被裁到看不见（旅人页的「撤下」就是这么消失的）。
            GUILayout.Space(8f);
            if (Btn("刷新", 46f, PanelTheme.Mini, 20f)) ResetBuffers();
            GUILayout.EndHorizontal();

            Divider();

            var entries = CampCoinService.Collect(_showAllCoins);
            string keyword = (_coinSearch ?? string.Empty).Trim();

            _coinScroll = BeginList(_coinScroll, 232f);
            int shown = 0;
            foreach (var entry in entries)
            {
                if (keyword.Length > 0
                    && entry.Name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) < 0
                    && !entry.Id.ToString().Contains(keyword))
                    continue;

                CoinRow(entry);
                shown++;
            }

            if (shown == 0)
                Hint(entries.Count == 0 ? "没有可显示的营地币：先玩到解锁第一种资源。" : "没有匹配的条目。");

            EndList();
            Hint("带 * 的是「已解锁但还没获得过」，值为 0；点「应用」或「满」会自动补条目。");
            EndCard();

            BeginCard("批量");
            GUILayout.BeginHorizontal();
            if (Btn("全部 +1万", 84f)) BatchAdd(10000);
            if (Btn("全部拉满", 84f, PanelTheme.ButtonPrimary)) BatchSet(Plugin.MaxValue.Value);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Hint("只影响上表里已经出现的营地币；未解锁的币不会被凭空解锁。");
            EndCard();
        }

        private void CoinRow(CampCoinEntry entry)
        {
            GUILayout.BeginHorizontal();

            string label = entry.HasEntry ? entry.Name : entry.Name + " *";
            Color saved = GUI.contentColor;
            if (!entry.HasEntry) GUI.contentColor = PanelTheme.TxtWarn;
            GUILayout.Label(label, PanelTheme.Sub, GUILayout.Width(96f));
            GUI.contentColor = saved;

            GUILayout.Label(entry.Value.ToString("N0"), PanelTheme.Value, GUILayout.Width(84f));

            string control = "coin_" + entry.Id;
            if (GUI.GetNameOfFocusedControl() != control)
                _coinBuf[entry.Id] = entry.Value.ToString();

            GUI.SetNextControlName(control);
            _coinBuf.TryGetValue(entry.Id, out var buffer);
            string typed = GUILayout.TextField(buffer ?? string.Empty, PanelTheme.Field,
                GUILayout.Width(88f), GUILayout.Height(20f));
            _coinBuf[entry.Id] = typed;

            if (Btn("应用", 42f, PanelTheme.Mini, 20f))
            {
                if (int.TryParse(typed, out var value))
                {
                    CampCoinService.Set(entry.Id, value);
                    Notify($"{entry.Name} 已设为 {value:N0}");
                }
                else
                {
                    Notify("请输入整数。");
                }
            }

            if (Btn("+1万", 46f, PanelTheme.Mini, 20f))
            {
                CampCoinService.Add(entry.Id, 10000);
                Notify($"{entry.Name} +10,000");
            }

            if (Btn("满", 28f, PanelTheme.Mini, 20f))
            {
                CampCoinService.Set(entry.Id, Plugin.MaxValue.Value);
                Notify($"{entry.Name} 已拉满");
            }

            GUILayout.EndHorizontal();
        }

        private void BatchAdd(int delta)
        {
            var entries = CampCoinService.Collect(_showAllCoins);
            int n = 0;
            foreach (var entry in entries)
            {
                if (CampCoinService.Add(entry.Id, delta)) n++;
            }

            ResetBuffers();
            Notify(n > 0 ? $"已为 {n} 项营地币各加 {delta:N0}" : "没有可修改的营地币。");
        }

        private void BatchSet(int value)
        {
            var entries = CampCoinService.Collect(_showAllCoins);
            int n = 0;
            foreach (var entry in entries)
            {
                if (CampCoinService.Set(entry.Id, value)) n++;
            }

            ResetBuffers();
            Notify(n > 0 ? $"已把 {n} 项营地币设为 {value:N0}" : "没有可修改的营地币。");
        }
    }
}
