using NeoModLoader.General;

namespace DiplomacyOverhaul
{
    public static class Powers
    {
        public const string MakeVassal = "vd_make_vassal";
        public const string InciteIndependence = "vd_incite_independence";
        public const string GreatRebellion = "vd_great_rebellion";
        public const string Mandate = "vd_mandate";
        public const string ReleaseCity = "vd_release_city";
        public const string CommandArmy = "vd_command_army";
        public const string OrderKingdomAttack = "vd_order_kingdom_attack";
        public const string TransferCity = "vd_transfer_city";
        public const string Annex = "vd_annex";

        internal static Kingdom _lord;

        public static void Init()
        {
            Core.Upsert(AssetManager.powers, new GodPower
            {
                id = MakeVassal,
                name = MakeVassal,
                force_map_mode = MetaType.Kingdom,
                can_drag_map = true,
                select_button_action = _ =>
                {
                    _lord = null;
                    WorldTip.showNow("vd_tip_pick_lord", true, "top");
                    return false;
                },
                click_special_action = (pTile, _) => ClickMakeVassal(pTile)
            });

            Core.Upsert(AssetManager.powers, new GodPower
            {
                id = InciteIndependence,
                name = InciteIndependence,
                force_map_mode = MetaType.Kingdom,
                can_drag_map = true,
                select_button_action = _ =>
                {
                    WorldTip.showNow("vd_tip_pick_incite", true, "top");
                    return false;
                },
                click_special_action = (pTile, _) => ClickIncite(pTile)
            });

            Core.Upsert(AssetManager.powers, new GodPower
            {
                id = GreatRebellion,
                name = GreatRebellion,
                force_map_mode = MetaType.Kingdom,
                can_drag_map = true,
                select_button_action = _ =>
                {
                    WorldTip.showNow("vd_tip_pick_great_rebellion", true, "top");
                    return false;
                },
                click_special_action = (pTile, _) => ClickGreatRebellion(pTile)
            });

            Core.Upsert(AssetManager.powers, new GodPower
            {
                id = Mandate,
                name = Mandate,
                force_map_mode = MetaType.Kingdom,
                can_drag_map = true,
                select_button_action = _ =>
                {
                    WorldTip.showNow("vd_tip_pick_mandate", true, "top");
                    return false;
                },
                click_special_action = (pTile, _) => ClickMandate(pTile)
            });

            InitReleaseCity();
            InitOrders();
            InitCityActions();
        }

        internal static City _transferCity;
        internal static Kingdom _annexer;

        static void InitCityActions()
        {
            Core.Upsert(AssetManager.powers, new GodPower
            {
                id = TransferCity,
                name = TransferCity,
                force_map_mode = MetaType.City,
                can_drag_map = true,
                select_button_action = _ =>
                {
                    _transferCity = null;
                    WorldTip.showNow("vd_tip_pick_transfer_city", true, "top");
                    return false;
                },
                click_special_action = (pTile, _) => ClickTransfer(pTile)
            });
            Core.Upsert(AssetManager.powers, new GodPower
            {
                id = Annex,
                name = Annex,
                force_map_mode = MetaType.Kingdom,
                can_drag_map = true,
                select_button_action = _ =>
                {
                    _annexer = null;
                    WorldTip.showNow("vd_tip_pick_annexer", true, "top");
                    return false;
                },
                click_special_action = (pTile, _) => ClickAnnex(pTile)
            });
        }

        static bool ClickTransfer(WorldTile pTile)
        {
            City city = pTile?.zone?.city;
            if (city == null || city.isRekt() || city.kingdom == null || city.kingdom.isNeutral()) return false;
            if (_transferCity == null || _transferCity.isRekt())
            {
                if (city.isCapitalCity())
                {
                    WorldTip.showNow("vd_tip_transfer_capital", true, "top");
                    return false;
                }
                _transferCity = city;
                Tip(LM.Get("vd_tip_pick_transfer_target").Replace("$city$", city.data.name));
                return false;
            }
            City source = _transferCity;
            Kingdom from = source.kingdom;
            string error = CityActions.TransferCity(source, city.kingdom);
            if (error != null)
            {
                WorldTip.showNow(error, true, "top");
                return false;
            }
            _transferCity = null;
            Tip(LM.Get("vd_tip_transferred").Replace("$city$", source.data.name)
                .Replace("$from$", from.data.name).Replace("$to$", city.kingdom.data.name));
            return true;
        }

        static bool ClickAnnex(WorldTile pTile)
        {
            Kingdom k = KingdomAt(pTile);
            if (k == null) return false;
            if (_annexer == null || !_annexer.isAlive())
            {
                _annexer = k;
                Tip(LM.Get("vd_tip_pick_annexed").Replace("$kingdom$", k.data.name));
                return false;
            }
            if (_annexer == k)
            {
                _annexer = null;
                WorldTip.showNow("vd_tip_cancelled", true, "top");
                return false;
            }
            Kingdom survivor = _annexer;
            string absorbedName = k.data.name;
            string error = CityActions.Annex(survivor, k);
            if (error != null)
            {
                WorldTip.showNow(error, true, "top");
                return false;
            }
            _annexer = null;
            Tip(LM.Get("vd_tip_annexed").Replace("$survivor$", survivor.data.name).Replace("$absorbed$", absorbedName));
            return true;
        }

        internal static City _source;
        internal static Army _army;

        static void InitOrders()
        {
            Core.Upsert(AssetManager.powers, new GodPower
            {
                id = CommandArmy,
                name = CommandArmy,
                force_map_mode = MetaType.Army,
                can_drag_map = true,
                select_button_action = _ =>
                {
                    SetArmy(null);
                    WorldTip.showNow("vd_tip_pick_army", true, "top");
                    return false;
                },
                click_special_action = (pTile, _) => ClickCommandArmy(pTile)
            });
            Core.Upsert(AssetManager.powers, new GodPower
            {
                id = OrderKingdomAttack,
                name = OrderKingdomAttack,
                force_map_mode = MetaType.Kingdom,
                can_drag_map = true,
                select_button_action = _ =>
                {
                    _source = null;
                    WorldTip.showNow("vd_tip_pick_attacker_kingdom", true, "top");
                    return false;
                },
                click_special_action = (pTile, _) => ClickKingdomOrder(pTile)
            });
        }

        // 先在军队视图里点军队，选中后切到城市视图点目标；原版每帧按神力的 force_map_mode 决定视图
        static void SetArmy(Army pArmy)
        {
            _army = pArmy;
            AssetManager.powers.get(CommandArmy).force_map_mode = pArmy == null ? MetaType.Army : MetaType.City;
        }

        internal static bool AliveArmy(Army pArmy) => pArmy != null && !pArmy.isRekt() && pArmy.countUnits() > 0;

        // 鼠标下的军队：优先看军队视图里的旗帜，其次看光标附近的士兵
        internal static Army ArmyUnderCursor()
        {
            if (World.world.nameplate_manager.cursor_over_text?.nano_object is Army army && AliveArmy(army)) return army;
            Army near = World.world.getActorNearCursor()?.army;
            return AliveArmy(near) ? near : null;
        }

        // 第一下点军队（旗帜或士兵），第二下点目标城市或空地；再点同一支军队取消
        static bool ClickCommandArmy(WorldTile pTile)
        {
            Army hovered = ArmyUnderCursor();
            if (!AliveArmy(_army))
            {
                SetArmy(null);
                if (hovered == null)
                {
                    WorldTip.showNow("vd_tip_pick_army", true, "top");
                    return false;
                }
                if (hovered.getCity() == null)
                {
                    WorldTip.showNow("vd_tip_army_no_city", true, "top");
                    return false;
                }
                SetArmy(hovered);
                Tip(LM.Get("vd_tip_army_picked").Replace("$army$", hovered.name));
                return false;
            }
            if (hovered == _army)
            {
                SetArmy(null);
                WorldTip.showNow("vd_tip_cancelled", true, "top");
                return false;
            }

            if (pTile == null) return false;
            City city = Orders.TargetCity(pTile);
            Army army = _army;
            bool friendly = city != null && Orders.Friendly(city.kingdom, army.getCity()?.kingdom);
            if (Orders.Release(army.getCity(), pTile))
            {
                Tip(LM.Get("vd_tip_army_released").Replace("$army$", army.name));
                SetArmy(null);
                return true;
            }
            string error = Orders.CommandArmy(army, pTile);
            if (error != null)
            {
                WorldTip.showNow(error, true, "top");
                return false;
            }
            if (city == null) Tip(LM.Get("vd_tip_army_holding").Replace("$army$", army.name));
            else Tip(LM.Get(friendly ? "vd_tip_army_moving" : "vd_tip_army_attacking")
                .Replace("$army$", army.name).Replace("$target$", city.data.name));
            SetArmy(null);
            return true;
        }

        // 第一下点出兵王国，第二下点目标城市
        static bool ClickKingdomOrder(WorldTile pTile)
        {
            City city = pTile?.zone?.city;
            if (city == null || city.isRekt() || city.kingdom == null || city.kingdom.isNeutral()) return false;

            if (_source == null || _source.isRekt())
            {
                _source = city;
                Tip(LM.Get("vd_tip_pick_target_city").Replace("$source$", city.kingdom.data.name));
                return false;
            }
            City source = _source;
            string error;
            if (Orders.Friendly(city.kingdom, source.kingdom))
            {
                // 点的是本国或友方的城市：全军前往驻扎
                int moved = Orders.RecallKingdom(source.kingdom, city);
                error = moved == 0 ? "vd_tip_no_army" : null;
                if (error == null)
                    Tip(LM.Get("vd_tip_kingdom_recall_ordered").Replace("$source$", source.kingdom.data.name)
                        .Replace("$target$", city.data.name).Replace("$count$", moved.ToString()));
            }
            else
            {
                int count = Orders.AttackWithKingdom(source.kingdom, city, out error);
                if (error == null)
                    Tip(LM.Get("vd_tip_kingdom_attack_ordered").Replace("$source$", source.kingdom.data.name)
                        .Replace("$target$", city.data.name).Replace("$count$", count.ToString()));
            }
            if (error != null)
            {
                WorldTip.showNow(error, true, "top");
                return false;
            }
            _source = null;
            return true;
        }

        static void InitReleaseCity()
        {
            Core.Upsert(AssetManager.powers, new GodPower
            {
                id = ReleaseCity,
                name = ReleaseCity,
                force_map_mode = MetaType.City,
                can_drag_map = true,
                select_button_action = _ =>
                {
                    WorldTip.showNow("vd_tip_pick_city", true, "top");
                    return false;
                },
                click_special_action = (pTile, _) => ClickReleaseCity(pTile)
            });
        }

        static bool ClickReleaseCity(WorldTile pTile)
        {
            City city = pTile?.zone?.city;
            if (city == null || city.isRekt()) return false;
            Kingdom owner = city.kingdom;
            string error = Vassals.ReleaseCity(city, out Kingdom created);
            if (error != null)
            {
                WorldTip.showNow(error, true, "top");
                return false;
            }
            Kingdom lord = Vassals.GetOverlord(created) ?? owner;
            Tip(LM.Get("vd_tip_city_released").Replace("$vassal$", created.data.name).Replace("$lord$", lord.data.name));
            return true;
        }

        static Kingdom KingdomAt(WorldTile pTile)
        {
            City city = pTile?.zone?.city;
            if (city == null || city.isRekt()) return null;
            Kingdom k = city.kingdom;
            if (k == null || k.isRekt() || k.isNeutral()) return null;
            return k;
        }

        static void Tip(string pText) => WorldTip.showNow(pText, false, "top");

        static bool ClickMakeVassal(WorldTile pTile)
        {
            Kingdom k = KingdomAt(pTile);
            if (k == null) return false;

            if (_lord == null || !_lord.isAlive())
            {
                _lord = k;
                Tip(LM.Get("vd_tip_pick_vassal").Replace("$lord$", k.data.name));
                return false;
            }
            if (_lord == k)
            {
                _lord = null;
                WorldTip.showNow("vd_tip_cancelled", true, "top");
                return false;
            }

            string error = Vassals.MakeVassal(_lord, k);
            if (error != null)
            {
                WorldTip.showNow(error, true, "top");
                return false;
            }
            Tip(LM.Get("vd_tip_done").Replace("$vassal$", k.data.name).Replace("$lord$", _lord.data.name));
            _lord = null;
            return true;
        }

        // 手动触发不看冷却，成功提示由 GreatRebellion 统一发
        static bool ClickGreatRebellion(WorldTile pTile)
        {
            Kingdom k = KingdomAt(pTile);
            if (k == null) return false;
            if (DiplomacyOverhaul.GreatRebellion.Trigger(k) == 0)
            {
                WorldTip.showNow("vd_tip_great_rebellion_failed", true, "top");
                return false;
            }
            return true;
        }

        static bool ClickMandate(WorldTile pTile)
        {
            Kingdom k = KingdomAt(pTile);
            if (k == null) return false;
            string error = Cycle.Advance(k);
            if (error == null) return true;
            WorldTip.showNow(error, true, "top");
            return false;
        }

        // 附庸立刻向宗主发动独立战争，结局交给 Patch_EndWar
        static bool ClickIncite(WorldTile pTile)
        {
            Kingdom k = KingdomAt(pTile);
            if (k == null) return false;
            Kingdom lord = Vassals.GetOverlord(k);
            if (lord == null)
            {
                WorldTip.showNow("vd_tip_not_vassal", true, "top");
                return false;
            }
            if (Independence.StartWar(k, lord) == null)
            {
                WorldTip.showNow("vd_tip_war_failed", true, "top");
                return false;
            }
            Tip(LM.Get("vd_tip_incited").Replace("$vassal$", k.data.name).Replace("$lord$", lord.data.name));
            return true;
        }
    }
}
