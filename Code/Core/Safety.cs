using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DiplomacyOverhaul
{
    // 模组的各个系统。补丁出错按系统记账（见 Safety）
    public enum Sys
    {
        Vassal,
        Peace,
        Government,
        Cycle,
        Parliament,
        Military,
        Economy,
        Interface
    }

    // 补丁和原版之间的边界：模组的代码不能把异常抛进原版。原版的每帧更新没有异常保护，
    // 抛进去的异常会跳过这一帧剩下的更新，条件不变就每帧如此（见 docs/dev/README.md 的「异常隔离」）。
    //
    // 补丁的写法：先问 Safety.On，系统停用了就照原版；自己的逻辑包在 try 里，出错交给 Safety.Fail 并照原版：
    //
    //   static bool Prefix(City __instance)
    //   {
    //       if (!Safety.On(Sys.Military)) return true;
    //       try { ... }
    //       catch (Exception e) { Safety.Fail(Sys.Military, "破城", e); return true; }
    //   }
    //
    // 同一个系统 30 秒内出错 5 次就熔断：本局停用这个系统的补丁，全部照原版，提示玩家一次；读档或新建世界后恢复。
    // 同一个 bug 通常会反复触发，开开停停反而容易留下做了一半的状态
    public static class Safety
    {
        const int TripCount = 5;
        const double TripWindowSeconds = 30;

        static readonly int Count = Enum.GetValues(typeof(Sys)).Length;
        static readonly bool[] _off = new bool[Count];
        static readonly Queue<double>[] _recent = Enumerable.Range(0, Count).Select(_ => new Queue<double>()).ToArray();
        // 熔断可能发生在并行线程（属性重算）上，提示留到主线程的下一帧再弹
        static readonly Queue<Sys> _pendingTips = new Queue<Sys>();
        static readonly Stopwatch _clock = Stopwatch.StartNew();
        static readonly object _lock = new object();

        public static bool On(Sys pSys) => !_off[(int)pSys];

        public static void Fail(Sys pSys, string pWhere, Exception pError)
        {
            Isolation.Report($"{Name(pSys)} · {pWhere}", pError);
            lock (_lock)
            {
                if (_off[(int)pSys]) return;
                double now = _clock.Elapsed.TotalSeconds;
                Queue<double> recent = _recent[(int)pSys];
                recent.Enqueue(now);
                while (recent.Count > 0 && now - recent.Peek() > TripWindowSeconds) recent.Dequeue();
                if (recent.Count < TripCount) return;
                _off[(int)pSys] = true;
                _pendingTips.Enqueue(pSys);
            }
            Debug.LogError($"[外交大修] {Name(pSys)} {TripWindowSeconds} 秒内出错 {TripCount} 次，本局已停用，改走原版逻辑");
        }

        // 每帧开头（主线程）
        public static void Tick()
        {
            lock (_lock)
            {
                while (_pendingTips.Count > 0)
                    WorldTip.showNow(LM.Get("vd_safety_off").Replace("$system$", Name(_pendingTips.Dequeue())), false, "top");
            }
        }

        // 读档、新建世界前
        public static void Reset()
        {
            lock (_lock)
            {
                Array.Clear(_off, 0, Count);
                foreach (Queue<double> q in _recent) q.Clear();
                _pendingTips.Clear();
            }
        }

        public static string Name(Sys pSys) => LM.Get(NameKey(pSys));

        static string NameKey(Sys pSys)
        {
            switch (pSys)
            {
                case Sys.Vassal: return "vd_group_vassal";
                case Sys.Peace: return "vd_group_peace";
                case Sys.Government: return "vd_group_government";
                case Sys.Cycle: return "vd_group_cycle";
                case Sys.Parliament: return "vd_group_parliament";
                case Sys.Military: return "vd_group_military";
                case Sys.Economy: return "vd_sys_economy";
                default: return "vd_sys_interface";
            }
        }

        // 开发时启动后检查：模组的每个前缀、后缀补丁都要自己兜住异常（方法体里有 catch；foreach、using 生成的 finally 不算），
        // 或者标上 [SafeBy] 说明为什么不需要。不合格的写进日志
        public static void Audit(Harmony pHarmony)
        {
            var bad = new List<string>();
            foreach (MethodBase original in pHarmony.GetPatchedMethods())
            {
                Patches info = Harmony.GetPatchInfo(original);
                foreach (Patch p in info.Prefixes.Concat(info.Postfixes))
                {
                    if (p.owner != pHarmony.Id || Guarded(p.PatchMethod)) continue;
                    bad.Add($"{p.PatchMethod.DeclaringType?.Name}.{p.PatchMethod.Name} → {original.DeclaringType?.Name}.{original.Name}");
                }
            }
            bad.Sort(StringComparer.Ordinal);
            foreach (string line in bad) Perf.Dev.Log("@unguarded " + line);
            Perf.Dev.Log($"@audit {bad.Count} 个补丁没有兜住异常");
        }

        static bool Guarded(MethodInfo pMethod) =>
            pMethod.IsDefined(typeof(SafeByAttribute)) || (pMethod.DeclaringType?.IsDefined(typeof(SafeByAttribute)) ?? false)
            || (pMethod.GetMethodBody()?.ExceptionHandlingClauses.Any(c => c.Flags == ExceptionHandlingClauseOptions.Clause) ?? false);
    }

    // 补丁不需要自己兜住异常的理由：异常在它调用的地方已经兜住，或者它只读一两个字段不会出错
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class SafeByAttribute : Attribute
    {
        public SafeByAttribute(string pReason) { }
    }
}
