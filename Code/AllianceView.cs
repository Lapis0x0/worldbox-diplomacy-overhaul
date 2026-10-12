using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 联盟地图模式按阵营显示：附庸归入宗主的阵营
    // 宗主有联盟时阵营是联盟，否则是宗主国；染色、名牌、高亮、提示、点击都按阵营
    public static class AllianceView
    {
        public static void Init()
        {
            MetaTypeAsset a = MetaTypeLibrary.alliance;
            a.draw_zones = Draw;
            a.check_cursor_highlight = Highlight;
            a.check_tile_has_meta = HasMeta;
            a.check_cursor_tooltip = Tooltip;
            a.click_action_zone = Click;
        }

        // 王国所在的阵营：宗主的联盟，没有联盟就是宗主国
        public static NanoObject Faction(Kingdom k)
        {
            Kingdom root = Vassals.Root(k);
            return (NanoObject)root.getAlliance() ?? root;
        }

        static Kingdom KingdomOn(TileZone pZone)
        {
            City city = pZone?.city;
            if (city.isRekt()) return null;
            Kingdom k = city.kingdom;
            return k.isRekt() ? null : k;
        }

        static IMetaObject RootOnZone(TileZone pZone)
        {
            Kingdom k = KingdomOn(pZone);
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

        static void Highlight(MetaTypeAsset pAsset, WorldTile pTile, QuantumSpriteAsset pQAsset)
        {
            Kingdom hovered = KingdomOn(pTile.zone);
            if (hovered == null) return;
            NanoObject faction = Faction(hovered);
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (Faction(k) != faction) continue;
                foreach (City city in k.getCities()) QuantumSpriteLibrary.colorZones(pQAsset, city.zones, pQAsset.color);
            }
            if (PlayerConfig.optionBoolEnabled("highlight_kingdom_enemies")) QuantumSpriteLibrary.colorEnemies(pQAsset, hovered);
        }

        static bool HasMeta(TileZone pZone, MetaTypeAsset pAsset, int pZoneOption)
        {
            Kingdom k = KingdomOn(pZone);
            return k != null && Faction(k) is Alliance;
        }

        static bool Tooltip(TileZone pZone, MetaTypeAsset pAsset, int pZoneOption)
        {
            Kingdom k = KingdomOn(pZone);
            if (k == null) return false;
            NanoObject faction = Faction(k);
            if (faction is Alliance) pAsset.cursor_tooltip_action(faction);
            else MetaTypeLibrary.kingdom.cursor_tooltip_action(faction);
            return true;
        }

        static bool Click(WorldTile pTile, string pPower)
        {
            Kingdom k = KingdomOn(pTile?.zone);
            if (k == null || k.isNeutral()) return false;
            NanoObject faction = Faction(k);
            if (faction is Alliance) MetaTypeLibrary.alliance.selectAndInspect(faction);
            else MetaTypeLibrary.kingdom.selectAndInspect(faction);
            return true;
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

    // 联盟图层的名牌：每个联盟一个名牌，挂在视野内最强的成员或附庸首都上；
    // 没有联盟的集团只在宗主首都显示宗主国名，附庸不单独显示
    [HarmonyPatch(typeof(NameplateLibrary), "actionAlliance")]
    static class Patch_AllianceNameplates
    {
        static bool InCamera(Kingdom k) =>
            k.hasCapital() && World.world.move_camera.isWithinCameraViewNotPowerBar(k.capital.city_center);

        static bool Prefix(NameplateLibrary __instance, NameplateManager pManager, NameplateAsset pAsset)
        {
            foreach (Alliance alliance in World.world.alliances)
            {
                Kingdom best = null;
                foreach (Kingdom k in World.world.kingdoms)
                {
                    if (Vassals.Root(k).getAlliance() != alliance || !InCamera(k)) continue;
                    if (best == null || best.power < k.power) best = k;
                }
                if (best != null) pManager.prepareNext(pAsset, alliance).showTextAlliance(alliance, best.capital);
            }

            int count = 0;
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (Vassals.IsVassal(k) || k.hasAlliance() || !InCamera(k)) continue;
                if (count >= pAsset.max_nameplate_count) break;
                count++;
                pManager.prepareNext(__instance._plate_kingdom, k).showTextKingdom(k, k.capital.city_center);
            }
            return false;
        }
    }
}
