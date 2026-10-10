using System.Collections.Generic;
using HarmonyLib;
using life.taxi;
using tools;

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

    // 跨海：每支军队单独一张打车单、单独一艘船，接人地点就在这支军队的队长脚下。
    // 不用原版的拼单：原版会把同岛出发的军队并成一单，只在第一个人那里接人。
    public static class Transport
    {
        public static void Dispatch(City pCity, WorldTile pTarget)
        {
            var units = new List<Actor>();
            foreach (Actor unit in pCity.army.units)
            {
                if (!unit.isRekt() && !unit.is_inside_boat) units.Add(unit);
            }
            if (units.Count == 0) return;

            // 把这些士兵从原来的单子里移出来
            foreach (Actor unit in units)
            {
                TaxiRequest old = TaxiManager.getRequestForActor(unit);
                if (old == null || old.isState(TaxiRequestState.Transporting)) continue;
                old.embarkToBoat(unit); // 原版用它把已上船的人从名单里移除，这里借来做移除
                if (old.countActors() == 0 && old.isState(TaxiRequestState.Pending)) TaxiManager.cancelRequest(old);
            }

            Actor lead = pCity.army.getCaptain();
            if (lead == null || lead.isRekt() || lead.is_inside_boat) lead = units[0];
            var request = new TaxiRequest(lead, pCity.kingdom, lead.current_tile, pTarget);
            foreach (Actor unit in units) request.addActor(unit);
            TaxiManager.list.Add(request);

            Boat boat = PickBoat(pCity.kingdom, lead.current_tile, pTarget);
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

        static bool IsIdleTransport(Actor pActor, out Boat pBoat)
        {
            pBoat = null;
            if (pActor.isRekt() || !pActor.asset.is_boat_transport) return false;
            pBoat = pActor.getSimpleComponent<Boat>();
            return pBoat != null && pBoat.taxi_request == null && !pBoat.hasPassengers();
        }

        // 港口所在水域能否开到目标岛边上
        static WorldTile OceanTileTowards(Docks pDocks, WorldTile pTarget)
        {
            if (pDocks.tiles_ocean.Count == 0) pDocks.recalculateOceanTiles();
            if (pDocks.tiles_ocean.Count == 0) return null;
            WorldTile ocean = pDocks.tiles_ocean[0];
            WorldTile landing = OceanHelper.findTileForBoat(ocean, pTarget);
            return landing != null && landing.isSameIsland(ocean) ? ocean : null;
        }

        // 原版规则：船的母港和军队在同一块大陆时，士兵要走到母港上船。
        // 所以先选离军队最近、能开到目标的港口，再给这个港口配一艘船。
        static Boat PickBoat(Kingdom pKingdom, WorldTile pArmy, WorldTile pTarget)
        {
            Docks port = null;
            WorldTile portOcean = null;
            int best = int.MaxValue;
            var allDocks = new List<Docks>();
            foreach (City city in pKingdom.getCities())
            {
                foreach (Building b in city.buildings)
                {
                    Docks docks = b.component_docks;
                    if (docks == null || b.isRekt()) continue;
                    allDocks.Add(docks);
                    if (!b.current_tile.isSameIsland(pArmy)) continue;
                    WorldTile ocean = OceanTileTowards(docks, pTarget);
                    if (ocean == null) continue;
                    int d = Dist(b.current_tile, pArmy);
                    if (d < best)
                    {
                        best = d;
                        port = docks;
                        portOcean = ocean;
                    }
                }
            }

            if (port == null)
            {
                // 军队所在大陆没有合适的港口：船会直接开到军队附近的海岸接人
                return NearestIdleBoat(pKingdom, pArmy, null) ?? Spawn(allDocks.Count > 0 ? allDocks.GetRandom() : null);
            }

            Boat boat = NearestIdleBoat(pKingdom, port.building.current_tile, portOcean);
            if (boat == null) return Spawn(port);
            if (boat.actor.getHomeBuilding() != port.building) port.addBoatToDock(boat.actor);
            return boat;
        }

        // pSameWater 不为空时，只找这片水域里的船（船能自己开过来）
        static Boat NearestIdleBoat(Kingdom pKingdom, WorldTile pNear, WorldTile pSameWater)
        {
            Boat best = null;
            int bestDist = int.MaxValue;
            foreach (City city in pKingdom.getCities())
            {
                foreach (Actor actor in city._boats)
                {
                    if (!IsIdleTransport(actor, out Boat boat)) continue;
                    if (pSameWater != null && !actor.current_tile.isSameIsland(pSameWater)) continue;
                    int d = Dist(actor.current_tile, pNear);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = boat;
                    }
                }
            }
            return best;
        }

        static Boat Spawn(Docks pDocks)
        {
            if (pDocks == null) return null;
            if (pDocks.tiles_ocean.Count == 0) pDocks.recalculateOceanTiles();
            if (pDocks.tiles_ocean.Count == 0) return null;
            City home = pDocks.building.city;
            string assetId = home?.getActorAsset().architecture_asset.actor_asset_id_transport;
            if (string.IsNullOrEmpty(assetId)) return null;
            Actor actor = World.world.units.createNewUnit(assetId, pDocks.tiles_ocean.GetRandom());
            if (actor == null) return null;
            pDocks.addBoatToDock(actor);
            return actor.getSimpleComponent<Boat>();
        }
    }

    // 士兵已经在一张有效的单子里时，不让原版 AI 再把他拼进别的单子
    [HarmonyPatch(typeof(TaxiManager), nameof(TaxiManager.newRequest))]
    static class Patch_TaxiNewRequest
    {
        static bool Prefix(Actor pActor)
        {
            TaxiRequest current = TaxiManager.getRequestForActor(pActor);
            return current == null || current.isState(TaxiRequestState.Finished);
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
