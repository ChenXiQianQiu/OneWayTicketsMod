// =============================================================================
// ScreenGuardService.cs — 局内黑屏卡死：诊断 + 自动解除
//
// 背景（用户报障）：
//   v0.2.2 之后「从战斗点前往安全点」时画面全黑，游戏仍在跑（对话/存档都在推进）。
//
// 反编译依据：
//   · Decompiled/World/World/WorldSceneRoot.cs:424-439
//     BlackScreen() / FadeBlackScreen() 只是给 black_screen（全屏 Image）起协程。
//   · Decompiled/World/World/WorldSceneRoot.cs:441-496
//     IBlackScreenCoroutine：alpha 0→1 → 清怪 / safe_area.Init() / 旅人设备进安全区 /
//     存档 → alpha 1→0 → black_screen.SetActive(false)。
//     ★ 这一整段是「协程」，中途任何一步抛异常（或对象被销毁）都会让协程直接终止，
//       黑幕就永久停在 alpha=1，而游戏逻辑（对话、AI、音频）照常跑 —— 与用户现象一致。
//     顺带 ban_esc 也会一直是 true（ESC 失效），玩家会以为游戏卡死。
//   · Decompiled/World/World/WorldSceneRoot.cs:511-526
//     IFadeBlackScreenCoroutine 用的是 WaitForSeconds（受 Time.timeScale 影响），
//     而进安全区的那条用的是 WaitForSecondsRealtime（不受影响）。
//   · Decompiled/World/World/WorldSceneRoot.cs:150-154
//     world_shadow 由 Request_Helper.delay_do(...,2,...) 关掉；Request 的倒计时
//     由 Ticker 驱动，而 Ticker.update 用的是 Time.deltaTime
//     （Decompiled/Foundations/Foundations.Tickers/Ticker.cs:78），
//     所以 Time.timeScale = 0 时这个倒计时不会走。
//
// 本服务做两件事：
//   1. 黑幕持续不透明超过 3 秒 → 往 BepInEx 日志打一份现场快照
//      （timeScale / 安全区标志 / 车速 / 旅人设备数量 / ban_esc / 世界遮罩…），
//      下次复现就能一眼看出卡在哪一步；
//   2. 超过 8 秒仍然黑 → 直接把遮罩收掉并解除 ban_esc，先把画面还给玩家。
//
// 只在局内（WorldSceneRoot 存在）生效；营地/加载界面不碰。
// =============================================================================

using System;
using System.Reflection;
using System.Text;
using Foundations;
using UnityEngine;
using World;
using World.Caravans;
using World.SafeArea;

namespace OneWayTicketsMod.Game
{
    internal static class ScreenGuardService
    {
        /// <summary>「黑屏守卫」总开关（默认开）。</summary>
        public static bool Enabled = true;

        /// <summary>黑幕不透明持续多久后开始记日志（秒，真实时间）。</summary>
        private const float WarnSeconds = 3f;

        /// <summary>持续多久后强制收掉黑幕（秒，真实时间）。</summary>
        private const float FixSeconds = 8f;

        /// <summary>不透明判定的 alpha 阈值。</summary>
        private const float OpaqueAlpha = 0.9f;

        private static float _checkInterval;
        private static float _nextCheck;

        private static float _blackSince = -1f;
        private static bool _warned;
        private static int _repeatLogs;
        private static float _nextRepeat;

        /// <summary>上一次自动解除的时间，用于面板显示。</summary>
        public static float LastFixAt { get; private set; } = -1f;

        /// <summary>自动解除过几次。</summary>
        public static int FixCount { get; private set; }

        public static WorldSceneRoot Root
        {
            get
            {
                try { return MonoBehaviourSingleton<WorldSceneRoot>.instance; }
                catch { return null; }
            }
        }

        private static FieldInfo _banEscField;
        private static bool _banEscChecked;

        private static FieldInfo BanEscField
        {
            get
            {
                if (_banEscChecked) return _banEscField;
                _banEscChecked = true;
                _banEscField = typeof(WorldSceneRoot).GetField("ban_esc",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                return _banEscField;
            }
        }

        /// <summary>当前黑幕是否「挂着且几乎不透明」。</summary>
        public static bool IsBlackScreenOpaque()
        {
            var root = Root;
            var img = root?.black_screen;
            if (img == null) return false;
            if (!img.gameObject.activeInHierarchy) return false;
            return img.color.a >= OpaqueAlpha;
        }

        /// <summary>黑幕已经挂了多久（秒）；没挂返回 0。</summary>
        public static float BlackSeconds =>
            _blackSince < 0f ? 0f : Mathf.Max(0f, Time.unscaledTime - _blackSince);

        /// <summary>每帧由宿主调用（内部 0.25 秒节流）。</summary>
        public static void Tick()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.25f;

            var root = Root;
            if (root == null)
            {
                // 离开局内：状态清零，避免下次进来带着旧计时
                Reset();
                return;
            }

            if (!Enabled) return;

            CheckBrokenTransform(root);

            if (!IsBlackScreenOpaque())
            {
                Reset();
                return;
            }

            if (_blackSince < 0f)
            {
                _blackSince = Time.unscaledTime;
                return;
            }

            float held = Time.unscaledTime - _blackSince;

            if (!_warned && held >= WarnSeconds)
            {
                _warned = true;
                _repeatLogs = 0;
                _nextRepeat = Time.unscaledTime + 5f;
                Plugin.LogWarn($"黑屏检测：黑幕已持续 {held:0.0} 秒不消失。{Snapshot()}");
            }
            else if (_warned && _repeatLogs < 4 && Time.unscaledTime >= _nextRepeat)
            {
                _repeatLogs++;
                _nextRepeat = Time.unscaledTime + 5f;
                Plugin.LogWarn($"黑屏检测（第 {_repeatLogs} 次复查，{held:0.0} 秒）：{Snapshot()}");
            }

            if (held >= FixSeconds)
            {
                ForceClear(held);
            }
        }

        /// <summary>
        /// 检查「位置 / 速度 / 相机」有没有被写成 NaN。
        /// Unity 里 NaN 坐标不会抛异常，只会让相机渲染出一片黑，
        /// 所以这类问题只能主动查。
        /// </summary>
        private static void CheckBrokenTransform(WorldSceneRoot root)
        {
            if (Time.unscaledTime < _nextBrokenCheck) return;
            _nextBrokenCheck = Time.unscaledTime + 1f;

            string detail = null;

            try
            {
                if (CaravanSpeedService.CheckBroken(out var d)) detail = d;
            }
            catch
            {
                // 忽略
            }

            if (detail == null)
            {
                try
                {
                    var cam = root.mainCamera;
                    if (cam != null)
                    {
                        var p = cam.transform.localPosition;
                        if (!IsFinite(p.x) || !IsFinite(p.y) || !IsFinite(p.z))
                            detail = $"相机 localPosition=({p.x}, {p.y}, {p.z})";
                    }
                }
                catch
                {
                    // 忽略
                }
            }

            if (detail == null)
            {
                _brokenReported = false;
                return;
            }

            // 已经确认坐标坏了：把速度归零，至少别让它继续算下去
            try
            {
                var ctx = WorldService.Ctx;
                if (ctx != null) ctx.caravan_velocity = Vector2.zero;
            }
            catch
            {
                // 忽略
            }

            if (_brokenReported) return;
            _brokenReported = true;
            Plugin.LogWarn($"坐标异常（画面全黑就是这个原因）：{detail}。{Snapshot()}");
        }

        private static float _nextBrokenCheck;
        private static bool _brokenReported;

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>把黑幕收掉并解除 ESC 锁定，让玩家至少能继续操作。</summary>
        private static void ForceClear(float held)
        {
            var root = Root;
            var img = root?.black_screen;
            if (img == null) return;

            try
            {
                var c = img.color;
                img.color = new Color(c.r, c.g, c.b, 0f);
                img.gameObject.SetActive(false);

                var ban = BanEscField;
                if (ban != null) ban.SetValue(root, false);

                // 黑幕是进安全区用的，卡住时安全区大概率已经初始化完了，
                // 把「淡出未完成」留下的标志清掉，免得下次进场景又触发一次淡出。
                try
                {
                    var ds = SingletonAccess.Get<Share_DS>();
                    ds?.remove("is_world_fade_black");
                }
                catch
                {
                    // 忽略
                }

                FixCount++;
                LastFixAt = Time.unscaledTime;
                Plugin.LogWarn($"黑屏检测：持续 {held:0.0} 秒仍未恢复，已自动收掉黑幕并解除 ESC 锁定（累计 {FixCount} 次）。");
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("黑屏检测：自动收黑幕失败：" + ex.Message);
            }

            Reset();
        }

        private static void Reset()
        {
            _blackSince = -1f;
            _warned = false;
            _repeatLogs = 0;
        }

        // ---------------------------------------------------------------- 现场快照

        /// <summary>把当时的关键状态压成一行，方便从日志里定位卡在哪一步。</summary>
        public static string Snapshot()
        {
            var sb = new StringBuilder(256);

            try
            {
                sb.Append("timeScale=").Append(Time.timeScale.ToString("0.##"));

                var ctx = WorldService.Ctx;
                if (ctx != null)
                {
                    sb.Append("，in_safe_area=").Append(ctx.in_safe_area);
                    sb.Append("，in_safe_area2=").Append(ctx.in_safe_area2);
                    sb.Append("，world_invincible=").Append(ctx.world_invincible);
                    sb.Append("，freeze_camera=").Append(ctx.freeze_camera);
                    sb.Append("，caravan_pos=(")
                      .Append(ctx.caravan_pos.x.ToString("0.##")).Append(',')
                      .Append(ctx.caravan_pos.y.ToString("0.##")).Append(')');
                    sb.Append("，caravan_velocity=(")
                      .Append(ctx.caravan_velocity.x.ToString("0.##")).Append(',')
                      .Append(ctx.caravan_velocity.y.ToString("0.##")).Append(')');
                }
                else
                {
                    sb.Append("，WorldContext=null");
                }

                try
                {
                    sb.Append("，move_type=").Append(CaravanMover.move_type);
                }
                catch
                {
                    // 忽略
                }

                var root = Root;
                if (root != null)
                {
                    try
                    {
                        var safe = root.safe_area;
                        sb.Append("，safe_area.active=")
                          .Append(safe != null && safe.gameObject.activeSelf);
                    }
                    catch
                    {
                        // 忽略
                    }

                    try
                    {
                        sb.Append("，world_shadow.active=")
                          .Append(root.world_shadow != null && root.world_shadow.activeSelf);
                    }
                    catch
                    {
                        // 忽略
                    }

                    try
                    {
                        var ban = BanEscField;
                        if (ban != null) sb.Append("，ban_esc=").Append(ban.GetValue(root));
                    }
                    catch
                    {
                        // 忽略
                    }
                }

                sb.Append("，旅人=").Append(CountCharacters());
                sb.Append("，设备异常=").Append(SafeAbnormal());

                try
                {
                    var battle = SingletonAccess.Get<BattleContext>();
                    if (battle != null)
                        sb.Append("，wheel_tractive_force_role=").Append(battle.wheel_tractive_force_role);
                }
                catch
                {
                    // 忽略
                }
            }
            catch (Exception ex)
            {
                sb.Append("（快照异常：").Append(ex.Message).Append('）');
            }

            return sb.ToString();
        }

        private static int CountCharacters()
        {
            try
            {
                int n = 0;
                foreach (var c in CharacterGuardService.All())
                    if (c != null) n++;
                return n;
            }
            catch
            {
                return -1;
            }
        }

        private static int SafeAbnormal()
        {
            try { return DeviceGuardService.AbnormalCount(); }
            catch { return -1; }
        }
    }
}
