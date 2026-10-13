using NeoModLoader.api;

namespace DiplomacyOverhaul
{
    // 模组设置；ModConfig 由外壳在启动和热重载时传入
    public static class Settings
    {
        public static ModConfig Config;

        public static int OpinionBonus => Int("vd_group_vassal", "vd_opinion_bonus", 30);
        public static int RebelOpinion => Int("vd_group_vassal", "vd_rebel_opinion", 0);
        public static int RebelPowerRatio => Int("vd_group_vassal", "vd_rebel_power_ratio", 120);
        public static bool FeudalEnabled => Bool("vd_group_vassal", "vd_feudal_enabled", true);
        public static bool CapitulationEnabled => Bool("vd_group_capitulation", "vd_capitulation_enabled", true);
        public static int CapitulationMinYears => Int("vd_group_capitulation", "vd_capitulation_min_years", 5);
        public static int CapitulationCityLoss => Int("vd_group_capitulation", "vd_capitulation_city_loss", 50);
        public static int CapitulationPowerRatio => Int("vd_group_capitulation", "vd_capitulation_power_ratio", 200);
        public static bool CycleEnabled => Bool("vd_group_cycle", "vd_cycle_enabled", true);
        public static int RestorationChance => Int("vd_group_cycle", "vd_restoration_chance", 30);
        public static bool GovernmentAutoEnabled => Bool("vd_group_government", "vd_gov_auto_enabled", true);
        public static bool GovernmentNameEnabled => Bool("vd_group_government", "vd_gov_name_enabled", true);
        public static bool GovernmentEventsEnabled => Bool("vd_group_government", "vd_gov_events_enabled", true);
        public static bool PerfEnabled => Bool("vd_group_perf", "vd_perf_enabled", true);

        static int Int(string pGroup, string pId, int pDefault)
        {
            try { return Config[pGroup][pId].IntVal; }
            catch { return pDefault; }
        }

        static bool Bool(string pGroup, string pId, bool pDefault)
        {
            try { return Config[pGroup][pId].BoolVal; }
            catch { return pDefault; }
        }
    }
}
