// =============================================================================
// DeviceGuardService.cs — 局内设备免疫「异常状态」
//
// 反编译依据：
//   · Decompiled/World/World.Devices/Device.cs:21-30
//     DeviceState 枚举：normal / immue / fired(燃烧) / stupor(机械故障) /
//                       acid(腐蚀) / bind(缠绕) / wet(潮湿)。
//   · Decompiled/World/World.Devices/Device.cs:795-960
//     ITarget.applied_outlier（显式接口实现）是这些状态的唯一入口，
//     outlier_type 为 "fire" / "blunt" / "acid" / "wrap" / "water"，
//     "sharp" / "flash" 目前是空分支。
//     ★ 该方法第一行就是 `if (state == DeviceState.immue) return;`
//       —— 所以只要把 state 钉在 immue，所有异常都进不来。
//   · Decompiled/World/World.Devices/Device.cs:392-402  CheckEmergency
//     每 tick 对 immue 状态做 `immu_countdown--`，减到 0 就回 normal，
//     所以要在轮询里把 immu_countdown 一起续上，否则只免疫 1 tick。
//   · Decompiled/World/World.Devices/Device.cs:279-290
//     EnterSafeArea 会把 device_emergency 通知给各 View 移除并把 state 复位成 normal，
//     正好可以拿来当「一键清除异常」。
//
// 为什么是「轮询」而不是 Harmony 前置补丁：
//   本作的 Harmony 补丁实测完全不执行（详见 docs/模块说明-旅人与工位.md 4.1），
//   所以 v0.2.2 起改成每 0.2 秒轮询钉状态，不再依赖补丁。
//
// 说明：本开关只免疫「异常状态」，普通掉血仍然走 Device.Hurt（要免掉血请用无限耐久）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
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

        /// <summary>钉住 immue 时写的倒计时（单位是 tick，120 tick ≈ 1 秒）。</summary>
        private const int ImmuTicks = 999999;

        private static FieldInfo _immuField;
        private static bool _immuFieldChecked;
        private static float _nextTick;

        public static DeviceMgr Mgr => SingletonAccess.Mgr<DeviceMgr>("DeviceMgr");

        public static bool Ready => Mgr != null;

        /// <summary>Device.immu_countdown 是私有字段，反射一次缓存起来。</summary>
        private static FieldInfo ImmuField
        {
            get
            {
                if (_immuFieldChecked) return _immuField;
                _immuFieldChecked = true;

                _immuField = typeof(Device).GetField("immu_countdown",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (_immuField == null)
                    Plugin.LogWarn("设备免疫：找不到 Device.immu_countdown，改为只做逐帧清除（异常会闪一下就消失）");

                return _immuField;
            }
        }

        // ---------------------------------------------------------------- 开关

        public static void SetEnabled(bool value)
        {
            if (ImmuneEnabled == value) return;
            ImmuneEnabled = value;
            if (!value) RestoreAll();
        }

        /// <summary>
        /// 每帧调用（面板关着也跑）：把车上所有设备的 state 钉在 immue，
        /// 这样 applied_outlier 第一行就 return，五种异常都进不来。
        /// 用 0.2 秒节流；immue 状态本身会持续到下一次写入，不会掉。
        /// </summary>
        public static void Tick()
        {
            if (!ImmuneEnabled) return;
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.2f;

            var mgr = Mgr;
            if (mgr?.slots_device == null) return;

            foreach (var slot in mgr.slots_device)
            {
                var d = slot?.slot_device;
                if (d == null) continue;
                Pin(d);
            }
        }

        private static void Pin(Device d)
        {
            try
            {
                // 先清掉已经挂上的异常（图标会一起消失），再钉免疫
                if (d.device_emergency != null) d.EnterSafeArea();

                if (d.state != Device.DeviceState.immue) d.state = Device.DeviceState.immue;

                // CheckEmergency 每 tick 会 immu_countdown--，不续上就只免疫 1 tick
                var field = ImmuField;
                if (field != null) field.SetValue(d, ImmuTicks);
            }
            catch (Exception ex)
            {
                // 单台设备出错不影响其它设备，但必须留痕：
                // Device.EnterSafeArea() 里会对每个 view 调 notify_remove_emergency，
                // 只要有一个 view 是已销毁的，就会抛 NRE。
                // 游戏自己的进安全区协程也会调同一个方法（WorldSceneRoot.cs:483-486），
                // 那边没有 try/catch —— 一旦它抛异常，协程直接终止，黑幕就永远停在 alpha=1。
                // 所以这里把异常记下来，方便和「进安全区黑屏」对上号。
                WarnDeviceOnce(d, ex);
            }
        }

        private static readonly HashSet<string> WarnedDevices = new HashSet<string>();

        private static void WarnDeviceOnce(Device d, Exception ex)
        {
            string key = (d?.desc?.name ?? "?") + "|" + ex.GetType().Name + "|" + ex.Message;
            if (!WarnedDevices.Add(key)) return;
            Plugin.LogWarn($"设备清理异常（{d?.desc?.name}）：{ex.GetType().Name} {ex.Message}");
        }

        /// <summary>关掉开关时把本 Mod 钉的 immue 状态还原成 normal。</summary>
        private static void RestoreAll()
        {
            var mgr = Mgr;
            if (mgr?.slots_device == null) return;

            var field = ImmuField;
            foreach (var slot in mgr.slots_device)
            {
                var d = slot?.slot_device;
                if (d == null) continue;
                try
                {
                    if (d.state != Device.DeviceState.immue) continue;
                    d.state = Device.DeviceState.normal;
                    field?.SetValue(d, 0);
                }
                catch
                {
                    // 忽略
                }
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

        /// <summary>当前处于「免疫中」的设备数量。</summary>
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
                if (!e.Abnormal) continue;
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
