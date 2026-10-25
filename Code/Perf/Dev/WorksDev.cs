using System.Linq;

namespace DiplomacyOverhaul.Perf
{
    // 开发命令 works：建筑增幅和营建
    //   works stats        全世界各类建筑有几座城有、每城的领地格子数分布
    //   works [国家]        一个国家每座城的各类建筑数、营建名额、增幅、需要程度，定下的项目
    //   works run          立即跑一次营建
    //   works why <城名>    这座城每类建筑在原版建造判定里卡在哪：上限、前置、材料、找不找得到地方
    static class WorksDev
    {
        static readonly string[] Types =
            { Buildings.Market, Buildings.Temple, Buildings.Barracks, Buildings.Tower, Buildings.Statue, Buildings.Windmill };

        public static void Run(string[] a)
        {
            string mode = a.Length > 1 ? a[1] : null;
            if (mode == "run")
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Works.UpdateAll();
                Dev.Log($"  ran {sw.Elapsed.TotalMilliseconds:F2}ms");
                sw.Restart();
                foreach (Kingdom x in World.world.kingdoms) if (x.isAlive() && x.isCiv()) Treasury.Reserve(x);
                Dev.Log($"  reserve all {sw.Elapsed.TotalMilliseconds:F2}ms");
                sw.Restart();
                foreach (City c in World.world.cities.list) if (c.isAlive()) c.getLoyalty(true);
                Dev.Log($"  loyalty all {sw.Elapsed.TotalMilliseconds:F2}ms cities={World.world.cities.list.Count}");
                return;
            }
            if (mode == "why" && a.Length > 2)
            {
                foreach (City c in World.world.cities.list.Where(x => x.isAlive() && x.name == a[2])) Why(c);
                return;
            }
            if (mode == "stats")
            {
                var cities = World.world.cities.list.Where(c => c.isAlive() && c.kingdom != null && c.kingdom.isCiv()).ToList();
                Dev.Log($"  cities={cities.Count} on={Buildings.On}");
                foreach (string t in Types)
                    Dev.Log($"    {t}: cities={cities.Count(c => Buildings.Count(c, t) > 0)} total={cities.Sum(c => Buildings.Count(c, t))}");
                var zones = cities.Select(c => c.zones.Count).OrderBy(z => z).ToList();
                if (zones.Count > 0)
                    Dev.Log($"    zones min={zones[0]} p25={zones[zones.Count / 4]} median={zones[zones.Count / 2]} p75={zones[zones.Count * 3 / 4]} max={zones[zones.Count - 1]}");
                return;
            }
            Kingdom k = mode == null ? World.world.kingdoms.OrderByDescending(x => x.countCities()).FirstOrDefault()
                : long.TryParse(mode, out long id) ? World.world.kingdoms.get(id) : World.world.kingdoms.FirstOrDefault(x => x.data.name == mode);
            if (k == null) { Dev.Log("  no kingdom"); return; }
            k.data.get("vd_works_city", out long pc, -1L);
            k.data.get("vd_works_type", out string pt, null);
            k.data.get("vd_works_since", out int ps, 0);
            Dev.Log($"  [{k.id}] {k.data.name} tier={Armory.TierOf(k)} treasury={Treasury.Get(k)} reserve={Treasury.Reserve(k)}"
                    + $" fiscal={Policies.Value(k, Policies.Arms)} worksLast={Ledger.LastYear(k, Ledger.Flow.Works)} worksThis={Ledger.ThisYear(k, Ledger.Flow.Works)}"
                    + $" project={(pc >= 0 ? World.world.cities.get(pc)?.name + "/" + pt + "@" + ps : "-")}");
            foreach (City c in k.getCities())
            {
                string counts = string.Join(" ", Types.Select(t => $"{t.Substring(5)}={Buildings.Count(c, t)}+{Buildings.Granted(c, t)}"));
                Dev.Log($"    {c.name}: zones={c.zones.Count} pop={c.getPopulationPeople()} gold={c.getResourcesAmount("gold")} {counts}"
                        + $" templeLoyalty={UnityEngine.Mathf.RoundToInt(Buildings.TempleLoyalty * Buildings.Weight(c, Buildings.Temple))}"
                        + $" army={c.getArmyMaxMultiplier():0.00} slots={c.getMaxWarriors()} need={Need(c):0.00} loyalty={c.getLoyalty()} tax={Ledger.LastYear(c)}");
            }
        }

        static void Why(City c)
        {
            Dev.Log($"  {c.name} [{c.kingdom?.data.name}] gold={c.getResourcesAmount("gold")} wood={c.getResourcesAmount("wood")} stone={c.getResourcesAmount("stone")} metals={c.getResourcesAmount("common_metals")}"
                    + $" buildings={c.buildings.Count} pop={c.status.population} constructing={c.under_construction_building?.asset.id ?? "-"} timer={c.timer_build:0.0}");
            var orders = AssetManager.city_build_orders.get(c.getActorAsset().build_order_template_id);
            foreach (BuildOrder o in orders.list)
            {
                if (o.upgrade) continue;
                BuildingAsset b = o.getBuildingAsset(c);
                if (!Types.Contains(b.type)) continue;
                bool use = ai.behaviours.CityBehBuild.canUseBuildAsset(o, c);
                bool res = ai.behaviours.CityBehBuild.hasResourcesForBuildAsset(o, c);
                c.data.get(Buildings.WorksKey(b.type) + "_blocked", out int blocked, 0);
                bool site = Site(c, b);
                bool house = ai.behaviours.CityBehBuild.getOnHouseTile(c, b) != null;
                Dev.Log($"    {b.type} {b.id}: count={c.countBuildingsType(b.type, false)} limit={c.getLimitOfBuildingsType(o)} granted={Buildings.Granted(c, b.type)} blocked={blocked} site={site} onHouse={house} canUse={use} resources={res}"
                        + $" cost=g{b.cost.gold}/w{b.cost.wood}/s{b.cost.stone}/m{b.cost.common_metals}");
            }
        }

        // 照原版 tryToBuild 找开工的地块，不真的盖
        static bool Site(City c, BuildingAsset b)
        {
            if (b.type == "type_training_dummies") return ai.behaviours.CityBehBuild.getTileTrainingDummy(b, c) != null;
            if (b.docks) return ai.behaviours.CityBehBuild.getDockTile(c) != null;
            if (b.build_prefer_replace_house && ai.behaviours.CityBehBuild.getOnHouseTile(c, b) != null) return true;
            var zones = new System.Collections.Generic.List<TileZone>();
            var m = HarmonyLib.AccessTools.Method(typeof(ai.behaviours.CityBehBuild), "fillPossibleZones");
            m.Invoke(null, new object[] { b, c, zones });
            if (zones.Count == 0) return false;
            WorldTile t = null;
            if (b.build_place_center) t = ai.behaviours.CityBehBuild.tryToBuildInZones(zones, b, c, true);
            if (t == null) t = ai.behaviours.CityBehBuild.tryToBuildInZones(zones, b, c);
            return t != null;
        }

        static float Need(City c)
        {
            c.data.get("vd_works_need", out float v, 0f);
            return v;
        }
    }
}
