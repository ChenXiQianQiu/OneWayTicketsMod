// =============================================================================
// SingletonAccess.cs — 安全读取游戏里的「单例」
//
// 游戏的 Foundations.Singleton<T> 把实例放在私有静态字段 m_instance 里，
// 而它的 instance 属性在字段为空时会 new 一个出来。
// Mod 里如果在游戏还没初始化时访问 instance，就会凭空造一个「假单例」，
// 后续游戏自己再赋值时容易出现两个实例互相看不见的情况。
// 所以这里统一走反射直接读字段：为空就返回 null，让上层显示「未就绪」。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using Foundations;

namespace OneWayTicketsMod.Game
{
    internal static class SingletonAccess
    {
        private static readonly Dictionary<Type, FieldInfo> FieldCache = new Dictionary<Type, FieldInfo>();

        /// <summary>读取 Singleton&lt;T&gt;.m_instance，不做任何创建。为空返回 null。</summary>
        public static T Get<T>() where T : class, new()
        {
            try
            {
                var type = typeof(T);
                if (!FieldCache.TryGetValue(type, out var field))
                {
                    field = typeof(Singleton<>).MakeGenericType(type)
                        .GetField("m_instance", BindingFlags.Static | BindingFlags.NonPublic);
                    FieldCache[type] = field;
                }

                return field?.GetValue(null) as T;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>局内（探索中）通过 Mission 挂载的 Manager，例如 BackPack / CharacterMgr。</summary>
        public static T Mgr<T>(string name) where T : class, IMgr
        {
            try
            {
                var mission = Get<Mission>();
                if (mission == null) return null;
                return mission.try_get_mgr<T>(name, out var mgr) ? mgr : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
