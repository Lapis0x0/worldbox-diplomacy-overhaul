using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul.Perf
{
    // 测一段时间的帧耗时。detail 模式另外打开原版 Bench，把各子系统「每帧平均毫秒」排出来；
    // Bench 本身有开销，所以比较优化前后的总帧耗时要用不带 detail 的测量
    public static class Measure
    {
        const string ProfileHarmonyId = "shizhiyun.worldbox_performance.profile";

        public static bool Running { get; private set; }
        static string _label;
        static float _duration;
        static float _elapsed;
        static bool _detail;
        static bool _mono;
        static readonly List<float> _frames = new List<float>();
        static int _gc_start;
        static Harmony _profile;
        static readonly Dictionary<(string group, string id), double> _acc = new Dictionary<(string, string), double>();
        static long _dirty_tiles;
        static long _alloc_total_start;
        static long _frame_used;
        static int _frame_gc;
        static long _frame_alloc;
        static int _gc_frames;
        static readonly List<string> _spikes = new List<string>();
        static double _world_time_start;
        static int _saved_vsync;
        static int _saved_target_fps;

        public static void Begin(string pLabel, float pSeconds, bool pDetail, bool pMono = false)
        {
            _mono = pMono;
            _label = pLabel;
            _duration = pSeconds;
            _elapsed = 0f;
            _detail = pDetail;
            _frames.Clear();
            _acc.Clear();
            _mono_total = 0;
            _calls.Clear();
            _alloc.Clear();
            _alloc_total_start = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            _dirty_tiles = 0;
            _gc_start = System.GC.CollectionCount(0);
            _frame_used = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            _frame_gc = _gc_start;
            _frame_alloc = 0;
            _gc_frames = 0;
            _spikes.Clear();
            _world_time_start = World.world.getCurWorldTime();
            // 垂直同步和帧率上限会把帧耗时取整到刷新间隔，测量时去掉
            _saved_vsync = QualitySettings.vSyncCount;
            _saved_target_fps = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            if (pDetail)
            {
                Bench.bench_enabled = true;
                _profile = new Harmony(ProfileHarmonyId);
                _profile.Patch(AccessTools.Method(typeof(Bench), nameof(Bench.benchEnd)),
                    postfix: new HarmonyMethod(typeof(Measure), nameof(BenchEndPostfix)));
                _profile.Patch(AccessTools.Method(typeof(Bench), nameof(Bench.benchSave)),
                    postfix: new HarmonyMethod(typeof(Measure), nameof(BenchSavePostfix)));
                foreach (Type batch in new[] { typeof(Batch<Actor>), typeof(Batch<Building>) })
                    _profile.Patch(AccessTools.Method(batch, "runUpdater"), new HarmonyMethod(typeof(Measure), nameof(JobPrefix)),
                        new HarmonyMethod(typeof(Measure), nameof(JobPostfix)));
                // 泛型类型的方法：每个阶段一个专用补丁，键名写死，不用 __originalMethod
                foreach (string phase in new[] { "Pre", "Parallel", "Post" })
                    _profile.Patch(AccessTools.Method(typeof(JobManagerBase<BatchBuildings, Building>), "updateBaseJobs" + phase),
                        new HarmonyMethod(typeof(Measure), nameof(JobPrefix)),
                        new HarmonyMethod(typeof(Measure), "Phase" + phase + "Postfix"));
                foreach (MethodBase method in ExtraTargets())
                {
                    if (method == null || method.DeclaringType.IsGenericType) continue;
                    try
                    {
                        _profile.Patch(method, new HarmonyMethod(typeof(Measure), nameof(TimerPrefix)),
                            new HarmonyMethod(typeof(Measure), nameof(TimerPostfix)));
                    }
                    catch (Exception e) { Dev.Log($"! cannot time {method?.DeclaringType?.Name}.{method?.Name}: {e.Message}"); }
                }
            }
            if (pMono)
            {
                // 所有脚本的 Update/LateUpdate 各自计时，看帧时间里脚本占多少、Unity 本身（渲染、界面布局）占多少
                _profile = _profile ?? new Harmony(ProfileHarmonyId);
                int n = 0;
                foreach (MethodBase method in MonoTargets())
                {
                    try
                    {
                        _profile.Patch(method, new HarmonyMethod(typeof(Measure), nameof(TimerPrefix)),
                            new HarmonyMethod(typeof(Measure), nameof(MonoPostfix)));
                        n++;
                    }
                    catch { }
                }
                Dev.Log($"  timing {n} script update methods");
            }
            Running = true;
        }

        static IEnumerable<MethodBase> MonoTargets()
        {
            Type[] types;
            try { types = typeof(MapBox).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null).ToArray(); }
            foreach (Type type in types)
            {
                if (type.IsGenericType || type.ContainsGenericParameters || !typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                foreach (string name in new[] { "Update", "LateUpdate", "FixedUpdate", "OnGUI" })
                {
                    MethodInfo m = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (m != null && !m.IsAbstract && m.GetMethodBody() != null) yield return m;
                }
            }
        }

        static void MonoPostfix(State __state, MethodBase __originalMethod)
        {
            if (!Running) return;
            if (!_mono_keys.TryGetValue(__originalMethod, out string key))
                _mono_keys[__originalMethod] = key = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name;
            double s = (Stopwatch.GetTimestamp() - __state.time) / (double)Stopwatch.Frequency;
            Add("mono", key, s);
            _mono_total += s;
            _calls.TryGetValue(key, out int c);
            _calls[key] = c + 1;
        }

        static readonly Dictionary<MethodBase, string> _mono_keys = new Dictionary<MethodBase, string>();
        static double _mono_total;

        public static void Abort()
        {
            if (Running)
            {
                QualitySettings.vSyncCount = _saved_vsync;
                Application.targetFrameRate = _saved_target_fps;
            }
            // 按 ID 撤掉，包括热重载前旧程序集、或打补丁中途失败留下的计时补丁
            new Harmony(ProfileHarmonyId).UnpatchSelf();
            _profile = null;
            Bench.bench_enabled = false;
            Running = false;
        }

        internal static void Tick()
        {
            if (!Running) return;
            float dt = Time.unscaledDeltaTime;
            // 第一帧的耗时属于开始测量之前
            if (_elapsed > 0f || _frames.Count > 0) _frames.Add(dt);
            // 每帧分配量：托管堆占用的增长，发生回收的那一帧不算
            long used = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            int gcs = System.GC.CollectionCount(0);
            if (gcs == _frame_gc && used > _frame_used) _frame_alloc += used - _frame_used;
            else if (gcs != _frame_gc) _gc_frames++;
            if (dt > 0.1f && _frames.Count > 0) _spikes.Add($"{dt * 1000:F0}ms{(gcs != _frame_gc ? "(gc)" : "")}");
            _frame_used = used;
            _frame_gc = gcs;
            _dirty_tiles += World.world.dirty_tiles_last;
            _elapsed += dt;
            if (_elapsed >= _duration) Finish();
        }

        static void Finish()
        {
            Abort();
            int n = _frames.Count;
            if (n == 0) return;
            float[] sorted = _frames.OrderBy(f => f).ToArray();
            double total = _frames.Sum(f => (double)f);
            float P(float q) => sorted[Mathf.Clamp((int)(q * n), 0, n - 1)] * 1000f;
            MapBox w = World.world;
            var sb = new StringBuilder();
            sb.Append($"@result {_label} frames={n} fps={n / total:F1} avg={total / n * 1000:F1}ms p50={P(0.5f):F1} p95={P(0.95f):F1} p99={P(0.99f):F1} max={sorted[n - 1] * 1000:F0}");
            sb.Append($" alloc={_frame_alloc / 1024.0 / Math.Max(1, n - _gc_frames):F0}KB/frame");
            sb.Append($" game_vsync={_saved_vsync} game_fps_cap={_saved_target_fps} refresh={Screen.currentResolution.refreshRateRatio.value:F0}");
            sb.Append($" world={(World.world.getCurWorldTime() - _world_time_start) / total:F2}/s");
            sb.Append($" gc={System.GC.CollectionCount(0) - _gc_start} speed={Config.time_scale_asset?.id} paused={Config.paused}");
            sb.Append($" units={w.units.Count} buildings={w.buildings.Count} zoom={MoveCamera.instance.main_camera.orthographicSize:F0}");
            sb.Append($" heap={UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576}MB");
            if (_spikes.Count > 0) sb.Append(" spikes=" + string.Join(",", _spikes));
            Dev.Log(sb.ToString());
            if (_detail) Dev.Log($"  dirty_tiles/frame={(double)_dirty_tiles / n:F0} tiles={World.world.tiles_list.Length}");
            if (_detail || _mono) DumpDetail(n);
            if (_mono) Dev.Log($"  scripts total {_mono_total / n * 1000:F2} ms/frame of {total / n * 1000:F2} ms/frame");
            if (_detail)
            {
                Dev.Log("  -- alloc (KB/frame)");
                foreach (var kv in _alloc.OrderByDescending(kv => kv.Value).Take(30))
                    Dev.Log($"  {kv.Value / 1024.0 / n,8:F1} KB  {kv.Key}");
            }
            if (Opt.verify && Opt.stats_container_pool) StatsPool.VerifyNoAlias();
            Verifier.Dump();
            CityBuildings.DumpKinds();
        }

        static void DumpDetail(int pFrames)
        {
            foreach (var g in _acc.GroupBy(kv => kv.Key.group).OrderByDescending(g => g.Max(kv => kv.Value)))
            {
                var top = g.OrderByDescending(kv => kv.Value).Take(25).Where(kv => kv.Value / pFrames * 1000 >= 0.02).ToList();
                if (top.Count == 0) continue;
                Dev.Log($"  -- {g.Key}");
                foreach (var kv in top)
                {
                    string calls = (kv.Key.group == "extra" || kv.Key.group == "mono") && _calls.TryGetValue(kv.Key.id, out int c) ? $"  ({(double)c / pFrames:F2}/frame)" : "";
                    Dev.Log($"  {kv.Value / pFrames * 1000,8:F2} ms  {kv.Key.group}/{kv.Key.id}{calls}");
                }
            }
        }

        static void Add(string pGroup, string pId, double pSeconds)
        {
            var pKey = (pGroup, pId);
            _acc.TryGetValue(pKey, out double v);
            _acc[pKey] = v + pSeconds;
        }

        static void BenchEndPostfix(string pID, string pGroupID, double __result)
        {
            if (Running) Add(pGroupID, pID, __result);
        }

        static void BenchSavePostfix(string pID, string pGroupID, double pValue)
        {
            if (Running) Add(pGroupID, pID, pValue);
        }

        // 原版没埋点的几处：自己计时
        // 开发命令 probe 追加的临时计时目标，形如 Actor.updatePathMovement
        public static readonly List<string> Probes = new List<string>();

        static IEnumerable<MethodBase> ExtraTargets()
        {
            foreach (string probe in Probes)
            {
                int dot = probe.LastIndexOf('.');
                Type type = AccessTools.TypeByName(probe.Substring(0, dot));
                if (type == null) { Dev.Log("! no type " + probe); continue; }
                foreach (MethodInfo m in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (m.Name == probe.Substring(dot + 1) && m.GetMethodBody() != null) yield return m;
            }
            yield return AccessTools.Method(typeof(MapBox), "Update");
            yield return AccessTools.Method(typeof(MapBox), "updateSimulation");
            yield return AccessTools.Method(typeof(MapBox), "updateDirtyMetaContainersAndCleanup");
            yield return AccessTools.Method(typeof(MapBox), "calculateVisibleObjects");
            yield return AccessTools.Method(typeof(MapBox), "updateBuildings");
            yield return AccessTools.Method(typeof(MapBox), "updateMapLayers");
            yield return AccessTools.Method(typeof(QuantumSpriteManager), "update");
            yield return AccessTools.Method(typeof(MapLayer), "updatePixels");
            yield return AccessTools.Method(typeof(WorldLayerEdges), "redraw");
            yield return AccessTools.Method(typeof(MapBox), "redrawMiniMap");
            yield return AccessTools.Method(typeof(MapBox), "checkAnyMetaAddedRemoved");
            foreach (System.Type type in new[] { typeof(CityManager), typeof(CultureManager), typeof(KingdomManager), typeof(LanguageManager), typeof(ReligionManager), typeof(WildKingdomsManager) })
                foreach (MethodInfo m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    if (m.Name.StartsWith("beginChecks")) yield return m;
            yield return AccessTools.Method(typeof(MetaSystemManager<Clan, ClanData>), "beginChecksUnits");
            foreach (string name in new[] { "checkDirtyUnits", "checkEventUnitsDestroy", "checkEventBuildingsDestroy", "checkEventHouses", "checkSimManagerLists", "checkObjectsToDestroy" })
                yield return AccessTools.Method(typeof(MapBox), name);
            yield return AccessTools.Method(typeof(BuildingZonesSystem), "update");
            yield return AccessTools.Method(typeof(ActorManager), "prepareForMetaChecks");
            // 注意：泛型类型上的方法拿不到 __originalMethod，运行时会每帧抛异常，不能放进来
            yield return AccessTools.Method(typeof(BuildingManager), "update");
            yield return AccessTools.Method(typeof(ActorManager), "update");
            yield return AccessTools.Method(typeof(CityManager), "update");
            yield return AccessTools.Method(typeof(KingdomManager), "update");
            // 每个地图图层各自的 draw
            foreach (System.Type type in typeof(MapLayer).Assembly.GetTypes())
            {
                if (!typeof(MapLayer).IsAssignableFrom(type)) continue;
                MethodInfo draw = type.GetMethod("draw", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (draw != null && !draw.IsAbstract) yield return draw;
            }
        }

        public struct State
        {
            public long time;
            public long used;
            public int gc;
        }

        static State Now() => new State
        {
            time = Stopwatch.GetTimestamp(),
            used = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(),
            gc = System.GC.CollectionCount(0),
        };

        // 分配量：前后读托管堆占用（粒度 4KB，累计多帧后统计上可用），期间发生回收的那次丢弃
        static readonly Dictionary<string, long> _alloc = new Dictionary<string, long>();

        // 先读结束时的占用，再做任何可能分配内存的事（拼键名等），否则会把测量自己的分配算进去
        static long Delta(State pStart)
        {
            long used = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            if (System.GC.CollectionCount(0) != pStart.gc) return 0;
            return used - pStart.used;
        }

        static void AddAlloc(string pKey, long delta)
        {
            if (delta <= 0) return;
            _alloc.TryGetValue(pKey, out long v);
            _alloc[pKey] = v + delta;
        }

        static void TimerPrefix(out State __state) => __state = Now();

        static void TimerPostfix(State __state, MethodBase __originalMethod, object __instance)
        {
            if (!Running) return;
            long delta = Delta(__state);
            string name = Key(__originalMethod, __instance);
            Add("extra", name, (Stopwatch.GetTimestamp() - __state.time) / (double)Stopwatch.Frequency);
            AddAlloc(name, delta);
            _calls.TryGetValue(name, out int c);
            _calls[name] = c + 1;
        }

        static void PhasePrefix(out State __state) => __state = Now();
        static void PhasePrePostfix(State __state, object __instance) => Phase(__state, __instance, "pre");
        static void PhaseParallelPostfix(State __state, object __instance) => Phase(__state, __instance, "parallel");
        static void PhasePostPostfix(State __state, object __instance) => Phase(__state, __instance, "post");

        static void Phase(State pState, object pManager, string pPhase)
        {
            if (!Running) return;
            long ticks = Stopwatch.GetTimestamp() - pState.time;
            long delta = Delta(pState);
            var k = (pManager, pPhase);
            if (!_phase_keys.TryGetValue(k, out string key))
            {
                Type t = pManager.GetType();
                if (!_job_id.TryGetValue(t, out FieldInfo f)) _job_id[t] = f = AccessTools.Field(t, "id");
                _phase_keys[k] = key = "jobs " + f.GetValue(pManager) + "." + pPhase;
            }
            Add("extra", key, ticks / (double)Stopwatch.Frequency);
            AddAlloc(key, delta);
        }

        // 键名缓存：统计代码本身不能每次分配字符串，否则会算进外层方法的分配量
        static readonly Dictionary<object, string> _job_keys = new Dictionary<object, string>();
        static readonly Dictionary<(object, string), string> _phase_keys = new Dictionary<(object, string), string>();
        static readonly Dictionary<(MethodBase, Type), string> _method_keys = new Dictionary<(MethodBase, Type), string>();

        static string Key(MethodBase pMethod, object pInstance)
        {
            Type it = pInstance?.GetType();
            if (_method_keys.TryGetValue((pMethod, it), out string key)) return key;
            string owner = pInstance is MapLayer || pInstance is BaseSystemManager ? it.Name : pMethod.DeclaringType.Name;
            _method_keys[(pMethod, it)] = key = owner + "." + pMethod.Name;
            return key;
        }

        static readonly Dictionary<Type, FieldInfo> _job_id = new Dictionary<Type, FieldInfo>();

        static void JobPrefix(out State __state) => __state = Now();

        static void JobPostfix(State __state, object pObj)
        {
            if (!Running) return;
            long delta = Delta(__state);
            Type t = pObj.GetType();
            if (!_job_keys.TryGetValue(pObj, out string key))
            {
                if (!_job_id.TryGetValue(t, out FieldInfo f)) _job_id[t] = f = AccessTools.Field(t, "id");
                _job_keys[pObj] = key = "job " + f.GetValue(pObj);
            }
            AddAlloc(key, delta);
        }

        static readonly Dictionary<string, int> _calls = new Dictionary<string, int>();
    }
}
