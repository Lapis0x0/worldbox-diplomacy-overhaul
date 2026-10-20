using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul.Perf
{
    // 性能优化的总入口。每项优化都只减少原版的重复计算，不改变游戏结果，规则和验证方法见 docs/performance.md。
    // 玩家在设置里只有一个总开关；某项优化运行时出错，就只关掉这一项，之后走原版逻辑
    public static class Perf
    {
        static bool _enabled;
        static readonly HashSet<string> _failed = new HashSet<string>();

        public static void Start(string pFolder)
        {
            Apply(Settings.PerfEnabled);
            Dev.Start(pFolder);
        }

        public static void Stop()
        {
            Dev.Stop();
        }

        // 每帧读一次设置，总开关变了就整体切换
        internal static void Tick()
        {
            bool on = Settings.PerfEnabled;
            if (on != _enabled) Apply(on);
        }

        static void Apply(bool pOn)
        {
            // 关闭期间原版自己维护名单，增量记录会漏；重新打开时先整体重建一次
            if (pOn && !_enabled)
            {
                CityBuildings.ForceAll();
                SimZones.Invalidate();
                Favorites.SetDirty();
            }
            _enabled = pOn;
            foreach (FieldInfo field in Opt.All())
            {
                if (field.Name == nameof(Opt.verify)) continue;
                bool failed;
                lock (_failed) failed = _failed.Contains(field.Name);
                field.SetValue(null, pOn && !failed);
            }
        }

        // 可能在并行任务的线程上调用
        public static void Fail(string pOpt, Exception pError)
        {
            lock (_failed)
            {
                if (!_failed.Add(pOpt)) return;
            }
            Opt.Set(pOpt, false);
            Debug.LogError($"[外交大修] 性能优化 {pOpt} 出错，已关闭这一项，改用原版逻辑：{pError}");
        }
    }

    [SafeBy("只读设置开关")]
    [HarmonyPatch(typeof(MapBox), "Update")]
    static class Perf_Tick
    {
        static void Prefix() => Perf.Tick();
    }
}
