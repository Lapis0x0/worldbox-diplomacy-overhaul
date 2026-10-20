using NeoModLoader.api;

namespace DiplomacyOverhaul
{
    // 模组设置：只有各系统的开关。ModConfig 由外壳在启动和热重载时传入，改了马上存盘
    public static class Settings
    {
        public static ModConfig Config;

        public const string GroupVassal = "vd_group_vassal";
        public const string GroupPeace = "vd_group_peace";
        public const string GroupGovernment = "vd_group_government";
        public const string GroupCycle = "vd_group_cycle";
        public const string GroupPerf = "vd_group_perf";

        public static bool FeudalEnabled => Get(GroupVassal, "vd_feudal_enabled");
        public static bool PeaceEnabled => Get(GroupPeace, "vd_peace_enabled");
        public static bool CycleEnabled => Get(GroupCycle, "vd_cycle_enabled");
        public static bool GovernmentAutoEnabled => Get(GroupGovernment, "vd_gov_auto_enabled");
        public static bool GovernmentNameEnabled => Get(GroupGovernment, "vd_gov_name_enabled");
        public static bool GovernmentEventsEnabled => Get(GroupGovernment, "vd_gov_events_enabled");
        public static bool PerfEnabled => Get(GroupPerf, "vd_perf_enabled");

        // 开关默认都是开的
        public static bool Get(string pGroup, string pId)
        {
            try { return Config[pGroup][pId].BoolVal; }
            catch { return true; }
        }

        public static void Set(string pGroup, string pId, bool pValue)
        {
            try
            {
                Config[pGroup][pId].SetValue(pValue);
                Config.Save();
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning("[外交大修] 保存设置失败：" + pGroup + "/" + pId + " " + e.Message);
            }
        }
    }
}
