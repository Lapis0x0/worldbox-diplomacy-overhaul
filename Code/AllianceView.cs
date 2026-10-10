using HarmonyLib;

namespace DiplomacyOverhaul
{
    // 联盟地图模式：附庸显示为宗主所在联盟的颜色；宗主没有联盟时，整个集团用宗主国的颜色
    public static class AllianceView
    {
        public static void Init()
        {
            MetaTypeLibrary.alliance.draw_zones = Draw;
        }

        static IMetaObject RootOnZone(TileZone pZone)
        {
            Kingdom k = pZone?.city?.kingdom;
            return k == null ? null : Vassals.Root(k);
        }

        static void Draw(MetaTypeAsset pAsset)
        {
            ZoneCalculator zones = World.world.zone_calculator;
            int option = pAsset.getZoneOptionState();
            foreach (Kingdom k in World.world.kingdoms)
            {
                Kingdom root = Vassals.Root(k);
                bool hasAlliance = root.getAlliance() != null;
                bool inBloc = root != k || Vassals.HasVassals(k);
                foreach (City city in k.getCities())
                {
                    foreach (TileZone zone in city.zones)
                    {
                        zones.drawBegin();
                        if (hasAlliance) zones.drawZoneAlliance(zone, option);
                        else if (inBloc) zones.drawZoneMeta(zone, MetaTypeLibrary.kingdom, RootOnZone);
                        else zones.drawZoneCity(zone);
                        zones.drawEnd(zone);
                    }
                }
            }
        }
    }

    // 联盟上色与边界判断都通过这里取联盟；附庸借用宗主的联盟
    [HarmonyPatch(typeof(TileZone), nameof(TileZone.getAllianceOnZone))]
    static class Patch_AllianceOnZone
    {
        static void Postfix(TileZone __instance, ref Alliance __result)
        {
            if (__result != null) return;
            Kingdom k = __instance.city?.kingdom;
            if (k == null) return;
            Kingdom root = Vassals.Root(k);
            if (root != k) __result = root.getAlliance();
        }
    }
}
