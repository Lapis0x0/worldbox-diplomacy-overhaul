using NeoModLoader.General;

namespace DiplomacyOverhaul
{
    public static class Powers
    {
        public const string MakeVassal = "vd_make_vassal";
        public const string ReleaseVassal = "vd_release_vassal";

        static Kingdom _lord;

        public static void Init()
        {
            AssetManager.powers.add(new GodPower
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

            AssetManager.powers.add(new GodPower
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
