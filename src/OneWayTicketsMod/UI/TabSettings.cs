// =============================================================================
// TabSettings.cs — 「设置」页签：快捷键、缩放、字体、目标值、诊断
// =============================================================================

using OneWayTicketsMod.Game;
using UnityEngine;
using static OneWayTicketsMod.UI.PanelWidgets;

namespace OneWayTicketsMod.UI
{
    internal sealed partial class MainPanel
    {
        private void DrawSettingsTab()
        {
            BeginCard("按键");
            KeyBindRow("打开 / 关闭面板", Plugin.ToggleKey.Value,
                key => Plugin.ToggleKey.Value = key, Plugin.ToggleKey,
                "点一下右边的按钮，然后按下想用的键；按 Esc 取消。");
            EndCard();

            BeginCard("数值");
            GUILayout.BeginHorizontal();
            Caption("满资源目标值", 96f);
            string control = "max_value";
            if (GUI.GetNameOfFocusedControl() != control) _maxValueBuf = Plugin.MaxValue.Value.ToString();
            GUI.SetNextControlName(control);
            _maxValueBuf = GUILayout.TextField(_maxValueBuf ?? string.Empty, PanelTheme.Field,
                GUILayout.Width(96f), GUILayout.Height(20f));
            if (Btn("应用", 42f, PanelTheme.Mini, 20f))
            {
                if (int.TryParse(_maxValueBuf, out var value) && value > 0)
                {
                    Plugin.MaxValue.Value = value;
                    Notify($"目标值 → {value:N0}");
                }
                else
                {
                    Notify("请输入正整数。");
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Hint("所有「满」按钮和一键满资源都用这个数值。");
            EndCard();

            BeginCard("显示");
            bool font = ToggleChip(Plugin.UseSystemFont.Value, "使用系统中文字体");
            if (font != Plugin.UseSystemFont.Value)
            {
                Plugin.UseSystemFont.Value = font;
                Notify(font ? "已切换为系统字体" : "已切回 Unity 内置字体");
            }
            Info($"当前字体：{PanelTheme.FontName}");
            Hint("面板缩放用左下角的 － / ＋ 调整，会自动记住。");
            EndCard();

            BeginCard("交互");
            bool guard = ToggleChip(Plugin.ClickGuard.Value, "面板打开时挡住游戏点击");
            if (guard != Plugin.ClickGuard.Value)
            {
                Plugin.ClickGuard.Value = guard;
                Notify(guard ? "已开启点击拦截" : "已关闭点击拦截（点面板会同时点到底下的游戏按钮）");
            }
            Hint("面板是 IMGUI 覆盖层，默认不会拦住游戏的 UI 点击。开启后会临时禁用游戏的 UI 输入模块。");
            Hint("如果发现开着面板时游戏 UI 卡住不动，把它关掉即可。");
            EndCard();

            BeginCard("工位显示");
            bool cubicleShow = ToggleChip(Plugin.CubicleAlwaysShow.Value, "工位格子常显（进战斗后新设备也能放人）");
            if (cubicleShow != Plugin.CubicleAlwaysShow.Value)
            {
                Plugin.CubicleAlwaysShow.Value = cubicleShow;
                Notify(cubicleShow ? "已开启：工位格子会一直显示" : "已关闭：恢复游戏原本的解锁逻辑", 3.5f);
            }
            Info(CubicleViewService.Diagnose());
            GUILayout.BeginHorizontal();
            if (Btn("立即修复", 66f, PanelTheme.Mini, 20f))
                Notify(CubicleViewService.RepairNow(), 4f);
            if (Btn("一键上工位", 80f, PanelTheme.Mini, 20f))
                Notify(CubicleViewService.FillAll(), 5f);
            if (Btn("写诊断到日志", 88f, PanelTheme.Mini, 20f))
                Notify(CubicleViewService.Dump(), 4f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Hint("「工位格子」要能放人得同时满足三层：设备有工位数据 → 格子和工位绑上 → 格子是激活的。");
            Hint("任何一层断了，表现都是「空占位框 / 点上去没反应」。开启后每秒把三层都补齐一遍。");
            Hint("「一键上工位」走的是和手点格子完全相同的入口：它能把人安排上、手点不行 → 问题在点击层；");
            Hint("它也安排不上 → 问题在数据层。无论哪种，点「写诊断到日志」后把 LogOutput.log 里 [工位诊断] 的行发我。");
            EndCard();

            BeginCard("诊断");
            KV("Mod 版本", $"{Plugin.PluginName} v{Plugin.Version}", 110f);
            KV("CommonContext", CampCoinService.Ready ? "已就绪" : "未创建（还没进营地）", 110f);
            KV("Mission", SingletonAccess.Get<Foundations.Mission>() != null ? "已就绪" : "未创建（不在局内）", 110f);
            KV("BackPackMgr", WorldService.BackPack != null ? "已就绪" : "不在局内", 110f);
            KV("CharacterMgr", TeamService.Ready ? "已就绪" : "不在局内", 110f);
            KV("DeviceMgr", DeviceGuardService.Ready ? "已就绪" : "不在局内", 110f);
            KV("WorldContext", WorldService.Ctx != null ? "已就绪" : "不在局内", 110f);
            KV("无限耐久", WorldService.InfiniteDurability ? "开启中" : "关闭", 110f);
            KV("不会晕车", CharacterGuardService.NoComaEnabled ? "开启中" : "关闭", 110f);
            KV("设备免疫异常", DeviceGuardService.ImmuneEnabled ? "开启中" : "关闭", 110f);
            KV("车速锁定", CaravanSpeedService.LockEnabled
                ? $"开启中（{CaravanSpeedService.LockTarget:0.0}）"
                : "关闭", 110f);
            Hint("全部显示「未就绪」时，先确认游戏已完全进入（营地或探索场景）。");
            EndCard();
        }

        private string _maxValueBuf = string.Empty;
    }
}
