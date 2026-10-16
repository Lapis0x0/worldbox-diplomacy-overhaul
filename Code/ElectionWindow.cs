using System.Collections.Generic;
using System.Text;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using UnityPools;
using static DiplomacyOverhaul.UiKit;
using Object = UnityEngine.Object;

namespace DiplomacyOverhaul
{
    // 选举与任命：从王国窗口左侧页签栏打开。左边共和国是执政选举、君主国是王位继承，右边是各城的封疆图。
    // 窗口分三块，每块只在自己用到的数据变了时重建
    public static class ElectionWindow
    {
        const string WindowId = "vd_election";
        public const string IconPath = "ui/Icons/iconCrown";
        const float W = ParliamentWindow.W, H = ParliamentWindow.H;
        const float LeftX = 18f, LeftW = 266f, RightX = 298f, RightW = 244f, Top = 26f;
        // 封疆图里每个区块画几个像素
        const int ZonePx = 4;

        static readonly Color Panel = Hex("#2c2f29");
        static readonly Color Sea = Hex("#2f5f9e");
        static readonly Color Land = Hex("#6f7662");
        static readonly Color Vacant = Hex("#bfbfb8");

        static KingdomTabWindow _shell;
        static Kingdom _kingdom;
        static RectTransform _left, _map, _detail;
        static string _leftKey, _mapKey, _detailKey;
        static long _city = -1, _pick = -1, _clan = -1;
        static Texture2D _texture;

        public static void Init()
        {
            _shell = new KingdomTabWindow(WindowId, IconPath, 2, W, H, Open, Tick);
            _shell.Init();
            _left = _map = _detail = null;
        }

        public static void Open(Kingdom pKingdom)
        {
            if (pKingdom == null || !pKingdom.isAlive()) return;
            if (_kingdom != pKingdom) _city = _pick = _clan = -1;
            _kingdom = pKingdom;
            KingdomTabWindow.Current = pKingdom;
            if (_shell.Create())
            {
                _left = Box(_shell.root, 0f, 0f, W, H, "vd_section_left");
                _map = Box(_shell.root, 0f, 0f, W, H, "vd_section_map");
                _detail = Box(_shell.root, 0f, 0f, W, H, "vd_section_detail");
                Header(_shell.root, RightX, Top, RightW, LM.Get("vd_election_map"), "ui/Icons/iconLeaders");
            }
            _leftKey = _mapKey = _detailKey = null;
            _shell.Show(pKingdom);
            Refresh();
        }

        static void Tick()
        {
            if (!_shell.IsOpen) return;
            if (_kingdom == null || !_kingdom.isAlive())
            {
                _shell.window.clickHide();
                return;
            }
            _shell.Fit();
            Refresh();
        }

        static void Refresh()
        {
            if (_shell.root == null || _kingdom == null) return;
            if (Government.IsRepublic(_kingdom))
            {
                Election e = Regime.Vote(_kingdom);
                Rebuild(_left, ref _leftKey, RepublicKey(e), () => BuildRepublic(e));
            }
            else
            {
                List<Actor> heirs = Heirs(_kingdom);
                Rebuild(_left, ref _leftKey, MonarchyKey(heirs), () => BuildMonarchy(heirs));
            }
            City city = SelectedCity();
            Rebuild(_map, ref _mapKey, MapKey(city), () => BuildMap(city));
            Rebuild(_detail, ref _detailKey, DetailKey(city), () => BuildDetail(city));
        }

        static void Rebuild(RectTransform pRoot, ref string pKey, string pNew, System.Action pBuild)
        {
            if (pKey == pNew) return;
            pKey = pNew;
            for (int i = pRoot.childCount - 1; i >= 0; i--)
            {
                GameObject child = pRoot.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
            pBuild();
        }

        static string ClanName(Clan pClan) => pClan != null ? pClan.name : "";

        static string Age(Actor pActor) => LM.Get("vd_election_age").Replace("$age$", pActor.getAge().ToString());

        // ---------- 共和国：执政选举 ----------

        static string RepublicKey(Election e)
        {
            var sb = new StringBuilder("r").Append(_kingdom.id).Append('|');
            Actor king = _kingdom.king;
            sb.Append(king != null && king.isAlive() ? king.data.id : -1).Append('|').Append(Regime.YearsToElection(_kingdom)).Append('|');
            sb.Append(_pick).Append('|').Append(_clan).Append('|');
            if (e != null)
            {
                foreach (Candidate c in e.candidates) sb.Append(c.actor.data.id).Append(':').Append(c.votes).Append(':').Append(c.actor.getName()).Append(',');
                foreach (KeyValuePair<ParliamentClan, Candidate> v in e.votes)
                    sb.Append(v.Key.clan.id).Append('>').Append(v.Value.actor.data.id).Append(':').Append(v.Key.seats).Append(',');
            }
            return sb.ToString();
        }

        static void BuildRepublic(Election e)
        {
            Header(_left, LeftX, Top, LeftW, LM.Get("vd_election_republic"), "ui/Icons/iconKings");

            // 执政和离改选还有几年
            float y = Top + 23f;
            RectTransform status = Sliced(_left, Slot, LeftX, y, LeftW, 18f);
            Actor king = _kingdom.king;
            string exec = king != null && king.isAlive()
                ? LM.Get("vd_parliament_ruling") + " <color=" + Tag(ClanText(king.clan)) + ">" + king.getName() + "</color>"
                : LM.Get("vd_election_vacant");
            Text(status, exec, 7f, 0f, LeftW - 14f, 18f, 7, Ink, TextAnchor.MiddleLeft);
            if (king != null && king.isAlive())
                Text(status, LM.Get("vd_election_in").Replace("$years$", Regime.YearsToElection(_kingdom).ToString()), 7f, 0f, LeftW - 14f, 18f, 6, Muted, TextAnchor.MiddleRight);
            y += 21f;
            if (e == null) return;

            // 玩家点过的候选人才加框，「指定执政」指定的就是他
            Candidate picked = e.candidates.Find(c => c.actor.data.id == _pick);

            // 候选人
            const float cardH = 64f;
            float cw = (LeftW - 8f) / 3f;
            for (int i = 0; i < e.candidates.Count; i++)
            {
                Candidate c = e.candidates[i];
                long id = c.actor.data.id;
                RectTransform card = Button(_left, LeftX + i * (cw + 4f), y, cw, cardH, Slot, () => { _pick = id; Refresh(); });
                Avatar(card, c.actor, (cw - 26f) / 2f, 5f, 26f);
                Text(card, Fit(c.actor.getName(), 7, cw - 6f), 0f, 33f, cw, 10f, 7, Color.white, TextAnchor.MiddleCenter);
                Text(card, Fit(ClanName(c.clan.clan), 6, cw - 6f), 0f, 43f, cw, 8f, 6, ClanText(c.clan.clan), TextAnchor.MiddleCenter);
                Text(card, c.votes + " <size=6><color=" + Tag(Muted) + ">" + LM.Get("vd_election_seats") + "</color></size>", 0f, 51f, cw, 12f, 10, Gold, TextAnchor.MiddleCenter);
                if (c == picked) Frame(card, -1f);
            }
            y += cardH + 4f;

            // 议席半圆：按候选人分块，从左到右和卡片一样，块里按氏族的议席从多到少
            const float slotH = 88f, aw = 152f, ah = 78f;
            RectTransform slot = Sliced(_left, Slot, LeftX, y, LeftW, slotH);
            RectTransform area = Box(slot, 8f, (slotH - ah) / 2f, aw, ah);
            var order = new List<ParliamentClan>();
            foreach (Candidate c in e.candidates)
            {
                var backers = new List<ParliamentClan>();
                foreach (KeyValuePair<ParliamentClan, Candidate> v in e.votes)
                {
                    if (v.Value == c) backers.Add(v.Key);
                }
                backers.Sort((a, b) => b.seats.CompareTo(a.seats));
                order.AddRange(backers);
            }
            List<Vector3> seats = SeatLayout(Parliament.TotalSeats(e.clans), aw, ah, out float size);
            int s = 0;
            foreach (ParliamentClan pc in order)
            {
                Color color = ClanColor(e.votes[pc].clan.clan);
                float alpha = _clan < 0 || _clan == pc.clan.id ? 1f : 0.25f;
                for (int i = 0; i < pc.seats && s < seats.Count; i++, s++)
                    Swatch(area, seats[s].x - size / 2f, seats[s].y - size / 2f, size, color, alpha);
            }
            // 半圆右边一栏写预计当选的人：头像、名字（长了折行）、得票
            float rx = aw + 14f, rw = LeftW - rx - 6f;
            string winnerName = e.winner.actor.getName();
            float nameH = Measure(winnerName, 8) <= rw ? 10f : 20f;
            float by = (slotH - (9f + 2f + 26f + 4f + nameH + 3f + 12f)) / 2f;
            Text(slot, LM.Get("vd_election_forecast"), rx, by, rw, 9f, 6, Muted, TextAnchor.MiddleCenter);
            Avatar(slot, e.winner.actor, rx + (rw - 26f) / 2f, by + 11f, 26f);
            RectTransform name = Text(slot, winnerName, rx, by + 41f, rw, nameH, 8, ClanText(e.winner.clan.clan), TextAnchor.UpperCenter);
            name.GetComponent<UnityEngine.UI.Text>().horizontalOverflow = HorizontalWrapMode.Wrap;
            string tally = e.winner.votes + " <color=" + Tag(Muted) + ">/ " + Parliament.TotalSeats(e.clans) + " " + LM.Get("vd_election_seats") + "</color>";
            Text(slot, tally, rx, by + 44f + nameH, rw, 12f, 8, Gold, TextAnchor.MiddleCenter);
            y += slotH + 3f;

            // 表决明细：每个氏族投给谁
            const float rowH = 11f;
            float buttonsY = H - 30f;
            RectTransform list = Scroll(_left, "scroll_votes", LeftX, y, LeftW, buttonsY - 3f - y, order.Count * rowH);
            float ry = 0f;
            foreach (ParliamentClan pc in order)
            {
                long id = pc.clan.id;
                Candidate c = e.votes[pc];
                RectTransform row = Button(list, 0f, ry, LeftW - 6f, rowH, null, () => { _clan = _clan == id ? -1 : id; Refresh(); });
                if (_clan == id) Rect(row, 0f, 0f, LeftW - 6f, rowH, new Color(1f, 1f, 1f, 0.08f));
                Swatch(row, 6f, 3f, 5f, ClanColor(pc.clan), 1f);
                Text(row, Fit(ClanName(pc.clan), 6, 120f), 16f, 0f, 124f, rowH, 6, Ink, TextAnchor.MiddleLeft);
                Text(row, pc.seats.ToString(), 140f, 0f, 28f, rowH, 6, Gold, TextAnchor.MiddleRight);
                Avatar(row, c.actor, 176f, 1f, 9f);
                Text(row, Fit(c.actor.getName(), 6, 70f), 189f, 0f, 72f, rowH, 6, ClanText(c.clan.clan), TextAnchor.MiddleLeft);
                ry += rowH;
            }

            float bw = (LeftW - 4f) / 2f;
            RectTransform now = Button(_left, LeftX, buttonsY, bw, 18f, "ui/special/special_buttonRed", () =>
            {
                Regime.Reelect(_kingdom);
                _pick = -1;
                Refresh();
            });
            Text(now, LM.Get("vd_election_now"), 0f, 0f, bw, 18f, 7, Color.white, TextAnchor.MiddleCenter);
            Actor appointee = picked?.actor;
            RectTransform appoint = Button(_left, LeftX + bw + 4f, buttonsY, bw, 18f, "ui/special/special_buttonGray", () =>
            {
                if (appointee == null) return;
                Regime.Appoint(_kingdom, appointee);
                _pick = -1;
                Refresh();
            });
            Text(appoint, LM.Get("vd_election_appoint"), 0f, 0f, bw, 18f, 7, Color.white, TextAnchor.MiddleCenter);
        }

        // ---------- 君主国：王位继承 ----------

        // 原版挑继承人的同一套：王室里能继位的人，按年龄和文化的继承特质排
        static List<Actor> Heirs(Kingdom pKingdom)
        {
            var result = new List<Actor>();
            if (!pKingdom.data.royal_clan_id.hasValue()) return result;
            Clan royal = World.world.clans.get(pKingdom.data.royal_clan_id);
            if (royal == null) return result;
            using (var pool = new ListPool<Actor>())
            {
                foreach (Actor u in royal.units)
                {
                    if (royal.fitToRule(u, pKingdom)) pool.Add(u);
                }
                if (pKingdom.hasCulture()) ListSorters.sortUnitsSortedByAgeAndTraits(pool, pKingdom.culture);
                else pool.Sort(ListSorters.sortUnitByAgeOldFirst);
                foreach (Actor u in pool) result.Add(u);
            }
            return result;
        }

        static string MonarchyKey(List<Actor> pHeirs)
        {
            var sb = new StringBuilder("m").Append(_kingdom.id).Append('|');
            Actor king = _kingdom.king;
            if (king != null && king.isAlive())
                sb.Append(king.data.id).Append(':').Append(king.getAge()).Append(':').Append(king.level).Append(':').Append(Date.getYearsSince(_kingdom.data.timestamp_king_rule)).Append('|');
            foreach (Actor a in pHeirs) sb.Append(a.data.id).Append(':').Append(a.getAge()).Append(',');
            return sb.ToString();
        }

        static void BuildMonarchy(List<Actor> pHeirs)
        {
            string title = LM.Get("vd_election_monarch");
            Header(_left, LeftX, Top, LeftW, title, "ui/Icons/iconKings");
            float y = Top + 23f;
            Actor king = _kingdom.king;
            const float cardH = 62f;
            RectTransform card = Sliced(_left, Slot, LeftX, y, LeftW, cardH);
            if (king != null && king.isAlive())
            {
                RectTransform avatar = Avatar(card, king, 8f, 7f, 48f);
                Frame(avatar, -1.5f);
                Text(card, king.getName(), 64f, 6f, 160f, 14f, 10, Color.white, TextAnchor.MiddleLeft);
                Text(card, LM.Get("vd_parliament_royal") + " · " + ClanName(king.clan), 64f, 22f, 160f, 10f, 6, ClanText(king.clan), TextAnchor.MiddleLeft);
                string reign = Age(king) + " · " + LM.Get("vd_election_reign").Replace("$years$", Date.getYearsSince(_kingdom.data.timestamp_king_rule).ToString());
                Text(card, reign, 64f, 33f, 160f, 10f, 6, Muted, TextAnchor.MiddleLeft);
                string trait = king.s_personality != null ? king.s_personality.getTranslatedName() + " · " : "";
                Text(card, trait + LM.Get("vd_election_level").Replace("$level$", king.level.ToString()), 64f, 44f, 160f, 10f, 6, Muted, TextAnchor.MiddleLeft);
                ClanBanner(card, king.clan, LeftW - 34f, 12f, 24f, 38f);
            }
            y += cardH + 4f;

            Header(_left, LeftX, y, LeftW, LM.Get("vd_election_succession"), "ui/Icons/iconFamily");
            y += 23f;
            if (pHeirs.Count == 0)
            {
                Text(_left, LM.Get("vd_election_no_heir"), LeftX + 6f, y, LeftW - 12f, 14f, 7, Muted, TextAnchor.MiddleLeft);
                return;
            }
            const float rowH = 22f;
            RectTransform list = Scroll(_left, "scroll_heirs", LeftX, y, LeftW, H - 12f - y, pHeirs.Count * (rowH + 2f));
            float ry = 0f;
            for (int i = 0; i < pHeirs.Count; i++)
            {
                Actor a = pHeirs[i];
                RectTransform row = Sliced(list, Slot, 0f, ry, LeftW - 6f, rowH);
                string rank = i == 0 ? LM.Get("vd_election_heir") : LM.Get("vd_election_rank").Replace("$n$", (i + 1).ToString());
                Text(row, rank, 7f, 0f, 50f, rowH, 6, i == 0 ? Gold : Muted, TextAnchor.MiddleLeft);
                Avatar(row, a, 58f, 3f, 16f);
                Text(row, Fit(a.getName(), 7, 110f), 80f, 0f, 112f, rowH, 7, Color.white, TextAnchor.MiddleLeft);
                string sex = LM.Get(a.isSexMale() ? "vd_election_male" : "vd_election_female");
                Text(row, sex + " · " + Age(a), 0f, 0f, LeftW - 14f, rowH, 6, Muted, TextAnchor.MiddleRight);
                if (i == 0) Frame(row, -1f);
                ry += rowH + 2f;
            }
        }

        // ---------- 封疆 ----------

        static City SelectedCity()
        {
            City capital = null;
            foreach (City c in _kingdom.getCities())
            {
                if (c.id == _city) return c;
                if (c.isCapitalCity()) capital = c;
            }
            if (capital == null)
            {
                foreach (City c in _kingdom.getCities()) { capital = c; break; }
            }
            _city = capital != null ? capital.id : -1;
            return capital;
        }

        // 城的议席归谁：首都归国王或执政，其余归城主
        static Actor Owner(City pCity)
        {
            if (pCity.isCapitalCity() && _kingdom.hasKing() && _kingdom.king.isAlive()) return _kingdom.king;
            return pCity.leader != null && pCity.leader.isAlive() ? pCity.leader : null;
        }

        static string MapKey(City pSelected)
        {
            var sb = new StringBuilder().Append(_kingdom.id).Append('|').Append(pSelected?.id ?? -1).Append('|');
            foreach (City c in _kingdom.getCities())
            {
                Actor o = Owner(c);
                sb.Append(c.id).Append(':').Append(c.zones.Count).Append(':').Append(o != null && o.hasClan() ? o.clan.id : -1).Append(c.isCapitalCity() ? "c" : "").Append(',');
            }
            return sb.ToString();
        }

        // 抽象的封疆图：每个区块一格，国内按城的归属家族上色、城与城之间描边，国外陆地一种淡色，水面一种蓝色
        static void BuildMap(City pSelected)
        {
            float y = Top + 23f, h = H - 12f - 48f - y;
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
            x0 = (x0 + x1) / 2 - zw / 2;
            y0 = (y0 + y1) / 2 - zh / 2;

            var colors = new Dictionary<City, Color>();
            foreach (City c in _kingdom.getCities())
            {
                Actor o = Owner(c);
                colors[c] = o != null && o.hasClan() ? ClanColor(o.clan) : Vacant;
            }
            ZoneCalculator zones = World.world.zone_calculator;
            if (_texture != null) Object.Destroy(_texture);
            _texture = new Texture2D(zw * ZonePx, zh * ZonePx, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[zw * ZonePx * zh * ZonePx];
            for (int zy = 0; zy < zh; zy++)
            for (int zx = 0; zx < zw; zx++)
            {
                TileZone z = zones.getZone(x0 + zx, y0 + zy);
                City mine = null;
                bool own = z != null && owner.TryGetValue(z, out mine);
                Color baseColor = own ? colors[mine] : z == null || z.tiles_with_ground * 2 < z.tiles_with_liquid ? Sea : Land;
                Color border = Color.Lerp(baseColor, Color.black, 0.5f);
                bool left = own && !Same(zones, owner, x0 + zx - 1, y0 + zy, mine);
                bool right = own && !Same(zones, owner, x0 + zx + 1, y0 + zy, mine);
                bool down = own && !Same(zones, owner, x0 + zx, y0 + zy - 1, mine);
                bool up = own && !Same(zones, owner, x0 + zx, y0 + zy + 1, mine);
                for (int py = 0; py < ZonePx; py++)
                for (int px = 0; px < ZonePx; px++)
                {
                    bool edge = (px == 0 && left) || (px == ZonePx - 1 && right) || (py == 0 && down) || (py == ZonePx - 1 && up);
                    pixels[(zy * ZonePx + py) * zw * ZonePx + zx * ZonePx + px] = edge ? border : baseColor;
                }
            }
            _texture.SetPixels32(pixels);
            _texture.Apply();

            float scale = Mathf.Min((RightW - 6f) / _texture.width, (h - 6f) / _texture.height);
            float mw = _texture.width * scale, mh = _texture.height * scale;
            float ox = (RightW - mw) / 2f, oy = (h - mh) / 2f;
            Sprite sprite = Sprite.Create(_texture, new UnityEngine.Rect(0, 0, _texture.width, _texture.height), new Vector2(0.5f, 0.5f), 100f);
            RectTransform img = Picture(slot, sprite, ox, oy, mw, mh);
            img.GetComponent<Image>().preserveAspect = false;

            // 城的标记：首都是王冠，其余是小方块，点一下看这座城
            foreach (City c in _kingdom.getCities())
            {
                WorldTile t = c.getTile();
                if (t == null || t.zone == null) continue;
                float cx = ox + ((t.zone.x - x0) * ZonePx + ZonePx / 2f) * scale;
                float cy = oy + mh - ((t.zone.y - y0) * ZonePx + ZonePx / 2f) * scale;
                long id = c.id;
                RectTransform mark = Button(slot, cx - 6f, cy - 6f, 12f, 12f, null, () => { _city = id; Refresh(); });
                if (c.isCapitalCity()) Icon(mark, IconPath, 1f, 1f, 10f);
                else Swatch(mark, 4f, 4f, 4f, Ink, 1f);
                if (c == pSelected) Frame(mark, -1f);
            }
        }

        static bool Same(ZoneCalculator pZones, Dictionary<TileZone, City> pOwner, int pX, int pY, City pCity)
        {
            TileZone z = pZones.getZone(pX, pY);
            return z != null && pOwner.TryGetValue(z, out City c) && c == pCity;
        }

        static string DetailKey(City pCity)
        {
            if (pCity == null) return "";
            Actor o = Owner(pCity);
            return pCity.id + "|" + pCity.data.name + "|" + pCity.getPopulationPeople() + "|" + (o != null ? o.data.id + ":" + o.getAge() + ":" + (o.hasClan() ? o.clan.id : -1) : "-");
        }

        static void BuildDetail(City pCity)
        {
            if (pCity == null) return;
            float y = H - 12f - 46f;
            RectTransform slot = Sliced(_detail, Slot, RightX, y, RightW, 46f);
            Actor o = Owner(pCity);
            var parts = new List<string> { pCity.data.name };
            if (pCity.isCapitalCity()) parts.Add(LM.Get("vd_election_capital"));
            parts.Add(LM.Get("vd_election_people").Replace("$n$", pCity.getPopulationPeople().ToString()));
            parts.Add(LM.Get("vd_election_city_seats").Replace("$n$", Parliament.CitySeats(pCity).ToString()));
            float tx = o != null ? 44f : 8f;
            Text(slot, string.Join(" · ", parts), tx, 5f, RightW - tx - 34f, 10f, 6, Muted, TextAnchor.MiddleLeft);
            if (o == null) return;
            Avatar(slot, o, 7f, 6f, 32f);
            string role = o == _kingdom.king ? (Government.IsRepublic(_kingdom) ? LM.Get("vd_parliament_ruling") : LM.Get("vd_election_monarch")) : LM.Get("vd_election_leader");
            Text(slot, Fit(role + " " + o.getName() + " · " + Age(o), 8, RightW - tx - 34f), tx, 16f, RightW - tx - 34f, 13f, 8, Color.white, TextAnchor.MiddleLeft);
            Text(slot, Fit(ClanName(o.clan), 6, RightW - tx - 34f), tx, 30f, RightW - tx - 34f, 10f, 6, ClanText(o.clan), TextAnchor.MiddleLeft);
            ClanBanner(slot, o.clan, RightW - 28f, 7f, 20f, 32f);
        }
    }
}
