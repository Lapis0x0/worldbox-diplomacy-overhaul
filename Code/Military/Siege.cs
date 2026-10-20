using System;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 攻城：原版只要城里还站着一个守方单位，进度就封顶在 5%，而攻方队长进城后只在相邻区块乱转，
    // 大城里几百人可能永远碰不到那一个人。这里两处修正，对所有军队生效：
    // 1. 队长进城后直奔最近的守方单位，城里没人了就去城中心
    // 2. 攻方人数达到守方的 5 倍时，守方挡不住进度
    public static class Siege
    {
        const int OverwhelmRatio = 5;

        // 城里离队长最近、能占城的守方单位（战士、城主、国王）所在地块
        public static WorldTile NearestDefender(Actor pActor, City pCity)
        {
            Kingdom defender = pCity.kingdom;
            if (defender == null) return null;
            WorldTile best = null;
            float bestDist = float.MaxValue;
            Nearest(defender, pCity, pActor.current_tile, ref best, ref bestDist);
            return best;
        }

        // 反过来，打进城里、离 pFrom 最近的敌方单位所在地块，驻扎的军队据此守城。
        // 帮友方守城时只算守军自己国家也在交战的敌人，没开战的打不起来
        public static WorldTile NearestIntruder(WorldTile pFrom, City pCity, Kingdom pGuard)
        {
            Kingdom owner = pCity.kingdom;
            if (owner.isRekt() || pGuard.isRekt()) return null;
            WorldTile best = null;
            float bestDist = float.MaxValue;
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (k != owner && owner.isEnemy(k) && pGuard.isEnemy(k)) Nearest(k, pCity, pFrom, ref best, ref bestDist);
            }
            return best;
        }

        static void Nearest(Kingdom pKingdom, City pCity, WorldTile pFrom, ref WorldTile pBest, ref float pBestDist)
        {
            foreach (Actor unit in pKingdom.units)
            {
                if (!unit.isAlive() || unit.isInsideSomething() || !unit.profession_asset.can_capture) continue;
                WorldTile tile = unit.current_tile;
                if (tile.zone_city != pCity || !tile.isSameIsland(pFrom)) continue;
                float d = Toolbox.SquaredDist(pFrom.x, pFrom.y, tile.x, tile.y);
                if (d < pBestDist)
                {
                    pBestDist = d;
                    pBest = tile;
                }
            }
        }

        // 原版这一轮因为守方阻挡没涨进度，而攻方已经压倒守方：补上这一轮的进度
        public static void Overwhelm(City pCity, float pTicksBefore, float pElapsed)
        {
            Kingdom owner = pCity.kingdom;
            if (owner == null || pCity.countWarriors() == 0) return;
            if (!pCity._capturing_units.TryGetValue(owner, out int defenders) || defenders <= 0) return;

            Kingdom attacker = null;
            int attackers = 0;
            foreach (var kv in pCity._capturing_units)
            {
                if (kv.Key == owner || !kv.Key.isEnemy(owner) || kv.Value <= attackers) continue;
                attacker = kv.Key;
                attackers = kv.Value;
            }
            if (attacker == null || attackers < defenders * OverwhelmRatio) return;
            if (pCity._capture_ticks > pTicksBefore) return; // 原版已经涨过了
            if (pCity.being_captured_by != null && pCity.being_captured_by.isAlive() && pCity.being_captured_by != attacker) return;

            pCity.being_captured_by = attacker;
            pCity._capture_ticks = pTicksBefore + 1f + pElapsed;
            if (pCity._capture_ticks >= 100f) pCity.finishCapture(attacker);
        }
    }

    [HarmonyPatch(typeof(ai.behaviours.BehCityActorCheckAttack), nameof(ai.behaviours.BehCityActorCheckAttack.execute))]
    static class Patch_SiegeHunt
    {
        static void Postfix(Actor pActor, ai.behaviours.BehResult __result)
        {
            if (!Safety.On(Sys.Military)) return;
            try
            {
                if (!Military.On || __result != ai.behaviours.BehResult.Continue) return;
                City target = pActor.city?.target_attack_zone?.city;
                if (target == null || pActor.current_tile.zone.city != target) return;
                WorldTile tile = Siege.NearestDefender(pActor, target);
                if (tile == null)
                {
                    WorldTile center = target.getTile();
                    if (center != null && center.isSameIsland(pActor.current_tile)) tile = center;
                }
                if (tile != null) pActor.beh_tile_target = tile;
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "破城清剿", e); }
        }
    }

    [HarmonyPatch(typeof(City), "updateCapture")]
    static class Patch_SiegeOverwhelm
    {
        // 原版每 0.1 秒结算一次：入口处计时器已到点说明这一帧会结算
        static void Prefix(City __instance, out float __state)
        {
            __state = -1f;
            if (!Safety.On(Sys.Military)) return;
            try
            {
                __state = __instance._timer_capture <= 0f ? __instance._capture_ticks : -1f;
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "破城", e); }
        }

        static void Postfix(City __instance, float pElapsed, float __state)
        {
            if (__state < 0f || !Safety.On(Sys.Military)) return;
            try
            {
                if (!Military.On || __instance.isRekt()) return;
                Siege.Overwhelm(__instance, __state, pElapsed);
            }
            catch (Exception e) { Safety.Fail(Sys.Military, "破城", e); }
        }
    }
}
