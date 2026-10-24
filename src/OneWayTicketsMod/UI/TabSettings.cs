// =============================================================================
// TabSettings.cs — 「设置」页签：快捷键、缩放、字体、奇物表显隐、目标值、诊断
//
// v0.4.1：删掉了「交互 → 面板打开时挡住游戏点击」（UiInputGuard）。
// 那个开关默认就是关的，没人开它，留着只占版面。
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
                "点右边按钮再按想要的键，Esc 取消。");
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
            Hint("所有「满」按钮都用这个数值。");
            EndCard();

            BeginCard("显示");
            bool font = ToggleChip(Plugin.UseSystemFont.Value, "使用系统中文字体");
            if (font != Plugin.UseSystemFont.Value)
            {
                Plugin.UseSystemFont.Value = font;
                Notify(font ? "已切换为系统字体" : "已切回 Unity 内置字体");
            }
            Info($"当前字体：{PanelTheme.FontName}");

            bool hideRelic = ToggleChip(Plugin.HideRelicWindow.Value, "隐藏安全点的奇物表");
            if (hideRelic != Plugin.HideRelicWindow.Value)
            {
                Plugin.HideRelicWindow.Value = hideRelic;
                Notify(hideRelic ? "已隐藏：奇物墙不再挡住安全点界面" : "已恢复显示奇物墙", 3.5f);
            }
            Info(RelicWindowService.Diagnose());
            Hint("只是把安全点边上那面奇物墙藏起来，奇物效果和数量都不受影响。");
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
            if (Btn("写诊断到日志", 88f, PanelTheme.Mini, 20f))
                Notify(CubicleViewService.Dump(), 4f);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Hint("格子点不开 / 点上去没反应时开这个，每秒自动补齐；排不上人就把日志里的 [工位诊断] 发我。");
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
            EndCard();
        }

        private string _maxValueBuf = string.Empty;
    }
}
