using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul.Perf
{
    // 国界：原版每次重画后把计时器设为 0.01 秒，等于每帧重画。改为 0.1 秒。
    // 切换显示模式、选中王国时原版会清零计时器立即重画，这一点不受影响
    [HarmonyPatch(typeof(ZoneCalculator), "redrawZones")]
    static class Throttle_Borders
    {
        const float Interval = 0.1f;

        static void Postfix(ZoneCalculator __instance)
        {
            if (Opt.visual_throttle && __instance._redraw_timer == 0.01f) __instance._redraw_timer = Interval;
        }
    }

    // 右下角统计面板的计数（野兽、病患、建筑、植物）：原版计时器 0.1，按固定步长 0.02 递减，即每 5 帧重数全部单位和建筑。改为每 25 帧
    [HarmonyPatch(typeof(MapStats), nameof(MapStats.updateStatsForPanel))]
    static class Throttle_Panel
    {
        static void Postfix(MapStats __instance)
        {
            if (Opt.visual_throttle && __instance._timer_stats == 0.1f) __instance._timer_stats = 0.5f;
        }
    }

    // 家族物种图标：原版每帧遍历全部家族找图标位置。改为每 0.2 秒重算一次位置和图标，每帧照常绘制
    [HarmonyPatch(typeof(QuantumSpriteLibrary), "drawFamilySpeciesIcons")]
    static class Throttle_FamilyIcons
    {
        const float Interval = 0.2f;

        static readonly List<(Sprite sprite, Vector3 pos)> _icons = new List<(Sprite, Vector3)>();
        static float _next;

        static bool Prefix(QuantumSpriteAsset pAsset)
        {
            if (!Opt.visual_throttle) return true;
            try { return Run(pAsset); }
            catch (System.Exception e) { Perf.Fail(nameof(Opt.visual_throttle), e); return true; }
        }

        static bool Run(QuantumSpriteAsset pAsset)
        {
            if (!PlayerConfig.optionBoolEnabled("map_species_families"))
            {
                _next = 0f;
                return false;
            }
            float now = Time.unscaledTime;
            if (now >= _next)
            {
                _next = now + Interval;
                _icons.Clear();
                foreach (Family family in World.world.families)
                {
                    if (!family.isAlive() || family.units.Count == 0) continue;
                    Actor actor = family.units[0];
                    if (actor.isRekt() || !actor.current_zone.visible) continue;
                    _icons.Add((family.getActorAsset().getSpriteIcon(), actor.current_tile.zone.centerTile.posV3));
                }
            }
            foreach (var (sprite, pos) in _icons)
                QuantumSpriteLibrary.drawQuantumSprite(pAsset, pos).setSprite(sprite);
            return false;
        }
    }
}
