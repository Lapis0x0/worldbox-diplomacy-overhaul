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

        static Kingdom _lord;

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
        }

        static City _source;

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
            if (pWholeKingdom ? city.kingdom == _source.kingdom : city == _source)
            {
                _source = null;
                WorldTip.showNow("vd_tip_cancelled", true, "top");
                return false;
            }

            City source = _source;
            string error;
            if (pWholeKingdom)
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
