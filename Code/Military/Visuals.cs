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

        // 选好出兵方后：本国和友方的城市是驻扎目的地（绿），其他是进攻目标（红）
        static Color TargetColor(Kingdom pSource, City pTarget) =>
            pSource != null && Orders.Friendly(pTarget.kingdom, pSource) ? ColorRally : ColorTarget;

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
            // 原版地图上军队旗子的精灵自带文字，只在调试选项里才显示人数；改为选中的军队显示，打开了那个选项则全部显示
            QuantumSpriteAsset armies = AssetManager.quantum_sprites.get("armies");
            if (armies != null) armies.draw_call = DrawArmyFlags;
        }

        // 地图上的军旗：地方军用原版的旗，禁军是金边深色布加一颗金星，遮罩颜色和锚点与原版相同
        static readonly Sprite LocalFlag = SpriteTextureLoader.getSprite("civ/icons/minimap_flag");
        static readonly Sprite GuardFlag = GuardFlagSprite();

        static Sprite GuardFlagSprite()
        {
            Sprite ours = SpriteTextureLoader.getSprite("ui/Icons/vd_flag_guard");
            if (ours == null) return LocalFlag;
            return Sprite.Create(ours.texture, ours.textureRect, new Vector2(0.5f, LocalFlag.pivot.y / LocalFlag.rect.height), LocalFlag.pixelsPerUnit);
        }

        // 玩家下过令的军队，旗帜上的人数按命令着色：进攻红，驻扎、扎营绿
        static readonly Color CountAttack = new Color(1f, 0.45f, 0.4f);
        static readonly Color CountStation = new Color(0.5f, 1f, 0.55f);

        // 样式和原版打开「军队旁显示人数」时相同
        static void ShowCount(QuantumSpriteWithText pSprite, string pText, Color pColor)
        {
            TextMesh text = pSprite.text;
            text.gameObject.SetActive(true);
            text.text = pText;
            text.color = pColor;
            Renderer renderer = text.GetComponent<Renderer>();
            renderer.sortingLayerID = pSprite.sprite_renderer.sortingLayerID;
            renderer.sortingOrder = pSprite.sprite_renderer.sortingOrder;
        }

        // 正在下令的军队：框选、单击旗帜、拉近框选的士兵所属的军队，或者指挥军队神力选好的那支
        static List<Army> CommandedArmies()
        {
            if (!World.world.isAnyPowerSelected()) return BoxCommand.SelectedArmies();
            var list = new List<Army>();
            if (Selected(Powers.CommandArmy) && Powers.AliveArmy(Powers._army)) list.Add(Powers._army);
            return list;
        }

        // 照抄原版 QuantumSpriteLibrary.drawArmies，只改人数：选中的军队、玩家下过令的军队显示；打开了原版「军队旁显示人数」选项则全部显示
        static void DrawArmyFlags(QuantumSpriteAsset pAsset)
        {
            if (!PlayerConfig.optionBoolEnabled("marks_armies")) return;
            if (global::Zones.showArmyZones() && global::Zones.showMapNames()) return;
            List<Army> selected = CommandedArmies();
            bool show_all = DebugConfig.isOn(DebugOption.ShowAmountNearArmy);
            int num = 0;
            for (int i = 0; i < World.world.armies.list.Count; i++)
            {
                if (num > 2) break;
                Army army = World.world.armies.list[i];
                if (!army.hasCaptain()) continue;
                Actor captain = army.getCaptain();
                if (captain.isInMagnet() || !captain.current_zone.visible || !captain.isKingdomCiv()) continue;
                Kingdom kingdom = captain.kingdom;
                var sprite = (QuantumSpriteWithText)QuantumSpriteLibrary.drawQuantumSprite(pAsset, captain.current_position, null, kingdom, captain.city);
                bool commanded = Orders.IsCommanded(army);
                if (show_all || commanded || selected.Contains(army))
                    ShowCount(sprite, army.countUnits().ToString(),
                        !commanded ? Color.white : Orders.IsForced(army) ? CountAttack : CountStation);
                else sprite.text.gameObject.SetActive(false);
                if (!pAsset.group_system.is_within_active_index) num++;
                sprite.setSprite(DynamicSprites.getIcon(Guard.Is(army) ? GuardFlag : LocalFlag, kingdom.getColor()));
            }
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
            || Selected(Powers.CommandArmy) || Selected(Powers.OrderKingdomAttack) || Selected(Powers.ReleaseArmy)
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
                foreach (Army army in Orders.ArmiesOf(source.kingdom)) ArmyArrow(pAsset, army, mouse);
            }
            else if (Selected(Powers.ReleaseArmy))
            {
                // 指着一个国家就画它听命令的军队到目的地，否则画所有国家的
                Kingdom kingdom = HoveredCity()?.kingdom;
                foreach (Army army in World.world.armies)
                    if ((kingdom == null || Orders.KingdomOf(army) == kingdom) && Orders.IsCommanded(army)) DestinationArrow(pAsset, army);
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

        static void DestinationArrow(QuantumSpriteAsset pAsset, Army pArmy)
        {
            WorldTile to = Orders.Destination(pArmy);
            if (to != null) ArmyArrow(pAsset, pArmy, to.posV);
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
                // 框选了士兵：鼠标下的城市按进攻（红）/驻扎（绿）着色；已经驻扎在这座城：白色，右键是解除驻扎
                List<Army> armies = BoxCommand.SelectedArmies();
                if (armies.Count > 0 && hovered != null)
                    Zones(pAsset, hovered.zones, BoxCommand.AllStationedAt(armies, World.world.getMouseTilePosCachedFrame())
                        ? ColorSource : TargetColor(Orders.KingdomOf(armies[0]), hovered));
                return;
            }
            if (!AnyOwnPowerSelected()) return;
            City source = Alive(Powers._source);
            Kingdom lord = Alive(Powers._lord);

            if (Selected(Powers.CommandArmy))
            {
                // 选好军队后：鼠标下的城市按进攻（红）/驻扎（绿）着色
                Army army = Powers.AliveArmy(Powers._army) ? Powers._army : null;
                // 已经驻扎在这座城：白色，点了是解除驻扎
                if (army != null && hovered != null && Powers.ArmyUnderCursor() != army)
                    Zones(pAsset, hovered.zones, hovered == Orders.GetGarrison(army) ? ColorSource : TargetColor(Orders.KingdomOf(army), hovered));
            }
            else if (Selected(Powers.OrderKingdomAttack))
            {
                if (source != null) Kingdom(pAsset, source.kingdom, ColorSource);
                if (hovered != null)
                {
                    if (source == null) Kingdom(pAsset, hovered.kingdom, ColorSource);
                    else Zones(pAsset, hovered.zones, TargetColor(source.kingdom, hovered));
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
            else if (Selected(Powers.ReleaseArmy))
            {
                // 这个国家有听玩家命令的军队：整个国家白色，点了全部交还
                if (hovered != null && Orders.CommandedOf(hovered.kingdom).Count > 0)
                    Kingdom(pAsset, hovered.kingdom, ColorSource);
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
