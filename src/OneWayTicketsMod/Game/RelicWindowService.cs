// =============================================================================
// RelicWindowService.cs — 隐藏安全点的「奇物表」
//
// 反编译依据：
//   1. Decompiled/World/World.SafeArea/SafeAreaView.cs:57
//        public RelicMgrWindowView relic_window;
//      安全点（撤离界面）上那面奇物墙就是它。
//   2. Decompiled/World/World.Relic/RelicMgrWindowView.cs:53-60
//        UpdateWindowPosByRelicCount() 只按 relic_list.Count 做「滑进来 / 滑出去」的位移动画，
//        整个窗口纯展示，不参与战斗、存档、结算任何一条逻辑。
//   3. Decompiled/World/World.Relic/RelicMgrView.cs:22-24、92-107
//        init_row = 3、row_cnt = 7，ins_relic() 按 ceil(奇物数 / 7) 行生成格子。
//        也就是说奇物越多，这张表越长 —— 奇物攒到几十个时，
//        它会从屏幕左边一路铺到屏幕顶，把安全点的背景和按钮全挡住。
//   4. Decompiled/World/World.Relic/RelicView.cs:56-76
//        格子只有 OnPointerEnter 才弹说明，被隐藏后不会留下任何残留 UI。
//
// 做法：把 RelicMgrWindowView 所在的 GameObject SetActive(false)。
//   · 只动显示层，不碰 RelicMgr.relic_list，奇物本身的加成一点不受影响；
//   · 换场景时游戏会重建窗口，所以每 0.5 秒补一次「又冒出来就再关掉」；
//   · 关掉开关立刻恢复显示（会记下自己关过哪些对象，只恢复自己关的那几个）。
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using World.Relic;

namespace OneWayTicketsMod.Game
{
    internal static class RelicWindowService
    {
        /// <summary>「隐藏奇物表」总开关（面板「设置」页可关）。</summary>
        public static bool Hidden;

        /// <summary>自己关掉的窗口对象，关开关时逐个恢复（游戏销毁掉的会自动跳过）。</summary>
        private static readonly List<GameObject> HiddenObjects = new List<GameObject>();

        private static float _next;

        /// <summary>最近一次扫描在当前场景里找到的奇物窗口数量。</summary>
        private static int _found;

        /// <summary>累计关掉过多少次（换场景重建后再次关掉也会 +1），面板上做提示用。</summary>
        private static int _hideCount;

        // ------------------------------------------------------------ 每帧轮询

        public static void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + 0.5f;

            if (!Hidden)
            {
                if (HiddenObjects.Count > 0) Restore();
                return;
            }

            Apply();
        }

        /// <summary>面板切换开关时调用：立刻生效，不用等下一轮轮询。</summary>
        public static void SetHidden(bool hidden)
        {
            Hidden = hidden;
            _next = 0f;

            if (hidden) Apply();
            else Restore();
        }

        // ------------------------------------------------------------ 隐藏 / 恢复

        private static void Apply()
        {
            try
            {
                var windows = FindLive<RelicMgrWindowView>();
                _found = windows.Count;

                foreach (var window in windows)
                    Hide(window.gameObject);

                // 兜底：万一某个版本里格子不是挂在窗口下面，把它也一起收掉，
                // 否则只关窗口会留下一条没有背景的格子带。
                if (windows.Count > 0)
                {
                    foreach (var grid in FindLive<RelicMgrView>())
                    {
                        if (grid == null) continue;
                        if (UnderAny(grid.transform, windows)) continue;
                        Hide(grid.gameObject);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.LogWarn("隐藏奇物表时出错：" + ex.Message);
            }
        }

        private static void Hide(GameObject go)
        {
            if (go == null) return;

            // 保险：万一某个版本把奇物窗口和安全点根视图挂在同一个对象上，
            // 关掉它会把整张撤离界面（撤离按钮 / 金币 / 选路）一起关掉 —— 那种情况宁可不生效。
            if (go.GetComponent<World.SafeArea.SafeAreaView>() != null) return;

            if (go.activeSelf)
            {
                go.SetActive(false);
                _hideCount++;
            }

            if (!HiddenObjects.Contains(go)) HiddenObjects.Add(go);
        }

        private static void Restore()
        {
            foreach (var go in HiddenObjects)
            {
                // Unity 的「假 null」：换场景后对象已被销毁，这里直接跳过
                if (go != null && !go.activeSelf) go.SetActive(true);
            }
            HiddenObjects.Clear();
        }

        // ------------------------------------------------------------ 工具

        /// <summary>
        /// 找出当前场景里活着的组件实例。
        /// 用 FindObjectsOfTypeAll 是因为窗口可能正处于 inactive 状态（我们关掉之后），
        /// 普通的 FindObjectsOfType 找不到它；同时要排掉 prefab 资源（scene 不合法）。
        /// </summary>
        private static List<T> FindLive<T>() where T : Component
        {
            var list = new List<T>();
            var all = Resources.FindObjectsOfTypeAll<T>();
            if (all == null) return list;

            foreach (var item in all)
            {
                if (item == null) continue;
                var go = item.gameObject;
                if (go == null) continue;
                if (!go.scene.IsValid() || !go.scene.isLoaded) continue;
                list.Add(item);
            }
            return list;
        }

        private static bool UnderAny(Transform target, List<RelicMgrWindowView> windows)
        {
            foreach (var window in windows)
            {
                if (window == null) continue;
                if (target == window.transform) return true;
                if (target.IsChildOf(window.transform)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ 面板文案

        public static string Diagnose()
        {
            if (!Hidden)
                return _found > 0
                    ? $"当前场景有 {_found} 个奇物窗口（开关关闭，正常显示）"
                    : "未开启（打开后进安全点会自动隐藏）";

            if (HiddenObjects.Count > 0)
                return $"已隐藏 {HiddenObjects.Count} 个奇物窗口（累计关闭 {_hideCount} 次）";
            return "当前场景没有奇物表（进安全点后会自动隐藏）";
        }
    }
}
