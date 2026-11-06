using System;
using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;

namespace DiplomacyOverhaul
{
    // 集团地图视图：底栏「智域与生命」页签里「氏族图层」旁边多一个「集团图层」开关，打开后地图按集团给城上色。
    // 一座城属于城主家族所在的集团，颜色和边界取魁首家族（原版氏族的颜色），不在集团里的城不上色。
    // 悬停显示国家和集团名；点一下打开那个国家的议会窗口并选中这个集团；名牌写国家和集团名、合计议席。
    //
    // 原版的地图视图是 MetaTypeLibrary 里的一项，按开关挑出当前那一项。这一项不登记进库（库里的每一项还牵涉窗口、列表、
    // 历史等），只在挑当前视图时换成它；它借用氏族的 map_mode，名牌走原版氏族名牌那条路，在那里换成集团的
    public static class BlocMapView
    {
        public const string PowerId = "vd_bloc_layer";
        const string OptionId = "vd_map_bloc_layer";
        const string SpaceName = "_space_vd_bloc";

        static MetaTypeAsset _asset;

        internal static bool Active => _asset != null && Settings.ParliamentEnabled && PlayerConfig.dict.TryGetValue(OptionId, out PlayerOptionData d) && d.boolVal;

        public static void Init()
        {
            OptionAsset option = Core.Upsert(AssetManager.options_library, new OptionAsset { id = OptionId, type = OptionType.Bool });
            // 开关只记在内存里，不写进原版的玩家设置文件：重启后默认关着
            if (!PlayerConfig.dict.ContainsKey(OptionId)) PlayerConfig.dict[OptionId] = new PlayerOptionData(OptionId) { boolVal = false };

            Core.Upsert(AssetManager.powers, new GodPower
            {
                id = PowerId,
                name = PowerId,
                unselect_when_window = true,
                map_modes_switch = true,
                toggle_name = OptionId,
                toggle_action = Toggle
            });

            _asset = new MetaTypeAsset
            {
                id = "vd_bloc",
                map_mode = MetaType.Clan,
                option_id = OptionId,
                option_asset = option,
                get_selected = () => null,
                draw_zones = Draw,
                check_cursor_highlight = Highlight,
                check_tile_has_meta = (pZone, _, _) => Guard("集团视图", () => Of(pZone?.city) != null),
                tile_get_metaobject = (pZone, _) => Of(pZone?.city)?.clan,
                check_cursor_tooltip = Tooltip,
                click_action_zone = Click
            };
        }

        // 和原版的地图视图开关一样：打开时关掉其他视图
        static void Toggle(string pPower)
        {
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                GodPower power = AssetManager.powers.get(pPower);
                WorldTip.instance.showToolbarText(power);
                PlayerOptionData data = PlayerConfig.dict[OptionId];
                data.boolVal = !data.boolVal;
                if (data.boolVal) PowerLibrary.disableAllOtherMapModes(pPower);
                else WorldTip.instance.startHide();
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "集团视图开关", e); }
        }

        // 按钮：复制原版氏族视图的按钮（带开关图标），放在它后面一列的下格，和其他视图的按钮排在同一行
        public static void BuildButton()
        {
            PowerButton clan = PowerButton.get("clan_layer");
            if (clan == null) return;
            Transform tab = clan.transform.parent;
            for (int i = tab.childCount - 1; i >= 0; i--)
            {
                GameObject child = tab.GetChild(i).gameObject;
                if (child.name != PowerId && child.name != SpaceName) continue;
                PowerButton old = child.GetComponent<PowerButton>();
                if (old != null)
                {
                    PowerButton.power_buttons.Remove(old);
                    PowerButton.toggle_buttons.Remove(old);
                }
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
            if (!Settings.ParliamentEnabled) return;

            int at = clan.transform.GetSiblingIndex() + 1;
            var space = new GameObject(SpaceName, typeof(RectTransform));
            space.transform.SetParent(tab, false);
            space.transform.SetSiblingIndex(at);

            // 先放进一个关着的节点里复制，改好名字再挂上去：按钮在启用时按名字找神力
            var holder = new GameObject("vd_holder");
            holder.SetActive(false);
            GameObject button = UnityEngine.Object.Instantiate(clan.gameObject, holder.transform);
            button.name = PowerId;
            Image icon = button.transform.Find("Icon")?.GetComponent<Image>();
            if (icon != null) icon.sprite = SpriteTextureLoader.getSprite(ParliamentWindow.IconPath);
            button.transform.SetParent(tab, false);
            button.transform.SetSiblingIndex(at + 1);
            UnityEngine.Object.Destroy(holder);
            button.GetComponent<PowerButton>()?.checkToggleIcon();

            PowersTab powers = tab.GetComponent<PowersTab>();
            if (powers != null) powers._children = -1;
        }

        // 原版挑当前地图视图：集团视图开着时换成它
        internal static MetaTypeAsset Current(MetaTypeAsset pVanilla) => pVanilla == null && Active ? _asset : pVanilla;

        // ---------- 城属于哪个集团 ----------

        internal class Entry
        {
            public Kingdom kingdom;
            public Bloc bloc;
            public Clan clan;
            public readonly List<City> cities = new List<City>();
        }

        class Cache
        {
            public string raw;
            public int year;
            public readonly Dictionary<City, Entry> cities = new Dictionary<City, Entry>();
            public readonly List<Entry> blocs = new List<Entry>();
        }

        static readonly Dictionary<long, Cache> _caches = new Dictionary<long, Cache>();
        static int _refreshed = -1;

        // 每帧最多核对一次：集团或年份变了的国家要重建（议会的势力一年一变，城换手跟着年份走）。
        // 重建要算整个议会，一帧只重建一个国家，其余的先用旧的，免得年初所有国家挤在同一帧
        static void Refresh()
        {
            if (_refreshed == Time.frameCount) return;
            _refreshed = Time.frameCount;
            int year = Date.getCurrentYear();
            bool rebuilt = false;
            _alive.Clear();
            foreach (Kingdom k in World.world.kingdoms)
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                string raw = Blocs.Raw(k);
                if (raw.Length == 0) continue;
                _alive.Add(k.id);
                if (rebuilt || _caches.TryGetValue(k.id, out Cache c) && c.raw == raw && c.year == year) continue;
                rebuilt = true;
                c = new Cache { raw = raw, year = year };
                foreach (Bloc b in Blocs.Of(k, Parliament.Clans(k)))
                {
                    var e = new Entry { kingdom = k, bloc = b, clan = b.leader.clan };
                    foreach (ParliamentClan m in b.members)
                        foreach (City city in m.cities)
                        {
                            e.cities.Add(city);
                            c.cities[city] = e;
                        }
                    c.blocs.Add(e);
                }
                _caches[k.id] = c;
            }
            if (_caches.Count == _alive.Count) return;
            _gone.Clear();
            foreach (long id in _caches.Keys)
                if (!_alive.Contains(id)) _gone.Add(id);
            foreach (long id in _gone) _caches.Remove(id);
        }

        static readonly HashSet<long> _alive = new HashSet<long>();
        static readonly List<long> _gone = new List<long>();

        internal static Entry Of(City pCity)
        {
            if (pCity.isRekt() || pCity.kingdom.isRekt()) return null;
            Refresh();
            return _caches.TryGetValue(pCity.kingdom.id, out Cache c) && c.cities.TryGetValue(pCity, out Entry e) ? e : null;
        }

        internal static IEnumerable<Entry> All()
        {
            Refresh();
            foreach (Cache c in _caches.Values)
                foreach (Entry e in c.blocs) yield return e;
        }

        // ---------- 原版视图的几个回调 ----------

        static T Guard<T>(string pWhere, Func<T> pBody)
        {
            if (!Safety.On(Sys.Interface)) return default;
            try { return pBody(); }
            catch (Exception e)
            {
                Safety.Fail(Sys.Interface, pWhere, e);
                return default;
            }
        }

        static IMetaObject MetaOf(TileZone pZone) => Of(pZone?.city)?.clan;
        // 上色时每块地和它四周都要问一次，委托只建一次
        static readonly MetaZoneGetMetaSimple _metaOf = MetaOf;

        static void Draw(MetaTypeAsset pAsset) => Guard("集团视图上色", () =>
        {
            ZoneCalculator zones = World.world.zone_calculator;
            foreach (Kingdom k in World.world.kingdoms)
                foreach (City city in k.getCities())
                    foreach (TileZone zone in city.zones)
                    {
                        zones.drawBegin();
                        zones.drawZoneMeta(zone, pAsset, _metaOf);
                        zones.drawEnd(zone);
                    }
            return true;
        });

        static void Highlight(MetaTypeAsset pAsset, WorldTile pTile, QuantumSpriteAsset pQAsset) => Guard("集团视图高亮", () =>
        {
            Entry e = Of(pTile?.zone?.city);
            if (e == null) return false;
            foreach (City city in e.cities)
                if (!city.isRekt()) QuantumSpriteLibrary.colorZones(pQAsset, city.zones, pQAsset.color);
            return true;
        });

        const string TipName = "vd_bloc_map_tip";

        // 地图上的集团名前面带上国家名：各国的集团都以城命名，光看集团名分不出是哪国的
        internal static string PlateName(Entry pEntry) =>
            LM.Get("vd_bloc_plate").Replace("$kingdom$", pEntry.kingdom.data.name).Replace("$bloc$", Blocs.Name(pEntry.bloc));
        static Entry _tipFor;

        static bool Tooltip(TileZone pZone, MetaTypeAsset pAsset, int pZoneOption) => Guard("集团视图提示", () =>
        {
            Entry e = Of(pZone?.city);
            if (e == null) return false;
            // 悬停只写名字，详情点开议会窗口看。每帧都会问一次，换了集团才重写文字
            if (_tipFor != e)
            {
                _tipFor = e;
                LM.AddToCurrentLocale(TipName, PlateName(e));
            }
            global::Tooltip.hideTooltip(e.clan, true, "tip");
            global::Tooltip.show(e.clan, "tip", new TooltipData { tip_name = TipName, tooltip_scale = 0.7f, is_sim_tooltip = true });
            return true;
        });

        static bool Click(WorldTile pTile, string pPower) => Guard("集团视图点击", () =>
        {
            Entry e = Of(pTile?.zone?.city);
            if (e == null) return false;
            ParliamentWindow.OpenBloc(e.kingdom, e.bloc.Key);
            return true;
        });
    }

    // 原版挑当前地图视图：所有原版视图都关着、集团视图开着时，换成集团视图
    [HarmonyPatch(typeof(Zones), nameof(Zones.getMapMetaAsset))]
    static class Patch_BlocMapAsset
    {
        static void Postfix(ref MetaTypeAsset __result)
        {
            if (!Safety.On(Sys.Interface)) return;
            try { __result = BlocMapView.Current(__result); }
            catch (Exception e) { Safety.Fail(Sys.Interface, "集团视图", e); }
        }
    }

    // Z、X 轮换地图图层：原版按一张写死的图层表轮换，集团图层不在表里会被跳过。轮换时把它排在氏族图层后面
    [HarmonyPatch(typeof(HotkeyLibrary), "switchZones")]
    static class Patch_BlocMapHotkey
    {
        static bool Prefix(HotkeyLibrary __instance, int pIndexChange)
        {
            if (!Safety.On(Sys.Interface) || !Settings.ParliamentEnabled) return true;
            try
            {
                MetaType[] vanilla = __instance._meta_zones;
                int ours = Array.IndexOf(vanilla, MetaType.Clan) + 1;
                if (ours <= 0) return true;
                // 轮换表：原版的图层，氏族后面插一格集团图层（用 -1 表示）
                int count = vanilla.Length + 1;
                int at;
                if (BlocMapView.Active) at = ours;
                else
                {
                    int i = Array.IndexOf(vanilla, Zones.getCurrentMapBorderMode(pCheckOnlyOption: true));
                    at = i < 0 ? -1 : i >= ours ? i + 1 : i;
                }
                int next = ((at + pIndexChange) % count + count) % count;
                string power;
                if (next == ours) power = BlocMapView.PowerId;
                else power = AssetManager.meta_type_library.getAsset(vanilla[next > ours ? next - 1 : next]).power_option_zone_id;
                GodPower god = AssetManager.powers.get(power);
                god.toggle_action(power);
                PowerButtonSelector.instance.checkToggleIcons();
                WorldTip.instance.showToolbarText(god);
                return false;
            }
            catch (Exception e)
            {
                Safety.Fail(Sys.Interface, "轮换地图图层", e);
                return true;
            }
        }
    }

    // 名牌：集团视图借用氏族的名牌
    [HarmonyPatch(typeof(NameplateManager), "getCurrentMode")]
    static class Patch_BlocMapNameplateMode
    {
        static void Postfix(ref MetaType __result)
        {
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                if (BlocMapView.Active && !Zones.hasPowerForceMapMode() && Zones.showMapNames() && Zones.getCurrentMapBorderMode().isNone()) __result = MetaType.Clan;
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "集团视图名牌", e); }
        }
    }

    // 每个集团一个名牌，挂在视野内它议席最多的城上，写集团名和合计议席
    [HarmonyPatch(typeof(NameplateLibrary), "actionClan")]
    static class Patch_BlocMapNameplates
    {
        static readonly System.Func<NameplateText, string, int, string> Format =
            AccessTools.MethodDelegate<System.Func<NameplateText, string, int, string>>(AccessTools.Method(typeof(NameplateText), "getStringForNameplate"));
        static readonly System.Action<NameplateText, string, Vector3, int> SetText =
            AccessTools.MethodDelegate<System.Action<NameplateText, string, Vector3, int>>(AccessTools.Method(typeof(NameplateText), "setText"));
        static readonly System.Action<NameplateText, int> SetPriority =
            AccessTools.MethodDelegate<System.Action<NameplateText, int>>(AccessTools.Method(typeof(NameplateText), "setPriority"));

        static bool Prefix(NameplateManager pManager, NameplateAsset pAsset)
        {
            if (!Safety.On(Sys.Interface) || !BlocMapView.Active || Zones.getCurrentMapBorderMode() != MetaType.None) return true;
            try
            {
                foreach (BlocMapView.Entry e in BlocMapView.All())
                {
                    City best = null;
                    int bestSeats = -1;
                    foreach (City city in e.cities)
                    {
                        if (city.isRekt() || !World.world.move_camera.isWithinCameraViewNotPowerBar(city.city_center)) continue;
                        int seats = Parliament.CitySeats(city);
                        if (seats > bestSeats) { best = city; bestSeats = seats; }
                    }
                    if (best == null) continue;
                    NameplateText plate = pManager.prepareNext(pAsset, e.clan);
                    plate.showTextClanCity(e.clan, best);
                    SetText(plate, Format(plate, BlocMapView.PlateName(e), e.bloc.seats), best.city_center, 10);
                    SetPriority(plate, e.bloc.seats);
                }
                return false;
            }
            catch (Exception ex)
            {
                Safety.Fail(Sys.Interface, "集团视图名牌", ex);
                return true;
            }
        }
    }
}
