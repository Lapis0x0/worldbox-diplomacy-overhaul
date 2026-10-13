using System;
using System.Collections.Generic;
using HarmonyLib;

namespace DiplomacyOverhaul.Perf
{
    // 区块的建筑名单（chunk.objects）：原版 sim_zones 每次重算，只要有一个区块的建筑变了，
    // 就遍历全部建筑（大地图两万多栋，含每一棵树），把落在有变动区块里的可用建筑重新登记。
    // 一次重算通常只有约 20 个区块有变动（共 1296 个）。
    // 这里按区块维护一份全部建筑的索引，只取有变动区块里的建筑。
    // 登记顺序影响找建筑、找敌人时「先找到哪个」，必须和原版一致：原版的顺序是建筑总名单内部
    // 哈希表的槽位顺序（扩容不改变槽位号），所以按槽位号排序即可得到同样的顺序
    static class SimZones
    {
        static readonly Dictionary<MapChunk, List<Building>> _by_chunk = new Dictionary<MapChunk, List<Building>>();
        static readonly Dictionary<Building, MapChunk> _chunk_of = new Dictionary<Building, MapChunk>();
        static MapChunk[] _chunks;
        static bool _invalid = true;

        static readonly List<(int slot, Building building)> _todo = new List<(int, Building)>();
        static readonly List<Building> _vanilla = new List<Building>();
        static readonly Comparison<(int slot, Building building)> BySlot = (a, b) => a.slot.CompareTo(b.slot);

        static readonly Func<HashSet<Building>, Building, int> SlotOf =
            AccessTools.MethodDelegate<Func<HashSet<Building>, Building, int>>(AccessTools.Method(typeof(HashSet<Building>), "InternalIndexOf"));

        public static void Invalidate() => _invalid = true;

        static ObjectContainer<Building> Container => World.world.buildings._container;

        // 建筑总名单的增删在 checkAddRemove 里才真正生效，在这里同步索引
        public static void OnFlush(ObjectContainer<Building> pContainer)
        {
            if (!ReferenceEquals(pContainer, Container)) return;
            // 开关关着时不跟踪，重新打开后整体重建
            if (!Opt.sim_zones_buildings) _invalid = true;
            if (_invalid) return;
            foreach (Building building in pContainer._to_add) Track(building);
            foreach (Building building in pContainer._to_remove) Untrack(building);
        }

        static void Track(Building pBuilding)
        {
            if (_chunk_of.ContainsKey(pBuilding)) return;
            MapChunk chunk = pBuilding.current_tile?.chunk;
            if (chunk == null)
            {
                _invalid = true;
                return;
            }
            _chunk_of[pBuilding] = chunk;
            if (!_by_chunk.TryGetValue(chunk, out List<Building> list)) _by_chunk[chunk] = list = new List<Building>();
            list.Add(pBuilding);
        }

        static void Untrack(Building pBuilding)
        {
            if (!_chunk_of.TryGetValue(pBuilding, out MapChunk chunk)) return;
            _chunk_of.Remove(pBuilding);
            _by_chunk[chunk].Remove(pBuilding);
        }

        static void EnsureIndex()
        {
            MapChunk[] chunks = World.world.map_chunk_manager.chunks;
            HashSet<Building> all = Container._hashSet;
            if (!_invalid && ReferenceEquals(chunks, _chunks) && _chunk_of.Count == all.Count) return;
            _chunks = chunks;
            _chunk_of.Clear();
            foreach (List<Building> list in _by_chunk.Values) list.Clear();
            _by_chunk.Clear();
            _invalid = false;
            foreach (Building building in all) Track(building);
        }

        public static bool CheckBuildings()
        {
            if (!Opt.sim_zones_buildings) return true;
            try { Collect(); }
            catch (Exception e)
            {
                _invalid = true;
                Perf.Fail(nameof(Opt.sim_zones_buildings), e);
                return true;
            }
            // 以下和原版 checkBuildings 对每栋建筑做的事相同
            for (int i = 0; i < _todo.Count; i++)
            {
                Building building = _todo[i].building;
                if (building.isCiv() && building.asset.docks && building.component_docks.hasOceanTiles())
                    building.component_docks.tiles_ocean[0].region.island.addDock(building);
                building.chunk.objects.addBuilding(building);
            }
            return false;
        }

        static void Collect()
        {
            EnsureIndex();
            HashSet<Building> all = Container._hashSet;
            _todo.Clear();
            MapChunk[] chunks = World.world.map_chunk_manager.chunks;
            for (int i = 0; i < chunks.Length; i++)
            {
                MapChunk chunk = chunks[i];
                if (!chunk.buildings_dirty || !_by_chunk.TryGetValue(chunk, out List<Building> list)) continue;
                for (int j = 0; j < list.Count; j++)
                {
                    Building building = list[j];
                    if (building.isUsable()) _todo.Add((SlotOf(all, building), building));
                }
            }
            _todo.Sort(BySlot);
            if (Opt.verify) Verify();
        }

        // 原版的筛选：按总名单顺序，可用且所在区块有变动
        static void Verify()
        {
            _vanilla.Clear();
            List<Building> list = World.world.buildings.getSimpleList();
            for (int i = 0; i < list.Count; i++)
            {
                Building building = list[i];
                if (building.isUsable() && building.chunk.buildings_dirty) _vanilla.Add(building);
            }
            int mismatches = 0;
            string example = null;
            int n = Math.Max(_vanilla.Count, _todo.Count);
            for (int i = 0; i < n; i++)
            {
                Building a = i < _todo.Count ? _todo[i].building : null;
                Building b = i < _vanilla.Count ? _vanilla[i] : null;
                if (ReferenceEquals(a, b)) continue;
                mismatches++;
                example ??= $"#{i} ours={a?.asset.id}:{a?.data.id} vanilla={b?.asset.id}:{b?.data.id} counts={_todo.Count}/{_vanilla.Count}";
            }
            Verifier.Report("sim_zones_buildings", mismatches, n, example);
            if (_chunk_of.Count != Container._hashSet.Count)
                Verifier.Report("sim_zones_index", 1, 1, $"index={_chunk_of.Count} all={Container._hashSet.Count}");
        }
    }

    [HarmonyPatch(typeof(ObjectContainer<Building>), nameof(ObjectContainer<Building>.checkAddRemove))]
    static class SimZones_Flush
    {
        static void Prefix(object __instance)
        {
            if (!(__instance is ObjectContainer<Building> container)) return;
            try { SimZones.OnFlush(container); }
            catch (System.Exception e) { SimZones.Invalidate(); Perf.Fail(nameof(Opt.sim_zones_buildings), e); }
        }
    }

    [HarmonyPatch(typeof(SimObjectsZones), "checkBuildings")]
    static class SimZones_CheckBuildings
    {
        static bool Prefix() => SimZones.CheckBuildings();
    }
}
