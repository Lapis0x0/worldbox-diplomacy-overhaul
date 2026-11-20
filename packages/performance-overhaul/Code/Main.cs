using HarmonyLib;
using NeoModLoader.api;
using NeoModLoader.services;

namespace DiplomacyOverhaul.Perf
{
    // 独立的性能优化模组的入口。优化代码在发布时从外交大修的 Code/Perf/ 复制进来
    public class PerfMain : BasicMod<PerfMain>
    {
        const string OverhaulId = "lapis0x0_diplomacy_overhaul";
        const string HarmonyId = "lapis0x0.performance_overhaul";

        protected override void OnModLoad()
        {
            PerfHost.Config = GetConfig();
        }

        // 所有模组都加载完才调用。外交大修自带同一份优化，装了它就让位
        public override void Init()
        {
            base.Init();
            if (ModCompileLoadService.IsModLoaded(OverhaulId))
            {
                LogInfo("外交大修已自带性能优化，本模组不启用");
                return;
            }
            Perf.Start();
            new Harmony(HarmonyId).PatchAll(typeof(PerfMain).Assembly);
            LogInfo("性能大修 loaded");
        }
    }
}
