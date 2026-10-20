using System;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 联盟地图模式按阵营显示（见 Camp）：染色、名牌、高亮、提示、点击都按阵营
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
            NanoObject faction = Camp.Of(hovered);
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (Camp.Of(k) != faction) continue;
                foreach (City city in k.getCities()) QuantumSpriteLibrary.colorZones(pQAsset, city.zones, pQAsset.color);
            }
            if (PlayerConfig.optionBoolEnabled("highlight_kingdom_enemies")) QuantumSpriteLibrary.colorEnemies(pQAsset, hovered);
        }

        static bool HasMeta(TileZone pZone, MetaTypeAsset pAsset, int pZoneOption)
        {
            Kingdom k = KingdomOn(pZone);
            return k != null && Camp.Of(k) is Alliance;
        }

        static bool Tooltip(TileZone pZone, MetaTypeAsset pAsset, int pZoneOption)
        {
            Kingdom k = KingdomOn(pZone);
            if (k == null) return false;
            NanoObject faction = Camp.Of(k);
            if (faction is Alliance)
            {
                pAsset.cursor_tooltip_action(faction);
                return true;
            }
            _blocTooltip = true;
            try { MetaTypeLibrary.kingdom.cursor_tooltip_action(faction); }
            finally { _blocTooltip = false; }
            return true;
        }

        // 没有联盟的集团借用王国提示，人口和兵力换成整个集团的
        static bool _blocTooltip;

        internal static void BlocStats(Tooltip pTooltip, Kingdom pKingdom)
        {
            if (!_blocTooltip) return;
            SetIcon(pTooltip, "i_population", Camp.Population(pKingdom));
            SetIcon(pTooltip, "i_army", Camp.Warriors(pKingdom));
        }

        internal static void SetIcon(Tooltip pTooltip, string pName, int pValue)
        {
            StatsIcon icon = pTooltip.transform.FindRecursive(pName)?.GetComponent<StatsIcon>();
            if (icon != null) icon.setValue(pValue);
        }

        static bool Click(WorldTile pTile, string pPower)
        {
            Kingdom k = KingdomOn(pTile?.zone);
            if (k == null || k.isNeutral()) return false;
            NanoObject faction = Camp.Of(k);
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
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                if (__result != null) return;
                Kingdom k = __instance.city?.kingdom;
                if (k == null) return;
                Kingdom root = Vassals.Root(k);
                if (root != k) __result = root.getAlliance();
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "联盟地块", e); }
        }
    }

    // 联盟图层的名牌：每个联盟一个名牌，挂在视野内最强的成员或附庸首都上；
    // 没有联盟的集团只在宗主首都显示宗主国名，附庸不单独显示。名牌上的人口是整个阵营的
    [HarmonyPatch(typeof(NameplateLibrary), "actionAlliance")]
    static class Patch_AllianceNameplates
    {
        static readonly AccessTools.FieldRef<NameplateText, NameplateRenderingType> LastMode =
            AccessTools.FieldRefAccess<NameplateText, NameplateRenderingType>("_last_mode");
        static readonly System.Func<NameplateText, string, int, string> Format =
            AccessTools.MethodDelegate<System.Func<NameplateText, string, int, string>>(AccessTools.Method(typeof(NameplateText), "getStringForNameplate"));
        static readonly System.Action<NameplateText, string, Vector3, int> SetText =
            AccessTools.MethodDelegate<System.Action<NameplateText, string, Vector3, int>>(AccessTools.Method(typeof(NameplateText), "setText"));
        static readonly System.Action<NameplateText, int> SetPriority =
            AccessTools.MethodDelegate<System.Action<NameplateText, int>>(AccessTools.Method(typeof(NameplateText), "setPriority"));

        static bool InCamera(Kingdom k) =>
            k.hasCapital() && World.world.move_camera.isWithinCameraViewNotPowerBar(k.capital.city_center);

        // 原版写好名牌后，把人口换成整个阵营的；打开原版「显示战士数」时附上阵营的兵力
        static void CampText(NameplateText pPlate, NanoObject pCamp, string pName, Vector3 pPos)
        {
            int population = Camp.Population(pCamp);
            string text = Format(pPlate, pName, population);
            if (LastMode(pPlate) == NameplateRenderingType.Full && DebugConfig.isOn(DebugOption.ShowWarriorsCityText))
                text += " | " + Camp.Warriors(pCamp);
            SetText(pPlate, text, pPos, 10);
            SetPriority(pPlate, population);
        }

        static bool Prefix(NameplateLibrary __instance, NameplateManager pManager, NameplateAsset pAsset)
        {
            if (!Safety.On(Sys.Interface)) return true;
            try
            {
                foreach (Alliance alliance in World.world.alliances)
                {
                    Kingdom best = null;
                    foreach (Kingdom k in World.world.kingdoms)
                    {
                        if (Vassals.Root(k).getAlliance() != alliance || !InCamera(k)) continue;
                        if (best == null || best.power < k.power) best = k;
                    }
                    if (best == null) continue;
                    NameplateText plate = pManager.prepareNext(pAsset, alliance);
                    plate.showTextAlliance(alliance, best.capital);
                    CampText(plate, alliance, alliance.name, best.capital.city_center);
                }

                int count = 0;
                foreach (Kingdom k in World.world.kingdoms)
                {
                    if (Vassals.IsVassal(k) || k.hasAlliance() || !InCamera(k)) continue;
                    if (count >= pAsset.max_nameplate_count) break;
                    count++;
                    NameplateText plate = pManager.prepareNext(__instance._plate_kingdom, k);
                    plate.showTextKingdom(k, k.capital.city_center);
                    if (Vassals.HasVassals(k)) CampText(plate, k, Government.DisplayName(k), k.capital.city_center);
                }
                return false;
            }
            catch (Exception e)
            {
                Safety.Fail(Sys.Interface, "联盟名牌", e);
                return true;
            }
        }
    }

    // 联盟的悬停提示：人口和兵力算整个阵营，成员的附庸也算进去
    [HarmonyPatch(typeof(TooltipLibrary), "showAlliance")]
    static class Patch_AllianceTooltip
    {
        static void Postfix(Tooltip pTooltip, TooltipData pData)
        {
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                Alliance alliance = pData?.alliance;
                if (alliance == null) return;
                AllianceView.SetIcon(pTooltip, "i_population", Camp.Population(alliance));
                AllianceView.SetIcon(pTooltip, "i_army", Camp.Warriors(alliance));
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "联盟提示", e); }
        }
    }
}
