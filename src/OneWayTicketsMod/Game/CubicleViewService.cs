// =============================================================================
// CubicleViewService.cs — 工位格子（显示 + 可放人）
//
// ⚠ v0.2.6 的教训（已实测证伪）：
//   v0.2.6 认为病因是 BattleContext.active_cubicle 被 BattleContext.Init() 清成 false，
//   于是每秒重放 Tech_Unlock_Cubicle 那两行。结果用户实测「还是不显示」，
//   而 BepInEx 日志里「active_cubicle 被游戏清成 false」一次都没出现
//   —— 说明 active_cubicle 一直是 true，这条路根本不是病因。
//   而且 mgr.SetCubicle(false) 会连带把「装备面板里的只读视图（only_read）」的格子也点亮，
//   那个视图是 EquipSlot.set_panel() 特意 HideStation() 掉的，点亮就是新 bug。
//
// 所以 v0.2.7 改成「不再猜，逐层硬修 + 写日志自证」。
//
// ⚠ v0.2.8 的补充结论（日志实证）：
//   用 Dump() 打日志后确认：世界里的 6 台设备、15 个格子 **全部 active=True、全部已绑工位、死格 0**
//   —— 也就是说 ①②③ 三层本来就没坏，v0.2.6 / v0.2.7 一直在修一个不存在的毛病。
//   真正坏掉的是「装备面板」那套 UI（EquipSlot.cub_list），它被
//   DeviceUiView.show_emergency 的 NullReferenceException 掐断在 EquipSlot.set_panel 里，
//   导致「把工位格子 SetActive(true)」那段永远执行不到。
//   根因是本 Mod 的「设备免疫异常状态」把 device.state 钉在 immue、而 device_emergency 是 null
//   —— 详见 Game/DeviceGuardService.cs 顶部注释，v0.2.8 已改为「事后清除」。
//   本类保留，因为它是 ①②③ 层的保险 + 唯一的诊断入口。
//
// 反编译依据（工位格子从「数据」到「能点」要过的四道关）：
//   1. 数据层 Decompiled/World/World.Devices/Device.cs:429-437 InitData()
//        if (rc.cubicles != null)
//            foreach (uint cubicle in rc.cubicles)
//                { var item = Cubicle_Helper.AddDeviceCubicle(this, cubicle); cubicle_list.Add(item); }
//      工位对象是按 device_all.cubicles 这个 id 列表建出来的，建不出来 cubicle_list 就是空的。
//   2. 绑定层 Decompiled/World/World.Devices.DeviceUiViews/DeviceUiView.cs:40-46 attach()
//        for (int i = 0; i < stations.Count; i++)
//            if (owner.cubicle_list.Count > i) owner.cubicle_list[i].add_view(stations[i]);
//      UI 上每个格子(CubicleUiView)必须和 cubicle_list[i] 绑上，否则格子是「死的」。
//   3. 显示层 同一个 attach() 的 :60-66（active_cubicle 判断）+ HideStation()
//   4. 点击层 Decompiled/World/World.Devices.DeviceUiViews/DeviceStationView.cs:41-53
//        PointerClick → Cubicle_Helper.SetCubicle(cubicle)
//      Decompiled/World/World.Helpers/Cubicle_Helper.cs:65-79
//        if (GetAllCharacters().Contains(selectCharacter) && ... && bc != null && ...)
//      cubicle 为 null（没绑定）时点击**静默失败**，什么都不会发生 —— 表现就是「不让放」。
//
// 本类的做法：每秒扫一遍所有设备的战斗视图，逐项补齐
//   a) cubicle_list 为空但 device_all.cubicles 有数据 → 按数据补建工位
//   b) 格子没绑工位 → 绑到 cubicle_list[i]
//   c) 格子被 SetActive(false) → 硬开
//   d) 实在没有工位数据可绑 → 把死格子藏起来（不显示假的格子骗人）
// 同时提供 Dump()：把每台设备 / 每个视图 / 每个格子的真实状态写进 BepInEx 日志，
// 下一次出问题直接看日志就知道卡在哪一层，不用再截图猜。
// =============================================================================

using System.Collections.Generic;
using System.Text;
using Commons;
using UnityEngine;
using World;
using World.Cubicles;
using World.Devices;
using World.Devices.DeviceUiViews;
using World.Helpers;

namespace OneWayTicketsMod.Game
{
    internal static class CubicleViewService
    {
        /// <summary>「工位格子常显」总开关（面板「设置」页可关）。</summary>
        public static bool ForceVisible;

        /// <summary>「自动补满空工位」总开关（面板「旅人」页可关）。</summary>
        public static bool AutoFillEnabled;

        /// <summary>最近一次修复的统计，面板上显示用。</summary>
        public struct RepairStats
        {
            public int Devices;        // 扫到的设备数
            public int CreatedCubicles; // 补建的工位数据
            public int BoundStations;   // 补绑定的格子
            public int ShownStations;   // 硬开的格子
            public int HiddenStations;  // 藏掉的死格子
        }

        private static float _next;

        /// <summary>最近一轮扫描的统计。</summary>
        private static RepairStats _last;

        /// <summary>扫描轮次（每秒 +1）。注意这是「检查了几遍」，不是「修了几次」。</summary>
        private static int _scanCount;

        /// <summary>累计真正改动过的项数（补建 / 补绑定 / 补显示 / 藏死格）。</summary>
        private static int _totalFixed;

        public static DeviceMgr Mgr => SingletonAccess.Mgr<DeviceMgr>("DeviceMgr");

        public static bool Ready => Mgr != null;

        // ------------------------------------------------------------ 每秒轮询

        public static void Tick()
        {
            // 自动补位独立于「工位格子常显」：那个开关只管显示层，这个只管排人
            TickAutoFill();

            if (!ForceVisible) return;

            float now = Time.realtimeSinceStartup;
            if (now < _next) return;
            _next = now + 1f;

            var mgr = Mgr;
            if (mgr == null) return;

            // 顺手把科技标志补上（不是主修法，只是别让它成为额外的坑）
            var ctx = SingletonAccess.Get<BattleContext>();
            if (ctx != null && !ctx.active_cubicle) ctx.active_cubicle = true;

            Scan(mgr);
        }

        /// <summary>扫一轮，并累计「真正改动了几项」。</summary>
        private static RepairStats Scan(DeviceMgr mgr)
        {
            var s = RepairAll(mgr);
            _last = s;
            _scanCount++;
            _totalFixed += FixedOf(s);
            return s;
        }

        private static int FixedOf(RepairStats s)
            => s.CreatedCubicles + s.BoundStations + s.ShownStations + s.HiddenStations;

        // ------------------------------------------------------------ 面板按钮

        /// <summary>面板「立即修复」：立刻跑一遍并返回一行摘要。</summary>
        public static string RepairNow()
        {
            var mgr = Mgr;
            if (mgr == null) return "不在局内，读不到设备管理器。";

            var ctx = SingletonAccess.Get<BattleContext>();
            if (ctx != null) ctx.active_cubicle = true;

            var s = Scan(mgr);
            if (FixedOf(s) == 0)
                return $"检查了 {s.Devices} 台设备，格子都正常，没有可修的东西。";

            return $"补工位 {s.CreatedCubicles} · 补绑定 {s.BoundStations} · 补显示 {s.ShownStations}"
                 + (s.HiddenStations > 0 ? $" · 藏死格 {s.HiddenStations}" : "");
        }

        // ------------------------------------------------------------ 核心修复

        private static RepairStats RepairAll(DeviceMgr mgr)
        {
            var stats = new RepairStats();

            try
            {
                foreach (var device in Devices(mgr))
                {
                    if (device == null) continue;
                    stats.Devices++;

                    // a) 数据层：设备有工位数据但一个都没建出来
                    stats.CreatedCubicles += EnsureCubicles(device);

                    // b/c/d) 绑定层 + 显示层
                    if (device.views == null) continue;
                    foreach (var view in device.views)
                    {
                        var ui = view as DeviceUiView;
                        if (ui == null || ui.stations == null) continue;

                        // 装备面板里的只读视图：EquipSlot.set_panel() 自己会 HideStation()，
                        // 它用 EquipSlot.cub_list 显示格子，这里绝不能去点亮它的 stations，
                        // 否则装备面板会多出一排幽灵格子（v0.2.6 的副作用）。
                        if (ui.only_read) continue;

                        for (int i = 0; i < ui.stations.Count; i++)
                        {
                            var station = ui.stations[i];
                            if (station == null) continue;

                            // b) 没绑工位 → 绑到第 i 个工位（和游戏 attach() 的规则一致）
                            if (station.owner == null && i < device.cubicle_list.Count)
                            {
                                var cub = device.cubicle_list[i];
                                if (cub != null)
                                {
                                    cub.add_view(station);
                                    stats.BoundStations++;
                                }
                            }

                            // d) 还是没绑上 → 这格永远放不了人，藏掉
                            if (station.owner == null)
                            {
                                if (station.gameObject.activeSelf)
                                {
                                    station.gameObject.SetActive(false);
                                    stats.HiddenStations++;
                                }
                                continue;
                            }

                            // c) 有工位但格子被关掉了 → 硬开
                            if (!station.gameObject.activeSelf)
                            {
                                station.gameObject.SetActive(true);
                                stats.ShownStations++;
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.LogWarn("修复工位格子时出错：" + ex.Message);
            }

            return stats;
        }

        /// <summary>
        /// 按 device_all.cubicles 把缺失的工位数据补建出来。
        /// 只在「一个都没有」时补，避免把已存在的工位重复加进 CubiclesMgr。
        /// </summary>
        private static int EnsureCubicles(Device device)
        {
            if (device.cubicle_list == null) return 0;
            if (device.cubicle_list.Count > 0) return 0;

            var wanted = device.desc?.cubicles;
            if (wanted == null || wanted.Count == 0) return 0;

            int added = 0;
            foreach (var id in wanted)
            {
                try
                {
                    var cub = Cubicle_Helper.AddDeviceCubicle(device, id);
                    if (cub == null) continue;
                    device.cubicle_list.Add(cub);
                    added++;
                }
                catch (System.Exception ex)
                {
                    Plugin.LogWarn($"补建工位失败（设备 {device.desc?.id} 工位 {id}）：{ex.Message}");
                    break;
                }
            }

            if (added > 0)
                Plugin.LogInfo($"工位修复：设备 {DeviceName(device)} 原本没有任何工位，已按配置补建 {added} 个。");

            return added;
        }

        // ------------------------------------------------------------ 一键上工位 / 自动补位

        private static float _nextAuto;

        /// <summary>「一键上工位」按钮：跑一次并把结果写成一句人话。</summary>
        public static string FillAll() => FillCore(false, out _);

        /// <summary>
        /// 自动补位（面板「旅人」页的开关）：每 2 秒跑一次，没人可安排就静默返回 0。
        ///
        /// 一个必要的保护：如果玩家正在用游戏自己的流程手动点选旅人
        /// （Cubicle_Helper.GetSelectCharacter() 不为空），这一轮直接跳过 ——
        /// 否则自动补位会把玩家选好的人抢去别的工位。
        /// </summary>
        public static int AutoFill()
        {
            // ★ v0.4.2 修：Cubicle_Helper.GetSelectCharacter() 内部不判空
            //   （World.Helpers/Character_Helper.cs 直接 return mgr.select_character），
            //   而营地 / 主菜单根本没有 CharacterMgr —— 每 2 秒一次空引用刷屏就是这么来的。
            //   所以先自己确认两个管理器都在，再碰游戏的静态工具类。
            if (!Ready) return 0;
            if (SingletonAccess.Mgr<World.Characters.CharacterMgr>("CharacterMgr") == null) return 0;

            if (Cubicle_Helper.GetSelectCharacter() != null) return 0;

            FillCore(true, out int placed);
            if (placed > 0) Plugin.LogInfo($"[自动工位] 已自动安排 {placed} 人上工位。");
            return placed;
        }

        private static void TickAutoFill()
        {
            if (!AutoFillEnabled) return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextAuto) return;
            _nextAuto = now + 2f;

            try
            {
                AutoFill();
                _lastAutoError = null;
            }
            catch (System.Exception ex)
            {
                // 同一条错误只在第一次记日志：自动补位每 2 秒跑一轮，
                // 一个稳定的空引用会在 LogOutput.log 里刷出成百上千行，把真正有用的日志淹掉。
                if (ex.Message != _lastAutoError)
                {
                    _lastAutoError = ex.Message;
                    Plugin.LogWarn("自动补位出错（同类错误之后不再重复记录）：" + ex);
                }
            }
        }

        /// <summary>上一轮自动补位的错误信息，用于去重刷屏。</summary>
        private static string _lastAutoError;

        /// <summary>
        /// 把前排空闲旅人自动安排到车上的空工位。
        ///
        /// 用途有三个：
        ///   1. 真正的功能——不想一个个点的时候一键铺满；
        ///   2. 自动补位——开关打开后每 2 秒兜一次底；
        ///   3. 诊断——它走的是和「玩家点格子」完全相同的入口
        ///      （Cubicle_Helper.SetSelectCharacter + SetCubicle），
        ///      如果这里能安排上人而手点不行，问题就在 UI 点击层（格子没激活 / 被别的界面挡住），
        ///      如果这里也安排不上，问题在数据层（没工位 / 没绑定 / 旅人状态不允许）。
        ///
        /// 只遍历 mgr.slots_device —— 也就是真的装在车上的设备，
        /// 不会像 v0.2.5 删掉的「工位分配」那样把旅人塞进已经不在车上的历史设备。
        /// quiet = true 时不返回给人看的提示（自动补位用）。
        /// </summary>
        private static string FillCore(bool quiet, out int placed)
        {
            placed = 0;

            var mgr = Mgr;
            if (mgr == null) return quiet ? null : "不在局内，读不到设备管理器。";

            var charMgr = SingletonAccess.Mgr<World.Characters.CharacterMgr>("CharacterMgr");
            if (charMgr == null) return quiet ? null : "读不到旅人管理器。";

            var empty = new List<BasicCubicle>();
            int deviceCount = 0;
            foreach (var device in Devices(mgr))
            {
                deviceCount++;
                if (device.cubicle_list == null) continue;
                foreach (var cub in device.cubicle_list)
                    if (cub != null && cub.worker == null) empty.Add(cub);
            }

            if (deviceCount == 0) return quiet ? null : "车上没有设备。";
            if (empty.Count == 0)
                return quiet ? null : $"车上 {deviceCount} 台设备都没有空工位（或者压根没工位数据）。";

            var idle = new List<World.Characters.Character>();
            if (charMgr.characters != null)
            {
                foreach (var c in charMgr.characters)
                {
                    if (c == null) continue;
                    if (c.state != null) continue;                       // 昏迷 / 亢奋等状态不安排
                    if (Cubicle_Helper.GetCubicle(c) != null) continue;   // 已经在别的工位上
                    idle.Add(c);
                }
            }

            if (idle.Count == 0)
                return quiet ? null : $"空工位 {empty.Count} 个，但操作中没有空闲旅人。";

            int ok = 0;
            for (int i = 0; i < empty.Count && ok < idle.Count; i++)
            {
                var cub = empty[i];
                var who = idle[ok];
                try
                {
                    Cubicle_Helper.SetSelectCharacter(who);
                    Cubicle_Helper.SetCubicle(cub);
                    if (cub.worker != null) ok++;
                    else Cubicle_Helper.SetSelectCharacter(null);
                }
                catch (System.Exception ex)
                {
                    Plugin.LogWarn("安排旅人上工位失败：" + ex.Message);
                    try { Cubicle_Helper.SetSelectCharacter(null); } catch { }
                }
            }

            placed = ok;

            if (quiet) return null;
            if (ok == 0)
                return $"空工位 {empty.Count} 个、空闲旅人 {idle.Count} 人，但一个都没安排上——说明卡在数据/状态层，点「写诊断到日志」发我看。";

            return $"已安排 {ok} 人上工位（空工位 {empty.Count} 个 · 空闲旅人 {idle.Count} 人）。";
        }

        // ------------------------------------------------------------ 诊断

        /// <summary>
        /// 把工位相关的真实状态整段写进 BepInEx 日志（LogOutput.log），
        /// 关键字统一是「[工位诊断]」，出问题时直接 grep 这个就能看到卡在哪一层。
        /// </summary>
        public static string Dump()
        {
            var sb = new StringBuilder();
            void W(string line) { sb.AppendLine("[工位诊断] " + line); }

            W("===== 开始 =====");

            var mgr = Mgr;
            if (mgr == null)
            {
                W("读不到 DeviceMgr（当前不在探索 / 战斗场景）");
                W("===== 结束 =====");
                Plugin.LogInfo(sb.ToString().TrimEnd());
                return "不在局内，已把「读不到设备管理器」写进日志。";
            }

            var ctx = SingletonAccess.Get<BattleContext>();
            var world = SingletonAccess.Get<WorldContext>();
            var charMgr = SingletonAccess.Mgr<World.Characters.CharacterMgr>("CharacterMgr");

            W($"active_cubicle={(ctx != null && ctx.active_cubicle ? "true" : "false")}"
              + $" · 安全区={(world != null && world.in_safe_area ? "true" : "false")}"
              + $" · 开关={(ForceVisible ? "开" : "关")}");

            var selected = Cubicle_Helper.GetSelectCharacter();
            W($"选中旅人={(selected == null ? "无（这时点格子什么都不会发生）" : TeamService.NameOf(selected))}");

            if (charMgr != null)
                W($"旅人：操作中 {charMgr.characters?.Count ?? 0} / 休息区 {charMgr.backup?.backup_characters?.Count ?? 0}");

            int deviceCount = 0, stationTotal = 0, stationActive = 0, stationBound = 0, deadSlot = 0;

            foreach (var pair in DeviceSlots(mgr))
            {
                var device = pair.Value;
                if (device == null) continue;
                deviceCount++;

                var wanted = device.desc?.cubicles;
                W($"- [{pair.Key}] {DeviceName(device)}"
                  + $" 配置工位={wanted?.Count ?? 0}"
                  + $" 已建工位={device.cubicle_list?.Count ?? 0}"
                  + $" 视图={device.views?.Count ?? 0}");

                if (device.cubicle_list != null)
                {
                    for (int i = 0; i < device.cubicle_list.Count; i++)
                    {
                        var cub = device.cubicle_list[i];
                        if (cub == null) { W($"    工位#{i} = null"); continue; }
                        W($"    工位#{i} id={cub.desc?.id} 行为={cub.desc?.cubicle_behaviour}"
                          + $" 工人={(cub.worker == null ? "无" : TeamService.NameOf(cub.worker))}"
                          + $" 视图数={cub.views?.Count ?? 0}");
                    }
                }

                if (device.views == null) continue;
                int vi = 0;
                foreach (var v in device.views)
                {
                    var ui = v as DeviceUiView;
                    if (ui == null) continue;
                    W($"    视图#{vi} {ui.GetType().Name} only_read={ui.only_read}"
                      + $" 格子={ui.stations?.Count ?? 0}");
                    if (ui.stations == null) { vi++; continue; }

                    for (int i = 0; i < ui.stations.Count; i++)
                    {
                        var st = ui.stations[i];
                        if (st == null) { W($"      格子#{i} = null"); continue; }
                        stationTotal++;
                        if (st.gameObject.activeSelf) stationActive++;
                        if (st.owner != null) stationBound++;
                        else deadSlot++;
                        W($"      格子#{i} active={st.gameObject.activeSelf}"
                          + $" 绑定={(st.owner == null ? "无（死格子，点了没反应）" : "工位 " + st.owner.desc?.id)}"
                          + $" 工人={(st.owner?.worker == null ? "无" : TeamService.NameOf(st.owner.worker))}"
                          + $" 高亮={(st.highlight != null && st.highlight.activeSelf ? "on" : "off")}");
                    }
                    vi++;
                }
            }

            W($"合计：设备 {deviceCount} 台 · 格子 {stationTotal} 个（显示 {stationActive} / 已绑定 {stationBound} / 死格 {deadSlot}）");

            // 装备面板（EquipSlot）是另一套 UI：它不用 DeviceUiView.stations，
            // 而是用 EquipSlot.cub_list 这一组 CubicleUiView。v0.2.7 之前它会被
            // show_emergency 的空引用掐断，导致 cub_list 全部停在被关掉的状态。
            try
            {
                var equips = Object.FindObjectsOfType<World.Devices.Equip.EquipSlot>();
                if (equips == null || equips.Length == 0)
                {
                    W("装备面板：未打开");
                }
                else
                {
                    W($"装备面板：{equips.Length} 个槽位");
                    foreach (var es in equips)
                    {
                        if (es == null || es.cub_list == null) continue;
                        int b = 0, a = 0;
                        foreach (var cv in es.cub_list)
                        {
                            if (cv == null) continue;
                            if (cv.owner != null) b++;
                            if (cv.gameObject.activeSelf) a++;
                        }
                        W($"  [{es.gameObject.name}] 工位格={es.cub_list.Count} 已绑定={b} 已显示={a}"
                          + (es.cub_list.Count > 0 && a == 0 ? "  ← 全被关掉了（init 被异常掐断的特征）" : ""));
                    }
                }
            }
            catch (System.Exception ex)
            {
                W("装备面板诊断失败：" + ex.Message);
            }

            W($"已扫描 {_scanCount} 轮（每秒 1 轮，只读检查；累计真正修补 {_totalFixed} 项）");
            W("===== 结束 =====");

            var text = sb.ToString().TrimEnd();
            Plugin.LogInfo(text);
            return $"已写入日志：设备 {deviceCount} 台 · 格子 {stationTotal} 个（显示 {stationActive} / 绑定 {stationBound} / 死格 {deadSlot}）";
        }

        /// <summary>面板诊断行（一行摘要，不写日志）。</summary>
        public static string Diagnose()
        {
            var mgr = Mgr;
            if (mgr == null) return "不在局内";

            var ctx = SingletonAccess.Get<BattleContext>();
            int total = 0, shown = 0, bound = 0;
            foreach (var device in Devices(mgr))
            {
                if (device.views == null) continue;
                foreach (var view in device.views)
                {
                    var ui = view as DeviceUiView;
                    if (ui == null || ui.stations == null) continue;
                    foreach (var station in ui.stations)
                    {
                        if (station == null) continue;
                        total++;
                        if (station.gameObject.activeSelf) shown++;
                        if (station.owner != null) bound++;
                    }
                }
            }

            var sb = new StringBuilder();
            sb.Append("active_cubicle=").Append(ctx != null && ctx.active_cubicle ? "true" : "false");
            sb.Append(" · 设备 ").Append(CountDevices(mgr)).Append(" 台");
            sb.Append(" · 格子 ").Append(shown).Append('/').Append(total).Append(" 显示");
            sb.Append(" · ").Append(bound).Append(" 个已绑工位");
            // 明确写「扫描」而不是「修复」：每秒扫一轮是常态，不等于每秒都在改东西
            if (_scanCount > 0) sb.Append(" · 已扫 ").Append(_scanCount).Append(" 轮");
            sb.Append(_totalFixed > 0 ? $"（累计修补 {_totalFixed} 项）" : "（一直无需修补）");
            return sb.ToString();
        }

        // ------------------------------------------------------------ 工具

        private static IEnumerable<KeyValuePair<string, Device>> DeviceSlots(DeviceMgr mgr)
        {
            if (mgr.slots_device == null) yield break;
            foreach (var slot in mgr.slots_device)
            {
                if (slot == null) continue;
                yield return new KeyValuePair<string, Device>(slot._name, slot.slot_device);
            }
        }

        private static IEnumerable<Device> Devices(DeviceMgr mgr)
        {
            foreach (var pair in DeviceSlots(mgr))
                if (pair.Value != null) yield return pair.Value;
        }

        private static int CountDevices(DeviceMgr mgr)
        {
            int n = 0;
            foreach (var _ in Devices(mgr)) n++;
            return n;
        }

        private static string DeviceName(Device device)
        {
            try
            {
                var localized = Localization_Utility.get_localization(device.desc?.name);
                if (!string.IsNullOrEmpty(localized)) return localized;
            }
            catch
            {
                // 本地化表未就绪
            }
            return "设备" + (device.desc?.id.ToString() ?? "?");
        }
    }
}
