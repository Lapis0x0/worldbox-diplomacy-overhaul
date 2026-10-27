using System;
using System.Collections.Generic;
using HarmonyLib;
using life.taxi;
using tools;

namespace DiplomacyOverhaul
{
    // 跨海：每支军队单独一张打车单、单独一艘船，接人地点就在这支军队的队长脚下。
    // 不用原版的拼单：原版会把同岛出发的军队并成一单，只在第一个人那里接人。
    public static class Transport
    {
        // 国家 AI 调兵时不弹"没有运输船"的提示
        internal static bool Quiet;

        public static void Dispatch(Army pArmy, WorldTile pTarget)
        {
            Kingdom kingdom = Orders.KingdomOf(pArmy);
            if (kingdom == null) return;
            var units = new List<Actor>();
            foreach (Actor unit in pArmy.units)
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

            Actor lead = pArmy.getCaptain();
            if (lead == null || lead.isRekt() || lead.is_inside_boat) lead = units[0];
            var request = new TaxiRequest(lead, kingdom, lead.current_tile, pTarget);
            foreach (Actor unit in units) request.addActor(unit);
            TaxiManager.list.Add(request);

            Boat boat = PickBoat(kingdom, lead.current_tile, pTarget);
            if (boat == null)
            {
                if (!Quiet) WorldTip.showNow("vd_tip_no_boat", true, "top");
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
            if (!Safety.On(Sys.Military)) return true;
            try
            {
                if (!Military.On) return true;
                TaxiRequest current = TaxiManager.getRequestForActor(pActor);
                return current == null || current.isState(TaxiRequestState.Finished);
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "军队摆渡", e); return true; }
        }
    }
}
