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

        // 原版士兵只在城市有进攻目标时才跟随队长；驻扎、扎营清掉了进攻目标，士兵会留在原地各干各的，
        // 只有队长一个人过去。把"跟随队长"决策的启动条件换成：有进攻目标或者在驻扎、扎营。
        // 重复执行只是重新赋值，热重载安全
        public static void Init()
        {
            DecisionAsset follow = AssetManager.decisions_library.get("warrior_army_follow_leader");
            if (follow != null) follow.action_check_launch = FollowLeader;
        }

        // 除最后一条外照抄原版
        static bool FollowLeader(Actor pActor)
        {
            if (!pActor.hasCity() || !pActor.isArmyGroupWarrior() || !pActor.army.hasCaptain()) return false;
            if (!pActor.current_tile.isSameIsland(pActor.army.getCaptain().current_tile)) return false;
            return pActor.city.hasAttackZoneOrder() || HasStation(pActor.city);
        }

        public static void CheckForced(City pCity)
        {
            if (!_forced.TryGetValue(pCity.id, out long targetId)) return;
            if (pCity.target_attack_city == null || pCity.target_attack_city.id != targetId) _forced.Remove(pCity.id);
        }

        // 确保两国处于交战状态；返回 null 表示成功，否则返回失败原因的本地化 key
        public static string EnsureWar(Kingdom pAttacker, Kingdom pTarget)
        {
            if (pAttacker == null || pTarget == null || pAttacker == pTarget) return "vd_tip_invalid";
            if (pAttacker.isEnemy(pTarget)) return null;
            // 附庸不能对第三国宣战；对自己集团开战则允许，开战后附庸关系自动解除
            if (Vassals.IsVassal(pAttacker) && !Vassals.SameBloc(pAttacker, pTarget)) return "vd_tip_vassal_no_war";
            if (World.world.diplomacy.startWar(pAttacker, pTarget, WarTypeLibrary.normal) == null) return "vd_tip_war_failed";
            return null;
        }

        static bool HasArmy(City pCity) => pCity.hasArmy() && pCity.army.countUnits() > 0;

        // 本国、同一集团（宗主和附庸）、同一联盟：点它们的城市是去驻扎守城，不是宣战
        public static bool Friendly(Kingdom a, Kingdom b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;
            if (a.isEnemy(b)) return false;
            return Vassals.SameBloc(a, b) || Alliance.isSame(a.getAlliance(), b.getAlliance());
        }

        // 驻扎：军队所属城市 → 驻扎的城市 id，存在城市数据里，读档后照样守着。可以驻扎在附庸、宗主、盟友的城里。
        // 驻扎期间原版 AI 不给这支军队挑进攻目标：平时队长在城中心附近走动，敌军进城就去打最近的那个；
        // 下进攻命令或再点一次驻扎城解除
        const string KeyGarrison = "vd_garrison";

        public static City GetGarrison(City pCity)
        {
            if (pCity?.data == null) return null;
            pCity.data.get(KeyGarrison, out long id, -1L);
            if (id < 0) return null;
            City target = World.world.cities.get(id);
            if (target != null && !target.isRekt() && Friendly(target.kingdom, pCity.kingdom)) return target;
            // 驻扎城丢了，或者那个国家不再是友方
            pCity.data.removeLong(KeyGarrison);
            return null;
        }

        // 扎营：军队所属城市 → 扎营的地块 id。玩家让军队停在城外的空地上，和驻扎一样不挑进攻目标，
        // 队长只在那块地附近走动
        const string KeyHold = "vd_hold";
        const int HoldRadius = 4;

        public static WorldTile GetHold(City pCity)
        {
            if (pCity?.data == null) return null;
            pCity.data.get(KeyHold, out long id, -1L);
            if (id < 0) return null;
            WorldTile[] tiles = World.world.tiles_list;
            if (id < tiles.Length && CanHold(tiles[id])) return tiles[id];
            // 地块变成了水或山
            pCity.data.removeLong(KeyHold);
            return null;
        }

        public static bool CanHold(WorldTile pTile) => pTile != null && !pTile.Type.liquid && !pTile.Type.block;

        static bool NearHold(WorldTile pTile, WorldTile pHold) =>
            System.Math.Abs(pTile.x - pHold.x) <= HoldRadius && System.Math.Abs(pTile.y - pHold.y) <= HoldRadius;

        // 驻扎城市和扎营地块二选一，都为 null 时交还原版 AI
        static void SetStation(City pCity, City pGarrison, WorldTile pHold)
        {
            if (pGarrison == null) pCity.data.removeLong(KeyGarrison);
            else pCity.data.set(KeyGarrison, pGarrison.id);
            if (pHold == null) pCity.data.removeLong(KeyHold);
            else pCity.data.set(KeyHold, (long)pHold.tile_id);
        }

        public static bool HasStation(City pCity) => GetGarrison(pCity) != null || GetHold(pCity) != null;

        // 驻扎：城里有敌人就去最近的敌人那里，没有就在城中心附近挑；扎营：在那块地附近挑
        static WorldTile StationTile(City pCity, WorldTile pFrom)
        {
            City garrison = GetGarrison(pCity);
            if (garrison != null)
            {
                WorldTile enemy = Siege.NearestIntruder(pFrom, garrison, pCity.kingdom);
                if (enemy != null) return enemy;
                WorldTile center = garrison.getTile();
                if (center != null) return Around(center);
                return garrison.zones.Count > 0 ? garrison.zones.GetRandom().tiles.GetRandom() : null;
            }
            WorldTile hold = GetHold(pCity);
            return hold != null ? Around(hold) : null;
        }

        static WorldTile Around(WorldTile pCenter)
        {
            WorldTile tile = World.world.GetTile(pCenter.x + Randy.randomInt(-HoldRadius, HoldRadius + 1),
                pCenter.y + Randy.randomInt(-HoldRadius, HoldRadius + 1));
            return tile != null && tile.isSameIsland(pCenter) ? tile : pCenter;
        }

        // 驻扎、扎营中的队长随机走动时，从驻地挑地块；还没过海时交给原版，免得走离接人的地方
        public static bool TakeStationTile(Actor pActor, out WorldTile pTile)
        {
            pTile = null;
            if (!pActor.isArmyGroupLeader()) return false;
            WorldTile tile = StationTile(pActor.city, pActor.current_tile);
            if (tile == null || !pActor.current_tile.isSameIsland(tile)) return false;
            pTile = tile;
            return true;
        }

        // 原版队长每做完一件事就随机挑下一件（闲逛、等士兵、社交……），"往驻地走"只是其中之一，
        // 路远时几乎不动。队长还没到驻地、也没在打仗或坐船时，强制它继续赶路
        public static void PushToStation(City pCity)
        {
            Actor captain = pCity.army?.getCaptain();
            if (captain == null || captain.isRekt() || captain.is_inside_boat || captain.isFighting()) return;
            City garrison = GetGarrison(pCity);
            if (garrison != null)
            {
                // 敌军进城：不管队长在走还是在等，重新出发去打最近的那个（敌人会动，每次都重新挑）
                if (Siege.NearestIntruder(captain.current_tile, garrison, pCity.kingdom) != null)
                {
                    captain.setTask("warrior_army_leader_move_random", true, false, true);
                    return;
                }
                if (captain.current_tile.zone.city == garrison) return;
            }
            WorldTile hold = GetHold(pCity);
            if (hold != null && NearHold(captain.current_tile, hold)) return;
            if (captain.ai?.task?.id == "warrior_army_leader_move_random") return;
            captain.setTask("warrior_army_leader_move_random", true, false, true);
        }

        // 点的正是这支军队驻扎的城、或扎营的地方附近
        public static bool IsStationedAt(City pFrom, WorldTile pTile)
        {
            City garrison = GetGarrison(pFrom);
            if (garrison != null) return pTile.zone.city == garrison;
            WorldTile hold = GetHold(pFrom);
            return hold != null && TargetCity(pTile) == null && NearHold(pTile, hold);
        }

        // 选中的军队已经驻扎在这座城、或扎营在这附近：解除，交还原版 AI
        public static bool Release(City pFrom, WorldTile pTile)
        {
            if (!IsStationedAt(pFrom, pTile)) return false;
            SetStation(pFrom, null, null);
            return true;
        }

        // 召回：队长 → 召回城市里的目标地块，由 Patch_RallyTile 在原版"随机走动"任务里取用
        static readonly Dictionary<Actor, WorldTile> _rally = new Dictionary<Actor, WorldTile>();

        public static bool TakeRallyTile(Actor pActor, out WorldTile pTile)
        {
            if (!_rally.TryGetValue(pActor, out pTile)) return false;
            _rally.Remove(pActor);
            return true;
        }

        // 点的是本国或友方的城市：军队前往那座城并驻扎
        public static string Recall(City pFrom, City pDest)
        {
            if (pFrom == null || pDest == null || !Friendly(pFrom.kingdom, pDest.kingdom)) return "vd_tip_invalid";
            if (!HasArmy(pFrom)) return "vd_tip_no_army";
            MoveArmy(pFrom, pDest);
            return null;
        }

        public static int RecallKingdom(Kingdom pKingdom, City pDest)
        {
            int count = 0;
            foreach (City city in new List<City>(pKingdom.getCities()))
            {
                if (!HasArmy(city)) continue;
                MoveArmy(city, pDest);
                count++;
            }
            return count;
        }

        // 点的是城外的空地：军队前往那里扎营
        public static string Hold(City pFrom, WorldTile pTile)
        {
            if (pFrom == null || !CanHold(pTile)) return "vd_tip_cannot_hold";
            if (!HasArmy(pFrom)) return "vd_tip_no_army";
            MoveArmy(pFrom, null, pTile);
            return null;
        }

        static void MoveArmy(City pFrom, City pDest, WorldTile pHold = null)
        {
            pFrom.target_attack_city = null;
            pFrom.target_attack_zone = null;
            _forced.Remove(pFrom.id);
            SetStation(pFrom, pDest, pHold);

            Actor captain = pFrom.army.getCaptain();
            WorldTile from = (captain ?? pFrom.army.units[0]).current_tile;
            // 扎营就走到点的那块地，驻扎就走到城中心附近（城里有敌人就直接去打）
            WorldTile tile = pHold ?? StationTile(pFrom, from);
            if (tile == null) return;
            if (from.isSameIsland(tile))
            {
                if (captain == null) return;
                _rally[captain] = tile;
                captain.setTask("warrior_army_leader_move_random", true, false, true);
                RallySoldiers(pFrom.army, captain);
            }
            else
            {
                Transport.Dispatch(pFrom, tile);
            }
        }

        // 点中的城市：属于某个王国的才算，荒野、无主的城市都算空地
        public static City TargetCity(WorldTile pTile)
        {
            City city = pTile?.zone?.city;
            return city != null && !city.isRekt() && city.kingdom != null && !city.kingdom.isNeutral() ? city : null;
        }

        // 下令时把士兵从手头的事里叫出来（睡觉、做物品、聊天……），直接跟上队长。
        // 原版要等每个人把手头的事做完才开始跟，刚下令时队伍拖得很长
        static void RallySoldiers(Army pArmy, Actor pCaptain)
        {
            foreach (Actor unit in pArmy.units)
            {
                if (unit == pCaptain || unit.isRekt() || unit.is_inside_boat || unit.isFighting()) continue;
                if (!unit.current_tile.isSameIsland(pCaptain.current_tile)) continue;
                if (unit.ai?.task?.id == "warrior_army_follow_leader") continue;
                unit.setTask("warrior_army_follow_leader", true, false, true);
            }
        }

        // 指挥一支军队：点别国城市就进攻，点本国或友方城市就前往那里驻扎，点空地就前往那里扎营
        public static string CommandArmy(Army pArmy, WorldTile pTile)
        {
            City from = pArmy?.getCity();
            if (from == null) return "vd_tip_army_no_city";
            City target = TargetCity(pTile);
            if (target == null) return Hold(from, pTile);
            return Friendly(target.kingdom, from.kingdom) ? Recall(from, target) : Attack(from, target);
        }

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
            SetStation(pFrom, null, null);
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
                if (captain == null) return;
                captain.setTask("warrior_army_leader_move_to_attack_target", true, false, true);
                RallySoldiers(pFrom.army, captain);
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

    [HarmonyPatch(typeof(ai.behaviours.BehFindRandomTile), nameof(ai.behaviours.BehFindRandomTile.execute))]
    static class Patch_RallyTile
    {
        static bool Prefix(Actor pActor, ref ai.behaviours.BehResult __result)
        {
            if (!Orders.TakeRallyTile(pActor, out WorldTile tile) && !Orders.TakeStationTile(pActor, out tile)) return true;
            pActor.beh_tile_target = tile;
            __result = ai.behaviours.BehResult.Continue;
            return false;
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

    // 补员：原版新兵只在队长回到军队所属的那座城里时才编入军队，军队驻扎在别的城、在本国境内调动时都补不了。
    // 这里放宽成队长在本国任何一座城里、和新兵在同一块大陆，新兵就编入，然后自己走去找队长
    [HarmonyPatch(typeof(ai.behaviours.BehCheckCityActorArmyGroup), nameof(ai.behaviours.BehCheckCityActorArmyGroup.execute))]
    static class Patch_JoinArmyInKingdom
    {
        static void Postfix(Actor pActor, ai.behaviours.BehResult __result)
        {
            if (__result != ai.behaviours.BehResult.Continue || pActor.hasArmy() || !pActor.city.hasArmy()) return;
            Army army = pActor.city.army;
            Actor captain = army.getCaptain();
            if (captain == null || captain.isRekt() || captain.isInsideSomething()) return;
            if (captain.current_tile.zone_city?.kingdom != pActor.kingdom) return;
            if (!captain.current_tile.isSameIsland(pActor.current_tile)) return;
            pActor.setArmy(army);
        }
    }

    // 原版有进攻目标时战士只从最高优先级的决策里挑（跟随队长就在这一级），不会去闲逛、社交；
    // 驻扎、扎营时也一样，不然士兵有两成的决策会从所有决策里随机挑，跟着跟着就散了
    [HarmonyPatch(typeof(Actor), nameof(Actor.isAbleToSkipPriorityLevels))]
    static class Patch_StationDiscipline
    {
        static void Postfix(Actor __instance, ref bool __result)
        {
            if (__result && __instance.isWarrior() && __instance.hasCity() && Orders.HasStation(__instance.city)) __result = false;
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

    // 驻扎、扎营中的城市不挑进攻目标；目标失效、被原版 AI 换掉后，进攻命令自动解除
    [HarmonyPatch(typeof(ai.behaviours.CityBehCheckAttackZone), nameof(ai.behaviours.CityBehCheckAttackZone.execute))]
    static class Patch_CheckAttackZone
    {
        static bool Prefix(City pCity, ref ai.behaviours.BehResult __result)
        {
            if (!Orders.HasStation(pCity)) return true;
            Orders.PushToStation(pCity);
            pCity.target_attack_city = null;
            pCity.target_attack_zone = null;
            __result = ai.behaviours.BehResult.Continue;
            return false;
        }

        static void Postfix(City pCity) => Orders.CheckForced(pCity);
    }
}
