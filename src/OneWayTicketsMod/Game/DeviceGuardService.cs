// =============================================================================
// DeviceGuardService.cs — 局内设备免疫「异常状态」
//
// 反编译依据：
//   · Decompiled/World/World.Devices/Device.cs:21-30
//     DeviceState 枚举：normal / immue / fired(燃烧) / stupor(机械故障) /
//                       acid(腐蚀) / bind(缠绕) / wet(潮湿)。
//   · Decompiled/World/World.Devices/Device.cs:795-960
//     ITarget.applied_outlier（显式接口实现）是这些状态的唯一入口，
//     outlier_type 为 "fire" / "blunt" / "acid" / "wrap" / "water"。
//   · Decompiled/World/World.Devices/Device.cs:276-289  EnterSafeArea()
//       if (device_emergency != null) { 通知各 View 移除异常图标; device_emergency = null; }
//       state = normal;
//     —— 这是游戏自己的「清干净」入口，本类就靠它。
//
// ⚠ v0.2.8 重大修正：不再把 state 钉在 immue
//
//   v0.2.2 ~ v0.2.7 的做法是把 `state` 钉在 `DeviceState.immue`，
//   因为 applied_outlier 第一行就是 `if (state == immue) return;`。
//   这在战斗里确实有效，但会**打断装备面板**，导致「设备的工位格子直接消失」：
//
//     Decompiled/World/World.Devices.DeviceUiViews/DeviceUiView.cs:67-69
//         if (owner.state != Device.DeviceState.normal)
//             show_emergency(owner.device_emergency);
//     Decompiled/World/World.Devices.DeviceUiViews/DeviceUiView.cs:233
//         de.init();          ← de 是 null 时直接 NullReferenceException，没有 null 判断
//
//   immue 状态下 device_emergency 恒为 null → 必然 NRE。而异常抛在：
//     EquipmentMgrView.OnEnable → init() → EquipSlot.init() → EquipSlot.set_panel()
//       → device.add_view(panel_view) → DeviceUiView.attach() → show_emergency(null)  ✗
//   set_panel() 里 add_view 之后才是「把工位格子打开」的代码：
//     Decompiled/World/World.Devices.Equip/EquipSlot.cs:169-173
//         device.cubicle_list[i].add_view(cub_list[i]);
//         cub_list[i].gameObject.SetActive(value: true);   ← 永远执行不到
//   而 EquipSlot.init() 开头刚刚把 cub_list 全部 SetActive(false)（同文件 191-194）。
//   → 装备面板里那台设备的工位格子被关掉后再也打不开，玩家看到的就是
//     「工位直接消失、点不了」，异常还会继续往上抛，把后面几个槽位的 init 一起废掉。
//
//   BepInEx 日志实证（v0.2.7 运行，安全区外打开装备面板）：
//     NullReferenceException
//       World.Devices.DeviceUiViews.DeviceUiView.show_emergency
//       World.Devices.DeviceUiViews.DeviceUiView.attach
//       Foundations.MVVM.Model`2[T,V].add_view
//       World.Devices.Equip.EquipSlot.set_panel
//       World.Devices.Equip.EquipSlot.init
//       World.Devices.Equip.EquipmentMgrView.OnEnable
//   同一局里进安全区后（DeviceGuard 会临时撒手）打开装备面板则正常，
//   工位的视图数从 3 变成 4 —— 正好证明是这一步被 NRE 掐断的。
//
// 所以改成「事后清除」而不是「事前钉住」：
//   每 0.1 秒扫一遍，只要设备有 device_emergency 或 state 不是 normal，
//   就调 EnterSafeArea() 把它清干净并复位成 normal。
//   效果上依旧是「设备不会有异常状态」（异常最多存在 0.1 秒），
//   但 state 永远是 normal，装备面板再也不会 NRE。
//
// 代价：异常图标可能闪一下（≤0.1 秒）。比起装备面板直接废掉，这个代价可以接受。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using Commons;
using UnityEngine;
using World.Devices;

namespace OneWayTicketsMod.Game
{
    internal sealed class DeviceEntry
    {
        public Device Device;
        public string Slot;
        public string Name;
        public int Hp;
        public int HpMax;
        public Device.DeviceState State;
        public bool Alive;

        /// <summary>immue 是游戏自己的「异常免疫中」缓冲状态，不算异常。</summary>
        public bool Abnormal => State != Device.DeviceState.normal && State != Device.DeviceState.immue;
    }

    internal static class DeviceGuardService
    {
        /// <summary>「设备免疫异常状态」总开关，轮询每次读它。</summary>
        public static bool ImmuneEnabled { get; private set; }

        /// <summary>累计清掉的异常次数（面板显示用）。</summary>
        public static int ClearedCount { get; private set; }

        /// <summary>轮询间隔：越短异常存在的时间越短，但别太密。</summary>
        private const float Interval = 0.1f;

        private static float _nextTick;

        public static DeviceMgr Mgr => SingletonAccess.Mgr<DeviceMgr>("DeviceMgr");

        public static bool Ready => Mgr != null;

        // ---------------------------------------------------------------- 开关

        public static void SetEnabled(bool value)
        {
            if (ImmuneEnabled == value) return;
            ImmuneEnabled = value;
            if (!value) RestoreAll();
        }

        /// <summary>
        /// 每帧调用（面板关着也跑）：把车上设备的异常清掉并复位成 normal。
        /// 注意这里**不再**把 state 钉在 immue —— 那会让装备面板的
        /// EquipSlot.set_panel 抛 NullReferenceException，工位格子直接打不开。
        /// </summary>
        public static void Tick()
        {
            if (!ImmuneEnabled) return;
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + Interval;

            var mgr = Mgr;
            if (mgr?.slots_device == null) return;

            foreach (var slot in mgr.slots_device)
            {
                var d = slot?.slot_device;
                if (d == null) continue;
                Guard(d);
            }
        }

        /// <summary>把一台设备拉回「正常」：清异常 + 复位状态（含通知视图移除图标）。</summary>
        private static void Guard(Device d)
        {
            try
            {
                bool hasEmergency = d.device_emergency != null;
                bool wrongState = d.state != Device.DeviceState.normal;
                if (!hasEmergency && !wrongState) return;

                if (hasEmergency) ClearedCount++;
                // EnterSafeArea 就是游戏自己的清理入口：通知各 View 移除异常图标、
                // 把 device_emergency 置空、state 复位成 normal。见 Device.cs:276-289。
                d.EnterSafeArea();
            }
            catch
            {
                // 单台设备出错不影响其它设备
            }
        }

        /// <summary>关掉开关时把设备状态还原成 normal。</summary>
        private static void RestoreAll()
        {
            var mgr = Mgr;
            if (mgr?.slots_device == null) return;

            foreach (var slot in mgr.slots_device)
            {
                var d = slot?.slot_device;
                if (d == null) continue;
                try { d.EnterSafeArea(); }
                catch { /* 忽略 */ }
            }
        }

        // ---------------------------------------------------------------- 查询

        /// <summary>车上所有挂了设备的槽位（含已损毁但槽位还在的）。</summary>
        public static List<DeviceEntry> Collect()
        {
            var list = new List<DeviceEntry>();
            var mgr = Mgr;
            if (mgr?.slots_device == null) return list;

            foreach (var slot in mgr.slots_device)
            {
                var d = slot?.slot_device;
                if (d == null) continue;

                list.Add(new DeviceEntry
                {
                    Device = d,
                    Slot = slot._name,
                    Name = Describe(d),
                    Hp = d.current_hp,
                    HpMax = d.max_hp,
                    State = d.state,
                    Alive = d.is_validate,
                });
            }
            return list;
        }

        /// <summary>处于异常状态的设备数量（immue 不算异常）。</summary>
        public static int AbnormalCount()
        {
            int n = 0;
            foreach (var e in Collect())
                if (e.Abnormal) n++;
            return n;
        }

        /// <summary>当前处于「免疫中」的设备数量（保留给旧面板文案用，现在基本恒为 0）。</summary>
        public static int ImmuneCount()
        {
            int n = 0;
            foreach (var e in Collect())
                if (e.State == Device.DeviceState.immue) n++;
            return n;
        }

        /// <summary>把车上的设备全部恢复正常状态（等价于进安全区时的那套清理）。</summary>
        public static int ClearAll()
        {
            int n = 0;
            foreach (var e in Collect())
            {
                if (e.State == Device.DeviceState.normal && e.Device.device_emergency == null) continue;
                e.Device.EnterSafeArea();
                n++;
            }
            return n;
        }

        // ---------------------------------------------------------------- 名称

        private static string Describe(Device d)
        {
            try
            {
                string key = d.desc?.name;
                if (string.IsNullOrEmpty(key)) return "设备";

                string localized = Localization_Utility.get_localization(key);
                if (string.IsNullOrEmpty(localized)) return StripTags(key);

                return StripTags(localized);
            }
            catch
            {
                return "设备";
            }
        }

        /// <summary>本地化串里带 &lt;sprite ...&gt; 之类的富文本，IMGUI 不解析，必须剥掉。</summary>
        private static string StripTags(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return text;

            var sb = new StringBuilder(text.Length);
            bool inside = false;
            foreach (char ch in text)
            {
                if (ch == '<') { inside = true; continue; }
                if (ch == '>') { inside = false; continue; }
                if (!inside) sb.Append(ch);
            }
            return sb.ToString().Trim();
        }
    }
}
