using System.Collections.Generic;
using HarmonyLib;

namespace DiplomacyOverhaul.Perf
{
    // 单位找敌人是每帧分配内存最多的地方：
    // checkObjectList 按 IEnumerable 遍历，List 的枚举器每次被装箱；随机遍历时 LoopRandom 的结构体枚举器也被装箱；
    // 把某个王国在区块里的单位、建筑加进敌人列表时，List<Actor> 加进 List<BaseSimObject> 只能走接口枚举。
    // 这里用下标遍历复刻同样的逻辑：遍历顺序、随机数的消耗次数和顺序都与原版相同
    static class EnemySearch
    {
        public static BaseSimObject Find(BaseSimObject pSelf, bool pAttackBuildings)
        {
            EnemyFinderData data = EnemiesFinder.findEnemiesFrom(pSelf.current_tile, pSelf.kingdom);
            if (data.isEmpty()) return null;
            bool closest = true;
            if (data.list.Count > 50) closest = Randy.randomChance(0.6f);
            List<BaseSimObject> list = data.list;
            // 与 LoopRandom 相同：创建时取随机起点，从起点开始绕一圈
            int offset = closest ? 0 : Randy.randomInt(0, list.Count);
            BaseSimObject result = Check(pSelf, list, offset, pAttackBuildings, closest, false, int.MaxValue);
            if (Opt.verify) Verify(pSelf, list, offset, pAttackBuildings, closest, result);
            return result;
        }

        // 与原版 BaseSimObject.checkObjectList 相同，只是按下标遍历
        static BaseSimObject Check(BaseSimObject self, List<BaseSimObject> pList, int pOffset, bool pAttackBuildings, bool pFindClosest, bool pIgnoreStunned, int pMaxDist)
        {
            int num = int.MaxValue;
            BaseSimObject result = null;
            long num2 = pMaxDist == int.MaxValue ? pMaxDist : (pMaxDist * pMaxDist + 1);
            bool flag = self.isActor() && self.a.hasMeleeAttack();
            WorldTile worldTile = self.current_tile;
            UnityEngine.Vector2Int pos = worldTile.pos;
            int count = pList.Count;
            for (int i = 0; i < count; i++)
            {
                BaseSimObject p = pList[pOffset == 0 ? i : (i + pOffset) % count];
                if (!p.isAlive() || p == self) continue;
                WorldTile worldTile2 = p.current_tile;
                if (pFindClosest)
                {
                    num = Toolbox.SquaredDistVec2(worldTile2.pos, pos);
                    if (num >= num2) continue;
                }
                if ((!pIgnoreStunned || !p.isActor() || !p.a.hasStatusStunned())
                    && self.canAttackTarget(p, pCheckForFactions: true, pAttackBuildings)
                    && (!flag || worldTile2.isSameIsland(worldTile) || (!worldTile2.Type.block && worldTile.region.island.isConnectedWith(worldTile2.region.island)))
                    && (!p.isBuilding() || !self.isKingdomCiv() || !p.b.asset.city_building || p.b.asset.tower || !(p.kingdom.getSpecies() == self.kingdom.getSpecies()))
                    && !self.shouldIgnoreTarget(p))
                {
                    if (!pFindClosest) return p;
                    if (num <= 4) return p;
                    result = p;
                    num2 = num;
                }
            }
            return result;
        }

        // 与原版 EnemyFinderContainer.findEnemiesOfKingdomInChunk 相同，只是逐个加入
        public static void FindInChunk(EnemyFinderData pData, MapChunk pChunk, Kingdom pMainKingdom)
        {
            if (pChunk.objects.kingdoms.Count == 0) return;
            List<long> kingdoms = pChunk.objects.kingdoms;
            bool peaceful = WorldLawLibrary.world_law_peaceful_monsters.isEnabled();
            if (pMainKingdom.asset.mobs && peaceful) return;
            List<BaseSimObject> list = pData.list;
            int start = list.Count;
            for (int i = 0; i < kingdoms.Count; i++)
            {
                long id = kingdoms[i];
                Kingdom other = World.world.kingdoms.getCivOrWildViaID(id);
                if (other != null && (!peaceful || !other.asset.mobs) && pMainKingdom.isEnemy(other))
                {
                    List<Actor> units = pChunk.objects.getUnits(id);
                    List<Building> buildings = pChunk.objects.getBuildings(id);
                    if (list.Capacity < list.Count + units.Count + buildings.Count) list.Capacity = System.Math.Max(list.Capacity * 2, list.Count + units.Count + buildings.Count);
                    for (int j = 0; j < units.Count; j++) list.Add(units[j]);
                    for (int j = 0; j < buildings.Count; j++) list.Add(buildings[j]);
                }
            }
            if (Opt.verify) VerifyChunk(pData, pChunk, pMainKingdom, peaceful, start);
        }

        static void Verify(BaseSimObject pSelf, List<BaseSimObject> pList, int pOffset, bool pAttackBuildings, bool pClosest, BaseSimObject pOurs)
        {
            List<BaseSimObject> order = pList;
            if (pOffset != 0)
            {
                order = new List<BaseSimObject>(pList.Count);
                for (int i = 0; i < pList.Count; i++) order.Add(pList[(i + pOffset) % pList.Count]);
            }
            BaseSimObject vanilla = pSelf.checkObjectList(order, pAttackBuildings, pClosest, false);
            if (vanilla == pOurs) Verifier.Report("enemy_search_target", 0, 1, null);
            else Verifier.Report("enemy_search_target", 1, 1, $"ours={Describe(pOurs)} vanilla={Describe(vanilla)}");
        }

        // 建筑没有名字，读 name 会抛异常
        static string Describe(BaseSimObject pObject)
        {
            if (pObject == null) return "null";
            return pObject.isActor() ? pObject.a.name : pObject.b.asset.id + ":" + pObject.b.data.id;
        }

        static void VerifyChunk(EnemyFinderData pData, MapChunk pChunk, Kingdom pMainKingdom, bool pPeaceful, int pStart)
        {
            var vanilla = new List<BaseSimObject>();
            List<long> kingdoms = pChunk.objects.kingdoms;
            for (int i = 0; i < kingdoms.Count; i++)
            {
                Kingdom other = World.world.kingdoms.getCivOrWildViaID(kingdoms[i]);
                if (other != null && (!pPeaceful || !other.asset.mobs) && pMainKingdom.isEnemy(other))
                {
                    vanilla.AddRange(pChunk.objects.getUnits(kingdoms[i]));
                    vanilla.AddRange(pChunk.objects.getBuildings(kingdoms[i]));
                }
            }
            bool same = vanilla.Count == pData.list.Count - pStart;
            for (int i = 0; same && i < vanilla.Count; i++) same = vanilla[i] == pData.list[pStart + i];
            Verifier.Report("enemy_search_list", same ? 0 : 1, 1, $"ours={pData.list.Count - pStart} vanilla={vanilla.Count}");
        }
    }

    [HarmonyPatch(typeof(BaseSimObject), nameof(BaseSimObject.findEnemyObjectTarget))]
    static class EnemySearch_Find
    {
        static bool Prefix(BaseSimObject __instance, bool pAttackBuildings, ref BaseSimObject __result)
        {
            if (!Opt.enemy_search_noalloc) return true;
            try { __result = EnemySearch.Find(__instance, pAttackBuildings); }
            catch (System.Exception e) { Perf.Fail(nameof(Opt.enemy_search_noalloc), e); return true; }
            return false;
        }
    }

    [HarmonyPatch(typeof(EnemyFinderContainer), "findEnemiesOfKingdomInChunk")]
    static class EnemySearch_Chunk
    {
        static bool Prefix(EnemyFinderData pData, MapChunk pChunk, Kingdom pMainKingdom)
        {
            if (!Opt.enemy_search_noalloc) return true;
            int count = pData.list.Count;
            try { EnemySearch.FindInChunk(pData, pChunk, pMainKingdom); }
            catch (System.Exception e)
            {
                // 撤掉已经加进去的，让原版重新加
                pData.list.RemoveRange(count, pData.list.Count - count);
                Perf.Fail(nameof(Opt.enemy_search_noalloc), e);
                return true;
            }
            return false;
        }
    }
}
