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
//   · Decompiled/World/World.Devices/Device.cs:279-290
//     EnterSafeArea 会把 device_emergency 通知给各 View 移除并把 state 复位成 normal，
//     正好可以拿来当「一键清除异常」。
//
// 说明：本开关只免疫「异常状态」，普通掉血仍然走 Device.Hurt（要免掉血请用无限耐久）。
// =============================================================================

using System.Collections.Generic;
using System.Text;
using Commons;
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

        public bool Abnormal => State != Device.DeviceState.normal;
    }

    internal static class DeviceGuardService
    {
        /// <summary>「设备免疫异常状态」总开关，Harmony 补丁每次都读它。</summary>
        public static bool ImmuneEnabled;

        public static DeviceMgr Mgr => SingletonAccess.Mgr<DeviceMgr>("DeviceMgr");

        public static bool Ready => Mgr != null;

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

        /// <summary>处于异常状态的设备数量。</summary>
        public static int AbnormalCount()
        {
            int n = 0;
            foreach (var e in Collect())
                if (e.Abnormal) n++;
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
