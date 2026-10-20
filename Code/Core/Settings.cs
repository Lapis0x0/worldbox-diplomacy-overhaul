using System.Collections.Generic;
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
        public const string GroupParliament = "vd_group_parliament";
        public const string KeyParliament = "vd_parliament_enabled";
        public const string GroupMilitary = "vd_group_military";
        public const string KeyMilitary = "vd_military_enabled";

        public static bool FeudalEnabled => Get(GroupVassal, "vd_feudal_enabled");
        public static bool PeaceEnabled => Get(GroupPeace, "vd_peace_enabled");
        public static bool CycleEnabled => Get(GroupCycle, "vd_cycle_enabled");
        public static bool GovernmentAutoEnabled => Get(GroupGovernment, "vd_gov_auto_enabled");
        public static bool GovernmentNameEnabled => Get(GroupGovernment, "vd_gov_name_enabled");
        public static bool GovernmentEventsEnabled => Get(GroupGovernment, "vd_gov_events_enabled");
        public static bool PerfEnabled => Get(GroupPerf, "vd_perf_enabled");
        public static bool ParliamentEnabled => Get(GroupParliament, KeyParliament);
        public static bool MilitaryEnabled => Get(GroupMilitary, KeyMilitary);

        // 政策每次读取都要看议会开关，读过的值记下来，改的时候一起改
        static readonly Dictionary<string, bool> _cache = new Dictionary<string, bool>();

        // 开关默认都是开的
        public static bool Get(string pGroup, string pId)
        {
            if (_cache.TryGetValue(pId, out bool v)) return v;
            try { v = Config[pGroup][pId].BoolVal; }
            catch { v = true; }
            _cache[pId] = v;
            return v;
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
            _cache[pId] = pValue;
            if (pId == KeyParliament) Parliament.OnToggle(pValue);
            if (pId == KeyMilitary) Military.OnToggle(pValue);
        }
    }
}
