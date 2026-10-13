using System.Collections.Generic;
using HarmonyLib;

namespace DiplomacyOverhaul.Perf
{
    // 地图边缘图层（WorldLayerEdges）：一个像素的边缘颜色只取决于该地块和它正上、正下两个邻居的类型。
    // 原版在地块变化时把所在区块连同周围 8 个区块整片重画；这里只重画变化的地块和上下邻居。
    // 变化的地块就是 MapBox.tiles_dirty，原版在 setTileDirty 里同时登记它和它的区块，在 redrawMiniMap 里一起清空
    [HarmonyPatch(typeof(WorldLayerEdges), nameof(WorldLayerEdges.redraw))]
    static class Edges_Redraw
    {
        static readonly Dictionary<int, UnityEngine.Color32> _ours = new Dictionary<int, UnityEngine.Color32>();

        static bool Prefix(WorldLayerEdges __instance)
        {
            if (!Opt.edges_incremental) return true;
            try { return Run(__instance); }
            catch (System.Exception e) { Perf.Fail(nameof(Opt.edges_incremental), e); return true; }
        }

        static bool Run(WorldLayerEdges __instance)
        {
            HashSet<MapChunk> dirty_chunks = __instance._dirty_chunks;
            if (dirty_chunks.Count == 0) return false;
            foreach (WorldTile tile in World.world.tiles_dirty)
            {
                Redraw(__instance, tile);
                if (tile.tile_up != null) Redraw(__instance, tile.tile_up);
                if (tile.tile_down != null) Redraw(__instance, tile.tile_down);
            }
            if (Opt.verify) Verify(__instance);
            dirty_chunks.Clear();
            __instance.updatePixels();
            return false;
        }

        static void Redraw(WorldLayerEdges pLayer, WorldTile pTile)
        {
            pLayer.pixels[pTile.data.tile_id] = Toolbox.clear;
            pLayer.redrawTile(pTile);
        }

        // 用原版的整片重画算一遍，和增量结果逐像素比较；比较完保留原版结果
        static void Verify(WorldLayerEdges pLayer)
        {
            var chunks = new HashSet<MapChunk>(pLayer._dirty_chunks);
            foreach (MapChunk chunk in pLayer._dirty_chunks) chunks.UnionWith(chunk.neighbours_all);
            _ours.Clear();
            foreach (MapChunk chunk in chunks)
                foreach (WorldTile tile in chunk.tiles)
                    _ours[tile.data.tile_id] = pLayer.pixels[tile.data.tile_id];
            int diff = 0;
            string example = null;
            foreach (MapChunk chunk in chunks)
            {
                foreach (WorldTile tile in chunk.tiles)
                {
                    Redraw(pLayer, tile);
                    UnityEngine.Color32 a = _ours[tile.data.tile_id], b = pLayer.pixels[tile.data.tile_id];
                    if (a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a) continue;
                    diff++;
                    example ??= $"tile {tile.x},{tile.y} type={tile.Type.id} ours={a} vanilla={b}";
                }
            }
            Verifier.Report("edges_incremental", diff, chunks.Count * 256, example);
        }
    }
}
