using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using static DiplomacyOverhaul.UiKit;
using Object = UnityEngine.Object;

namespace DiplomacyOverhaul
{
    // 局势窗口：从王国窗口左侧页签栏或右下角的局势按钮打开。左边是局势卡片（进行中的在前，落幕 20 年内的调暗排在后面），
    // 右边是选中那一个的起因、进度条、三个阶段的候选、趋势（落幕后是结局）和两边对垒。
    // 局势一年只变一次：数据指纹没变就不重建，推力明细只在重建时算
    public static class SituationWindow
    {
        const string WindowId = "vd_situation";
        public const float W = 560f, H = 390f;
        const float Top = 26f;
        const float LX = 10f, LW = 150f, CardH = 40f, CardGap = 4f;
        const float RX = 170f, RW = 380f;
        const float HeadY = Top + 24f, StageY = HeadY + 26f, BarY = StageY + 12f, BarH = 18f;
        const float PickY = BarY + BarH + 4f, PickRow = 11f;
        const float TrendY = PickY + 3f * PickRow + 6f, CampY = TrendY + 22f, WatchH = 16f;
        const float Eps = 0.0005f;

        static readonly Color Toward = Hex("#ff7a4a"), Away = Hex("#5fb8f0");
        static readonly Color EndLeft = Hex("#1e90d6"), EndRight = Hex("#d5491b");
        static readonly Color BarBack = Hex("#24261f"), PickGold = Hex("#ffb52e");
        static readonly Color StageFrom = Hex("#e8b53a"), StageTo = Hex("#d5491b");
        static readonly Color TrendBar = Hex("#9a9584");
        const string Ribbon = "ui/special/background_tab_content_2";
        const string TipKey = "vd_sit_tip";

        static KingdomTabWindow _shell;
        static Kingdom _kingdom;
        static RectTransform _body;
        static string _key, _selected;

        // 一张卡片：进行中的局势，或者 20 年内落幕的那一次
        class Entry
        {
            public SituationDef def;
            public SituationEnded ended;
            public float progress;
            public int years;
            public ParliamentClan by;
            public string byName;
            public SituationTally tally;
            public readonly SituationPick[] picks = new SituationPick[Situations.StageCount];
            // 王朝衰落：进度条画天命，数值是应得天命的组成，往左头（维系王朝）为正
            public bool decline;
            public float mandate;
            // 每年的变化：一般局势是进度，王朝衰落是天命；toward 统一换算成每年往右头走多少进度
            public float per, toward;
            // 大势、抽中的候选、各方；各方悬停看对谁的好感
            public readonly List<Row> trends = new List<Row>();
            public List<SituationPower> powers = new List<SituationPower>();
            public ParliamentClan favorTo;
            public string Key => def.id + (ended != null ? "_end" : "");
        }

        public static void Init()
        {
            // 排在选举与任命后面，藏起原版家族上面的空位，页签栏保持原来的长度
            _shell = new KingdomTabWindow(WindowId, Situations.IconPath, 3, W, H, Open, Tick) { enabled = () => Settings.ParliamentEnabled, spacer = "space" };
            _shell.Init();
            _body = null;
        }

        public static void Open(Kingdom pKingdom)
        {
            if (pKingdom == null || !pKingdom.isAlive() || !Settings.ParliamentEnabled) return;
            if (_kingdom != pKingdom) _selected = null;
            _kingdom = pKingdom;
            KingdomTabWindow.Current = pKingdom;
            if (_shell.Create()) _body = Box(_shell.root, 0f, 0f, W, H, "vd_section_body");
            _key = null;
            _shell.Show(pKingdom);
            Refresh();
        }

        static void Tick()
        {
            if (!_shell.IsOpen) return;
            if (_kingdom == null || !_kingdom.isAlive() || !Settings.ParliamentEnabled)
            {
                _shell.window.clickHide();
                return;
            }
            _shell.Fit();
            Refresh();
        }

        // 进度、抽中的候选、落幕记录、年份变了才重建
        static void Refresh()
        {
            if (_shell.root == null || _kingdom == null) return;
            var sb = new System.Text.StringBuilder();
            sb.Append(_kingdom.id).Append('|').Append(Date.getCurrentYear()).Append('|').Append(_selected);
            foreach (SituationDef d in Situations.All)
            {
                sb.Append('|').Append(d.id).Append(':').Append(Situations.Progress(_kingdom, d).ToString("0.000", CultureInfo.InvariantCulture));
                for (int s = 0; s < Situations.StageCount; s++) sb.Append(',').Append(Situations.Picked(_kingdom, d, s)?.id);
                sb.Append(':').Append(Situations.Ended(_kingdom, d)?.year);
            }
            string key = sb.ToString();
            if (key == _key) return;
            _key = key;
            for (int i = _body.childCount - 1; i >= 0; i--)
            {
                GameObject child = _body.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
            Build();
        }

        // 原版绸带贴图的九宫格边距是坏的（比贴图还大），拉伸时会画出图集里别的东西：
        // 用同一块像素重建一个，左右各留 40 像素的花纹，中间横向拉伸
        static Sprite _ribbon;

        static Sprite RibbonSprite()
        {
            if (_ribbon != null) return _ribbon;
            Sprite src = SpriteTextureLoader.getSprite(Ribbon);
            if (src == null) return null;
            _ribbon = Sprite.Create(src.texture, src.textureRect, new Vector2(0.5f, 0.5f), src.pixelsPerUnit, 0, SpriteMeshType.FullRect, new Vector4(40f, 0f, 40f, 0f));
            return _ribbon;
        }

        static List<Entry> Entries()
        {
            var list = new List<Entry>();
            List<ParliamentClan> clans = null;
            foreach (SituationDef d in Situations.All)
            {
                if (!Situations.IsActive(_kingdom, d)) continue;
                clans ??= Parliament.Clans(_kingdom);
                var e = new Entry { def = d, progress = Situations.Progress(_kingdom, d), years = Situations.Elapsed(_kingdom, d) };
                for (int s = 0; s < Situations.StageCount; s++) e.picks[s] = Situations.Picked(_kingdom, d, s);
                if (d == Decline.Def) DeclineRows(e, clans);
                else
                {
                    e.by = Situations.Initiator(_kingdom, d, clans);
                    e.byName = e.by != null ? e.by.clan.name : "";
                    e.favorTo = d.againstCrown ? clans.Find(c => c.ruling) : e.by;
                    if (e.by != null) GenericRows(e, Situations.Tally(_kingdom, d, clans, e.by));
                }
                list.Add(e);
            }
            var ended = new List<Entry>();
            foreach (SituationDef d in Situations.All)
            {
                SituationEnded end = Situations.Ended(_kingdom, d);
                if (end == null) continue;
                var e = new Entry { def = d, ended = end, progress = end.right ? 100f : 0f, years = end.year - end.since, byName = end.by };
                for (int s = 0; s < Situations.StageCount; s++) e.picks[s] = Situations.EndedPick(d, end, s);
                ended.Add(e);
            }
            list.AddRange(ended.OrderByDescending(e => e.ended.year));
            return list;
        }

        static void GenericRows(Entry pEntry, SituationTally pTally)
        {
            pEntry.tally = pTally;
            pEntry.powers = pTally.powers;
            foreach (var (p, v) in pTally.pressures)
                if (Mathf.Abs(v) > Eps) pEntry.trends.Add(new Row { name = LM.Get(p.id), push = v, color = TrendBar, nameColor = Ink });
            foreach (var (p, v) in pTally.picks)
                pEntry.trends.Add(new Row { name = LM.Get(p.id), push = v, color = PickGold, nameColor = Ink });
            pEntry.per = pTally.total * 100f / pEntry.def.years;
            pEntry.toward = pEntry.per;
        }

        // 王朝衰落：大势是应得天命的组成（基准不显示），各方是对王室的向背；每年天命向应得天命靠拢一成
        static void DeclineRows(Entry pEntry, List<ParliamentClan> pClans)
        {
            pEntry.decline = true;
            pEntry.mandate = Cycle.Mandate(_kingdom);
            pEntry.progress = Decline.Position(pEntry.mandate);
            Cycle.Governance g = Cycle.Govern(_kingdom);
            float scar = Cycle.ScarAging(_kingdom);
            void Trend(string pKey, float pValue)
            {
                if (Mathf.Abs(pValue) > 0.05f) pEntry.trends.Add(new Row { name = LM.Get(pKey), push = pValue, color = TrendBar, nameColor = Ink });
            }
            Trend("vd_mandate_decay", scar - Cycle.Aging(_kingdom));
            Trend("vd_mandate_scar", -scar);
            Trend("vd_mandate_cities", g.Cities);
            Trend("vd_mandate_ruler", g.Ruler);
            Trend("vd_mandate_vassals", g.Vassals);
            Trend("vd_mandate_people", g.People);
            Trend("vd_mandate_small_lord", g.SmallLord);
            float extra = 0f;
            for (int s = 0; s < Situations.StageCount; s++)
            {
                if (pEntry.picks[s] == null) continue;
                float v = Decline.PickValue(pEntry.picks[s], s);
                extra += v;
                pEntry.trends.Add(new Row { name = LM.Get(pEntry.picks[s].id), push = v, color = PickGold, nameColor = Ink });
            }
            pEntry.favorTo = pClans.Find(c => c.ruling);
            if (Settings.ParliamentEnabled)
            {
                foreach (Decline.Power p in Decline.Powers(_kingdom, pClans))
                {
                    // 只显示一位小数：先取整，不到 0.05 的归进观望，不写成 −0.0
                    pEntry.powers.Add(new SituationPower { clan = p.clan, push = Mathf.Round(p.value * 10f) / 10f, favor = p.favor });
                    extra += p.value;
                }
            }
            pEntry.per = Cycle.PullRate * (Cycle.Target(_kingdom, g) + extra - pEntry.mandate);
            // 天命往下走就是往右头（大叛乱）走：天命 30 点对应进度条 100
            pEntry.toward = -pEntry.per * 100f / 30f;
        }

        static void Build()
        {
            List<Entry> entries = Entries();
            // 和议会窗口一样，国名下面一行写政制名称
            ParliamentWindow.RegimeLine(_body, _kingdom, RegimeNames.Of(_kingdom), W);
            Header(_body, LX, Top, LW, LM.Get("vd_situation"), Situations.IconPath);
            if (entries.Count == 0)
            {
                Text(_body, LM.Get("vd_sit_empty"), RX, Top, RW, H - Top - 10f, 8, Muted, TextAnchor.MiddleCenter);
                return;
            }
            Entry sel = entries.Find(e => e.Key == _selected) ?? entries[0];
            _selected = sel.Key;
            float y = Top + 24f;
            foreach (Entry e in entries)
            {
                Card(e, y, e == sel);
                y += CardH + CardGap;
            }
            Detail(sel);
        }

        // ---------- 左边的卡片 ----------

        static void Card(Entry pEntry, float pY, bool pSelected)
        {
            RectTransform card = Button(_body, LX, pY, LW, CardH, Slot, () =>
            {
                _selected = pEntry.Key;
                Refresh();
            });
            if (pEntry.ended != null && !pSelected) card.gameObject.AddComponent<CanvasGroup>().alpha = 0.6f;
            RectTransform frame = Rect(card, 5f, 9f, 22f, 22f, Hex("#2c2f29"));
            frame.gameObject.AddComponent<Outline>().effectColor = Edge;
            Icon(frame, pEntry.def.icon ?? Situations.IconPath, 2f, 2f, 18f);
            float x = 32f, w = LW - x - 6f;
            Text(card, Fit(LM.Get(pEntry.def.id), 8, w - 34f), x, 3f, w, 11f, 8, Ink, TextAnchor.MiddleLeft);
            (string trend, Color color) = Short(pEntry);
            Text(card, trend, x, 3f, w, 11f, 7, color, TextAnchor.MiddleRight);
            Bar(card, pEntry, x, 17f, w, 5f, 1.5f);
            int end = EndOf(pEntry);
            Text(card, LM.Get(pEntry.def.id + "_left"), x, 25f, w, 10f, 6, Dim(Light(EndLeft), end == 1), TextAnchor.MiddleLeft);
            Text(card, LM.Get(pEntry.def.id + "_right"), x, 25f, w, 10f, 6, Dim(Light(EndRight), end == -1), TextAnchor.MiddleRight);
            if (pSelected) Frame(card, -2f);
        }

        // 卡片右上角：约几年（按月推进的局势是几个月）走到一头、僵持，或者落幕几年前
        static (string, Color) Short(Entry pEntry)
        {
            if (pEntry.ended != null) return (Ago(pEntry.ended.year), Muted);
            float per = pEntry.toward;
            if (Mathf.Abs(per) < 0.05f) return (LM.Get("vd_sit_stalemate"), Muted);
            if (pEntry.decline && per < 0f) return (LM.Get("vd_sit_rising"), Away);
            float left = per > 0f ? 100f - pEntry.progress : pEntry.progress;
            return (LM.Get(pEntry.def.monthly ? "vd_sit_about_month" : "vd_sit_about").Replace("$n$", Mathf.Max(1, Mathf.CeilToInt(left / Mathf.Abs(per))).ToString()), per > 0f ? Toward : Away);
        }

        // ---------- 右边的详情 ----------

        static void Detail(Entry pEntry)
        {
            SituationDef d = pEntry.def;
            Header(_body, RX, Top, RW, LM.Get(d.id), d.icon ?? Situations.IconPath);

            RectTransform head = Sliced(_body, Slot, RX, HeadY, RW, 20f);
            string age = pEntry.ended != null ? Ago(pEntry.ended.year) : LM.Get(pEntry.def.monthly ? "vd_sit_month" : "vd_sit_year").Replace("$n$", (pEntry.years + 1).ToString());
            Text(head, age, 8f, 0f, RW - 16f, 20f, 7, Muted, TextAnchor.MiddleRight);
            string desc = LM.Get(d.desc?.Invoke(_kingdom) ?? d.id + "_desc").Replace("$clan$", pEntry.byName).Replace("$kingdom$", _kingdom.data.name);
            Text(head, Fit(desc, 7, RW - 30f - Measure(age, 7)), 8f, 0f, RW - 16f, 20f, 7, Ink, TextAnchor.MiddleLeft);

            float inner = RW - 12f;
            int stage = Situations.StageOf(pEntry.progress), end = EndOf(pEntry);
            float[] from = StageEdges();
            for (int k = 0; k < Situations.StageCount; k++)
            {
                bool now = k == stage && end == 0;
                float mid = RX + 6f + inner * (from[k] + from[k + 1]) / 200f;
                Text(_body, LM.Get(d.id + "_stage" + k), mid - 40f, StageY - 1f, 80f, 12f, now ? 8 : 6, now ? Light(StageColor(k)) : Muted, TextAnchor.MiddleCenter);
            }
            RectTransform bar = Sliced(_body, Slot, RX, BarY, RW, BarH);
            Bar(bar, pEntry, 6f, 5f, inner, BarH - 10f, 3f);

            for (int k = 0; k < Situations.StageCount; k++)
            {
                SituationPick[] picks = d.stages[k];
                if (picks == null) continue;
                float cx = RX + 6f + inner * from[k] / 100f, cw = inner * (from[k + 1] - from[k]) / 100f;
                if (k > 0) Rect(_body, cx, PickY, 1f, 3f * PickRow, Line);
                for (int j = 0; j < picks.Length && j < 3; j++) PickRow3(picks[j], picks[j] == pEntry.picks[k], cx + 5f, PickY + j * PickRow, cw - 8f);
            }

            if (pEntry.ended != null)
            {
                Result(pEntry);
                return;
            }

            RectTransform trend = Sliced(_body, Slot, RX, TrendY, RW, 18f);
            float per = pEntry.per, toward = pEntry.toward;
            string perText = Mathf.Abs(toward) < 0.05f ? LM.Get("vd_sit_stalemate") : LM.Get(pEntry.def.monthly ? "vd_sit_per_month" : "vd_sit_per_year").Replace("$v$", Signed(per, 1));
            string progressText = pEntry.decline
                ? LM.Get("vd_sit_mandate").Replace("$n$", Mathf.RoundToInt(pEntry.mandate).ToString())
                : LM.Get("vd_sit_progress").Replace("$n$", Mathf.RoundToInt(pEntry.progress).ToString());
            Text(trend, progressText, 8f, 0f, 80f, 18f, 7, Ink, TextAnchor.MiddleLeft);
            // 每年的变化紧跟在进度后面
            Text(trend, perText, 8f + Measure(progressText, 7) + 6f, 0f, 120f, 18f, 7, Mathf.Abs(toward) < 0.05f ? Muted : toward > 0f ? Toward : Away, TextAnchor.MiddleLeft);
            Text(trend, Eta(pEntry, toward), 8f, 0f, RW - 16f, 18f, 7, Gold, TextAnchor.MiddleRight);

            if (pEntry.trends.Count > 0 || pEntry.powers.Count > 0) Camps(pEntry);
        }

        // 进度条：按阶段从琥珀到红，走过的部分实色、没走到的压暗，白色竖线是现在的位置
        static void Bar(RectTransform pParent, Entry pEntry, float pX, float pY, float pW, float pH, float pMark)
        {
            Rect(pParent, pX, pY, pW, pH, BarBack);
            float[] from = StageEdges();
            for (int k = 0; k < Situations.StageCount; k++)
            {
                float x0 = pW * from[k] / 100f, x1 = pW * from[k + 1] / 100f;
                Color c = StageColor(k);
                Rect(pParent, pX + x0, pY, x1 - x0, pH, Color.Lerp(BarBack, c, 0.3f));
                float filled = Mathf.Clamp(pW * pEntry.progress / 100f, x0, x1) - x0;
                if (filled > 0f) Rect(pParent, pX + x0, pY, filled, pH, c);
                if (k > 0) Rect(pParent, pX + x0 - 0.5f, pY, 1f, pH, Edge);
            }
            float at = pX + pW * Mathf.Clamp(pEntry.progress, 0f, 100f) / 100f;
            RectTransform mark = Rect(pParent, at - pMark / 2f, pY - pMark, pMark, pH + 2f * pMark, Color.white);
            mark.gameObject.AddComponent<Outline>().effectColor = Edge;
        }

        // 一个阶段的一个候选：抽中的点亮并写出效果，其余调暗；悬停看效果
        static void PickRow3(SituationPick pPick, bool pOn, float pX, float pY, float pW)
        {
            Color c = pPick.turn ? EndLeft : EndRight;
            Swatch(_body, pX, pY + 3f, 4f, pOn ? c : Color.Lerp(BarBack, c, 0.35f), 1f);
            string effect = Effect(pPick);
            if (pOn) Text(_body, effect, pX, pY, pW, PickRow, 6, EffectColor(pPick), TextAnchor.MiddleRight);
            float nameW = pOn ? pW - 8f - Measure(effect, 6) - 4f : pW - 8f;
            Text(_body, Fit(LM.Get(pPick.id), 6, nameW), pX + 7f, pY, nameW, PickRow, 6, pOn ? Ink : Muted, TextAnchor.MiddleLeft);
            Hover(Box(_body, pX, pY, pW, PickRow, "pick_hover"), LM.Get(pPick.id), new List<(string, string, string)> { (effect, "", Tag(EffectColor(pPick))) });
        }

        // 落幕后的结局：走到的那一头和抽中的结局（一头只有一种结局时是这一头的说明）
        static void Result(Entry pEntry)
        {
            SituationDef d = pEntry.def;
            bool right = pEntry.ended.right;
            RectTransform slot = Sliced(_body, Slot, RX, TrendY, RW, 18f);
            float x = 8f;
            string icon = right ? d.rightIcon : d.leftIcon;
            if (icon != null)
            {
                Icon(slot, icon, x, 3.5f, 11f);
                x += 15f;
            }
            string name = LM.Get(d.id + (right ? "_right" : "_left"));
            Text(slot, name, x, 0f, 100f, 18f, 7, Light(right ? EndRight : EndLeft), TextAnchor.MiddleLeft);
            x += Measure(name, 7) + 8f;
            SituationOutcome o = Situations.EndedOutcome(d, pEntry.ended);
            string what = o != null
                ? LM.Get(o.id) + " · " + LM.Get(o.id + "_desc")
                : LM.Get(d.id + (right ? "_right_desc" : "_left_desc")).Replace("$clan$", pEntry.byName);
            Text(slot, Fit(what, 7, RW - x - 8f), x, 0f, RW - x - 8f, 18f, 7, Ink, TextAnchor.MiddleLeft);
        }

        // ---------- 两边对垒 ----------

        // 一行：一股势力、一个集团、一项大势或一个抽中的候选
        class Row
        {
            public string name, tag;
            public float push;
            public Color color, nameColor;
            public int rank;
            public bool power;
            public List<(string, string, string)> tip;
        }

        static void Camps(Entry pEntry)
        {
            SituationDef d = pEntry.def;
            List<Row> trends = pEntry.trends;
            List<Row> powers = Powers(pEntry);
            float dir = pEntry.decline ? -1f : 1f;
            int digits = pEntry.decline ? 1 : 2;

            float colW = (RW - 6f) / 2f, h = H - 10f - CampY - WatchH - 4f;
            var watch = powers.Where(r => Mathf.Abs(r.push) <= Eps && r.rank != 0).ToList();
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1;
                float x = RX + side * (colW + 6f);
                RectTransform box = Sliced(_body, Slot, x, CampY, colW, h);
                box.name = "camp_" + side;
                // 栏头是原版的绸带，上面写立场
                RectTransform ribbon = Box(box, 6f, 3f, colW - 12f, 16f, "ribbon");
                Image rimg = ribbon.gameObject.AddComponent<Image>();
                rimg.sprite = RibbonSprite();
                rimg.type = Image.Type.Sliced;
                rimg.raycastTarget = false;
                Text(box, LM.Get(d.id + (right ? "_side_right" : "_side_left")), 0f, 3f, colW, 16f, 8, right ? Toward : Away, TextAnchor.MiddleCenter);
                var rows = new List<Row>();
                List<Row> mineTrends = trends.Where(r => right ? r.push * dir > Eps : r.push * dir < -Eps).OrderByDescending(r => Mathf.Abs(r.push)).ToList();
                List<Row> minePowers = powers.Where(r => right ? r.push * dir > Eps : r.push * dir < -Eps || (r.rank == 0 && Mathf.Abs(r.push) <= Eps))
                    .OrderBy(r => r.rank).ThenByDescending(r => Mathf.Abs(r.push)).ToList();
                float rh = 0f;
                if (mineTrends.Count > 0) rh += 10f + mineTrends.Count * 12f;
                if (minePowers.Count > 0) rh += 10f + minePowers.Count * 12f;
                RectTransform content = Scroll(box, "scroll_" + side, 4f, 20f, colW - 8f, h - 24f, rh);
                float y = 0f;
                y = Group(content, LM.Get("vd_sit_trends"), mineTrends, y, colW - 8f, right, digits);
                Group(content, LM.Get("vd_sit_powers"), minePowers, y, colW - 8f, right, digits);
            }
            if (watch.Count == 0) return;
            RectTransform w = Sliced(_body, Slot, RX, H - 10f - WatchH, RW, WatchH);
            Text(w, LM.Get("vd_sit_watch"), 8f, 0f, 40f, WatchH, 7, Muted, TextAnchor.MiddleLeft);
            float wx = 12f + Measure(LM.Get("vd_sit_watch"), 7);
            foreach (Row r in watch)
            {
                float nw = Measure(r.name, 7);
                if (wx + nw + 10f > RW - 8f) break;
                Rect(w, wx, 4f, 3f, 8f, r.color);
                Text(w, r.name, wx + 6f, 0f, nw + 4f, WatchH, 7, Ink, TextAnchor.MiddleLeft);
                wx += nw + 16f;
            }
        }

        static float Group(RectTransform pParent, string pLabel, List<Row> pRows, float pY, float pW, bool pRight, int pDigits)
        {
            if (pRows.Count == 0) return pY;
            Text(pParent, pLabel, 4f, pY, pW, 10f, 6, Hex("#9a9584"), TextAnchor.MiddleLeft);
            pY += 10f;
            foreach (Row r in pRows)
            {
                Rect(pParent, 4f, pY + 2f, 3f, 8f, r.color);
                float nx = 10f;
                bool none = Mathf.Abs(r.push) <= Eps;
                string value = none ? "—" : Signed(r.push, pDigits);
                float vw = Measure(value, 7) + 2f, tw = r.tag != null ? Measure(r.tag, 6) + 4f : 0f;
                string name = Fit(r.name, 7, pW - nx - vw - tw - 8f);
                Text(pParent, name, nx, pY, pW, 12f, 7, r.nameColor, TextAnchor.MiddleLeft);
                if (r.tag != null) Text(pParent, r.tag, nx + Measure(name, 7) + 3f, pY, tw, 12f, 6, Gold, TextAnchor.MiddleLeft);
                Text(pParent, value, 0f, pY, pW - 4f, 12f, 7, none ? Muted : pRight ? Toward : Away, TextAnchor.MiddleRight);
                if (r.tip != null) Hover(Box(pParent, 0f, pY, pW, 12f, "row_hover"), r.name, r.tip);
                pY += 12f;
            }
            return pY;
        }

        // 各方：王室第一，宗室第二，发起方第三，其余按推力排；同一个集团的成员收成一行
        static List<Row> Powers(Entry pEntry)
        {
            List<SituationPower> all = pEntry.powers;
            var clans = all.Select(p => p.clan).ToList();
            List<Bloc> blocs = Blocs.Of(_kingdom, clans);
            var rows = new List<Row>();
            var done = new HashSet<long>();
            int digits = pEntry.decline ? 1 : 2;
            foreach (SituationPower p in all)
            {
                if (done.Contains(p.clan.Key)) continue;
                Bloc bloc = blocs.Find(b => b.Has(p.clan));
                if (bloc == null || bloc.members.Count < 2)
                {
                    done.Add(p.clan.Key);
                    rows.Add(new Row
                    {
                        name = ClanName(p.clan), tag = PowerTag(pEntry, p.clan), push = p.push, rank = Rank(pEntry, p.clan), power = true,
                        color = ClanColor(p.clan.clan), nameColor = Ink, tip = PowerTip(pEntry, p)
                    });
                    continue;
                }
                var members = all.Where(m => bloc.Has(m.clan)).ToList();
                foreach (SituationPower m in members) done.Add(m.clan.Key);
                float dir = pEntry.decline ? -1f : 1f;
                var tip = members.Select(m => (ClanName(m.clan), Signed(m.push, digits), Tag(m.push * dir > Eps ? Toward : m.push * dir < -Eps ? Away : Muted))).ToList();
                rows.Add(new Row
                {
                    name = Blocs.Name(bloc), tag = members.Select(m => PowerTag(pEntry, m.clan)).FirstOrDefault(s => s != null),
                    push = members.Sum(m => m.push), rank = members.Min(m => Rank(pEntry, m.clan)), power = true,
                    color = ParliamentWindow.BlocColor(bloc), nameColor = Ink, tip = tip
                });
            }
            return rows;
        }

        static string ClanName(ParliamentClan pClan) => pClan.clan.name + (pClan.cadet ? " " + LM.Get("vd_parliament_cadet") : "");

        static int Rank(Entry pEntry, ParliamentClan pClan) =>
            pClan.ruling ? 0 : pClan.cadet ? 1 : pEntry.by != null && pClan.Key == pEntry.by.Key ? 2 : 3;

        static string PowerTag(Entry pEntry, ParliamentClan pClan)
        {
            if (pClan.ruling) return LM.Get(Government.IsRepublic(_kingdom) ? "vd_sit_tag_ruling" : "vd_sit_tag_royal");
            if (pEntry.by != null && pClan.Key == pEntry.by.Key) return LM.Get("vd_sit_tag_by");
            return null;
        }

        // 悬停：议席、政见得失、对发起方的好感
        static List<(string, string, string)> PowerTip(Entry pEntry, SituationPower pPower)
        {
            var lines = new List<(string, string, string)> { (LM.Get("vd_sit_tip_seats"), pPower.clan.seats.ToString(), null) };
            if (pEntry.favorTo == null || pPower.clan.Key == pEntry.favorTo.Key) return lines;
            if (!pEntry.decline) lines.Add((LM.Get("vd_sit_tip_gain"), Signed(pPower.gain, 2), null));
            lines.Add((LM.Get("vd_sit_tip_favor").Replace("$clan$", ClanName(pEntry.favorTo)), (pPower.favor > 0 ? "+" : pPower.favor < 0 ? "−" : "") + Mathf.Abs(pPower.favor), null));
            return lines;
        }

        // ---------- 小工具 ----------

        static void Hover(RectTransform pTarget, string pName, List<(string, string, string)> pLines)
        {
            Image hit = pTarget.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            pTarget.gameObject.AddComponent<HoverPointer>().show = () =>
            {
                // 原版把提示的标题和每行的名字都当语言 key 查，查不到会报错：先把文字注册成 key
                LM.AddToCurrentLocale(TipKey, pName);
                var lines = new List<(string, string, string)>();
                for (int i = 0; i < pLines.Count; i++)
                {
                    var (label, value, color) = pLines[i];
                    LM.AddToCurrentLocale(TipKey + "_" + i, label);
                    lines.Add((TipKey + "_" + i, value, color));
                }
                ParliamentWindow.TipName = TipKey;
                ParliamentWindow.TipEffects = new List<(string, string)>();
                ParliamentWindow.TipLines = lines;
                ParliamentWindow.TipVote = null;
                Tooltip.show(pTarget.gameObject, "tip", new TooltipData { tip_name = TipKey });
            };
        }


        // 趋势行右边：往右头推时说多久进入下一阶段或走到右头，往左头拉时说多久走到左头
        static string Eta(Entry pEntry, float pPer)
        {
            if (Mathf.Abs(pPer) < 0.05f) return "";
            if (pEntry.decline && pPer < 0f) return LM.Get("vd_sit_rising");
            SituationDef d = pEntry.def;
            float[] from = StageEdges();
            int stage = Situations.StageOf(pEntry.progress);
            if (pPer > 0f && stage < Situations.StageCount - 1)
                return LM.Get(d.monthly ? "vd_sit_eta_stage_month" : "vd_sit_eta_stage").Replace("$n$", Years(from[stage + 1] - pEntry.progress, pPer)).Replace("$stage$", LM.Get(d.id + "_stage" + (stage + 1)));
            bool right = pPer > 0f;
            return LM.Get(d.monthly ? "vd_sit_eta_end_month" : "vd_sit_eta_end").Replace("$n$", Years(right ? 100f - pEntry.progress : pEntry.progress, pPer))
                .Replace("$end$", LM.Get(d.id + (right ? "_right" : "_left")));
        }

        static string Years(float pDistance, float pPer) => Mathf.Max(1, Mathf.CeilToInt(pDistance / Mathf.Abs(pPer))).ToString();

        static string Ago(int pYear)
        {
            int n = Date.getCurrentYear() - pYear;
            return n > 0 ? LM.Get("vd_sit_ago").Replace("$n$", n.ToString()) : LM.Get("vd_sit_this_year");
        }

        // 走到哪一头：右头 1，左头 -1，进行中 0
        static int EndOf(Entry pEntry) => pEntry.ended == null ? 0 : pEntry.ended.right ? 1 : -1;

        static float[] StageEdges() => new[] { 0f, 33f, 67f, 100f };

        static Color StageColor(int pStage) => Color.Lerp(StageFrom, StageTo, pStage / (float)(Situations.StageCount - 1));

        static Color Light(Color pColor) => Color.Lerp(pColor, Color.white, 0.35f);

        static Color Dim(Color pColor, bool pDim) => pDim ? new Color(pColor.r, pColor.g, pColor.b, 0.4f) : pColor;

        static string Effect(SituationPick pPick)
        {
            if (pPick.loyalty != 0) return LM.Get("vd_sit_eff_loyalty").Replace("$v$", SignedInt(pPick.loyalty));
            if (pPick.damage != 0) return LM.Get("vd_sit_eff_damage").Replace("$v$", SignedInt(pPick.damage));
            return "";
        }

        static Color EffectColor(SituationPick pPick) => pPick.loyalty + pPick.damage >= 0 ? Away : Toward;

        static string SignedInt(int pValue) => (pValue > 0 ? "+" : pValue < 0 ? "−" : "") + Mathf.Abs(pValue);

        static string Signed(float pValue, int pDigits)
        {
            string s = Mathf.Abs(pValue).ToString(pDigits == 1 ? "0.0" : "0.00", CultureInfo.InvariantCulture);
            return (pValue > 0f ? "+" : pValue < 0f ? "−" : "") + s;
        }
    }
}
