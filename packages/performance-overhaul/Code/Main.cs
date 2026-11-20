using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace DiplomacyOverhaul.Perf
{
    // 独立的性能优化模组的入口。优化代码在发布时从外交大修的 Code/Perf/ 复制进来
    public class PerfMain : BasicMod<PerfMain>
    {
        // NML 的 UID 是 mod.json 里的 GUID 转成大写
        const string OverhaulUid = "LAPIS0X0_DIPLOMACY_OVERHAUL";
        const string HarmonyId = "lapis0x0.performance_overhaul";

        // 外交大修自带同一份优化，装了它就不挂补丁。mod.json 把它列为可选依赖，装了的话一定先于本模组加载
        protected override void OnModLoad()
        {
            if (ModCompileLoadService.IsModLoaded(OverhaulUid))
            {
                LogInfo("外交大修已自带性能优化，本模组不启用");
                return;
            }
            PerfHost.Config = GetConfig();
            Perf.Start();
            new Harmony(HarmonyId).PatchAll(typeof(PerfMain).Assembly);
            LogInfo("性能大修 loaded");
        }
    }
}
