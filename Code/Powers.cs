using NeoModLoader.General;

namespace DiplomacyOverhaul
{
    public static class Powers
    {
        public const string MakeVassal = "vd_make_vassal";
        public const string ReleaseVassal = "vd_release_vassal";
        public const string ReleaseCity = "vd_release_city";
        public const string OrderAttack = "vd_order_attack";
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
                id = ReleaseVassal,
                name = ReleaseVassal,
                force_map_mode = MetaType.Kingdom,
                can_drag_map = true,
                select_button_action = _ =>
                {
                    WorldTip.showNow("vd_tip_pick_release", true, "top");
                    return false;
                },
                click_special_action = (pTile, _) => ClickRelease(pTile)
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

        static void InitOrders()
        {
            Core.Upsert(AssetManager.powers, new GodPower
            {
                id = OrderAttack,
                name = OrderAttack,
                force_map_mode = MetaType.City,
                can_drag_map = true,
                select_button_action = _ =>
                {
                    _source = null;
                    WorldTip.showNow("vd_tip_pick_attacker_city", true, "top");
                    return false;
                },
                click_special_action = (pTile, _) => ClickOrder(pTile, false)
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
                click_special_action = (pTile, _) => ClickOrder(pTile, true)
            });
        }

        // 第一下点出兵方（城市或王国），第二下点目标城市
        static bool ClickOrder(WorldTile pTile, bool pWholeKingdom)
        {
            City city = pTile?.zone?.city;
            if (city == null || city.isRekt() || city.kingdom == null || city.kingdom.isNeutral()) return false;

            if (_source == null || _source.isRekt())
            {
                _source = city;
                string name = pWholeKingdom ? city.kingdom.data.name : city.data.name;
                Tip(LM.Get("vd_tip_pick_target_city").Replace("$source$", name));
                return false;
            }
            City source = _source;
            string error;
            if (city.kingdom == source.kingdom)
            {
                // 点的是自己国家的城市：召回军队
                if (pWholeKingdom)
                {
                    int moved = Orders.RecallKingdom(source.kingdom, city);
                    error = moved == 0 ? "vd_tip_no_army" : null;
                    if (error == null)
                        Tip(LM.Get("vd_tip_kingdom_recall_ordered").Replace("$source$", source.kingdom.data.name)
                            .Replace("$target$", city.data.name).Replace("$count$", moved.ToString()));
                }
                else
                {
                    error = Orders.Recall(source, city);
                    if (error == null)
                        Tip(LM.Get("vd_tip_recall_ordered").Replace("$source$", source.data.name).Replace("$target$", city.data.name));
                }
            }
            else if (pWholeKingdom)
            {
                int count = Orders.AttackWithKingdom(source.kingdom, city, out error);
                if (error == null)
                    Tip(LM.Get("vd_tip_kingdom_attack_ordered").Replace("$source$", source.kingdom.data.name)
                        .Replace("$target$", city.data.name).Replace("$count$", count.ToString()));
            }
            else
            {
                error = Orders.Attack(source, city);
                if (error == null)
                    Tip(LM.Get("vd_tip_attack_ordered").Replace("$source$", source.data.name).Replace("$target$", city.data.name));
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
            Tip(LM.Get("vd_tip_city_released").Replace("$vassal$", created.data.name).Replace("$lord$", owner.data.name));
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

        static bool ClickRelease(WorldTile pTile)
        {
            Kingdom k = KingdomAt(pTile);
            if (k == null) return false;
            Kingdom lord = Vassals.GetOverlord(k);
            if (lord == null)
            {
                WorldTip.showNow("vd_tip_not_vassal", true, "top");
                return false;
            }
            Vassals.Release(k);
            Tip(LM.Get("vd_tip_released").Replace("$vassal$", k.data.name).Replace("$lord$", lord.data.name));
            return true;
        }
    }
}
