using System.Collections.Generic;
using HarmonyLib;

namespace DiplomacyOverhaul.Perf
{
    // 城市建筑名单（City.buildings）：原版 CityManager.setDirtyBuildings(city) 忽略传入的城市，
    // 下一个 tick 把所有城市的名单从各自区域里全部重建一遍；城市区域里任何建筑（包括树）增减都会触发。
    // 这里只重建记下来的城市。原版靠全量重建掩盖了几处没有通知的变化，这里补上：
    // 区域划给新城市（原版只通知旧城市）、城市换王国、城市建筑的状态变化（遗迹、拆除、死亡）
    static class CityBuildings
    {
        static readonly HashSet<City> _dirty = new HashSet<City>();
        static readonly List<City> _snapshot = new List<City>();
        static bool _all = true;

        public static void ForceAll() => _all = true;

        public static void Mark(City pCity)
        {
            if (pCity == null) _all = true;
            else _dirty.Add(pCity);
        }

        public static void MarkBuilding(Building pBuilding)
        {
            if (!pBuilding.asset.city_building) return;
            City first = null;
            foreach (TileZone zone in pBuilding.zones)
            {
                if (zone.city == null) continue;
                _dirty.Add(zone.city);
                first ??= zone.city;
            }
            City city = pBuilding.current_tile?.zone.city;
            if (city != null)
            {
                _dirty.Add(city);
                first ??= city;
            }
            if (first != null) World.world.cities.setDirtyBuildings(first);
        }

        // 区域的 _dirty 字段不管 setDirty 被内联到哪里都会被设上，扫它比拦截方法调用可靠
        public static void ScanDirtyZones()
        {
            List<TileZone> zones = World.world.zone_calculator.zones;
            for (int i = 0; i < zones.Count; i++)
            {
                TileZone zone = zones[i];
                if (zone._dirty && zone.city != null) _dirty.Add(zone.city);
            }
        }

        public static bool Rebuild(CityManager pManager)
        {
            ScanDirtyZones();
            if (!Opt.city_buildings_partial || _all || Opt.verify)
            {
                _all = false;
                _dirty.Clear();
                return true;
            }
            // 重建中 setKingdomCiv 会连锁标脏；原版重建完把脏标记清零，这些标记也一并丢弃
            _snapshot.Clear();
            _snapshot.AddRange(_dirty);
            foreach (City city in _snapshot)
            {
                if (!city.isAlive()) continue;
                city.clearBuildingList();
                Kingdom kingdom = city.kingdom;
                for (int i = 0; i < city.zones.Count; i++)
                {
                    foreach (Building building in city.zones[i].buildings_all)
                    {
                        if (building.asset.city_building && building.isUsable())
                        {
                            building.setKingdomCiv(kingdom);
                            city.listBuilding(building);
                        }
                    }
                }
            }
            _dirty.Clear();
            return false;
        }

        // 验证：先按增量方式重建并记下结果，再让原版全量重建，逐个城市比较
        static readonly Dictionary<City, List<Building>> _ours = new Dictionary<City, List<Building>>();

        public static void VerifyBefore(CityManager pManager)
        {
            _ours.Clear();
            foreach (City city in pManager)
            {
                if (!_last.TryGetValue(city, out var last)) continue;
                bool same = last.list.Count == city.buildings.Count;
                for (int i = 0; same && i < last.list.Count; i++) same = last.list[i] == city.buildings[i];
                if (!same) Kind("list mutated between rebuilds");
                if (last.sig != Signature(city)) Kind(_dirty.Contains(city) ? "zones changed (marked)" : "zones changed (NOT marked)");
            }
            bool all = _all;
            _was_dirty.Clear();
            _was_dirty.UnionWith(_dirty);
            _all = false;
            bool saved = Opt.verify;
            Opt.verify = false;
            try { Rebuild(pManager); }
            finally { Opt.verify = saved; }
            if (all) return;
            foreach (City city in pManager)
                _ours[city] = new List<Building>(city.buildings);
            _kingdom_mismatch = 0;
            foreach (City city in pManager)
                for (int i = 0; i < city.zones.Count; i++)
                    foreach (Building building in city.zones[i].buildings_all)
                        if (building.asset.city_building && building.isUsable() && building.kingdom != city.kingdom) _kingdom_mismatch++;
        }

        static int _kingdom_mismatch;
        static readonly HashSet<City> _was_dirty = new HashSet<City>();
        static readonly Dictionary<string, int> _kinds = new Dictionary<string, int>();

        static string Describe(Building b) => $"{b.asset.id} alive={b.isAlive()} usable={b.isUsable()} state={b.data.state} anim={b.animation_state} zone_city={b.current_tile?.zone.city?.name}";

        static readonly Dictionary<City, (List<Building> list, string sig)> _last = new Dictionary<City, (List<Building>, string)>();

        static string Signature(City pCity)
        {
            var sb = new System.Text.StringBuilder();
            foreach (TileZone zone in pCity.zones)
            {
                sb.Append(zone.id).Append(':');
                foreach (Building b in zone.buildings_all) sb.Append(b.id).Append(',');
                sb.Append(';');
            }
            return sb.ToString();
        }

        static void Kind(string pKind)
        {
            _kinds.TryGetValue(pKind, out int n);
            _kinds[pKind] = n + 1;
        }

        public static void DumpKinds()
        {
            foreach (var kv in _kinds) Dev.Log($"  city mismatch kind: {kv.Key} x{kv.Value}");
            _kinds.Clear();
        }

        public static void VerifyAfter(CityManager pManager)
        {
            _last.Clear();
            foreach (City city in pManager) _last[city] = (new List<Building>(city.buildings), Signature(city));
            if (_ours.Count == 0) return;
            int diff = 0;
            string example = null;
            foreach (City city in pManager)
            {
                if (!_ours.TryGetValue(city, out List<Building> ours)) continue;
                List<Building> vanilla = city.buildings;
                bool same = ours.Count == vanilla.Count;
                for (int i = 0; same && i < ours.Count; i++) same = ours[i] == vanilla[i];
                if (same) continue;
                diff++;
                var a = new HashSet<Building>(ours);
                var b = new HashSet<Building>(vanilla);
                string kind;
                if (a.SetEquals(b)) kind = "order_only";
                else
                {
                    var extra = new List<Building>(a); extra.RemoveAll(b.Contains);
                    var missing = new List<Building>(b); missing.RemoveAll(a.Contains);
                    kind = extra.Count > 0 ? "extra: " + Describe(extra[0]) : "missing: " + Describe(missing[0]);
                }
                kind = (_was_dirty.Contains(city) ? "[rebuilt] " : "[kept] ") + kind;
                _kinds.TryGetValue(kind, out int n);
                _kinds[kind] = n + 1;
                example ??= $"city {city.name} ours={ours.Count} vanilla={vanilla.Count} {kind}";
            }
            if (_kingdom_mismatch > 0) example = (example ?? "") + $" kingdom_mismatch={_kingdom_mismatch}";
            Verifier.Report("city_buildings_partial", diff + (_kingdom_mismatch > 0 ? 1 : 0), _ours.Count, example);
            _ours.Clear();
        }
    }

    [HarmonyPatch(typeof(CityManager), "updateDirtyBuildings")]
    static class CityBuildings_Update
    {
        static bool Prefix(CityManager __instance)
        {
            if (Opt.verify && Opt.city_buildings_partial)
            {
                CityBuildings.VerifyBefore(__instance);
                return true;
            }
            // 出错时让原版全量重建，结果仍然正确
            try { return CityBuildings.Rebuild(__instance); }
            catch (System.Exception e) { Perf.Fail(nameof(Opt.city_buildings_partial), e); return true; }
        }

        static void Postfix(CityManager __instance)
        {
            if (Opt.verify && Opt.city_buildings_partial) CityBuildings.VerifyAfter(__instance);
        }
    }

    [HarmonyPatch(typeof(CityManager), nameof(CityManager.setDirtyBuildings))]
    static class CityBuildings_SetDirty
    {
        static void Prefix(City pCity) => CityBuildings.Mark(pCity);
    }

    // BuildingZonesSystem 会清掉区域的脏标记，先把脏区域所属的城市记下来
    [HarmonyPatch(typeof(BuildingZonesSystem), nameof(BuildingZonesSystem.update))]
    static class CityBuildings_ZoneSystem
    {
        static void Prefix()
        {
            if (Opt.city_buildings_partial && BuildingZonesSystem._dirty) CityBuildings.ScanDirtyZones();
        }
    }

    // 原版只通知被划走区域的旧城市
    [HarmonyPatch(typeof(TileZone), nameof(TileZone.setCity))]
    static class CityBuildings_ZoneCity
    {
        static void Prefix(TileZone __instance)
        {
            if (__instance.city != null) CityBuildings.Mark(__instance.city);
        }

        static void Postfix(City pCity)
        {
            if (pCity == null || !Opt.city_buildings_partial) return;
            CityBuildings.Mark(pCity);
            World.world.cities.setDirtyBuildings(pCity);
        }
    }

    // 城市状态刷新时会按到市中心的距离重排区域（List.Sort 不稳定，距离相同的区域先后会变），
    // 原版下一次全量重建就会得到不同顺序的名单；为了顺序一致，这里也记下
    [HarmonyPatch(typeof(City), "updateCityStatus")]
    static class CityBuildings_CityStatus
    {
        static void Postfix(City __instance) => CityBuildings.Mark(__instance);
    }

    [HarmonyPatch(typeof(City), nameof(City.setKingdom))]
    static class CityBuildings_CityKingdom
    {
        static void Postfix(City __instance)
        {
            if (!Opt.city_buildings_partial) return;
            CityBuildings.Mark(__instance);
            World.world.cities.setDirtyBuildings(__instance);
        }
    }

    [HarmonyPatch(typeof(Building), nameof(Building.setState))]
    static class CityBuildings_State
    {
        static void Postfix(Building __instance) => CityBuildings.MarkBuilding(__instance);
    }

    [HarmonyPatch(typeof(Building), nameof(Building.setAnimationState))]
    static class CityBuildings_AnimationState
    {
        static void Postfix(Building __instance, BuildingAnimationState pState)
        {
            if (pState == BuildingAnimationState.OnRemove) CityBuildings.MarkBuilding(__instance);
        }
    }

    // kill 会先清空 zones 再 setAlive(false)，所以在 kill 之前记下
    [HarmonyPatch(typeof(Building), nameof(Building.kill))]
    static class CityBuildings_Kill
    {
        static void Prefix(Building __instance)
        {
            if (__instance.isAlive()) CityBuildings.MarkBuilding(__instance);
        }
    }
}
