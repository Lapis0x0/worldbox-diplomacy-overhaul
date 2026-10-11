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
            Selected(Powers.MakeVassal) || Selected(Powers.InciteIndependence) || Selected(Powers.GreatRebellion) || Selected(Powers.Mandate) || Selected(Powers.ReleaseCity)
            || Selected(Powers.CommandArmy) || Selected(Powers.OrderKingdomAttack)
            || Selected(Powers.TransferCity) || Selected(Powers.Annex);

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

            if (Selected(Powers.CommandArmy) && Powers.AliveArmy(Powers._army))
            {
                ArmyArrow(pAsset, Powers._army, mouse);
            }
            else if (!World.world.isAnyPowerSelected())
            {
                // 框选了士兵：每支军队一条箭头，右键就往鼠标处下令
                foreach (Army army in BoxCommand.SelectedArmies()) ArmyArrow(pAsset, army, mouse);
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
            else if (Selected(Powers.TransferCity) && Alive(Powers._transferCity) != null)
            {
                Arrow(pAsset, Powers._transferCity, mouse);
            }
            else if (Selected(Powers.Annex) && Alive(Powers._annexer) != null)
            {
                foreach (City city in Powers._annexer.getCities()) Arrow(pAsset, city, mouse);
            }
        }

        static void ArmyArrow(QuantumSpriteAsset pAsset, Army pArmy, Vector2 pTo)
        {
            Actor lead = pArmy.getCaptain();
            if (lead == null || lead.isRekt()) lead = pArmy.units[0];
            Color color = pArmy.getColor().getColorMainSecond();
            QuantumSpriteLibrary.drawArrowQuantumSprite(pAsset, lead.current_position, pTo, ref color);
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
            if (World.world.isBusyWithUI()) return;
            City hovered = HoveredCity();
            if (!World.world.isAnyPowerSelected())
            {
                // 框选了士兵：鼠标下的城市按进攻（红）/驻扎（绿）着色
                List<Army> armies = BoxCommand.SelectedArmies();
                if (armies.Count > 0 && hovered != null) Zones(pAsset, hovered.zones, TargetColor(armies[0].getCity(), hovered));
                return;
            }
            if (!AnyOwnPowerSelected()) return;
            City source = Alive(Powers._source);
            Kingdom lord = Alive(Powers._lord);

            if (Selected(Powers.CommandArmy))
            {
                // 选好军队后：鼠标下的城市按进攻（红）/驻扎（绿）着色
                City home = Powers.AliveArmy(Powers._army) ? Alive(Powers._army.getCity()) : null;
                // 已经驻扎在这座城：白色，点了是解除驻扎
                if (home != null && hovered != null && Powers.ArmyUnderCursor() != Powers._army)
                    Zones(pAsset, hovered.zones, hovered == Orders.GetGarrison(home) ? ColorSource : TargetColor(home, hovered));
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
            else if (Selected(Powers.Mandate))
            {
                // 按当前阶段的颜色高亮，没有周期的国家用默认的白色
                if (hovered != null) Kingdom(pAsset, hovered.kingdom, Cycle.PhaseColor(hovered.kingdom) ?? ColorSource);
            }
            else if (Selected(Powers.InciteIndependence) || Selected(Powers.GreatRebellion))
            {
                if (hovered != null) Kingdom(pAsset, hovered.kingdom, ColorSource);
            }
            else if (Selected(Powers.TransferCity))
            {
                City moving = Alive(Powers._transferCity);
                if (moving != null) Zones(pAsset, moving.zones, ColorSource);
                if (hovered == null) return;
                if (moving == null) Zones(pAsset, hovered.zones, ColorSource);
                else if (hovered.kingdom != moving.kingdom) Kingdom(pAsset, hovered.kingdom, ColorRally);
            }
            else if (Selected(Powers.Annex))
            {
                Kingdom annexer = Alive(Powers._annexer);
                if (annexer != null) Kingdom(pAsset, annexer, ColorSource);
                if (hovered != null && hovered.kingdom != annexer) Kingdom(pAsset, hovered.kingdom, annexer != null ? ColorTarget : ColorSource);
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
