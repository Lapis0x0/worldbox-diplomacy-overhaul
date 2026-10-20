using HarmonyLib;

namespace DiplomacyOverhaul.Perf
{
    // 野生王国的建筑名单（Kingdom.buildings）：任何野生建筑（包括每一棵树、每一株植物）生灭都会标脏，
    // 原版在下一个 tick 遍历全部建筑重建所有野生王国的名单，于是几乎每个 tick 都在全量重扫。
    // 原版只有两处读这份名单：遗迹刷老鼠、蔓延衰退检查，都是定时运行。这里改为在这两处运行前才重建
    static class WildBuildings
    {
        public static bool Forcing;

        public static void Ensure()
        {
            Forcing = true;
            try { World.world.kingdoms_wild.beginChecksBuildings(); }
            finally { Forcing = false; }
        }
    }

    [SafeBy("只读两个静态开关")]
    [HarmonyPatch(typeof(WildKingdomsManager), nameof(WildKingdomsManager.beginChecksBuildings))]
    static class WildBuildings_Skip
    {
        // 跳过时 _dirty_buildings 保持为 true，留到用的时候
        static bool Prefix() => !Opt.wild_buildings_lazy || WildBuildings.Forcing;
    }

    [HarmonyPatch(typeof(WorldBehaviourActions), nameof(WorldBehaviourActions.updateRuinRatSpawn))]
    static class WildBuildings_RuinRats
    {
        static void Prefix()
        {
            if (!Opt.wild_buildings_lazy) return;
            try { WildBuildings.Ensure(); }
            catch (System.Exception e) { Perf.Fail(nameof(Opt.wild_buildings_lazy), e); }
        }
    }

    [HarmonyPatch(typeof(WorldBehaviourActionCreepDecay), "checkBiome")]
    static class WildBuildings_CreepDecay
    {
        static void Prefix()
        {
            if (!Opt.wild_buildings_lazy) return;
            try { WildBuildings.Ensure(); }
            catch (System.Exception e) { Perf.Fail(nameof(Opt.wild_buildings_lazy), e); }
        }
    }
}
