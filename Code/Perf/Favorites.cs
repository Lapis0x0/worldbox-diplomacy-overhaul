using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul.Perf
{
    // 收藏名单。原版有三处每次都遍历全部物品或单位来找收藏：
    // 收藏物品按钮、收藏单位按钮（每次有单位或建筑增减都检查），以及地图上的收藏物品图标（每帧）。
    // 这里每 0.5 秒重建一次名单，玩家切换收藏时立即重建；读档、掉落效果等直接改数据的情况由定时重建兜底
    public static class Favorites
    {
        const float Interval = 0.5f;

        static readonly List<Item> _items = new List<Item>();
        static readonly List<Actor> _units = new List<Actor>();
        static float _next;
        static bool _dirty = true;

        public static void SetDirty() => _dirty = true;

        static void Check()
        {
            float now = Time.unscaledTime;
            if (!_dirty && now < _next) return;
            _dirty = false;
            _next = now + Interval;
            _items.Clear();
            World.world.items.checkLists();
            foreach (Item item in World.world.items.list)
                if (item.isFavorite()) _items.Add(item);
            _units.Clear();
            foreach (Actor unit in World.world.units.getSimpleList())
                if (unit.isFavorite()) _units.Add(unit);
        }

        public static List<Item> Items
        {
            get { Check(); return _items; }
        }

        public static bool AnyItem()
        {
            foreach (Item item in Items)
                if (item.isFavorite() && !item.isRekt()) return true;
            return false;
        }

        public static bool AnyUnit()
        {
            Check();
            foreach (Actor unit in _units)
                if (unit.isFavorite() && !unit.isRekt()) return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(FavoriteItemSpriteSwitcher), "hasAny")]
    static class Favorites_ItemButton
    {
        static bool Prefix(ref bool __result)
        {
            if (!Opt.favorites_cache) return true;
            try { __result = Favorites.AnyItem(); }
            catch (System.Exception e) { Perf.Fail(nameof(Opt.favorites_cache), e); return true; }
            if (Opt.verify)
            {
                bool vanilla = false;
                foreach (Item item in World.world.items)
                    if (item.isFavorite()) { vanilla = true; break; }
                Verifier.Report("favorites_item_button", vanilla != __result ? 1 : 0, 1, $"ours={__result} vanilla={vanilla}");
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(FavoriteUnitSpriteSwitcher), "hasAny")]
    static class Favorites_UnitButton
    {
        static bool Prefix(ref bool __result)
        {
            if (!Opt.favorites_cache) return true;
            try { __result = Favorites.AnyUnit(); }
            catch (System.Exception e) { Perf.Fail(nameof(Opt.favorites_cache), e); return true; }
            if (Opt.verify)
            {
                bool vanilla = false;
                foreach (Actor unit in World.world.units)
                    if (unit.isFavorite()) { vanilla = true; break; }
                Verifier.Report("favorites_unit_button", vanilla != __result ? 1 : 0, 1, $"ours={__result} vanilla={vanilla}");
            }
            return false;
        }
    }

    // 与原版 drawFavoriteItemsMap 相同，只是遍历收藏名单而不是全部物品
    [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawFavoriteItemsMap")]
    static class Favorites_ItemIcons
    {
        static bool Prefix(QuantumSpriteAsset pAsset)
        {
            if (!Opt.favorites_cache) return true;
            try { return Run(pAsset); }
            catch (System.Exception e) { Perf.Fail(nameof(Opt.favorites_cache), e); return true; }
        }

        static bool Run(QuantumSpriteAsset pAsset)
        {
            if (!PlayerConfig.optionBoolEnabled("marks_favorite_items")) return false;
            int drawn = 0;
            foreach (Item item in Favorites.Items)
            {
                if (!item.isFavorite() || item.isRekt()) continue;
                Actor actor = item.getActor();
                if (actor.isRekt() || !actor.current_zone.visible) continue;
                Vector3 pos = actor.current_position;
                pos.y += 1f;
                QuantumSprite sprite = QuantumSpriteLibrary.drawQuantumSprite(pAsset, pos, null, actor.kingdom, actor.city);
                sprite.setSprite(item.getSprite());
                drawn++;
            }
            if (Opt.verify)
            {
                int vanilla = 0;
                foreach (Item item in World.world.items)
                {
                    if (!item.isFavorite()) continue;
                    Actor actor = item.getActor();
                    if (!actor.isRekt() && actor.current_zone.visible) vanilla++;
                }
                Verifier.Report("favorites_item_icons", vanilla != drawn ? 1 : 0, 1, $"ours={drawn} vanilla={vanilla}");
            }
            return false;
        }
    }

    [HarmonyPatch]
    static class Favorites_Switch
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Actor), nameof(Actor.switchFavorite));
            yield return AccessTools.Method(typeof(CoreSystemObject<ItemData>), nameof(CoreSystemObject<ItemData>.switchFavorite));
            yield return AccessTools.Method(typeof(Item), nameof(Item.setFavorite));
        }

        static void Postfix() => Favorites.SetDirty();
    }
}
