using System.Collections.Generic;
using System.Linq;
using NeoModLoader.General;
using UnityEngine;
using static DiplomacyOverhaul.UiKit;
using Object = UnityEngine.Object;

namespace DiplomacyOverhaul
{
    // 国库：从王国窗口左侧页签栏打开。上面是存金和去年的结余，中间是去年钱的流向图（左边来源汇进国库，
    // 国王的份停在中间条的上段，国库的份流向右边各项开支和结余），下面是账簿
    public static class TreasuryWindow
    {
        const string WindowId = "vd_treasury_window";
        public const string IconPath = "ui/Icons/iconMoney";
        public const float W = 250f, H = 330f;
        const float X = 10f, Inner = W - 20f, Top = 26f;
        const float SumY = Top + 24f, SumH = 24f;
        const float FlowY = SumY + SumH + 4f, FlowH = 150f;
        const float LogY = FlowY + FlowH + 6f, RowH = 13f;
        // 流向图里：左边短条的右边、中间条的左右、右边短条的左边（相对流向图）
        const float LX = 56f, NL = Inner / 2f - 2f, NR = Inner / 2f + 2f, RX = Inner - 56f, BarW = 3f;
        const float FlowTotal = 64f, FlowPad = 6f;
        const float BandAlpha = 0.5f;

        static readonly Color KingColor = Hex("#f5d08a");
        static readonly Color TreasuryColor = Hex("#e8a634");
        static readonly Color In = Hex("#5fb8f0"), Out = Hex("#ff7a4a");

        static KingdomTabWindow _shell;
        static Kingdom _kingdom;
        static RectTransform _body;
        static string _key;

        public static void Init()
        {
            // 放在名人页签前面，占掉家族下面的那个空位
            _shell = new KingdomTabWindow(WindowId, IconPath, 3, W, H, Open, Tick) { before = "Interesting People", spacer = "space (1)" };
            _shell.Init();
            _body = null;
        }

        public static void Open(Kingdom pKingdom)
        {
            if (pKingdom == null || !pKingdom.isAlive()) return;
            _kingdom = pKingdom;
            KingdomTabWindow.Current = pKingdom;
            if (_shell.Create())
            {
                Header(_shell.root, X, Top, Inner, LM.Get("vd_treasury"), IconPath);
                Header(_shell.root, X, LogY, Inner, LM.Get("vd_ledger"), "ui/Icons/iconBooks");
                _body = Box(_shell.root, 0f, 0f, W, H, "vd_section_body");
            }
            _key = null;
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

        // 存金、去年的账、账簿变了才重建
        static void Refresh()
        {
            if (_shell.root == null || _kingdom == null) return;
            var sb = new System.Text.StringBuilder();
            sb.Append(_kingdom.id).Append('|').Append(Treasury.Get(_kingdom)).Append('|').Append(Ledger.HasLastYear(_kingdom)).Append('|').Append(Government.IsRepublic(_kingdom));
            foreach (Ledger.Flow f in System.Enum.GetValues(typeof(Ledger.Flow))) sb.Append(',').Append(Ledger.LastYear(_kingdom, f));
            foreach (City c in _kingdom.getCities()) sb.Append(';').Append(c.id).Append(':').Append(Ledger.LastYear(c));
            _kingdom.data.get("vd_ledger_log", out string log, "");
            sb.Append('|').Append(log.Length > 0 ? log.Substring(0, Mathf.Min(log.Length, 40)) : "");
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

        // 流向图的一股
        struct Stream
        {
            public string name;
            public int amount;
            public Color color;
            public bool plus;   // 右边的结余记成进账
        }

        static void Build()
        {
            Account a = Account.Of(_kingdom);

            RectTransform sum = Sliced(_body, "ui/special/windowInnerSlicedRound", X, SumY, Inner, SumH);
            Text(sum, LM.Get("vd_treasury_balance"), 8f, 0f, 60f, SumH, 7, Muted, TextAnchor.MiddleLeft);
            Text(sum, Treasury.Get(_kingdom).ToString(), 8f + Measure(LM.Get("vd_treasury_balance"), 7) + 5f, 0f, 60f, SumH, 11, Gold, TextAnchor.MiddleLeft);
            if (a != null)
            {
                string net = Signed(a.Surplus);
                Text(sum, net, Inner - 8f - 50f, 0f, 50f, SumH, 9, a.Surplus >= 0 ? In : Out, TextAnchor.MiddleRight);
                Text(sum, LM.Get("vd_treasury_last_year"), Inner - 8f - Measure(net, 9) - 4f - 40f, 0f, 40f, SumH, 7, Muted, TextAnchor.MiddleRight);
            }

            RectTransform flow = Sliced(_body, "ui/special/windowInnerSlicedRound", X, FlowY, Inner, FlowH);
            if (a == null || a.InTotal == 0)
                Text(flow, LM.Get(a == null ? "vd_treasury_no_record" : "vd_treasury_idle"), 0f, 0f, Inner, FlowH, 7, Muted, TextAnchor.MiddleCenter);
            else Flow(flow, a);

            Ledgerbook();
        }

        static void Flow(RectTransform pParent, Account pAccount)
        {
            float scale = FlowTotal / pAccount.InTotal;
            float nodeH = pAccount.InTotal * scale, ny = (FlowH - nodeH) / 2f;
            float kingH = pAccount.King * scale;

            RectTransform canvas = Box(pParent, 0f, 0f, Inner, FlowH, "sankey");
            var graphic = canvas.gameObject.AddComponent<SankeyGraphic>();
            graphic.raycastTarget = false;
            var bands = new List<SankeyGraphic.Band>();

            Side(pParent, bands, pAccount.Incoming, scale, ny, false);
            Side(pParent, bands, pAccount.Outgoing, scale, ny + kingH, true);
            graphic.SetBands(bands);

            // 中间条：上段是领袖（国王、执政等）的份，下段国库，连成一根
            if (kingH > 0.5f)
            {
                Rect(pParent, NL, ny, NR - NL, kingH, KingColor);
                Text(pParent, Government.RulerTitle(_kingdom), NL - 30f, ny - 10f, NR - NL + 60f, 9f, 6, KingColor, TextAnchor.MiddleCenter);
            }
            Rect(pParent, NL, ny + kingH, NR - NL, nodeH - kingH, TreasuryColor);
            Text(pParent, LM.Get("vd_treasury"), NL - 30f, ny + nodeH + 1f, NR - NL + 60f, 9f, 6, TreasuryColor, TextAnchor.MiddleCenter);
        }

        // 一侧的各股：两头的短条在整个高度上均匀排开，带子从各处弯到中间条上，在中间条上紧挨着
        static void Side(RectTransform pParent, List<SankeyGraphic.Band> pBands, List<Stream> pList, float pScale, float pNodeY, bool pOut)
        {
            float slot = (FlowH - 2f * FlowPad) / Mathf.Max(1, pList.Count);
            float y = pNodeY;
            for (int i = 0; i < pList.Count; i++)
            {
                Stream s = pList[i];
                float h = Mathf.Max(1f, s.amount * pScale);
                float c = FlowPad + slot * i + slot / 2f, ly = c - h / 2f;
                var band = new Color(s.color.r, s.color.g, s.color.b, BandAlpha);
                if (pOut) pBands.Add(new SankeyGraphic.Band { x0 = NR, y0 = y, x1 = RX, y1 = ly, h = h, color = band });
                else pBands.Add(new SankeyGraphic.Band { x0 = LX, y0 = ly, x1 = NL, y1 = y, h = h, color = band });
                Rect(pParent, pOut ? RX : LX - BarW, ly, BarW, h, s.color);
                Color label = Color.Lerp(s.color, Color.white, 0.35f);
                string value = pOut && !s.plus ? "−" + s.amount : "+" + s.amount;
                float lx = pOut ? RX + BarW + 3f : 2f, lw = LX - BarW - 5f;
                TextAnchor align = pOut ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                Text(pParent, Fit(s.name, 6, lw), lx, c - 9f, lw, 9f, 6, label, align);
                Text(pParent, value, lx, c, lw, 9f, 6, Ink, align);
                y += h;
            }
        }

        static void Ledgerbook()
        {
            List<Ledger.Entry> entries = Ledger.Entries(_kingdom);
            float y = LogY + 22f, h = H - 10f - y;
            if (entries.Count == 0)
            {
                Text(_body, LM.Get("vd_ledger_empty"), X, y, Inner, RowH, 7, Muted, TextAnchor.MiddleCenter);
                return;
            }
            RectTransform content = Scroll(_body, "scroll_ledger", X, y, Inner, h, entries.Count * RowH);
            float ry = 0f;
            foreach (Ledger.Entry e in entries)
            {
                string name = LocalizedTextManager.stringExists(e.name) ? LM.Get(e.name) : e.name;
                string text = LM.Get(e.key).Replace("$name$", name);
                string year = LM.Get("vd_ledger_year").Replace("$year$", e.year.ToString());
                float amountW = e.amount != 0 ? 26f : 0f;
                Text(content, year, 4f, ry, 34f, RowH, 6, Muted, TextAnchor.MiddleLeft);
                Text(content, Fit(text, 6, Inner - 44f - amountW), 40f, ry, Inner - 44f - amountW, RowH, 6, Ink, TextAnchor.MiddleLeft);
                if (e.amount != 0) Text(content, Signed(e.amount), Inner - 4f - amountW, ry, amountW, RowH, 6, e.amount > 0 ? In : Out, TextAnchor.MiddleRight);
                Rect(content, 2f, ry + RowH - 1f, Inner - 4f, 1f, Line);
                ry += RowH;
            }
        }

        static string Signed(int pValue) => pValue >= 0 ? "+" + pValue : "−" + (-pValue);

        // 去年的账整理成流向图的两侧：左边进国库的各股，右边国库花出去的各股；收支不平时补一股结余或动用存金
        class Account
        {
            public readonly List<Stream> Incoming = new List<Stream>();
            public readonly List<Stream> Outgoing = new List<Stream>();
            public int King, InTotal, Surplus;

            static readonly Color CityMain = Hex("#e8a634"), CityRest = Hex("#c98a2a");
            static readonly Color Tribute = Hex("#3b8fd0"), Reparations = Hex("#4fa44a"), Drawdown = Hex("#8a8f7d");
            static readonly Color Guard = Hex("#c4452a"), Arms = Hex("#a33a22"), Plots = Hex("#8a4fc4"), Works = Hex("#3fae9a");
            static readonly Color Overlord = Hex("#5a7fb0"), ReparationsPaid = Hex("#7a8f3a"), Saved = Hex("#ffb52e");
            const int CitiesShown = 3;

            public static Account Of(Kingdom pKingdom)
            {
                if (!Ledger.HasLastYear(pKingdom)) return null;
                var a = new Account();
                int tax = Ledger.LastYear(pKingdom, Ledger.Flow.Tax);
                // 上缴最多的几座城单独列，其余合成一股；只差一座时也单独列
                var cities = pKingdom.getCities().Where(c => Ledger.LastYear(c) > 0).OrderByDescending(Ledger.LastYear).ToList();
                int shown = cities.Count <= CitiesShown + 1 ? cities.Count : CitiesShown;
                int listed = 0;
                for (int i = 0; i < shown; i++)
                {
                    int v = Mathf.Min(Ledger.LastYear(cities[i]), tax - listed);
                    if (v <= 0) break;
                    a.Add(a.Incoming, cities[i].name, v, CityMain);
                    listed += v;
                }
                if (tax - listed > 0)
                    a.Add(a.Incoming, LM.Get(listed > 0 ? "vd_treasury_other_cities" : "vd_treasury_cities").Replace("$count$", (cities.Count - shown).ToString()), tax - listed, CityRest);
                a.Add(a.Incoming, LM.Get("vd_treasury_tribute"), Ledger.LastYear(pKingdom, Ledger.Flow.Tribute), Tribute);
                a.Add(a.Incoming, LM.Get("vd_treasury_reparations"), Ledger.LastYear(pKingdom, Ledger.Flow.Reparations), Reparations);

                a.King = Mathf.Min(Ledger.LastYear(pKingdom, Ledger.Flow.King), tax);
                a.Add(a.Outgoing, LM.Get("vd_treasury_guard"), Ledger.LastYear(pKingdom, Ledger.Flow.Guard), Guard);
                a.Add(a.Outgoing, LM.Get("vd_treasury_arms"), Ledger.LastYear(pKingdom, Ledger.Flow.Arms), Arms);
                a.Add(a.Outgoing, LM.Get("vd_treasury_works"), Ledger.LastYear(pKingdom, Ledger.Flow.Works), Works);
                a.Add(a.Outgoing, LM.Get("vd_treasury_plots"), Ledger.LastYear(pKingdom, Ledger.Flow.Plots), Plots);
                a.Add(a.Outgoing, LM.Get("vd_treasury_overlord"), Ledger.LastYear(pKingdom, Ledger.Flow.Overlord), Overlord);
                a.Add(a.Outgoing, LM.Get("vd_treasury_reparations_paid"), Ledger.LastYear(pKingdom, Ledger.Flow.ReparationsPaid), ReparationsPaid);

                int income = a.Incoming.Sum(s => s.amount), spent = a.Outgoing.Sum(s => s.amount);
                a.Surplus = income - a.King - spent;
                if (a.Surplus > 0) a.Outgoing.Add(new Stream { name = LM.Get("vd_treasury_saved"), amount = a.Surplus, color = Saved, plus = true });
                else if (a.Surplus < 0) a.Add(a.Incoming, LM.Get("vd_treasury_drawdown"), -a.Surplus, Drawdown);
                a.InTotal = a.Incoming.Sum(s => s.amount);
                return a;
            }

            void Add(List<Stream> pList, string pName, int pAmount, Color pColor)
            {
                if (pAmount > 0) pList.Add(new Stream { name = pName, amount = pAmount, color = pColor });
            }
        }
    }
}
