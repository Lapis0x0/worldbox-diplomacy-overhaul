using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul.Perf
{
    // 开发用的命令通道：tools/dev.sh 把命令写进 bin/dev/cmd.txt，这里每帧按顺序执行，
    // 结果追加到 bin/dev/out.log。只有模组目录里有 bin/ 时才启用，玩家那边不会触发。
    //
    // 命令（每行一条）：
    //   load <存档目录>          复制到 bin/dev/run_save 后读档，等读完再继续
    //   speed <x1|x5|...>       设置速度
    //   pause <on|off>
    //   wait <秒>
    //   camera reset | camera <x> <y> <zoom>
    //   opt <字段名|all> <on|off>
    //   debugopt <选项> <on|off>   切换原版调试菜单的选项（DebugOption 的名字）
    //   settings                 打开模组设置窗口
    //   mil                      军队大修的状态：军队、禁军编队和人数、驻扎中的军队、欠饷的国家和城市
    //   setting <分组> <开关> <true|false>   改模组设置，如 setting vd_group_perf vd_perf_enabled false
    //   measure <标签> <秒> [detail] [mono]   测帧时间；detail 同时打开原版 Bench 分项计时，
    //                           mono 给所有脚本的 Update/LateUpdate 计时（都有额外开销）
    //   probe <类型.方法> ...     detail 测量时额外计时这些方法（同名重载都算），不带参数则清空
    //   shot <名字>              截图到 bin/dev/shots/<名字>.png
    //   fav <items|units> <数量>  随机设一些收藏（走玩家切换收藏的同一个方法）
    //   uitree <窗口 id> [深度]    打印窗口的节点层级：名字、是否激活、组件、位置和尺寸、文字
    //   gcinfo                   打印 GC 模式和托管堆大小
    //   heap <标签> [行数]        遍历托管堆，按类型统计个数和大小（很慢）
    //   gc                       强制完整回收，打印回收前后的占用（得到活对象的大小）
    //   parl [list [数量]]        列出城市最多的几个国家的议会：议席、氏族立场、政策、议案
    //   parl stats               各项政策每个选项有多少国家在用
    //   parl regime              各个政制名称有几个国家在用、是哪几个、领袖叫什么
    //   parl set <国家> <政策 id> <选项>   直接改一个国家的政策
    //   parl boats [数量]         城市最多的几个国家每个港口的运输船数、陆上单位的平均伤害
    //   parl draft [数量]         城市最多的几个国家：各股势力的处境、意识形态、各项政策的理想位置、忠诚修正，和它当提案人时收益最高的几项
    //   parl sim <轮数>           每个国家连开几轮议会（立场不变，不写世界日志），统计议案、政策分布和地方势力的忠诚修正，算完恢复原样
    //   parl trait <国家> <氏族名> [特质 id|clear|open]   给议会里的一个氏族加减政见特质，打印它的立场和派系；open 打开氏族窗口；不带参数数各政见有几家
    //   parl show|session|open [国家 id 或名字] [页签]   查看一个国家的议会；session 让它立即开一次议会；open 打开议会窗口（页签 domestic|military|diplomacy，再跟政策 id 展开这一项）
    //   parl lock <国家> <政策 id>   切换这项政策的锁定
    //   parl tribute             各国的税收体制、原版贡赋特质、实际上缴比例、附庸贡赋和政见不合合计
    //   parl election [国家]       打开选举与任命窗口
    //   parl vote [国家]           按眼下的议会选一次执政，只打印候选人（能力、声望、分量）和票数，不生效
    //   parl renown              各国国王的声望，城主声望和能力的分布
    //   parl rel [国家] [open <氏族名>]   各股势力两两之间的好感和明细；open 打开那个家族的窗口
    //   parl bill <国家> <政策 id> <选项>   按眼下的议会表决一份议案，只打印各氏族的票，不生效
    //   treasury [国家] | treasury open [国家] | treasury close | treasury tabs   一个国家的国库账本（今年、去年）和账簿；open 打开国库窗口；close 立即结一次年账；tabs 列出王国窗口页签栏
    //   arms [数量] | arms run | arms storage <国家>   城市最多的几个国家的军械制式、禁军装备分布；run 立即结算一次军械；storage 列出一个国家每座城的兵和仓库装备
    //   bench stress <附庸数>      城市最多的国家把最小的几个国家收为附庸、吞并其余所有国家，造一个大帝国（tools/perf-check.sh 用）
    //   bench run                 直接调用议会、好感、各定期结算系统等重函数计时，每项一行 @bench <名字> <毫秒>
    //   bench arrow [stale|restore|null]   射一支箭，把它记下的国家换成已销毁的国家，检查弹道兜底（变体见 BenchDev.Arrow）
    //   safety [fail <系统> [次数] | reset | throw on|off]   各系统熔断状态；fail 模拟某个系统的补丁出错，reset 全部恢复；
    //                           throw 让第一座城的原版更新每帧抛异常，检查原版每帧更新的兜底
    //   check                    巡检对象之间的关系（附庸、城市、单位、军队、弹道、战争指向的国家都还在），每条违规一行 @violation（tools/soak.sh 用）
    //   year                     写一行 @year <当前年份>
    //   end <token>              写一行 @done <token>，脚本据此判断结束
    public static class Dev
    {
        public static bool Enabled;
        static string _dir;
        static readonly Queue<string> _queue = new Queue<string>();
        static Func<bool> _blocker;
        static float _poll;
        static bool _ready;

        public static void Start(string pFolder)
        {
            Enabled = Directory.Exists(Path.Combine(pFolder, "bin"));
            if (!Enabled) return;
            _dir = Path.Combine(pFolder, "bin", "dev");
            Directory.CreateDirectory(_dir);
            _queue.Clear();
            _blocker = null;
            _ready = false;
            Measure.Abort();
        }

        public static void Stop()
        {
            Measure.Abort();
            Enabled = false;
        }

        public static string Dir => _dir;

        // 开发时各定期结算系统最近一次的耗时；单次超过一帧（16ms）就写一行 @spike
        public static readonly Dictionary<string, double> Timings = new Dictionary<string, double>();
        const double SpikeMs = 16.0;

        public static void Timed(string pName, long pStart)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - pStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Timings[pName] = ms;
            if (ms > SpikeMs) Log($"@spike {pName} {ms:F1}ms");
        }

        public static void Log(string pLine)
        {
            Debug.Log("[perf] " + pLine);
            try { File.AppendAllText(Path.Combine(_dir, "out.log"), pLine + "\n"); }
            catch { }
        }

        internal static void Update()
        {
            if (!_ready && Config.game_loaded && !SmoothLoader.isLoading())
            {
                _ready = true;
                Log("ready");
            }
            Measure.Tick();
            _poll += Time.unscaledDeltaTime;
            if (_poll >= 0.5f)
            {
                _poll = 0f;
                ReadCommands();
            }
            while (_queue.Count > 0)
            {
                if (_blocker != null)
                {
                    if (!_blocker()) return;
                    _blocker = null;
                }
                string line = _queue.Dequeue();
                try { Run(line); }
                catch (Exception e)
                {
                    Log("! " + line + " failed: " + e);
                }
            }
            if (_blocker != null && _blocker()) _blocker = null;
        }

        static void ReadCommands()
        {
            string path = Path.Combine(_dir, "cmd.txt");
            if (!File.Exists(path)) return;
            string[] lines = File.ReadAllLines(path);
            File.Delete(path);
            foreach (string line in lines)
                if (!string.IsNullOrWhiteSpace(line)) _queue.Enqueue(line.Trim());
        }

        static void Run(string pLine)
        {
            string[] a = pLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Log("> " + pLine);
            switch (a[0])
            {
                case "load": Load(pLine.Substring(5).Trim()); break;
                case "speed": Config.setWorldSpeed(a[1]); break;
                case "pause": Config.paused = a[1] == "on"; break;
                case "wait":
                    float until = Time.realtimeSinceStartup + F(a[1]);
                    _blocker = () => Time.realtimeSinceStartup >= until;
                    break;
                case "camera": Camera(a); break;
                case "debugopt":
                    DebugConfig.setOption((DebugOption)Enum.Parse(typeof(DebugOption), a[1]), a[2] == "on");
                    Log($"  {a[1]} = {DebugConfig.isOn((DebugOption)Enum.Parse(typeof(DebugOption), a[1]))}");
                    break;
                case "mil":
                {
                    int guardArmies = 0, guardUnits = 0, members = 0, stations = 0, armies = 0, unpaidCities = 0, unpaidKingdoms = 0;
                    foreach (Army army in World.world.armies)
                    {
                        armies++;
                        if (Guard.Is(army)) { guardArmies++; guardUnits += army.countUnits(); }
                        if (Orders.HasStation(army)) stations++;
                    }
                    foreach (Kingdom k in World.world.kingdoms)
                    {
                        if (!k.isAlive()) continue;
                        if (Guard.Penalty(k) > 0f) unpaidKingdoms++;
                        foreach (City c in k.getCities())
                        {
                            members += Guard.CountMembers(c);
                            if (Treasury.Penalty(c) > 0f) unpaidCities++;
                        }
                    }
                    Log($"  military={Military.On} armies={armies} guardArmies={guardArmies} guardUnits={guardUnits} members={members} stations={stations} unpaidKingdoms={unpaidKingdoms} unpaidCities={unpaidCities}");
                    break;
                }
                case "settings":
                    SettingsWindow.Open();
                    break;
                case "setting":
                    // 和玩家在设置窗口里点开关一样
                    Settings.Set(a[1], a[2], bool.Parse(a[3]));
                    Log($"  {a[1]}.{a[2]} = {a[3]}");
                    break;
                case "opt":
                    if (!Opt.Set(a[1], a[2] == "on")) Log("! unknown opt " + a[1]);
                    Log("  " + Opt.Describe());
                    break;
                case "probe":
                    Measure.Probes.Clear();
                    Measure.Probes.AddRange(a.Skip(1));
                    Log("  probes: " + string.Join(" ", Measure.Probes));
                    break;
                case "measure":
                    Measure.Begin(a[1], F(a[2]), a.Skip(3).Contains("detail"), a.Skip(3).Contains("mono"));
                    _blocker = () => !Measure.Running;
                    break;
                case "shot":
                    string shots = Path.Combine(_dir, "shots");
                    Directory.CreateDirectory(shots);
                    ScreenCapture.CaptureScreenshot(Path.Combine(shots, a[1] + ".png"));
                    float shot_until = Time.realtimeSinceStartup + 1f;
                    _blocker = () => Time.realtimeSinceStartup >= shot_until;
                    break;
                case "uitree":
                    // uitree <窗口 id> [深度]：打印窗口的节点层级（名字、是否激活、组件、位置和尺寸）
                    if (ScrollWindow._all_windows.TryGetValue(a[1], out ScrollWindow uw) && uw != null) UiTree(uw.transform, 0, a.Length > 2 ? int.Parse(a[2]) : 8);
                    else Log("  ! no window " + a[1]);
                    break;
                case "allocprobe":
                    try
                    {
                        foreach (int n in new[] { 100, 1000, 100000 })
                        {
                            long a0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                            var junk = new object[n / 8];
                            long a1 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                            var list = new List<object>();
                            for (int i = 0; i < n / 32; i++) list.Add(new object());
                            long a2 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                            Log($"  used-size delta: array of ~{n}B -> {a1 - a0}; {n / 32} small objects (~{n / 32 * 16 + n / 4}B) -> {a2 - a1} ({junk.Length})");
                        }
                    }
                    catch (Exception e) { Log("  GetAllocatedBytesForCurrentThread failed: " + e.GetType().Name + " " + e.Message); }
                    break;
                case "gcinfo":
                    Log($"  incremental={UnityEngine.Scripting.GarbageCollector.isIncremental} mode={UnityEngine.Scripting.GarbageCollector.GCMode} slice_ns={UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds} heap={UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / 1048576}MB used={UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576}MB total_alloc={System.GC.GetTotalMemory(false) / 1048576}MB");
                    break;
                case "gc":
                {
                    long before = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    System.GC.Collect();
                    System.GC.WaitForPendingFinalizers();
                    System.GC.Collect();
                    long after = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                    Log($"  full gc {sw.ElapsedMilliseconds}ms: used {before / 1048576}MB -> {after / 1048576}MB, heap={UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / 1048576}MB");
                    break;
                }
                case "heap": HeapWalk.Run(a[1], a.Length > 2 ? int.Parse(a[2]) : 40); break;
                case "fav": Fav(a[1], int.Parse(a[2])); break;
                case "parl": ParlDev.Run(a); break;
                case "arms": ArmsDev.Run(a); break;
                case "treasury": TreasuryDev.Run(a); break;
                case "bench": BenchDev.Run(a); break;
                case "check": CheckDev.Run(); break;
                case "year": Log("@year " + Date.getCurrentYear()); break;
                case "safety":
                    // safety fail <系统> [次数]：模拟这个系统的补丁出错，检查熔断；safety reset：全部恢复
                    if (a.Length > 2 && a[1] == "fail")
                    {
                        Sys sys = (Sys)Enum.Parse(typeof(Sys), a[2], true);
                        int n = a.Length > 3 ? int.Parse(a[3]) : 1;
                        for (int i = 0; i < n; i++) Safety.Fail(sys, "开发命令模拟", new InvalidOperationException("safety fail"));
                    }
                    else if (a.Length > 1 && a[1] == "reset") Safety.Reset();
                    else if (a.Length > 2 && a[1] == "throw") Dev_ThrowInCity.On = a[2] == "on";
                    Log("  " + string.Join(" ", Enum.GetValues(typeof(Sys)).Cast<Sys>().Select(s => s + "=" + (Safety.On(s) ? "on" : "off"))));
                    break;
                case "end": Log("@done " + (a.Length > 1 ? a[1] : "")); break;
                default: Log("! unknown command"); break;
            }
        }

        static void Load(string pSource)
        {
            string run = Path.Combine(_dir, "run_save");
            if (Directory.Exists(run)) Directory.Delete(run, true);
            Directory.CreateDirectory(run);
            foreach (string file in Directory.GetFiles(pSource))
            {
                string target = Path.Combine(run, Path.GetFileName(file));
                File.Copy(file, target);
                File.SetAttributes(target, FileAttributes.Normal);
            }
            SaveManager.setCurrentPathAndId(run, SaveManager.currentSlot);
            World.world.save_manager.startLoadSlot();
            float started = Time.realtimeSinceStartup;
            bool seen_loading = false;
            _blocker = () =>
            {
                if (SmoothLoader.isLoading()) seen_loading = true;
                if (!seen_loading && Time.realtimeSinceStartup - started < 30f) return false;
                if (SmoothLoader.isLoading() || !Config.game_loaded) return false;
                // 读档慢时原版会弹出日志控制台，它会拖慢帧率
                World.world.console?.Hide();
                Log($"  loaded in {Time.realtimeSinceStartup - started:F1}s units={World.world.units.Count}");
                return true;
            };
        }

        static void UiTree(Transform pNode, int pDepth, int pMax)
        {
            var rt = pNode as RectTransform;
            string comps = string.Join(",", pNode.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));
            string box = rt != null ? $" pos={rt.anchoredPosition} size={rt.rect.size}" : "";
            var text = pNode.GetComponent<UnityEngine.UI.Text>();
            Log($"{new string(' ', 2 + pDepth * 2)}{pNode.name}{(pNode.gameObject.activeSelf ? "" : " (off)")} [{comps}]{box}{(text != null ? " \"" + text.text + "\"" : "")}");
            if (pDepth >= pMax) return;
            foreach (Transform c in pNode) UiTree(c, pDepth + 1, pMax);
        }

        static void Camera(string[] a)
        {
            MoveCamera camera = MoveCamera.instance;
            if (a[1] == "reset")
            {
                World.world.centerCamera();
                camera.resetZoom();
                return;
            }
            Vector3 pos = camera.main_camera.transform.position;
            camera.main_camera.transform.position = new Vector3(F(a[1]), F(a[2]), pos.z);
            camera.forceZoom(F(a[3]));
        }

        static void Fav(string pKind, int pCount)
        {
            int done = 0;
            if (pKind == "items")
            {
                foreach (Item item in World.world.items.ToList().OrderBy(_ => Randy.random()))
                {
                    if (done >= pCount) break;
                    if (item.isFavorite() || item.getActor().isRekt()) continue;
                    item.switchFavorite();
                    done++;
                }
            }
            else
            {
                foreach (Actor unit in World.world.units.ToList().OrderBy(_ => Randy.random()))
                {
                    if (done >= pCount) break;
                    if (unit.isFavorite()) continue;
                    unit.switchFavorite();
                    done++;
                }
            }
            Log($"  favorited {done} {pKind}");
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }

    // 挂在帧首：世界更新抛异常时帧末的补丁不会执行，开发命令照样要能跑
    [SafeBy("Dev.Update 逐条命令兜住，只在开发时启用")]
    [HarmonyPatch(typeof(MapBox), "Update")]
    static class Dev_Update
    {
        static bool Prepare() => Dev.Enabled;

        static void Prefix() => Dev.Update();
    }

    [SafeBy("故意抛异常，只在开发时启用")]
    [HarmonyPatch(typeof(City), nameof(City.update))]
    static class Dev_ThrowInCity
    {
        public static bool On;

        static bool Prepare() => Dev.Enabled;

        static void Prefix(City __instance)
        {
            if (On && __instance == World.world.cities.list[0]) throw new InvalidOperationException("safety throw");
        }
    }
}
