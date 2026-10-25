using System;
using System.Collections.Generic;
using ai.behaviours;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 原版建筑盖完基本没有作用。这里在原版已有的环节上给几类建筑加效果，建设流程（选址、施工、一次建一座）照原版。
    // 同类建筑多盖的效果递减（见 Weight），国家营建多盖出来的建筑直接套用
    public static class Buildings
    {
        public const string Market = "type_market";
        public const string Temple = "type_temple";
        public const string Barracks = "type_barracks";
        public const string Tower = "type_watch_tower";
        public const string Statue = "type_statue";
        public const string Windmill = "type_windmill";

        public const float MarketShare = 0.5f;     // 市场：回家生钱多拿房子等级的几成
        public const int TempleLoyalty = 10;
        public const float BarracksArmy = 0.1f;    // 兵营：兵额多几成
        public const int TowerArrowsPerTier = 2;   // 箭塔：本国军械每高一档，一轮多射几支
        public const int StatueRenown = 1;         // 首都的雕像：国王每年的声望

        public static bool On => Settings.BuildingsEnabled;

        public static void Init()
        {
            Core.Upsert(AssetManager.loyalty_library, new LoyaltyAsset
            {
                id = "loyalty_vd_temple",
                translation_key = "loyalty_vd_temple",
                translation_key_negative = "loyalty_vd_temple",
                calc = TempleLoyaltyOf
            });
        }

        // 本城这类建筑建成、能用的座数
        public static int Count(City pCity, string pType)
        {
            List<Building> list = pCity.getBuildingListOfType(pType);
            if (list == null) return 0;
            int n = 0;
            foreach (Building b in list)
            {
                if (!b.isUnderConstruction() && b.isUsable()) n++;
            }
            return n;
        }

        // 效果倍数：第一座 1，之后每座是前一座的一半（1、1.5、1.75……）
        public static float Weight(int pCount) => pCount <= 0 ? 0f : 2f - 2f / (1 << Mathf.Min(pCount, 16));

        public static float Weight(City pCity, string pType) => On ? Weight(Count(pCity, pType)) : 0f;

        static int TempleLoyaltyOf(City pCity) => Mathf.RoundToInt(TempleLoyalty * Weight(pCity, Temple));

        // 同化：有神庙的城转化得快
        public static float AssimilationFactor(City pCity) => 1f + Weight(pCity, Temple);

        public static void UpdateAll()
        {
            if (!On) return;
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (!k.isAlive() || !k.isCiv() || !k.hasCapital() || !k.hasKing()) continue;
                int renown = Mathf.RoundToInt(StatueRenown * Weight(k.capital, Statue));
                if (renown > 0) k.king.addRenown(renown);
            }
        }

        // ---------- 营建：国家放宽的数量上限 ----------

        // 营建多给的名额，按建筑类型存在 city.data
        public static string WorksKey(string pType)
        {
            switch (pType)
            {
                case Market: return "vd_works_market";
                case Temple: return "vd_works_temple";
                case Barracks: return "vd_works_barracks";
                case Tower: return "vd_works_tower";
                case Statue: return "vd_works_statue";
                case Windmill: return "vd_works_windmill";
                default: return null;
            }
        }

        public static int Granted(City pCity, string pType)
        {
            string key = WorksKey(pType);
            if (key == null) return 0;
            pCity.data.get(key, out int n, 0);
            return n;
        }
    }

    // 市场：居民回家生钱时多拿一份。直接加到身上的钱里，不再播一次钱币特效
    [HarmonyPatch(typeof(BehGenerateLootFromHouse), nameof(BehGenerateLootFromHouse.execute))]
    static class Patch_MarketLoot
    {
        static void Postfix(Actor pActor, BehResult __result)
        {
            if (__result != BehResult.Continue || !Safety.On(Sys.Economy)) return;
            try
            {
                City city = pActor.city;
                if (city == null) return;
                float w = Buildings.Weight(city, Buildings.Market);
                if (w <= 0f) return;
                Building home = pActor.getHomeBuilding();
                if (home == null) return;
                int bonus = Mathf.Max(1, Mathf.RoundToInt(home.asset.loot_generation * Buildings.MarketShare * w));
                pActor.data.loot = Mathf.Clamp(pActor.data.loot + bonus, 0, 99999);
            }
            catch (Exception e) { Safety.Fail(Sys.Economy, "市场", e); }
        }
    }

    // 兵营：兵额 = 成年人口 × 这个倍数
    [HarmonyPatch(typeof(City), nameof(City.getArmyMaxMultiplier))]
    static class Patch_BarracksArmy
    {
        static void Postfix(City __instance, ref float __result)
        {
            if (!Safety.On(Sys.Economy)) return;
            try
            {
                float w = Buildings.Weight(__instance, Buildings.Barracks);
                if (w > 0f) __result *= 1f + Buildings.BarracksArmy * w;
            }
            catch (Exception e) { Safety.Fail(Sys.Economy, "兵营", e); }
        }
    }

    // 箭塔：一轮射几支箭按本国军械的档次增加。原版每次锁定目标时从建筑资料读这个数
    [HarmonyPatch(typeof(BuildingTower), "checkTargets")]
    static class Patch_TowerArrows
    {
        static void Postfix(BuildingTower __instance)
        {
            if (!__instance._shooting_active || !Buildings.On || !Safety.On(Sys.Economy)) return;
            try
            {
                Building b = __instance.building;
                if (b.asset.type != Buildings.Tower) return;
                Kingdom k = b.kingdom;
                if (k == null || !k.isCiv()) return;
                __instance._shooting_amount += Buildings.TowerArrowsPerTier * Armory.TierOf(k);
            }
            catch (Exception e) { Safety.Fail(Sys.Economy, "箭塔", e); }
        }
    }

    // 风车：原版只在城里第一座风车附近开农田，这里认所有风车。原版集合自己去重
    [HarmonyPatch(typeof(CityBehCheckFarms), "behFindTileForFarm")]
    static class Patch_Windmills
    {
        static bool Prefix(City pCity)
        {
            if (!Buildings.On || !Safety.On(Sys.Economy)) return true;
            try
            {
                List<Building> list = pCity.getBuildingListOfType(Buildings.Windmill);
                if (list == null || list.Count <= 1) return true;
                foreach (Building w in list)
                {
                    if (w.isUnderConstruction() || !w.isUsable()) continue;
                    MapRegion region = w.current_tile.region;
                    CityBehCheckFarms.checkRegion(region, w, pCity);
                    foreach (MapRegion r in region.neighbours) CityBehCheckFarms.checkRegion(r, w, pCity);
                }
                return false;
            }
            catch (Exception e)
            {
                Safety.Fail(Sys.Economy, "风车", e);
                return true;
            }
        }
    }

    // 营建：国家放宽的数量上限。原版上限 0 表示不限，不动
    [HarmonyPatch(typeof(City), nameof(City.getLimitOfBuildingsType))]
    static class Patch_WorksLimit
    {
        static void Postfix(City __instance, BuildOrder pElement, ref int __result)
        {
            if (__result == 0 || !Buildings.On || !Safety.On(Sys.Economy)) return;
            try
            {
                __result += Buildings.Granted(__instance, pElement.getBuildingAsset(__instance).type);
            }
            catch (Exception e) { Safety.Fail(Sys.Economy, "营建名额", e); }
        }
    }
}
