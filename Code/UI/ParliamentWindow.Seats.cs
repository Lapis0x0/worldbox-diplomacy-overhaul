using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using static DiplomacyOverhaul.UiKit;

namespace DiplomacyOverhaul
{
    // 议会窗口左边：议席、势力列表、坐标图
    public static partial class ParliamentWindow
    {
        const float ListW = 176f, RowH = 15f, ListY = Top + 24f + 146f;

        // 势力列表：集团一行表头（集团名、集团的立场、合计议席），成员缩进排在下面；没有集团的势力单独一行
        static void ClanList(List<ParliamentClan> pClans)
        {
            List<Unit> units = ListUnits(pClans);
            int rows = 0;
            foreach (Unit u in units) rows += u.bloc != null ? u.members.Count + 1 : 1;
            RectTransform content = Scroll("scroll_clans", LeftX, ListY, ListW, ListRows * (RowH + 2f), rows * (RowH + 2f));
            float ry = 0f;
            foreach (Unit u in units)
            {
                if (u.bloc == null)
                {
                    ClanRow(content, u.members[0], 0f, ry);
                    ry += RowH + 2f;
                    continue;
                }
                Bloc b = u.bloc;
                long key = b.Key;
                Color color = FactionColor(b.leader.faction);
                RectTransform row = Button(content, 0f, ry, ListW - 6f, RowH, Slot, () => Select(key));
                Bar(row, 6f, (RowH - 9f) / 2f, color);
                Text(row, UiKit.Fit(Blocs.Name(b), 7, 90f), 16f, 0f, 90f, RowH, 7, color, TextAnchor.MiddleLeft);
                Text(row, LM.Get(FactionKey(b.faction)), 108f, 0f, 36f, RowH, 6, Muted, TextAnchor.MiddleLeft);
                Text(row, b.seats.ToString(), ListW - 40f, 0f, 28f, RowH, 7, Gold, TextAnchor.MiddleRight);
                _section.frames[key] = Frame(row, -1f).gameObject;
                ry += RowH + 2f;
                foreach (ParliamentClan m in b.members)
                {
                    ClanRow(content, m, MemberIndent, ry);
                    ry += RowH + 2f;
                }
            }
        }

        const float MemberIndent = 8f;

        static void ClanRow(RectTransform pContent, ParliamentClan pc, float pIndent, float pY)
        {
            long id = pc.Key;
            RectTransform row = Button(pContent, pIndent, pY, ListW - 6f - pIndent, RowH, Slot, () => Select(id));
            Swatch(row, 6f, (RowH - 6f) / 2f, 6f, FactionColor(pc.faction), 1f);
            // 名字和王室、宗室的小字一起放在派系那一栏之前，放不下时截短名字
            string tag = Tag(pc);
            float nameW = 90f - pIndent;
            float tagW = tag != null ? Measure(tag, 6) + 3f : 0f;
            string name = UiKit.Fit(pc.clan.name, 7, nameW - tagW);
            Text(row, name, 16f, 0f, nameW, RowH, 7, Ink, TextAnchor.MiddleLeft);
            if (tag != null) Text(row, tag, 16f + Measure(name, 7) + 3f, 0f, 30f, RowH, 6, Gold, TextAnchor.MiddleLeft);
            Text(row, LM.Get(FactionKey(pc.faction)), 108f - pIndent, 0f, 36f, RowH, 6, Muted, TextAnchor.MiddleLeft);
            Text(row, pc.seats.ToString(), ListW - 40f - pIndent, 0f, 28f, RowH, 7, Gold, TextAnchor.MiddleRight);
            _section.frames[id] = Frame(row, -1f).gameObject;
        }

        // 集团的标识：一根带深色描边的竖条
        static void Bar(RectTransform pParent, float pX, float pY, Color pColor)
        {
            RectTransform bar = UiKit.Rect(pParent, pX, pY, 3f, 9f, pColor);
            var outline = bar.gameObject.AddComponent<Outline>();
            outline.effectColor = Edge;
            outline.effectDistance = new Vector2(0.5f, -0.5f);
        }

        // 半圆、列表、议案名单的一格：一个集团，或者一股不在集团里的势力
        class Unit
        {
            public Bloc bloc;
            public List<ParliamentClan> members;
            public Faction faction;
            public int seats;
        }

        // 半圆的顺序：按派系从激进到保守、同派系按议席排；集团按它的立场排，成员连在一起
        // pBlocs 为 false 时不分集团，每股势力各算一格（半圆关掉「显示集团」时用）
        static List<Unit> Units(List<ParliamentClan> pClans, bool pBlocs = true)
        {
            var units = new List<Unit>();
            if (pBlocs)
                foreach (Bloc b in _blocs) units.Add(new Unit { bloc = b, members = b.members, faction = b.faction, seats = b.seats });
            foreach (ParliamentClan pc in pClans)
                if (!pBlocs || !_blocOf.ContainsKey(pc.Key)) units.Add(new Unit { members = new List<ParliamentClan> { pc }, faction = pc.faction, seats = pc.seats });
            units.Sort((a, b) =>
            {
                int fa = Parliament.FactionOrder(a.faction), fb = Parliament.FactionOrder(b.faction);
                return fa != fb ? fa.CompareTo(fb) : b.seats.CompareTo(a.seats);
            });
            return units;
        }

        // 列表的顺序：王室（或它所在的集团）在最上面，宗室紧跟着
        static List<Unit> ListUnits(List<ParliamentClan> pClans)
        {
            List<Unit> list = Units(pClans);
            int at = 0;
            foreach (System.Predicate<ParliamentClan> match in new System.Predicate<ParliamentClan>[] { c => c.ruling, c => c.cadet })
            {
                int i = list.FindIndex(u => u.members.Exists(match));
                if (i < at) continue;
                Unit u = list[i];
                list.RemoveAt(i);
                list.Insert(at++, u);
            }
            return list;
        }

        // 列表里名字后面的小字：王室（共和国是执政）、宗室
        static string Tag(ParliamentClan pClan)
        {
            if (pClan.ruling) return LM.Get(Government.IsRepublic(_kingdom) ? "vd_parliament_ruling" : "vd_parliament_royal");
            return pClan.cadet ? LM.Get("vd_parliament_cadet") : null;
        }

        // 共和国里朝中的老家族叫元老派，地方新贵叫自治派
        internal static string FactionKey(Faction pFaction)
        {
            string key = "vd_faction_" + pFaction.ToString().ToLowerInvariant();
            bool republic = _kingdom != null && Government.IsRepublic(_kingdom);
            if (republic && (pFaction == Faction.Royalist || pFaction == Faction.Republican)) key += "_republic";
            return key;
        }

        // 外环上写集团名的那一圈要留的地方
        const float RingBand = 11f;

        // 议席半圆：按派系从激进（左）到保守（右）排开，座位按派系上色。集团的席位连在一起，
        // 后面铺一块魁首派系色的扇环，集团名沿外环逐字排开。中间写最大集团，没有集团时写最大派系
        static void Hemicycle(List<ParliamentClan> pClans, float pX, float pY)
        {
            const float aw = 262f, ah = 132f;
            RectTransform area = Box(_parent, pX + 2f, pY, aw, ah);
            bool blocs = _showBlocs && _blocs.Count > 0;
            List<Unit> units = Units(pClans, blocs);
            // 每个集团一块，相邻的散户合成一块，各块分开排：集团的边界在每一排都落在两个座位中间
            var segments = new List<int>();
            var segmentOf = new int[units.Count];
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].bloc != null || i == 0 || units[i - 1].bloc != null) segments.Add(0);
                segmentOf[i] = segments.Count - 1;
                segments[segments.Count - 1] += units[i].seats;
            }
            List<Vector3> seats = SeatLayout(segments.ToArray(), aw, ah, blocs ? RingBand : 0f, out float size, out Ring ring, out float[] bounds);
            var ranges = new List<(Unit unit, int first, int end)>();
            int s = 0;
            foreach (Unit u in units)
            {
                int first = s;
                s = Mathf.Min(seats.Count, s + u.seats);
                ranges.Add((u, first, s));
            }

            float rIn = ring.rIn - size / 2f - 2f, rOut = ring.rOut + size / 2f + 2f;
            var arcs = new List<(Bloc bloc, float t1, float t2)>();
            for (int i = 0; i < ranges.Count; i++)
            {
                var (u, first, end) = ranges[i];
                if (u.bloc == null || end <= first) continue;
                // 半圆两端的座位压在端点上，扇环在那一头竖直往下多铺一点，和内外环留的边一样宽
                int seg = segmentOf[i];
                float t1 = bounds[seg], t2 = bounds[seg + 1];
                float pad1 = seg == 0 ? size / 2f + 2f : 0f, pad2 = seg == segments.Count - 1 ? size / 2f + 2f : 0f;
                BlocShape shape = Shape(area, aw, ah, FactionColor(u.bloc.leader.faction));
                shape.Sector(ring.cx, ring.cy, rIn, rOut, t1, t2, pad1, pad2);
                DimShape(u.bloc, shape, BlocAlpha, BlocDim);
                BlocShape border = Shape(area, aw, ah, FactionColor(u.bloc.leader.faction), BorderAlpha);
                border.stroke = BorderWidth;
                border.Sector(ring.cx, ring.cy, rIn, rOut, t1, t2, pad1, pad2);
                DimShape(u.bloc, border, BorderAlpha, BorderDim);
                arcs.Add((u.bloc, t1, t2));
            }

            var owners = new long[seats.Count];
            s = 0;
            foreach (var (u, _, _) in ranges)
                foreach (ParliamentClan pc in u.members)
                {
                    Color color = FactionColor(pc.faction);
                    for (int i = 0; i < pc.seats && s < seats.Count; i++, s++)
                    {
                        Dimmable(pc, Swatch(area, seats[s].x - size / 2f, seats[s].y - size / 2f, size, color, 1f), 0.25f);
                        owners[s] = pc.Key;
                    }
                }
            PointClicks(area, seats, owners, size);
            RingNames(area, arcs, ring, rOut + 5f);

            string label, text;
            Color top;
            Bloc largest = null;
            if (blocs)
                foreach (Bloc b in _blocs)
                    if (largest == null || b.seats > largest.seats) largest = b;
            if (largest != null)
            {
                label = LM.Get("vd_parliament_largest_bloc");
                text = largest.city + " " + largest.seats;
                top = FactionColor(largest.leader.faction);
            }
            else
            {
                var bySeats = new Dictionary<Faction, int>();
                foreach (ParliamentClan pc in pClans) bySeats[pc.faction] = (bySeats.TryGetValue(pc.faction, out int n) ? n : 0) + pc.seats;
                Faction f = Faction.Royalist;
                int topSeats = -1;
                foreach (KeyValuePair<Faction, int> pair in bySeats)
                {
                    if (pair.Value > topSeats) { f = pair.Key; topSeats = pair.Value; }
                }
                label = LM.Get("vd_parliament_largest");
                text = LM.Get(FactionKey(f)) + " " + topSeats;
                top = FactionColor(f);
            }
            Text(area, label, 0f, ah - 34f, aw, 10f, 6, Muted, TextAnchor.MiddleCenter);
            Text(area, text, 0f, ah - 24f, aw, 18f, 14, top, TextAnchor.MiddleCenter);
        }

        // 议会半圆右上角「显示集团」：原版图层按钮上那颗开关圆点加几个字，点了切换半圆里画不画集团
        static void BlocToggle(float pX, float pY)
        {
            RectTransform button = Button(_parent, pX, pY, ToggleW, 10f, null, () =>
            {
                _showBlocs = !_showBlocs;
                Refresh();
            });
            ToggleIcon dot = PowerButton.get("clan_layer")?.transform.Find("ToggleIcon")?.GetComponent<ToggleIcon>();
            Sprite sprite = dot == null ? null : _showBlocs ? dot.spriteON : dot.spriteOFF;
            // 圆点和字一样高，往下挪一点对齐字的中线（字形的视觉中心比文本框中线低）
            if (sprite != null) UiKit.Picture(button, sprite, 0f, 3f, 9f, 9f);
            Text(button, LM.Get("vd_parliament_show_blocs"), 11f, 0f, ToggleW - 11f, 10f, 6, _showBlocs ? Color.white : Muted, TextAnchor.MiddleLeft);
        }

        internal const float ToggleW = 39f;

        // 集团底色和边框亮着、调淡时的透明度，边框的宽度，边框用魁首派系色
        const float BlocAlpha = 0.3f, BlocDim = 0.08f, BorderAlpha = 0.8f, BorderDim = 0.2f, BorderWidth = 1f;

        static BlocShape Shape(RectTransform pParent, float pW, float pH, Color pColor, float pAlpha = BlocAlpha)
        {
            RectTransform rt = Box(pParent, 0f, 0f, pW, pH, "bloc");
            BlocShape shape = rt.gameObject.AddComponent<BlocShape>();
            shape.color = new Color(pColor.r, pColor.g, pColor.b, pAlpha);
            shape.raycastTarget = false;
            return shape;
        }

        // 集团名沿外环逐字排开，以扇环正中为中心，可以伸出自己的扇环；和左边已经写下的名字撞上就只写魁首的城，
        // 还撞就不写（到列表里看）。半圆两端到头为止
        static void RingNames(RectTransform pArea, List<(Bloc bloc, float t1, float t2)> pArcs, Ring pRing, float pRadius)
        {
            const int font = 6;
            const float limit = 0.05f;
            float edge = Mathf.PI + limit;
            foreach (var (b, t1, t2) in pArcs)
            {
                string placed = null;
                float hi = 0f;
                foreach (string name in new[] { Blocs.Name(b), b.city })
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    float half = Measure(name, font) / 2f / pRadius, mid = (t1 + t2) / 2f;
                    float h = Mathf.Min(mid + half, Mathf.PI + limit), l = h - 2f * half;
                    if (l < -limit)
                    {
                        l = -limit;
                        h = l + 2f * half;
                    }
                    if (h > edge) continue;
                    placed = name;
                    hi = h;
                    edge = l - 3f / pRadius;
                    break;
                }
                if (placed == null) continue;
                Color color = FactionColor(b.leader.faction);
                float along = 0f;
                foreach (char c in placed)
                {
                    float w = Measure(c.ToString(), font);
                    float t = hi - (along + w / 2f) / pRadius;
                    along += w;
                    float x = pRing.cx + pRadius * Mathf.Cos(t), y = pRing.cy - pRadius * Mathf.Sin(t);
                    RectTransform rt = Text(pArea, c.ToString(), 0f, 0f, 12f, 12f, font, color, TextAnchor.MiddleCenter);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(x, -y);
                    rt.localEulerAngles = new Vector3(0f, 0f, t * Mathf.Rad2Deg - 90f);
                    DimShape(b, rt.GetComponent<Graphic>(), 1f, 0.3f);
                }
            }
        }

        // 点议席或坐标点，选中它所属的氏族：找离点击位置最近、且在 pSize 范围内的点
        static void PointClicks(RectTransform pArea, List<Vector3> pSeats, long[] pOwners, float pSize)
        {
            Image hit = pArea.GetComponent<Image>();
            if (hit == null)
            {
                hit = pArea.gameObject.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
            }
            hit.raycastTarget = true;
            var trigger = pArea.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var click = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick };
            click.callback.AddListener(data =>
            {
                var e = (UnityEngine.EventSystems.PointerEventData)data;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(pArea, e.position, e.pressEventCamera, out Vector2 local)) return;
                var point = new Vector2(local.x, -local.y);
                int best = -1;
                float bestDist = pSize;
                for (int i = 0; i < pSeats.Count; i++)
                {
                    float d = Vector2.Distance(point, pSeats[i]);
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                if (best >= 0) Select(pOwners[best]);
            });
            trigger.triggers.Add(click);
        }

        // 立场坐标图：横轴激进（左）到保守（右），纵轴集权（上）到分权（下），王室的点戴皇冠
        static void Compass(List<ParliamentClan> pClans, float pX, float pY, float pW, float pH)
        {
            RectTransform slot = Sliced(_parent, "ui/special/windowInnerSlicedRound", pX, pY, pW, pH);
            float cx = pW / 2f, cy = pH / 2f;
            Rect(slot, cx - 0.5f, 8f, 1f, pH - 16f, Line);
            Rect(slot, 4f, cy - 0.5f, pW - 8f, 1f, Line);
            Text(slot, LM.Get("vd_parliament_central"), 0f, 1f, pW, 8f, 5, Muted, TextAnchor.MiddleCenter);
            Text(slot, LM.Get("vd_parliament_local"), 0f, pH - 9f, pW, 8f, 5, Muted, TextAnchor.MiddleCenter);
            Text(slot, LM.Get("vd_parliament_radical"), 4f, cy - 9f, 30f, 8f, 5, Muted, TextAnchor.MiddleLeft);
            Text(slot, LM.Get("vd_parliament_conservative"), pW - 34f, cy - 9f, 30f, 8f, 5, Muted, TextAnchor.MiddleRight);
            float rx = pW / 2f - 8f, ry = pH / 2f - 12f;
            // 集团：成员的点圈成一块，压在点下面
            foreach (Bloc b in _blocs)
            {
                var pts = new List<Vector2>();
                foreach (ParliamentClan m in b.members) pts.Add(new Vector2(cx - m.x * rx, cy - m.y * ry));
                BlocShape shape = Shape(slot, pW, pH, FactionColor(b.leader.faction));
                shape.Hull(pts, 6f);
                DimShape(b, shape, BlocAlpha, BlocDim);
                BlocShape border = Shape(slot, pW, pH, FactionColor(b.leader.faction), BorderAlpha);
                border.stroke = BorderWidth;
                border.Hull(pts, 6f);
                DimShape(b, border, BorderAlpha, BorderDim);
            }
            var points = new List<Vector3>();
            var owners = new long[pClans.Count];
            foreach (ParliamentClan pc in pClans)
            {
                float size = 3f + Mathf.Sqrt(pc.seats) * 0.45f;
                float x = cx - pc.x * rx, y = cy - pc.y * ry;
                owners[points.Count] = pc.Key;
                points.Add(new Vector3(x, y));
                Dimmable(pc, Swatch(slot, x - size / 2f, y - size / 2f, size, FactionColor(pc.faction), 1f), 0.3f);
                if (pc.ruling) Dimmable(pc, Icon(slot, RulerTitles.CrownIcon(_kingdom), x - 5f, y - size / 2f - 10f, 10f), 0.3f);
            }
            PointClicks(slot, points, owners, 8f);
        }

        static void Dimmable(ParliamentClan pClan, RectTransform pTarget, float pAlpha)
        {
            _section.dims.Add(new Dim
            {
                clan = pClan.Key, bloc = _blocOf.TryGetValue(pClan.Key, out long b) ? b : -1, graphic = pTarget.GetComponent<Graphic>(),
                outline = pTarget.GetComponent<Outline>(), alpha = pAlpha, lit = 1f
            });
        }

        static void DimShape(Bloc pBloc, Graphic pGraphic, float pLit, float pDim)
        {
            _section.dims.Add(new Dim { clan = -1, bloc = pBloc.Key, shape = true, graphic = pGraphic, alpha = pDim, lit = pLit });
        }

        // 选中的集团：魁首和几家；选中的氏族：族长和占的城
        static string DetailText(List<ParliamentClan> pClans)
        {
            Bloc bloc = _blocs.Find(b => b.Key == _selected);
            if (bloc != null)
            {
                string leader = bloc.leader.clan.name + (bloc.leader.cadet ? " " + LM.Get("vd_parliament_cadet") : "");
                return $"<color={UiKit.Tag(FactionColor(bloc.leader.faction))}>{Blocs.Name(bloc)}</color> · {LM.Get("vd_bloc_leader")} <color=#8fb0ff>{leader}</color> · "
                       + LM.Get("vd_bloc_count").Replace("$count$", bloc.members.Count.ToString());
            }
            ParliamentClan pc = pClans.Find(c => c.Key == _selected);
            if (pc == null) return "";
            var parts = new List<string>();
            Actor head = pc.cadet ? pc.voice : pc.clan.getChief();
            if (head != null && head.isAlive()) parts.Add($"{LM.Get(pc.cadet ? "vd_parliament_cadet_head" : "vd_parliament_chief")} <color=#8fb0ff>{head.getName()}</color>");
            var cities = new List<string>();
            foreach (City c in pc.cities) cities.Add(c.data.name);
            if (cities.Count > 0) parts.Add(string.Join(LM.Get("vd_parliament_separator"), cities));
            return string.Join(" · ", parts);
        }

        static void Detail(string pText, float pX, float pY)
        {
            if (pText.Length == 0) return;
            Text(_parent, pText, pX, pY, LeftW, 20f, 6, Muted, TextAnchor.UpperLeft).GetComponent<Text>().horizontalOverflow = HorizontalWrapMode.Wrap;
        }
    }
}
