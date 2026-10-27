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

        static void ClanList(List<ParliamentClan> pClans)
        {
            List<ParliamentClan> list = RulingFirst(pClans);
            RectTransform content = Scroll("scroll_clans", LeftX, ListY, ListW, ListRows * (RowH + 2f), list.Count * (RowH + 2f));
            float ry = 0f;
            foreach (ParliamentClan pc in list)
            {
                long id = pc.Key;
                RectTransform row = Button(content, 0f, ry, ListW - 6f, RowH, "ui/special/windowInnerSlicedRound", () => Select(id));
                Swatch(row, 6f, (RowH - 6f) / 2f, 6f, FactionColor(pc.faction), 1f);
                // 名字和王室、宗室的小字一起放在派系那一栏之前，放不下时截短名字
                string tag = Tag(pc);
                float tagW = tag != null ? Measure(tag, 6) + 3f : 0f;
                string name = UiKit.Fit(pc.clan.name, 7, 90f - tagW);
                Text(row, name, 16f, 0f, 90f, RowH, 7, Ink, TextAnchor.MiddleLeft);
                if (tag != null) Text(row, tag, 16f + Measure(name, 7) + 3f, 0f, 30f, RowH, 6, Gold, TextAnchor.MiddleLeft);
                Text(row, LM.Get(FactionKey(pc.faction)), 108f, 0f, 36f, RowH, 6, Muted, TextAnchor.MiddleLeft);
                Text(row, pc.seats.ToString(), ListW - 40f, 0f, 28f, RowH, 7, Gold, TextAnchor.MiddleRight);
                _section.frames[id] = Frame(row, -1f).gameObject;
                ry += RowH + 2f;
            }
        }

        // 列表里名字后面的小字：王室（共和国是执政）、宗室
        static string Tag(ParliamentClan pClan)
        {
            if (pClan.ruling) return LM.Get(Government.IsRepublic(_kingdom) ? "vd_parliament_ruling" : "vd_parliament_royal");
            return pClan.cadet ? LM.Get("vd_parliament_cadet") : null;
        }

        static List<ParliamentClan> RulingFirst(List<ParliamentClan> pClans)
        {
            // 王室在最上面，宗室紧跟着
            var list = new List<ParliamentClan>(pClans);
            int at = 0;
            foreach (System.Predicate<ParliamentClan> match in new System.Predicate<ParliamentClan>[] { c => c.ruling, c => c.cadet })
            {
                int i = list.FindIndex(match);
                if (i < 0) continue;
                ParliamentClan r = list[i];
                list.RemoveAt(i);
                list.Insert(at++, r);
            }
            return list;
        }

        // 共和国里朝中的老家族叫元老派，地方新贵叫自治派
        static string FactionKey(Faction pFaction)
        {
            string key = "vd_faction_" + pFaction.ToString().ToLowerInvariant();
            bool republic = _kingdom != null && Government.IsRepublic(_kingdom);
            if (republic && (pFaction == Faction.Royalist || pFaction == Faction.Republican)) key += "_republic";
            return key;
        }

        // 议席半圆：按派系从激进（左）到保守（右）排开，中间写最大派系
        static void Hemicycle(List<ParliamentClan> pClans, float pX, float pY)
        {
            const float aw = 262f, ah = 132f;
            RectTransform area = Box(_parent, pX + 2f, pY, aw, ah);
            int total = Parliament.TotalSeats(pClans);
            List<Vector3> seats = SeatLayout(total, aw, ah, out float size);
            var owners = new long[seats.Count];
            int s = 0;
            foreach (ParliamentClan pc in pClans)
            {
                Color color = FactionColor(pc.faction);
                for (int i = 0; i < pc.seats && s < seats.Count; i++, s++)
                {
                    Dimmable(pc, Swatch(area, seats[s].x - size / 2f, seats[s].y - size / 2f, size, color, 1f), 0.25f);
                    owners[s] = pc.Key;
                }
            }
            PointClicks(area, seats, owners, size);

            var bySeats = new Dictionary<Faction, int>();
            foreach (ParliamentClan pc in pClans) bySeats[pc.faction] = (bySeats.TryGetValue(pc.faction, out int n) ? n : 0) + pc.seats;
            Faction top = Faction.Royalist;
            int topSeats = -1;
            foreach (KeyValuePair<Faction, int> pair in bySeats)
            {
                if (pair.Value > topSeats) { top = pair.Key; topSeats = pair.Value; }
            }
            Text(area, LM.Get("vd_parliament_largest"), 0f, ah - 34f, aw, 10f, 6, Muted, TextAnchor.MiddleCenter);
            Text(area, LM.Get(FactionKey(top)) + " " + topSeats, 0f, ah - 24f, aw, 18f, 14, FactionColor(top), TextAnchor.MiddleCenter);
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
            _section.dims.Add(new Dim { clan = pClan.Key, graphic = pTarget.GetComponent<Graphic>(), outline = pTarget.GetComponent<Outline>(), alpha = pAlpha });
        }

        // 选中的氏族：族长和占的城
        static string DetailText(List<ParliamentClan> pClans)
        {
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
