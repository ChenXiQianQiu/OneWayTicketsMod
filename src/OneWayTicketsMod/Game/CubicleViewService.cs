// =============================================================================
// CubicleViewService.cs — 工位格子常显（修「进战斗后工位消失、放不了人」）
//
// 反编译依据：
//   · Decompiled/World/World.Devices.DeviceUiViews/DeviceUiView.cs:60-66
//       if (!Singleton<BattleContext>.instance.active_cubicle)
//           foreach (CubicleUiView station in stations) station.gameObject.SetActive(false);
//     设备 UI 视图 attach 时，只要 active_cubicle 是 false，就把所有工位格子关掉。
//   · Decompiled/World/World.Techs/Tech_Unlock_Cubicle.cs:8-13
//       public static void @do(string _num)
//       {
//           Singleton<BattleContext>.instance.active_cubicle = true;   // ← 唯一的置 true 点
//           ...try_get_mgr("DeviceMgr", out DeviceMgr mgr);
//           mgr.SetCubicle(hide: false);                               // ← 把格子显示出来
//       }
//     也就是说「工位格子」本来是靠科技解锁才出现的，而且只在解锁那一刻执行一次。
//   · Decompiled/World/World/BattleContext.cs:351 / :431
//       Init() → reset_context() → active_cubicle = false;
//     每次场景初始化（WorldSceneRoot.cs:129 调 Init）都会把这个标志清掉，
//     而科技不会再执行一遍。
//
// 于是就有了这个 bug 链条：
//   1. 解锁工位科技 → active_cubicle = true，格子显示；
//   2. 换场景 / 进战斗 → BattleContext.Init() 把 active_cubicle 清成 false；
//   3. 此后「新建」的设备视图（战斗中装上/换上的设备，比如转轮机枪）attach 时，
//      因为 active_cubicle 是 false，工位格子直接被 SetActive(false) 关掉；
//      而早就在场上的设备视图是之前建的，格子还开着 —— 所以只有新设备「工位消失」。
//   4. 玩家看到的就是：这台设备的工位变成空的占位框，点也没反应（站位对象没激活，
//      收不到点击），也就是「不让放」。
//
// 修法：把科技那两行按秒重新执行一遍（默认开启，可在面板「设置」里关）：
//   active_cubicle = true; DeviceMgr.SetCubicle(hide: false);
// 只影响「格子显不显示」，不碰任何战斗数值；设备数量变化时也会补一次，
// 因为新设备的视图可能是在两次轮询之间创建的。
// =============================================================================

using System.Text;
using UnityEngine;
using World;
using World.Devices;
using World.Devices.DeviceUiViews;

namespace OneWayTicketsMod.Game
{
    internal static class CubicleViewService
    {
        /// <summary>「工位格子常显」总开关。</summary>
        public static bool ForceVisible;

        private static float _next;
        private static int _lastDeviceCount = -1;
        private static bool _loggedOff;
        private static int _repairCount;

        public static DeviceMgr Mgr => SingletonAccess.Mgr<DeviceMgr>("DeviceMgr");

        public static bool Ready => Mgr != null;

        /// <summary>每帧调用（内部 1 秒节流）。</summary>
        public static void Tick()
        {
            if (!ForceVisible) return;

            float now = Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + 1f;

            var mgr = Mgr;
            if (mgr == null) return;

            var ctx = SingletonAccess.Get<BattleContext>();
            bool flagOff = ctx != null && !ctx.active_cubicle;

            if (flagOff)
            {
                // 和 Tech_Unlock_Cubicle.@do 完全一样的两步
                ctx.active_cubicle = true;
                if (!_loggedOff)
                {
                    Plugin.LogInfo("工位格子常显：active_cubicle 被游戏清成 false（换场景 / 进战斗），已重新置 true。");
                    _loggedOff = true;
                }
            }
            else
            {
                _loggedOff = false;
            }

            int count = DeviceCount(mgr);
            // 设备数量变了说明有新装 / 换装的设备，它的视图可能刚建好又把格子关了
            if (flagOff || count != _lastDeviceCount)
            {
                ShowAll(mgr);
                _lastDeviceCount = count;
                _repairCount++;
            }
        }

        /// <summary>面板「立即修复」用：立刻执行一遍并返回说明。</summary>
        public static string RepairNow()
        {
            var mgr = Mgr;
            if (mgr == null) return "不在局内，读不到设备管理器。";

            var ctx = SingletonAccess.Get<BattleContext>();
            bool wasOff = ctx != null && !ctx.active_cubicle;
            if (ctx != null) ctx.active_cubicle = true;

            ShowAll(mgr);
            _lastDeviceCount = DeviceCount(mgr);
            _repairCount++;

            return wasOff
                ? "已重新打开工位显示（游戏把 active_cubicle 清成了 false）"
                : "已刷新所有设备的工位格子";
        }

        /// <summary>面板诊断行。</summary>
        public static string Diagnose()
        {
            var mgr = Mgr;
            if (mgr == null) return "不在局内";

            var ctx = SingletonAccess.Get<BattleContext>();
            int total = 0, shown = 0;
            foreach (var device in Devices(mgr))
            {
                foreach (var view in device.views)
                {
                    if (!(view is DeviceUiView ui) || ui.stations == null) continue;
                    foreach (var station in ui.stations)
                    {
                        if (station == null) continue;
                        total++;
                        if (station.gameObject.activeSelf) shown++;
                    }
                }
            }

            var sb = new StringBuilder();
            sb.Append("active_cubicle=").Append(ctx != null && ctx.active_cubicle ? "true" : "false");
            sb.Append(" · 设备 ").Append(DeviceCount(mgr)).Append(" 台");
            sb.Append(" · 站位 ").Append(shown).Append('/').Append(total).Append(" 显示");
            if (_repairCount > 0) sb.Append(" · 已补显示 ").Append(_repairCount).Append(" 次");
            return sb.ToString();
        }

        private static void ShowAll(DeviceMgr mgr)
        {
            try
            {
                // 等价于 Tech_Unlock_Cubicle 的第二行：对每台设备 notify_set_cubicle(false)
                mgr.SetCubicle(false);
            }
            catch (System.Exception ex)
            {
                Plugin.LogWarn("显示工位格子失败：" + ex.Message);
            }
        }

        private static int DeviceCount(DeviceMgr mgr)
        {
            int count = 0;
            if (mgr.slots_device == null) return 0;
            foreach (var slot in mgr.slots_device)
                if (slot?.slot_device != null) count++;
            return count;
        }

        private static System.Collections.Generic.IEnumerable<Device> Devices(DeviceMgr mgr)
        {
            if (mgr.slots_device == null) yield break;
            foreach (var slot in mgr.slots_device)
            {
                var device = slot?.slot_device;
                if (device != null) yield return device;
            }
        }
    }
}
