using System;
using System.Collections.Generic;
using HarmonyLib;

namespace DiplomacyOverhaul
{
    // 军队的命令：进攻、驻扎、扎营。命令记在军队上：地方军的进攻目标就是所属城市的（原版字段），
    // 禁军编队不挂城，进攻目标记在编队上（见 Guard）
    public static class Orders
    {
        // 军队 id → 玩家指定的目标城市 id；目标失效后由原版 AI（禁军是国家 AI）接管
        static readonly Dictionary<long, long> _forced = new Dictionary<long, long>();

        public static bool IsForced(Army pArmy) => pArmy != null && _forced.ContainsKey(pArmy.id);

        // 原版士兵只在城市有进攻目标时才跟随队长；驻扎、扎营清掉了进攻目标，士兵会留在原地各干各的，
        // 只有队长一个人过去。把"跟随队长"决策的启动条件换成：有进攻目标或者在驻扎、扎营。
        // 重复执行只是重新赋值，热重载安全
        public static void Init()
        {
            DecisionAsset follow = AssetManager.decisions_library.get("warrior_army_follow_leader");
            if (follow != null) follow.action_check_launch = FollowLeader;
        }

        // 除最后一条外照抄原版。禁军编队的士兵执行 AI 时，户籍城的进攻目标临时换成编队的（见 Guard）
        static bool FollowLeader(Actor pActor)
        {
            if (!pActor.hasCity() || !pActor.isArmyGroupWarrior() || !pActor.army.hasCaptain()) return false;
            if (!pActor.current_tile.isSameIsland(pActor.army.getCaptain().current_tile)) return false;
            return pActor.city.hasAttackZoneOrder() || HasStation(pActor.army);
        }

        public static void CheckForced(Army pArmy)
        {
            if (pArmy == null || !_forced.TryGetValue(pArmy.id, out long targetId)) return;
            City target = AttackTarget(pArmy);
            if (target == null || target.id != targetId) _forced.Remove(pArmy.id);
        }

        // 军队的进攻目标
        public static City AttackTarget(Army pArmy) =>
            Guard.Is(pArmy) ? Guard.Target(pArmy) : pArmy?.getCity()?.target_attack_city;

        public static bool HasAttack(Army pArmy) => AttackTarget(pArmy) != null;

        static void SetAttack(Army pArmy, City pTarget, TileZone pZone)
        {
            if (Guard.Is(pArmy))
            {
                Guard.SetTarget(pArmy, pTarget, pZone);
                return;
            }
            City city = pArmy.getCity();
            if (city == null) return;
            city.target_attack_city = pTarget;
            city.target_attack_zone = pZone;
        }

        static TileZone AttackZone(Army pArmy) =>
            Guard.Is(pArmy) ? Guard.Zone(pArmy) : pArmy?.getCity()?.target_attack_zone;

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

        public static bool HasUnits(Army pArmy) => pArmy != null && !pArmy.isRekt() && pArmy.countUnits() > 0;

        // 一个国家所有有兵的军队：各城的地方军和禁军编队
        public static List<Army> ArmiesOf(Kingdom pKingdom)
        {
            var list = new List<Army>();
            foreach (Army army in World.world.armies)
            {
                if (HasUnits(army) && KingdomOf(army) == pKingdom) list.Add(army);
            }
            return list;
        }

        // 军队所属的国家，已经灭亡的算没有。军队的城市没了时，原版 Army.getKingdom 返回军队记着的旧国家，
        // 那个国家可能已经灭亡、被原版清空了 asset 和 data，对它判断敌友、读政策都会空引用。
        // 模组里取军队的国家一律用这个
        public static Kingdom KingdomOf(Army pArmy)
        {
            Kingdom k = pArmy?.getKingdom();
            return k.isRekt() ? null : k;
        }

        // 本国和同阵营（见 Camp）：点它们的城市是去驻扎守城，不是宣战
        public static bool Friendly(Kingdom a, Kingdom b)
        {
            if (a.isRekt() || b.isRekt()) return false;
            if (a == b) return true;
            if (a.isEnemy(b)) return false;
            return Camp.Same(a, b);
        }

        // 驻扎：军队 → 驻扎的城市 id，存在军队数据里，读档后照样守着。可以驻扎在附庸、宗主、盟友的城里。
        // 驻扎期间不挑进攻目标：平时队长在城中心附近走动，敌军进城就去打最近的那个；
        // 下进攻命令或再点一次驻扎城解除
        const string KeyGarrison = "vd_garrison";

        public static City GetGarrison(Army pArmy)
        {
            if (pArmy?.data == null) return null;
            Migrate(pArmy, KeyGarrison);
            pArmy.data.get(KeyGarrison, out long id, -1L);
            if (id < 0) return null;
            City target = World.world.cities.get(id);
            if (target != null && !target.isRekt() && Friendly(target.kingdom, KingdomOf(pArmy))) return target;
            // 驻扎城丢了，或者那个国家不再是友方
            pArmy.data.removeLong(KeyGarrison);
            return null;
        }

        // 扎营：军队 → 扎营的地块 id。军队停在城外的空地上，和驻扎一样不挑进攻目标，
        // 队长只在那块地附近走动
        const string KeyHold = "vd_hold";
        const int HoldRadius = 4;

        public static WorldTile GetHold(Army pArmy)
        {
            if (pArmy?.data == null) return null;
            Migrate(pArmy, KeyHold);
            pArmy.data.get(KeyHold, out long id, -1L);
            if (id < 0) return null;
            WorldTile[] tiles = World.world.tiles_list;
            if (id < tiles.Length && CanHold(tiles[id])) return tiles[id];
            // 地块变成了水或山
            pArmy.data.removeLong(KeyHold);
            return null;
        }

        // 旧存档的命令记在军队所属城市上，搬到军队上
        static void Migrate(Army pArmy, string pKey)
        {
            City city = pArmy.getCity();
            if (city?.data == null) return;
            city.data.get(pKey, out long id, -1L);
            if (id < 0) return;
            city.data.removeLong(pKey);
            pArmy.data.set(pKey, id);
        }

        public static bool CanHold(WorldTile pTile) => pTile != null && !pTile.Type.liquid && !pTile.Type.block;

        static bool NearHold(WorldTile pTile, WorldTile pHold) =>
            System.Math.Abs(pTile.x - pHold.x) <= HoldRadius && System.Math.Abs(pTile.y - pHold.y) <= HoldRadius;

        // 驻扎城市和扎营地块二选一，都为 null 时交还原版 AI
        static void SetStation(Army pArmy, City pGarrison, WorldTile pHold)
        {
            if (pGarrison == null) pArmy.data.removeLong(KeyGarrison);
            else pArmy.data.set(KeyGarrison, pGarrison.id);
            if (pHold == null) pArmy.data.removeLong(KeyHold);
            else pArmy.data.set(KeyHold, (long)pHold.tile_id);
        }

        public static bool HasStation(Army pArmy) => GetGarrison(pArmy) != null || GetHold(pArmy) != null;

        internal static void ClearStation(Army pArmy) => SetStation(pArmy, null, null);

        // 关掉军队大修：所有军队的命令、驻扎、扎营、任务都撤掉，交还原版 AI
        internal static void ClearAll()
        {
            _forced.Clear();
            _rally.Clear();
            foreach (Army army in World.world.armies)
            {
                if (army?.data == null) continue;
                ClearStation(army);
                army.data.removeInt(Guard.KeyTask);
            }
        }

        // 驻扎：城里有敌人就去最近的敌人那里，没有就在城中心附近挑；扎营：在那块地附近挑
        static WorldTile StationTile(Army pArmy, WorldTile pFrom)
        {
            City garrison = GetGarrison(pArmy);
            if (garrison != null)
            {
                WorldTile enemy = Siege.NearestIntruder(pFrom, garrison, KingdomOf(pArmy));
                if (enemy != null) return enemy;
                WorldTile center = garrison.getTile();
                if (center != null) return Around(center);
                return garrison.zones.Count > 0 ? garrison.zones.GetRandom().tiles.GetRandom() : null;
            }
            WorldTile hold = GetHold(pArmy);
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
            WorldTile tile = StationTile(pActor.army, pActor.current_tile);
            if (tile == null || !pActor.current_tile.isSameIsland(tile)) return false;
            pTile = tile;
            return true;
        }

        // 原版队长每做完一件事就随机挑下一件（闲逛、等士兵、社交……），"往驻地走"只是其中之一，
        // 路远时几乎不动。队长还没到驻地、也没在打仗或坐船时，强制它继续赶路
        public static void PushToStation(Army pArmy)
        {
            Actor captain = pArmy?.getCaptain();
            if (captain == null || captain.isRekt() || captain.is_inside_boat || captain.isFighting()) return;
            City garrison = GetGarrison(pArmy);
            if (garrison != null)
            {
                // 敌军进城：不管队长在走还是在等，重新出发去打最近的那个（敌人会动，每次都重新挑）
                if (Siege.NearestIntruder(captain.current_tile, garrison, KingdomOf(pArmy)) != null)
                {
                    captain.setTask("warrior_army_leader_move_random", true, false, true);
                    return;
                }
                if (captain.current_tile.zone.city == garrison) return;
            }
            WorldTile hold = GetHold(pArmy);
            if (hold != null && NearHold(captain.current_tile, hold)) return;
            if (captain.ai?.task?.id == "warrior_army_leader_move_random") return;
            captain.setTask("warrior_army_leader_move_random", true, false, true);
        }

        // 队长到了驻地：在驻扎的城里，或在扎营的地方附近
        public static bool AtStation(Army pArmy)
        {
            Actor captain = pArmy?.getCaptain();
            if (captain == null || captain.isRekt()) return false;
            City garrison = GetGarrison(pArmy);
            if (garrison != null) return captain.current_tile.zone.city == garrison;
            WorldTile hold = GetHold(pArmy);
            return hold != null && NearHold(captain.current_tile, hold);
        }

        // 点的正是这支军队驻扎的城、或扎营的地方附近
        public static bool IsStationedAt(Army pArmy, WorldTile pTile)
        {
            City garrison = GetGarrison(pArmy);
            if (garrison != null) return pTile.zone.city == garrison;
            WorldTile hold = GetHold(pArmy);
            return hold != null && TargetCity(pTile) == null && NearHold(pTile, hold);
        }

        // 选中的军队已经驻扎在这座城、或扎营在这附近：解除，交还原版 AI（禁军交还国家 AI）
        public static bool Release(Army pArmy, WorldTile pTile)
        {
            if (!IsStationedAt(pArmy, pTile)) return false;
            SetStation(pArmy, null, null);
            Guard.OnPlayerRelease(pArmy);
            return true;
        }

        // 玩家亲手下的命令还在执行：进攻（只在内存里），或者玩家下的驻扎、扎营（国家 AI 安排的驻扎不算）
        public static bool IsCommanded(Army pArmy)
        {
            if (!HasUnits(pArmy)) return false;
            if (IsForced(pArmy)) return true;
            pArmy.data.get(Guard.KeyTask, out int task, Guard.TaskNone);
            return task == Guard.TaskPlayer && HasStation(pArmy);
        }

        // 命令的目的地：进攻的城、驻扎的城中心、扎营的地块
        public static WorldTile Destination(Army pArmy)
        {
            if (IsForced(pArmy)) return AttackTarget(pArmy)?.getTile();
            return GetGarrison(pArmy)?.getTile() ?? GetHold(pArmy);
        }

        // 交还指挥：清掉玩家的命令，地方军交还原版 AI，禁军交还国家
        public static bool ReleaseCommand(Army pArmy)
        {
            if (!IsCommanded(pArmy)) return false;
            if (IsForced(pArmy))
            {
                SetAttack(pArmy, null, null);
                _forced.Remove(pArmy.id);
            }
            SetStation(pArmy, null, null);
            Guard.OnPlayerRelease(pArmy);
            return true;
        }

        public static List<Army> CommandedOf(Kingdom pKingdom)
        {
            var list = new List<Army>();
            foreach (Army army in ArmiesOf(pKingdom))
                if (IsCommanded(army)) list.Add(army);
            return list;
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
        public static string Recall(Army pArmy, City pDest)
        {
            if (!HasUnits(pArmy)) return "vd_tip_no_army";
            if (pDest == null || !Friendly(KingdomOf(pArmy), pDest.kingdom)) return "vd_tip_invalid";
            MoveArmy(pArmy, pDest);
            return null;
        }

        public static int RecallKingdom(Kingdom pKingdom, City pDest)
        {
            List<Army> armies = ArmiesOf(pKingdom);
            foreach (Army army in armies)
            {
                MoveArmy(army, pDest);
                Guard.OnPlayerOrder(army);
            }
            return armies.Count;
        }

        // 点的是城外的空地：军队前往那里扎营
        public static string Hold(Army pArmy, WorldTile pTile)
        {
            if (!CanHold(pTile)) return "vd_tip_cannot_hold";
            if (!HasUnits(pArmy)) return "vd_tip_no_army";
            MoveArmy(pArmy, null, pTile);
            return null;
        }

        // 驻扎或扎营；国家 AI 给禁军编队下令也走这里
        internal static void MoveArmy(Army pArmy, City pDest, WorldTile pHold = null)
        {
            SetAttack(pArmy, null, null);
            _forced.Remove(pArmy.id);
            SetStation(pArmy, pDest, pHold);

            Actor captain = pArmy.getCaptain();
            WorldTile from = (captain ?? pArmy.units[0]).current_tile;
            // 扎营就走到点的那块地，驻扎就走到城中心附近（城里有敌人就直接去打）
            WorldTile tile = pHold ?? StationTile(pArmy, from);
            if (tile == null) return;
            if (from.isSameIsland(tile))
            {
                if (captain == null) return;
                _rally[captain] = tile;
                captain.setTask("warrior_army_leader_move_random", true, false, true);
                RallySoldiers(pArmy, captain);
            }
            else
            {
                Transport.Dispatch(pArmy, tile);
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

        // 玩家指挥一支军队：点别国城市就进攻，点本国或友方城市就前往那里驻扎，点空地就前往那里扎营
        public static string CommandArmy(Army pArmy, WorldTile pTile)
        {
            if (!HasUnits(pArmy) || KingdomOf(pArmy) == null) return "vd_tip_no_army";
            City target = TargetCity(pTile);
            string error = target == null ? Hold(pArmy, pTile)
                : Friendly(target.kingdom, KingdomOf(pArmy)) ? Recall(pArmy, target) : Attack(pArmy, target);
            if (error == null) Guard.OnPlayerOrder(pArmy);
            return error;
        }

        // 返回 null 表示成功，否则返回失败原因的本地化 key
        public static string Attack(Army pArmy, City pTarget)
        {
            if (!HasUnits(pArmy)) return "vd_tip_no_army";
            if (pTarget == null || pTarget == pArmy.getCity()) return "vd_tip_invalid";
            string error = EnsureWar(KingdomOf(pArmy), pTarget.kingdom);
            if (error != null) return error;
            Order(pArmy, pTarget);
            _forced[pArmy.id] = pTarget.id;
            return null;
        }

        // 举国进攻：所有军队都打同一个目标；返回参与的军队数
        public static int AttackWithKingdom(Kingdom pKingdom, City pTarget, out string pError)
        {
            pError = EnsureWar(pKingdom, pTarget?.kingdom);
            if (pError != null) return 0;
            List<Army> armies = ArmiesOf(pKingdom);
            foreach (Army army in armies)
            {
                Order(army, pTarget);
                _forced[army.id] = pTarget.id;
                Guard.OnPlayerOrder(army);
            }
            if (armies.Count == 0) pError = "vd_tip_no_army";
            return armies.Count;
        }

        // 进攻；国家 AI 给禁军编队下令也走这里（不算玩家命令）
        internal static void Order(Army pArmy, City pTarget)
        {
            SetStation(pArmy, null, null);
            // 原版在目标城市没有建筑时会把落点设成自己的城市，这里直接从目标城市的地块里选
            TileZone zone = pTarget.buildings.Count > 0
                ? pTarget.buildings.GetRandom().current_tile.zone
                : pTarget.zones.GetRandom();
            SetAttack(pArmy, pTarget, zone);

            Actor captain = pArmy.getCaptain();
            WorldTile from = (captain ?? pArmy.units[0]).current_tile;
            WorldTile to = zone.centerTile;
            if (from.isSameIsland(to))
            {
                if (captain == null) return;
                captain.setTask("warrior_army_leader_move_to_attack_target", true, false, true);
                RallySoldiers(pArmy, captain);
            }
            else
            {
                Transport.Dispatch(pArmy, to);
            }
        }

        // 驻扎、扎营的军队不挑进攻目标，并把队长拉回驻地。地方军在所属城市挑进攻目标时调用，禁军在 Guard 里定时调用
        public static void KeepStation(Army pArmy)
        {
            PushToStation(pArmy);
            SetAttack(pArmy, null, null);
        }
    }


    [HarmonyPatch(typeof(ai.behaviours.BehFindRandomTile), nameof(ai.behaviours.BehFindRandomTile.execute))]
    static class Patch_RallyTile
    {
        static bool Prefix(Actor pActor, ref ai.behaviours.BehResult __result)
        {
            if (!Safety.On(Sys.Military)) return true;
            try
            {
                if (!Military.On) return true;
                if (!Orders.TakeRallyTile(pActor, out WorldTile tile) && !Orders.TakeStationTile(pActor, out tile)) return true;
                pActor.beh_tile_target = tile;
                __result = ai.behaviours.BehResult.Continue;
                return false;
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "军队找路", e); return true; }
        }
    }


    // 补员：原版新兵只在队长回到军队所属的那座城里时才编入军队，军队驻扎在别的城、在本国境内调动时都补不了。
    // 这里放宽成队长在本国任何一座城里、和新兵在同一块大陆，新兵就编入，然后自己走去找队长
    [HarmonyPatch(typeof(ai.behaviours.BehCheckCityActorArmyGroup), nameof(ai.behaviours.BehCheckCityActorArmyGroup.execute))]
    static class Patch_JoinArmyInKingdom
    {
        static void Postfix(Actor pActor, ai.behaviours.BehResult __result)
        {
            if (!Safety.On(Sys.Military)) return;
            try
            {
                if (!Military.On || __result != ai.behaviours.BehResult.Continue || pActor.hasArmy() || !pActor.city.hasArmy() || Guard.IsMember(pActor)) return;
                Army army = pActor.city.army;
                Actor captain = army.getCaptain();
                if (captain == null || captain.isRekt() || captain.isInsideSomething()) return;
                if (captain.current_tile.zone_city?.kingdom != pActor.kingdom) return;
                if (!captain.current_tile.isSameIsland(pActor.current_tile)) return;
                pActor.setArmy(army);
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "跨城补员", e); }
        }
    }


    // 原版有进攻目标时战士只从最高优先级的决策里挑（跟随队长就在这一级），不会去闲逛、社交；
    // 驻扎、扎营时也一样，不然士兵有两成的决策会从所有决策里随机挑，跟着跟着就散了
    [HarmonyPatch(typeof(Actor), nameof(Actor.isAbleToSkipPriorityLevels))]
    static class Patch_StationDiscipline
    {
        static void Postfix(Actor __instance, ref bool __result)
        {
            if (!Safety.On(Sys.Military)) return;
            try
            {
                if (Military.On && __result && __instance.isWarrior() && __instance.hasArmy() && Orders.HasStation(__instance.army)) __result = false;
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "驻扎纪律", e); }
        }
    }


    // 出兵门槛：玩家命令的军队、禁军编队跳过"兵力达到上限 70% 才出兵"的限制；
    // 保守的地方军只在 LocalAI 派了出征时出兵（见 LocalAI）
    [HarmonyPatch(typeof(City), nameof(City.isOkToSendArmy))]
    static class Patch_OkToSendArmy
    {
        static bool Prefix(City __instance, ref bool __result)
        {
            if (!Safety.On(Sys.Military)) return true;
            try
            {
                if (!Military.On) return true;
                if (Guard.Acting != null || (__instance.hasArmy() && Orders.IsForced(__instance.army)))
                {
                    __result = true;
                    return false;
                }
                if (!__instance.hasArmy() || !LocalAI.Conservative(__instance.kingdom)) return true;
                __result = LocalAI.Sends(__instance);
                return false;
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "出兵门槛", e); return true; }
        }
    }


    // 驻扎、扎营中的城市不挑进攻目标；目标失效、被原版 AI 换掉后，进攻命令自动解除
    [HarmonyPatch(typeof(ai.behaviours.CityBehCheckAttackZone), nameof(ai.behaviours.CityBehCheckAttackZone.execute))]
    static class Patch_CheckAttackZone
    {
        static bool Prefix(City pCity, ref ai.behaviours.BehResult __result)
        {
            if (!Safety.On(Sys.Military)) return true;
            try
            {
                if (!Military.On || !pCity.hasArmy() || !Orders.HasStation(pCity.army)) return true;
                Orders.KeepStation(pCity.army);
                __result = ai.behaviours.BehResult.Continue;
                return false;
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "驻扎选目标", e); return true; }
        }

        static void Postfix(City pCity)
        {
            if (!Safety.On(Sys.Military)) return;
            try
            {
                if (Military.On && pCity.hasArmy()) Orders.CheckForced(pCity.army);
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "进攻命令解除", e); }
        }
    }
}
