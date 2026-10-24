// =============================================================================
// DeviceOutlierPatch.cs — 局内设备免疫异常状态（燃烧 / 机械故障 / 腐蚀 / 缠绕 / 潮湿）
//
// 反编译依据（Decompiled/World/World.Devices/Device.cs:795）：
//   void ITarget.applied_outlier(ITarget source, string outlier_type, int value)
//   是设备进入 fired / stupor / acid / bind / wet 的唯一入口，
//   调用方遍布所有伤害流程（Enemy_Hurt_Helper、各 Projectile、Weather、AdherentMgr…）。
//
// 注意：这是「显式接口实现」，编译后的方法名是 "World.ITarget.applied_outlier"，
//   名字里带命名空间点号，直接写字符串常量很脆（改个命名空间就失效）。
//   所以这里用 TargetMethod() 按后缀反射查找，稳定且不依赖名字拼写。
//
// 做法：前置补丁返回 false 跳过原方法 → 设备永远停在 normal 状态。
//   只免疫「异常状态」，普通掉血仍走 Device.Hurt（要免掉血请用无限耐久）。
//
// ★ v0.2.2 起本补丁只是「保留」：本作的 Harmony 补丁实测不执行
//   （见 docs/模块说明-旅人与工位.md 4.1），实际免疫由
//   DeviceGuardService.Tick 每 0.2 秒把 state 钉在 immue 实现
//   （applied_outlier 的第一行就是 `if (state == immue) return;`）。
//   这里留着是为了万一哪天补丁恢复可用，判定会更干净（连状态都不进）。
// =============================================================================

using System;
using System.Reflection;
using HarmonyLib;
using OneWayTicketsMod.Game;
using World.Devices;

namespace OneWayTicketsMod.Patches
{
    [HarmonyPatch]
    internal static class DeviceOutlierPatch
    {
        private static MethodBase TargetMethod()
        {
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(Device)))
            {
                if (method.Name.EndsWith("applied_outlier", StringComparison.Ordinal))
                    return method;
            }
            return null;
        }

        [HarmonyPrefix]
        private static bool Prefix() => !DeviceGuardService.ImmuneEnabled;
    }
}
