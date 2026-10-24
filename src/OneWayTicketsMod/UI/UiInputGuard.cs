// =============================================================================
// UiInputGuard.cs — 面板打开时挡住游戏 UI 的点击
//
// IMGUI 画在所有 Canvas 之上，但它不参与游戏的 EventSystem，
// 所以点面板上的按钮时，游戏里的 UGUI 按钮会「同时」被点到（点击穿透）。
// 本作是 UGUI + EventSystem 结构，因此面板可见期间把 EventSystem 上的
// BaseInputModule 临时禁用，关闭时原样恢复即可。
//
// 只影响 UI 事件；游戏自己直接读 InputSystem 的操作（例如按住屏幕驱动小车）
// 不受此开关控制。
// =============================================================================

using UnityEngine;
using UnityEngine.EventSystems;

namespace OneWayTicketsMod.UI
{
    internal static class UiInputGuard
    {
        private static BaseInputModule _disabled;

        /// <summary>每帧调用：面板可见就禁用游戏输入模块，不可见就恢复。</summary>
        public static void Sync(bool panelVisible, bool guardEnabled)
        {
            var system = EventSystem.current;
            var module = system != null ? system.GetComponent<BaseInputModule>() : null;

            if (panelVisible && guardEnabled)
            {
                // 切场景后 EventSystem 会换新实例，旧引用要还回去再拦新的
                if (_disabled != null && _disabled != module) Restore();

                if (module != null && module.enabled)
                {
                    module.enabled = false;
                    _disabled = module;
                }
            }
            else if (_disabled != null)
            {
                Restore();
            }
        }

        /// <summary>Mod 卸载时也要还回去，否则游戏 UI 会一直点不动。</summary>
        public static void Restore()
        {
            if (_disabled == null) return;
            try
            {
                _disabled.enabled = true;
            }
            catch
            {
                // 对象已随场景销毁，忽略
            }

            _disabled = null;
        }
    }
}
