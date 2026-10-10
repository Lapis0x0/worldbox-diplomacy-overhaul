using System.Collections.Generic;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 选择目标时的地图提示：用原版"量子精灵"画箭头（出兵方 → 鼠标）和区域高亮，做法同原版战争低语
    public static class Visuals
    {
        static readonly Color ColorSource = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color ColorTarget = new Color(1f, 0.15f, 0.15f, 0.3f);
        static readonly Color ColorRally = new Color(0.3f, 1f, 0.4f, 0.3f);

        // 选好出兵方后：自己国家的城市是召回目的地（绿），其他是进攻目标（红）
        static Color TargetColor(City pSource, City pTarget) =>
            pSource != null && pTarget.kingdom == pSource.kingdom ? ColorRally : ColorTarget;

        public static void Init()
        {
            Register(new QuantumSpriteAsset
            {
                id = "vd_arrows",
                id_prefab = "p_mapArrow_line",
                base_scale = 0.5f,
                draw_call = DrawArrows,
                render_map = true,
                render_gameplay = true
            });
            Register(new QuantumSpriteAsset
            {
                id = "vd_zones",
                id_prefab = "p_mapZone",
                base_scale = 1f,
                draw_call = DrawZones,
                render_map = true,
                add_camera_zoom_multiplier = false
            });
        }

        // 热重载时资源已存在：Upsert 只换绘制函数，渲染用的对象保留
        static void Register(QuantumSpriteAsset pAsset)
        {
            QuantumSpriteAsset asset = Core.Upsert(AssetManager.quantum_sprites, pAsset);
            // 原版每帧遍历的是缓存数组，游戏开始后新加的精灵要清掉缓存才会被画
            AssetManager.quantum_sprites._array = null;
            if (asset.group_system != null) return;
            QuantumSpriteGroupSystem system = new GameObject(asset.id).AddComponent<QuantumSpriteGroupSystem>();
            system.create(asset);
            asset.group_system = system;
            system.turn_off_renderer = asset.turn_off_renderer;
        }

        static bool Selected(string pPower) => World.world.isSelectedPower(pPower);

        static bool AnyOwnPowerSelected() =>
            Selected(Powers.MakeVassal) || Selected(Powers.ReleaseVassal) || Selected(Powers.ReleaseCity)
            || Selected(Powers.OrderAttack) || Selected(Powers.OrderKingdomAttack);

        static Kingdom Alive(Kingdom k) => k != null && !k.isRekt() && !k.isNeutral() ? k : null;
        static City Alive(City c) => c != null && !c.isRekt() ? c : null;

        static City HoveredCity()
        {
            City city = World.world.getMouseTilePosCachedFrame()?.zone_city;
            return city != null && !city.isRekt() && city.kingdom != null && !city.kingdom.isNeutral() ? city : null;
        }

        static void DrawArrows(QuantumSpriteAsset pAsset)
        {
            if (!InputHelpers.mouseSupported || World.world.isBusyWithUI()) return;
            Vector2 mouse = World.world.getMousePos();
            City source = Alive(Powers._source);

            if (Selected(Powers.OrderAttack) && source != null)
            {
                Arrow(pAsset, source, mouse);
            }
            else if (Selected(Powers.OrderKingdomAttack) && source != null)
            {
                foreach (City city in source.kingdom.getCities())
                {
                    if (city.hasArmy() && city.army.countUnits() > 0) Arrow(pAsset, city, mouse);
                }
            }
            else if (Selected(Powers.MakeVassal) && Alive(Powers._lord) != null)
            {
                foreach (City city in Powers._lord.getCities()) Arrow(pAsset, city, mouse);
            }
        }

        static void Arrow(QuantumSpriteAsset pAsset, City pFrom, Vector2 pTo)
        {
            WorldTile tile = pFrom.getTile();
            if (tile == null) return;
            Color color = pFrom.kingdom.getColor().getColorMainSecond();
            QuantumSpriteLibrary.drawArrowQuantumSprite(pAsset, tile.posV, pTo, ref color);
        }

        static void DrawZones(QuantumSpriteAsset pAsset)
        {
            if (World.world.isBusyWithUI() || !AnyOwnPowerSelected()) return;
            City hovered = HoveredCity();
            City source = Alive(Powers._source);
            Kingdom lord = Alive(Powers._lord);

            if (Selected(Powers.OrderAttack))
            {
                // 第一下：点亮鼠标下的城市；第二下：出兵城市点亮，目标城市标红
                if (source != null) Zones(pAsset, source.zones, ColorSource);
                if (hovered != null && hovered != source) Zones(pAsset, hovered.zones, source != null ? TargetColor(source, hovered) : ColorSource);
                else if (hovered != null && source != null) Zones(pAsset, hovered.zones, ColorRally);
            }
            else if (Selected(Powers.OrderKingdomAttack))
            {
                if (source != null) Kingdom(pAsset, source.kingdom, ColorSource);
                if (hovered != null)
                {
                    if (source == null) Kingdom(pAsset, hovered.kingdom, ColorSource);
                    else Zones(pAsset, hovered.zones, TargetColor(source, hovered));
                }
            }
            else if (Selected(Powers.MakeVassal))
            {
                if (lord != null) Kingdom(pAsset, lord, ColorSource);
                if (hovered != null && hovered.kingdom != lord) Kingdom(pAsset, hovered.kingdom, lord != null ? ColorTarget : ColorSource);
            }
            else if (Selected(Powers.ReleaseVassal))
            {
                if (hovered != null) Kingdom(pAsset, hovered.kingdom, ColorSource);
            }
            else if (Selected(Powers.ReleaseCity))
            {
                if (hovered != null) Zones(pAsset, hovered.zones, ColorSource);
            }
        }

        static void Zones(QuantumSpriteAsset pAsset, List<TileZone> pZones, Color pColor) =>
            QuantumSpriteLibrary.colorZones(pAsset, pZones, pColor);

        static void Kingdom(QuantumSpriteAsset pAsset, Kingdom pKingdom, Color pColor)
        {
            foreach (City city in pKingdom.getCities()) Zones(pAsset, city.zones, pColor);
        }
    }
}
