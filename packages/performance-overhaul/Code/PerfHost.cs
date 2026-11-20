using System;
using NeoModLoader.api;
using UnityEngine;

namespace DiplomacyOverhaul.Perf
{
    // 性能优化对所在模组的全部依赖，外交大修那边是 Code/Core/PerfHost.cs
    public static class PerfHost
    {
        public const string Name = "性能大修";

        internal static ModConfig Config;

        // 每帧读一次；读不到按默认开
        public static bool Enabled
        {
            get
            {
                try { return Config["vd_group_perf"]["vd_perf_enabled"].BoolVal; }
                catch { return true; }
            }
        }

        // 只有对照验证会写，独立模组里不开验证
        public static void Log(string pLine) => Debug.Log($"[{Name}] {pLine}");
    }
}

namespace DiplomacyOverhaul
{
    // 外交大修用它标出不需要自己兜住异常的补丁，这里只为让同一份代码能编译
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class SafeByAttribute : Attribute
    {
        public SafeByAttribute(string pReason) { }
    }
}
