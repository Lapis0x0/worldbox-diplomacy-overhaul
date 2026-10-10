using System.Collections.Generic;
using HarmonyLib;
using life.taxi;

namespace DiplomacyOverhaul
{
    // 玩家下达的进攻命令：直接改写城市的进攻目标，跨海时调度运输船
    public static class Orders
    {
        // 城市 id → 玩家指定的目标城市 id；目标失效后由原版 AI 接管
        static readonly Dictionary<long, long> _forced = new Dictionary<long, long>();

        public static bool IsForced(City pCity) => pCity != null && _forced.ContainsKey(pCity.id);

        public static void CheckForced(City pCity)
        {
            if (!_forced.TryGetValue(pCity.id, out long targetId)) return;
            if (pCity.target_attack_city == null || pCity.target_attack_city.id != targetId) _forced.Remove(pCity.id);
        }

        // 确保两国处于交战状态；返回 null 表示成功，否则返回失败原因的本地化 key
        public static string EnsureWar(Kingdom pAttacker, Kingdom pTarget)
        {
            if (pAttacker == null || pTarget == null || pAttacker == pTarget) return "vd_tip_invalid";
            if (Vassals.SameBloc(pAttacker, pTarget)) return "vd_tip_same_bloc";
            if (pAttacker.isEnemy(pTarget)) return null;
            if (Vassals.IsVassal(pAttacker)) return "vd_tip_vassal_no_war";
            if (World.world.diplomacy.startWar(pAttacker, pTarget, WarTypeLibrary.normal) == null) return "vd_tip_war_failed";
            return null;
        }

        static bool HasArmy(City pCity) => pCity.hasArmy() && pCity.army.countUnits() > 0;

        // 返回 null 表示成功，否则返回失败原因的本地化 key
        public static string Attack(City pFrom, City pTarget)
        {
            if (pFrom == null || pTarget == null || pFrom == pTarget) return "vd_tip_invalid";
            if (!HasArmy(pFrom)) return "vd_tip_no_army";
            string error = EnsureWar(pFrom.kingdom, pTarget.kingdom);
            if (error != null) return error;
            Order(pFrom, pTarget);
            return null;
        }

        // 举国进攻：所有有军队的城市都打同一个目标；返回参与的城市数
        public static int AttackWithKingdom(Kingdom pKingdom, City pTarget, out string pError)
        {
            pError = EnsureWar(pKingdom, pTarget?.kingdom);
            if (pError != null) return 0;
            int count = 0;
            foreach (City city in new List<City>(pKingdom.getCities()))
            {
                if (!HasArmy(city)) continue;
                Order(city, pTarget);
                count++;
            }
            if (count == 0) pError = "vd_tip_no_army";
            return count;
        }

        static void Order(City pFrom, City pTarget)
        {
            pFrom.target_attack_city = pTarget;
            // 原版在目标城市没有建筑时会把落点设成自己的城市，这里直接从目标城市的地块里选
            pFrom.target_attack_zone = pTarget.buildings.Count > 0
                ? pTarget.buildings.GetRandom().current_tile.zone
                : pTarget.zones.GetRandom();
            _forced[pFrom.id] = pTarget.id;

            Actor captain = pFrom.army.getCaptain();
            WorldTile from = (captain ?? pFrom.army.units[0]).current_tile;
            WorldTile to = pFrom.target_attack_zone.centerTile;
            if (from.isSameIsland(to))
            {
                if (captain != null) captain.setTask("warrior_army_leader_move_to_attack_target", true, false, true);
            }
            else
            {
                Transport.Dispatch(pFrom, to);
            }
        }
    }

    // 跨海：士兵立即登记打车请求，把最近的空闲运输船派过去；没有就在港口造一艘
    public static class Transport
    {
        public static void Dispatch(City pCity, WorldTile pTarget)
        {
            TaxiRequest request = null;
            foreach (Actor unit in new List<Actor>(pCity.army.units))
            {
                if (unit.isRekt() || unit.is_inside_boat) continue;
                TaxiManager.newRequest(unit, pTarget);
                request = request ?? TaxiManager.getRequestForActor(unit);
            }
            if (request == null || request.state != TaxiRequestState.Pending) return;

            Boat boat = FindIdleBoat(pCity.kingdom, request.getTileStart()) ?? SpawnBoat(pCity, request.getTileStart());
            if (boat == null)
            {
                WorldTip.showNow("vd_tip_no_boat", true, "top");
                return;
            }
            boat.taxi_request = request;
            request.assign(boat);
            boat.actor.setTask("boat_transport_go_load", true, false, true);
        }

        static int Dist(WorldTile a, WorldTile b) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);

        static Boat FindIdleBoat(Kingdom pKingdom, WorldTile pPickup)
        {
            Boat best = null;
            int bestDist = int.MaxValue;
            foreach (City city in pKingdom.getCities())
            {
                foreach (Actor actor in city._boats)
                {
                    if (actor.isRekt() || !actor.asset.is_boat_transport) continue;
                    Boat boat = actor.getSimpleComponent<Boat>();
                    if (boat == null || boat.taxi_request != null || boat.hasPassengers()) continue;
                    int d = Dist(actor.current_tile, pPickup);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = boat;
                    }
                }
            }
            return best;
        }

        // 优先选和军队同一座岛上的港口
        static Boat SpawnBoat(City pCity, WorldTile pPickup)
        {
            var sameIsland = new List<Docks>();
            var others = new List<Docks>();
            foreach (City city in pCity.kingdom.getCities())
            {
                foreach (Building b in city.buildings)
                {
                    Docks docks = b.component_docks;
                    if (docks == null || b.isRekt()) continue;
                    if (b.current_tile.isSameIsland(pPickup)) sameIsland.Add(docks);
                    else others.Add(docks);
                }
            }
            List<Docks> pool = sameIsland.Count > 0 ? sameIsland : others;
            if (pool.Count == 0) return null;

            Docks dock = pool.GetRandom();
            City home = dock.building.city;
            if (dock.tiles_ocean.Count == 0) dock.recalculateOceanTiles();
            if (dock.tiles_ocean.Count == 0) return null;

            string assetId = home.getActorAsset().architecture_asset.actor_asset_id_transport;
            if (string.IsNullOrEmpty(assetId)) return null;
            Actor actor = World.world.units.createNewUnit(assetId, dock.tiles_ocean.GetRandom());
            if (actor == null) return null;
            dock.addBoatToDock(actor);
            return actor.getSimpleComponent<Boat>();
        }
    }

    // 玩家命令的城市跳过"兵力达到上限 70% 才出兵"的限制
    [HarmonyPatch(typeof(City), nameof(City.isOkToSendArmy))]
    static class Patch_OkToSendArmy
    {
        static bool Prefix(City __instance, ref bool __result)
        {
            if (!Orders.IsForced(__instance) || !__instance.hasArmy()) return true;
            __result = true;
            return false;
        }
    }

    // 目标失效、被原版 AI 换掉后，命令自动解除
    [HarmonyPatch(typeof(ai.behaviours.CityBehCheckAttackZone), nameof(ai.behaviours.CityBehCheckAttackZone.execute))]
    static class Patch_CheckAttackZone
    {
        static void Postfix(City pCity) => Orders.CheckForced(pCity);
    }
}
