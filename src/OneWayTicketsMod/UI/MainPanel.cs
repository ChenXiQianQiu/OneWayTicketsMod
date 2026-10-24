// =============================================================================
// MainPanel.cs — 面板外壳（标题 / 页签 / 状态条 / 缩放）
//
// 页签实现按语义拆到同目录的 Tab*.cs（partial 类），本文件只放外壳与概览页。
// 所有样式都来自 PanelTheme，这里不出现 new GUIStyle。
// =============================================================================

using System.Collections.Generic;
using OneWayTicketsMod.Game;
using UnityEngine;
using static OneWayTicketsMod.UI.PanelWidgets;

namespace OneWayTicketsMod.UI
{
    internal sealed partial class MainPanel : ModPanelBase
    {
        protected override string Title => $"再再启程 资源编辑 v{Plugin.Version}";
        protected override string TitleNote => "（免费分享，禁止盗卖）";
        protected override string[] TabLabels => new[] { "概览", "营地资源", "局内", "旅人", "设备", "设置" };
        protected override int WindowId => 61701;

        protected override float DefaultWidth => 740f;
        protected override float DefaultHeight => 720f;
        protected override float MinWidth => 600f;
        protected override float MaxWidth => 1100f;
        protected override float MinHeight => 360f;
        protected override float MaxHeight => 1000f;

        protected override float LoadScale() => Plugin.PanelScale.Value;
        protected override void SaveScale(float value) => Plugin.PanelScale.Value = value;

        // ---------------------------------------------------------------- 输入缓冲

        private readonly Dictionary<int, string> _coinBuf = new Dictionary<int, string>();
        private string _missionCoinBuf = string.Empty;
        private string _hpBuf = string.Empty;
        private string _hpMaxBuf = string.Empty;
        private string _speedBuf = string.Empty;
        private string _coinSearch = string.Empty;

        private bool _showAllCoins;
        private Vector2 _coinScroll;
        private Vector2 _teamScroll;
        private Vector2 _partnerScroll;
        private Vector2 _deviceScroll;

        /// <summary>把所有输入框恢复成游戏当前值。</summary>
        private void ResetBuffers()
        {
            _coinBuf.Clear();
            _missionCoinBuf = string.Empty;
            _hpBuf = string.Empty;
            _hpMaxBuf = string.Empty;
            _speedBuf = string.Empty;
        }

        protected override void OnOpen() => ResetBuffers();

        protected override void DrawTab(int index)
        {
            switch (index)
            {
                case 0: DrawOverview(); break;
                case 1: DrawCampTab(); break;
                case 2: DrawWorldTab(); break;
                case 3: DrawTeamTab(); break;
                case 4: DrawDeviceTab(); break;
                case 5: DrawSettingsTab(); break;
            }
        }

        // ---------------------------------------------------------------- 状态条

        protected override void DrawStatusStrip()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(2f);

            var coins = CampCoinService.Collect(false);
            int owned = 0;
            foreach (var c in coins) if (c.HasEntry) owned++;

            StatusChip("营地币", CampCoinService.Ready ? $"{owned} 项" : "未就绪");
            StatusChip("局内金币", WorldService.TryGetCoin(out var coin) ? coin.ToString() : "—");
            StatusChip("车辆耐久", WorldService.TryGetCaravan(out var hp, out var hpMax) ? $"{hp}/{hpMax}" : "—");
            StatusChip("车速", CaravanSpeedService.TryGetSpeed(out var speed) ? speed.ToString("0.0") : "—");
            StatusChip("旅人", TeamService.Ready ? $"{TeamService.FrontCount} + {TeamService.BackupCount}" : "—");

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }

        private static void StatusChip(string label, string value)
        {
            GUILayout.Label(label, PanelTheme.Hint, GUILayout.Width(54f));
            GUILayout.Label(value, PanelTheme.Sub, GUILayout.Width(80f));
        }

        /// <summary>
        /// 胶囊式开关：左边一颗「已开启 / 已关闭」按钮，右边是说明文字。
        /// 比 GUI.skin.toggle 那个小方框更醒目，也更贴合本主题。
        /// </summary>
        private static bool ToggleChip(bool value, string label)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(value ? "已开启" : "已关闭",
                    value ? PanelTheme.ChipOn : PanelTheme.Chip,
                    GUILayout.Width(64f), GUILayout.Height(20f)))
                value = !value;
            GUILayout.Label(label, PanelTheme.Sub);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return value;
        }

        protected override void DrawFooterHint()
        {
            string key = Plugin.ToggleKey.Value == KeyCode.None ? "未绑定" : Plugin.ToggleKey.Value.ToString();
            GUILayout.Label($"按 {key} 开关面板 · 左下角可调缩放 · 右下角可拖拽改尺寸", PanelTheme.Hint);
        }

        // ---------------------------------------------------------------- 概览

        private void DrawOverview()
        {
            BeginCard("常用操作");
            GUILayout.BeginHorizontal();
            if (Btn("一键满资源", 116f, PanelTheme.ButtonPrimary, 24f)) DoMaxAll();
            if (Btn("补建未获得的营地币", 152f, PanelTheme.Button, 24f)) DoFillMissing();
            if (Btn("解锁全部营地币", 118f, PanelTheme.Button, 24f)) DoUnlockAll();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Hint("「一键满资源」= 已解锁的营地币全部拉满 + 局内金币拉满 + 车辆回满。局外数值在回营地/存档时落地。");
            Hint("「补建」用于秘能这类「已解锁但还没获得过」的币：先补一条 0，之后才能在营地里显示和修改。");
            EndCard();

            BeginCard("当前状态");
            KV("所在场景", WorldService.InWorld ? "局内（探索中）" : "局外（营地 / 菜单）", 88f);
            KV("营地币条目", CampCoinService.Ready
                ? $"{CampCoinService.Collect(false).Count} 项（含未解锁）"
                : "未就绪：还没进过营地", 88f);
            KV("局内金币", WorldService.TryGetCoin(out var coin) ? coin.ToString("N0") : "不在局内", 88f);
            KV("车辆耐久", WorldService.TryGetCaravan(out var hp, out var hpMax)
                ? $"{hp:N0} / {hpMax:N0}"
                : "不在局内", 88f);
            KV("车速", CaravanSpeedService.TryGetSpeed(out var speed)
                ? $"{speed:0.0}"
                : "不在局内", 88f);
            KV("旅人", TeamService.Ready
                ? $"操作中 {TeamService.FrontCount} 人 · 休息区 {TeamService.BackupCount} 人"
                : "不在局内", 88f);
            KV("昏迷旅人", TeamService.Ready ? $"{CharacterGuardService.ComaCount()} 人" : "不在局内", 88f);
            KV("异常设备", DeviceGuardService.Ready ? $"{DeviceGuardService.AbnormalCount()} 台" : "不在局内", 88f);
            Hint("数值实时读取自游戏对象，不写死地址，重开游戏也不会失效。");
            EndCard();
        }

        // ---------------------------------------------------------------- 操作实现

        private void DoMaxAll()
        {
            int target = Plugin.MaxValue.Value;
            int coins = CampCoinService.FillAll(target);
            bool coinOk = WorldService.SetCoin(target);
            bool repair = WorldService.RepairFull();

            if (coins == 0 && !coinOk && !repair)
            {
                Notify("没找到可修改的对象：先进入营地或开始一局探索。", 4f);
                return;
            }

            ResetBuffers();
            Notify($"已处理：营地币 {coins} 项 → {target:N0}"
                   + (coinOk ? "，局内金币已拉满" : "，局内金币不在场")
                   + (repair ? "，车辆已回满" : ""), 4.5f);
        }

        private void DoFillMissing()
        {
            int n = CampCoinService.FillMissing();
            ResetBuffers();
            Notify(n > 0 ? $"已补建 {n} 项未获得的营地币（值为 0）" : "没有需要补建的营地币。", 4f);
        }

        private void DoUnlockAll()
        {
            int n = CampCoinService.UnlockAll();
            ResetBuffers();
            Notify(n > 0 ? $"已解锁 {n} 项营地币" : "没有新的营地币可解锁（或数据表未就绪）。", 4f);
        }
    }
}
