using System.Collections.Generic;
using System.Text;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using static DiplomacyOverhaul.UiKit;

namespace DiplomacyOverhaul
{
    // 选举与任命窗口右边：各城的封疆图
    public static partial class ElectionWindow
    {
        static City FindCity(long pId)
        {
            if (pId < 0) return null;
            foreach (City c in _kingdom.getCities())
            {
                if (c.id == pId) return c;
            }
            return null;
        }

        // 下方显示的城：鼠标下的，其次点过的，都没有时是首都
        static City ShownCity()
        {
            City c = FindCity(_hover) ?? FindCity(_city);
            if (c != null) return c;
            foreach (City x in _kingdom.getCities())
            {
                if (x.isCapitalCity()) return x;
            }
            foreach (City x in _kingdom.getCities()) return x;
            return null;
        }

        // 城的议席归谁：首都归国王或执政，其余归城主
        static Actor Owner(City pCity)
        {
            if (pCity.isCapitalCity() && _kingdom.hasKing() && _kingdom.king.isAlive()) return _kingdom.king;
            return pCity.leader != null && pCity.leader.isAlive() ? pCity.leader : null;
        }

        static long OwnerClan(City pCity)
        {
            Actor o = pCity != null ? Owner(pCity) : null;
            return o != null && o.hasClan() ? o.clan.id : -1;
        }

        static string MapKey()
        {
            var sb = new StringBuilder().Append(_kingdom.id).Append('|');
            foreach (City c in _kingdom.getCities())
                sb.Append(c.id).Append(':').Append(c.zones.Count).Append(':').Append(OwnerClan(c)).Append(c.isCapitalCity() ? "c" : "").Append(',');
            return sb.ToString();
        }

        // 封疆图的格子：每格是哪座城（国外是 null）、原本的颜色
        static Texture2D _texture;
        static City[] _cells;
        static Color[] _cellColors;
        static long[] _cellClans;
        static int _zx0, _zy0, _zw, _zh;
        static float _scale;
        static readonly Dictionary<long, GameObject> _marks = new Dictionary<long, GameObject>();
        static long _appliedHighlight = -2, _appliedCity = -2;

        // 抽象的封疆图：每个区块一格，国内按城的议席归属家族上色、城与城之间描边，国外陆地一种淡色，水面一种蓝色
        static void BuildMap()
        {
            _marks.Clear();
            _cells = null;
            _appliedHighlight = _appliedCity = -2;
            float y = Top + 23f, h = MapH;
            RectTransform slot = Sliced(_map, Slot, RightX, y, RightW, h);
            Rect(slot, 3f, 3f, RightW - 6f, h - 6f, Sea);

            var owner = new Dictionary<TileZone, City>();
            foreach (City c in _kingdom.getCities())
            {
                foreach (TileZone z in c.zones) owner[z] = c;
            }
            if (owner.Count == 0) return;
            int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue;
            foreach (TileZone z in owner.Keys)
            {
                x0 = Mathf.Min(x0, z.x); x1 = Mathf.Max(x1, z.x);
                y0 = Mathf.Min(y0, z.y); y1 = Mathf.Max(y1, z.y);
            }
            // 四周留一点国外的地方，按显示区域的长宽比补齐
            float aspect = (RightW - 10f) / (h - 10f);
            int zw = x1 - x0 + 5, zh = y1 - y0 + 5;
            if (zw < zh * aspect) zw = Mathf.CeilToInt(zh * aspect);
            else zh = Mathf.CeilToInt(zw / aspect);
            _zx0 = (x0 + x1) / 2 - zw / 2;
            _zy0 = (y0 + y1) / 2 - zh / 2;
            _zw = zw;
            _zh = zh;

            var colors = new Dictionary<City, Color>();
            var clans = new Dictionary<City, long>();
            foreach (City c in _kingdom.getCities())
            {
                Actor o = Owner(c);
                colors[c] = o != null && o.hasClan() ? ClanColor(o.clan) : Vacant;
                clans[c] = OwnerClan(c);
            }
            ZoneCalculator zones = World.world.zone_calculator;
            _cells = new City[zw * zh];
            _cellColors = new Color[zw * zh];
            _cellClans = new long[zw * zh];
            for (int zy = 0; zy < zh; zy++)
            for (int zx = 0; zx < zw; zx++)
            {
                int i = zy * zw + zx;
                TileZone z = zones.getZone(_zx0 + zx, _zy0 + zy);
                City city = null;
                if (z != null && owner.TryGetValue(z, out city))
                {
                    _cells[i] = city;
                    _cellColors[i] = colors[city];
                    _cellClans[i] = clans[city];
                }
                else
                {
                    _cellColors[i] = z == null || z.tiles_with_ground * 2 < z.tiles_with_liquid ? Sea : Land;
                    _cellClans[i] = -1;
                }
            }
            if (_texture != null) Object.Destroy(_texture);
            _texture = new Texture2D(zw * ZonePx, zh * ZonePx, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

            _scale = Mathf.Min((RightW - 6f) / _texture.width, (h - 6f) / _texture.height);
            float mw = _texture.width * _scale, mh = _texture.height * _scale;
            float ox = (RightW - mw) / 2f, oy = (h - mh) / 2f;
            Sprite sprite = Sprite.Create(_texture, new UnityEngine.Rect(0, 0, _texture.width, _texture.height), new Vector2(0.5f, 0.5f), 100f);
            RectTransform img = Picture(slot, sprite, ox, oy, mw, mh);
            Image image = img.GetComponent<Image>();
            image.preserveAspect = false;
            image.raycastTarget = true;
            img.gameObject.AddComponent<MapPointer>();

            // 城的标记：首都是王冠，其余是小方块；悬停、点击都落在下面的地图上
            foreach (City c in _kingdom.getCities())
            {
                WorldTile t = c.getTile();
                if (t == null || t.zone == null) continue;
                float cx = ox + ((t.zone.x - _zx0) * ZonePx + ZonePx / 2f) * _scale;
                float cy = oy + mh - ((t.zone.y - _zy0) * ZonePx + ZonePx / 2f) * _scale;
                RectTransform mark = Box(slot, cx - 6f, cy - 6f, 12f, 12f, "mark");
                if (c.isCapitalCity()) Icon(mark, IconPath, 1f, 1f, 10f);
                else Swatch(mark, 4f, 4f, 4f, Ink, 1f);
                _marks[c.id] = Frame(mark, -1f).gameObject;
            }
        }

        static float MapH => H - 12f - DetailH - 4f - (Top + 23f);

        // 亮着的家族照原色，其余领地压暗；下方显示的那座城的标记加框
        static void ApplyMap()
        {
            if (_cells == null) return;
            long hl = Highlight;
            City shown = ShownCity();
            long shownId = shown != null ? shown.id : -1;
            if (hl == _appliedHighlight && shownId == _appliedCity) return;
            if (hl != _appliedHighlight)
            {
                int tw = _zw * ZonePx;
                var pixels = new Color32[tw * _zh * ZonePx];
                for (int zy = 0; zy < _zh; zy++)
                for (int zx = 0; zx < _zw; zx++)
                {
                    int i = zy * _zw + zx;
                    City mine = _cells[i];
                    Color color = _cellColors[i];
                    if (mine != null && hl >= 0 && _cellClans[i] != hl) color = Color.Lerp(color, Edge, 0.6f);
                    Color border = Color.Lerp(color, Color.black, 0.5f);
                    bool left = mine != null && Cell(zx - 1, zy) != mine;
                    bool right = mine != null && Cell(zx + 1, zy) != mine;
                    bool down = mine != null && Cell(zx, zy - 1) != mine;
                    bool up = mine != null && Cell(zx, zy + 1) != mine;
                    for (int py = 0; py < ZonePx; py++)
                    for (int px = 0; px < ZonePx; px++)
                    {
                        bool edge = (px == 0 && left) || (px == ZonePx - 1 && right) || (py == 0 && down) || (py == ZonePx - 1 && up);
                        pixels[(zy * ZonePx + py) * tw + zx * ZonePx + px] = edge ? border : color;
                    }
                }
                _texture.SetPixels32(pixels);
                _texture.Apply();
            }
            foreach (KeyValuePair<long, GameObject> m in _marks)
            {
                if (m.Value != null) m.Value.SetActive(m.Key == shownId);
            }
            _appliedHighlight = hl;
            _appliedCity = shownId;
        }

        static City Cell(int pX, int pY) => pX < 0 || pY < 0 || pX >= _zw || pY >= _zh ? null : _cells[pY * _zw + pX];

        // 地图上的一点（从图的左上角算，y 向下）是哪座城
        internal static City CityAt(Vector2 pPoint)
        {
            if (_cells == null) return null;
            int zx = Mathf.FloorToInt(pPoint.x / _scale / ZonePx);
            int zy = Mathf.FloorToInt((_zh * ZonePx - pPoint.y / _scale) / ZonePx);
            return Cell(zx, zy);
        }

        internal static void Hover(City pCity)
        {
            long id = pCity != null ? pCity.id : -1;
            if (id == _hover) return;
            _hover = id;
            Refresh();
        }

        // 点领地：锁定这个家族、看这座城；再点同一个家族的领地取消锁定；点国外取消
        internal static void Click(City pCity)
        {
            long clan = OwnerClan(pCity);
            _clan = pCity == null || clan == _clan ? -1 : clan;
            if (pCity != null) _city = pCity.id;
            Refresh();
        }

        const float DetailH = 62f;

        static string DetailKey(City pCity)
        {
            if (pCity == null) return "";
            Actor o = Owner(pCity);
            var sb = new StringBuilder().Append(pCity.id).Append('|').Append(pCity.data.name).Append('|').Append(pCity.getPopulationPeople()).Append('|');
            if (o != null) sb.Append(o.data.id).Append(':').Append(o.getAge()).Append(':').Append(OwnerClan(pCity));
            long clan = OwnerClan(pCity);
            foreach (City c in _kingdom.getCities())
            {
                if (OwnerClan(c) == clan) sb.Append(',').Append(c.id).Append(':').Append(Parliament.CitySeats(c));
            }
            return sb.ToString();
        }

        // 下方两层：上面是这座城和它的城主，下面是城主的家族占了几座城、共多少席
        static void BuildDetail(City pCity)
        {
            if (pCity == null) return;
            float y = H - 12f - DetailH;
            RectTransform slot = Sliced(_detail, Slot, RightX, y, RightW, DetailH);
            Actor o = Owner(pCity);
            var parts = new List<string> { pCity.data.name };
            if (pCity.isCapitalCity()) parts.Add(LM.Get("vd_election_capital"));
            parts.Add(LM.Get("vd_election_people").Replace("$n$", pCity.getPopulationPeople().ToString()));
            parts.Add(LM.Get("vd_election_city_seats").Replace("$n$", Parliament.CitySeats(pCity).ToString()));
            float tx = o != null ? 40f : 8f, tw = RightW - tx - 8f;
            Text(slot, Fit(string.Join(" · ", parts), 6, tw), tx, 5f, tw, 10f, 6, Muted, TextAnchor.MiddleLeft);
            if (o == null) return;
            Avatar(slot, o, 7f, 5f, 26f);
            string role = o == _kingdom.king ? Government.RulerTitle(_kingdom) : LM.Get("vd_election_leader");
            Text(slot, Fit(role + " " + o.getName() + " · " + Age(o), 8, tw), tx, 16f, tw, 13f, 8, Color.white, TextAnchor.MiddleLeft);

            Rect(slot, 6f, 36f, RightW - 12f, 1f, Line);
            if (!o.hasClan()) return;
            int cities = 0, seats = 0;
            foreach (City c in _kingdom.getCities())
            {
                if (OwnerClan(c) != o.clan.id) continue;
                cities++;
                seats += Parliament.CitySeats(c);
            }
            ClanBanner(slot, o.clan, 10f, 40f, 12f, 19f);
            string stats = LM.Get("vd_election_clan_cities").Replace("$n$", cities.ToString()) + " · " + LM.Get("vd_election_city_seats").Replace("$n$", seats.ToString());
            float sw = Measure(stats, 6) + 4f;
            Text(slot, Fit(ClanName(o.clan), 7, RightW - 34f - sw - 8f), 28f, 40f, RightW - 34f - sw, 18f, 7, ClanText(o.clan), TextAnchor.MiddleLeft);
            Text(slot, stats, RightW - 8f - sw, 40f, sw, 18f, 6, Muted, TextAnchor.MiddleRight);
        }
    }

    // 封疆图上的鼠标：移动时找鼠标下的城，点击时锁定家族
    public class MapPointer : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler, UnityEngine.EventSystems.IPointerClickHandler
    {
        bool _inside;
        Camera _camera;

        City At(Vector2 pScreen)
        {
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, pScreen, _camera, out Vector2 local)) return null;
            return ElectionWindow.CityAt(new Vector2(local.x, -local.y));
        }

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e)
        {
            _inside = true;
            _camera = e.enterEventCamera;
        }

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e)
        {
            _inside = false;
            ElectionWindow.Hover(null);
        }

        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e) => ElectionWindow.Click(At(e.position));

        void Update()
        {
            if (_inside) ElectionWindow.Hover(At(Input.mousePosition));
        }
    }
}
