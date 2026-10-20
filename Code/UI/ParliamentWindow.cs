using System;
using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using static DiplomacyOverhaul.UiKit;
using Object = UnityEngine.Object;

namespace DiplomacyOverhaul
{
    // 议会窗口：从王国窗口左侧页签栏打开。左边是议席和氏族，右边是政策和议案。
    // 界面用原版的窗口贴图拼出来，分成几块，每块只在自己用到的数据变了时重建；选中氏族只改样式
    public static class ParliamentWindow
    {
        const string WindowId = "vd_parliament";
        const string SideName = "vd_parliament_side";
        public const string IconPath = "ui/Icons/vd_parliament";
        public const float W = 560f, H = 330f;

        static readonly Color YesBar = Hex("#1e90d6");
        static readonly Color NoBar = Hex("#d5491b");
        internal static readonly Color YesText = Hex("#5fb8f0");
        internal static readonly Color NoText = Hex("#ff7a4a");

        static KingdomTabWindow _shell;
        static Kingdom _kingdom;
        static long _selected = -1;
        static PolicyTab _tab = PolicyTab.Domestic;
        internal static string _open;
        static Section _frame, _seats, _clans, _compass, _detail, _policies, _bill;
        static Section[] _sections;
        static Section _section;
        static RectTransform _parent;
        static long _appliedSelected = -2;
        static Text _billWhen;

        // 窗口的一块：记着上次建它时的数据指纹，按氏族调淡的图形和选中框
        class Section
        {
            public RectTransform root;
            public string key;
            public long kingdom = -1;
            public readonly List<Dim> dims = new List<Dim>();
            public readonly Dictionary<long, GameObject> frames = new Dictionary<long, GameObject>();
        }

        struct Dim
        {
            public long clan;
            public Graphic graphic;
            public Outline outline;
            public float alpha;
        }

        public static Color FactionColor(Faction pFaction)
        {
            switch (pFaction)
            {
                case Faction.Reformist: return Hex("#e5473b");
                case Faction.Republican: return Hex("#f2a52e");
                case Faction.Aristocrats: return Hex("#23a98f");
                default: return Hex("#3b7be0");
            }
        }

        // 热重载：旧版本建的窗口和按钮引用着旧代码，全部拆掉，用到时重建
        public static void Init()
        {
            _shell = new KingdomTabWindow(WindowId, IconPath, 1, W, H, Open, Tick);
            _shell.Init();
            foreach (RectTransform t in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                if (t != null && t.name == SideName) Object.DestroyImmediate(t.gameObject);
            }
            _side = null;
        }

        // ---------- 右下角的快捷按钮 ----------

        // 选中王国时左下角有原版的王国图标和取消按钮（CanvasLeft/SideCenterLeft），
        // 右下角对称放一个议会按钮，跟着它一起滑进滑出
        static UiMover _side;

        static UiMover CreateSide()
        {
            PowerButtonSelector selector = PowerButtonSelector.instance;
            Transform left = selector.cancelUnitSelectedMover.transform;
            RectTransform leftSide = (RectTransform)left.parent;
            Transform canvasRight = leftSide.parent.parent.Find("CanvasRight");
            Transform template = left.Find("Main Container/CancelSpecialSelector");
            if (canvasRight == null || template == null) return null;

            GameObject side = new GameObject(SideName, typeof(RectTransform));
            RectTransform sideRect = (RectTransform)side.transform;
            sideRect.SetParent(canvasRight, false);
            // CanvasRight 挂着竖排布局，不让它摆这个容器
            side.AddComponent<LayoutElement>().ignoreLayout = true;
            sideRect.anchorMin = sideRect.anchorMax = new Vector2(1f, 0f);
            sideRect.pivot = leftSide.pivot;
            sideRect.sizeDelta = leftSide.sizeDelta;
            sideRect.anchoredPosition = leftSide.anchoredPosition;

            // 原版按钮在移动器正中，移动器从屏幕外滑进来；这里左右反过来，
            // 按钮比原版矮，往右下角收一些，贴近屏幕边和底栏
            GameObject moverGo = new GameObject("Mover", typeof(RectTransform));
            RectTransform moverRect = (RectTransform)moverGo.transform;
            moverRect.SetParent(sideRect, false);
            moverRect.sizeDelta = ((RectTransform)left).sizeDelta;
            UiMover leftMover = selector.cancelUnitSelectedMover;
            UiMover mover = moverGo.AddComponent<UiMover>();
            mover.initInitPos = false;
            mover.initPos = new Vector3(-20f, leftMover.initPos.y - 27f, 0f);
            mover.hidePos = new Vector3(-leftMover.hidePos.x, mover.initPos.y, 0f);
            mover.setVisible(false, true);

            GameObject go = Object.Instantiate(template.gameObject, moverRect);
            go.name = "Button";
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c is SelectedPowerTabTopContainer || c is Animator) Object.DestroyImmediate(c);
            }
            Object.DestroyImmediate(go.transform.Find("IconX").gameObject);
            Object.DestroyImmediate(go.transform.Find("bg").gameObject);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(32f, 32f);
            RectTransform icon = (RectTransform)go.transform.Find("Icon");
            icon.anchoredPosition = Vector2.zero;
            icon.GetComponent<Image>().sprite = SpriteTextureLoader.getSprite(IconPath);

            Button button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => Open(SelectedKingdom()));
            TipButton tip = go.AddComponent<TipButton>();
            tip.showOnClick = false;
            tip.textOnClick = "vd_parliament";
            tip.textOnClickDescription = "vd_parliament_description";
            return mover;
        }

        static Kingdom SelectedKingdom()
        {
            if (SelectedUnit.isSet() || !SelectedObjects.isNanoObjectSet()) return null;
            return SelectedObjects.getSelectedNanoObject() as Kingdom;
        }

        internal static void UpdateSide()
        {
            bool show = PowerButtonSelector.instance.cancelUnitSelectedMover.visible && SelectedKingdom() != null;
            if (_side == null)
            {
                if (!show) return;
                _side = CreateSide();
                if (_side == null) return;
            }
            _side.setVisible(show);
        }

        // ---------- 窗口 ----------

        public static void Open(Kingdom pKingdom, PolicyTab pTab)
        {
            _tab = pTab;
            Open(pKingdom);
        }

        public static void Open(Kingdom pKingdom)
        {
            if (pKingdom == null || !pKingdom.isAlive()) return;
            if (_kingdom != pKingdom) _selected = -1;
            _kingdom = pKingdom;
            KingdomTabWindow.Current = pKingdom;
            if (_shell.Create())
            {
                _sections = new[] { _frame = NewSection("frame"), _seats = NewSection("seats"), _clans = NewSection("clans"), _compass = NewSection("compass"),
                    _detail = NewSection("detail"), _policies = NewSection("policies"), _bill = NewSection("bill") };
            }
            _shell.Show(pKingdom);
            Refresh();
        }

        static Section NewSection(string pName)
        {
            RectTransform rt = Box(_shell.root, 0f, 0f, W, H, "vd_section_" + pName);
            return new Section { root = rt };
        }

        internal static void Tick()
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

        // 每块算一个数据指纹，和上次不同才重建那一块
        static void Refresh()
        {
            if (_shell.root == null || _kingdom == null) return;
            List<ParliamentClan> clans = Parliament.Clans(_kingdom);
            if (_selected >= 0 && clans.Find(c => c.Key == _selected) == null) _selected = -1;
            bool republic = Government.IsRepublic(_kingdom);
            string k = _kingdom.id + "|";

            var ck = new System.Text.StringBuilder(k);
            foreach (ParliamentClan pc in clans)
                ck.Append(pc.Key).Append(':').Append(pc.clan.name).Append(':').Append(pc.seats).Append(':').Append((int)pc.faction).Append(':').Append(pc.ruling).Append('|');
            string clanKey = ck.ToString();

            bool built = Build(_frame, "frame", BuildFrame);
            built |= Build(_seats, clanKey, () =>
            {
                if (clans.Count > 0) Hemicycle(clans, LeftX, Top + 24f);
            });
            built |= Build(_clans, clanKey + republic, () => ClanList(clans));

            var pk = new System.Text.StringBuilder(clanKey);
            foreach (ParliamentClan pc in clans) pk.Append(pc.x.ToString("F2")).Append(',').Append(pc.y.ToString("F2")).Append('|');
            built |= Build(_compass, pk.ToString(), () => Compass(clans, LeftX + ListW + 4f, ListY, LeftW - ListW - 4f, ListRows * (RowH + 2f) - 2f));

            string detail = DetailText(clans);
            Build(_detail, k + detail, () => Detail(detail, LeftX, ListY + ListRows * (RowH + 2f) + 2f));

            Bill bill = Parliament.GetBill(_kingdom);
            var ok = new System.Text.StringBuilder(k).Append((int)_tab).Append('|').Append(_open).Append('|').Append(republic).Append('|');
            foreach (Policy p in Policies.All) ok.Append(p.shown(_kingdom) ? p.Get(_kingdom) : -1).Append(Parliament.Locked(_kingdom, p) ? 'L' : ',');
            if (bill != null) ok.Append('b').Append(bill.policy.id).Append(bill.option).Append(bill.player);
            Build(_policies, ok.ToString(), () => BuildPolicies(bill));

            VoteResult result = bill != null ? Parliament.Vote(_kingdom, bill, clans) : null;
            var bk = new System.Text.StringBuilder(clanKey);
            if (bill != null)
            {
                bk.Append('b').Append(bill.policy.id).Append(bill.option).Append(bill.player).Append(bill.proposer).Append('|').Append(bill.policy.Get(_kingdom)).Append('|').Append(result.yes).Append(':').Append(result.no);
                foreach (ParliamentClan pc in clans) bk.Append(result.votes.TryGetValue(pc.Key, out bool v) && v ? 'y' : 'n');
            }
            if (Build(_bill, bk.ToString(), () => BuildBill(clans, bill, result))) built = true;
            // 离表决还有几年：跨年只改这一行字
            if (bill != null && _billWhen != null)
            {
                string when = BillWhen(bill);
                if (_billWhen.text != when) _billWhen.text = when;
            }

            if (built || _appliedSelected != _selected) ApplySelection();
        }

        // 数据指纹变了就拆掉这一块重建，同一个国家时保留各个列表滚到的位置
        static bool Build(Section pSection, string pKey, Action pBuild)
        {
            if (pSection.key == pKey) return false;
            var scrolls = new Dictionary<string, float>();
            if (pSection.kingdom == _kingdom.id)
            {
                foreach (ScrollRect sr in pSection.root.GetComponentsInChildren<ScrollRect>()) scrolls[sr.name] = sr.content.anchoredPosition.y;
            }
            for (int i = pSection.root.childCount - 1; i >= 0; i--)
            {
                GameObject child = pSection.root.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
            pSection.dims.Clear();
            pSection.frames.Clear();
            if (pSection == _bill) _billWhen = null;
            pSection.key = pKey;
            pSection.kingdom = _kingdom.id;
            _section = pSection;
            _parent = pSection.root;
            pBuild();
            foreach (ScrollRect sr in pSection.root.GetComponentsInChildren<ScrollRect>())
            {
                if (!scrolls.TryGetValue(sr.name, out float y)) continue;
                float max = Mathf.Max(0f, sr.content.rect.height - ((RectTransform)sr.transform).rect.height);
                sr.content.anchoredPosition = new Vector2(sr.content.anchoredPosition.x, Mathf.Clamp(y, 0f, max));
            }
            return true;
        }

        // 选中一个氏族：其他氏族的议席和坐标点调淡，列表里给它加黄框
        static void ApplySelection()
        {
            _appliedSelected = _selected;
            foreach (Section s in _sections)
            {
                foreach (Dim d in s.dims)
                {
                    if (d.graphic == null) continue;
                    float a = _selected < 0 || _selected == d.clan ? 1f : d.alpha;
                    Color c = d.graphic.color;
                    d.graphic.color = new Color(c.r, c.g, c.b, a);
                    if (d.outline != null) d.outline.effectColor = new Color(Edge.r, Edge.g, Edge.b, a);
                }
                foreach (KeyValuePair<long, GameObject> f in s.frames)
                {
                    if (f.Value != null) f.Value.SetActive(f.Key == _selected);
                }
            }
        }

        static void Select(long pClan)
        {
            _selected = _selected == pClan ? -1 : pClan;
            Refresh();
            ScrollToSelected();
        }

        // 选中的氏族不在列表的可见范围里时，把列表滚过去
        static void ScrollToSelected()
        {
            if (_selected < 0 || !_clans.frames.TryGetValue(_selected, out GameObject frame) || frame == null) return;
            var row = (RectTransform)frame.transform.parent;
            var content = (RectTransform)row.parent;
            ScrollRect scroll = content.parent.GetComponent<ScrollRect>();
            float viewH = ((RectTransform)scroll.transform).rect.height;
            float top = -row.anchoredPosition.y, bottom = top + row.rect.height;
            float y = content.anchoredPosition.y;
            if (top < y) y = top;
            else if (bottom > y + viewH) y = bottom - viewH;
            scroll.StopMovement();
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, y);
        }

        // 不随数据变的部分：三个分区标题
        static void BuildFrame()
        {
            Header(LeftX, Top, LeftW, LM.Get("vd_parliament_seats"), IconPath);
            Header(RightX, Top, RightW, LM.Get("vd_parliament_policies"), "ui/Icons/iconKings");
            Header(RightX, BillY, RightW, LM.Get("vd_parliament_bill"), "ui/Icons/iconDiplomacy");
        }

        // ---------- 左边：议会 ----------

        const float LeftX = 18f, LeftW = 266f;
        const float RightX = 298f, RightW = 244f;
        const float Top = 26f;
        const int ListRows = 5;

        const float ListW = 176f, RowH = 15f, ListY = Top + 24f + 146f;
        const float PolicyY = Top + 43f, PolicyH = 112f, BillY = PolicyY + PolicyH + 4f;

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

        static string FactionKey(Faction pFaction) => "vd_faction_" + pFaction.ToString().ToLowerInvariant();

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
                if (pc.ruling) Dimmable(pc, Icon(slot, "ui/Icons/iconCrown", x - 5f, y - size / 2f - 10f, 10f), 0.3f);
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

        // ---------- 右边：政策和议案 ----------

        static void BuildPolicies(Bill bill)
        {
            // 页签：只显示有政策的
            var tabs = new List<PolicyTab>();
            foreach (PolicyTab t in new[] { PolicyTab.Domestic, PolicyTab.Military, PolicyTab.Diplomacy })
            {
                if (Policies.All.Exists(p => p.tab == t && p.shown(_kingdom))) tabs.Add(t);
            }
            if (!tabs.Contains(_tab) && tabs.Count > 0) _tab = tabs[0];
            float tw = (RightW - 4f * (tabs.Count - 1)) / Mathf.Max(1, tabs.Count);
            for (int i = 0; i < tabs.Count; i++)
            {
                PolicyTab t = tabs[i];
                RectTransform tab = Button(_parent, RightX + i * (tw + 4f), Top + 23f, tw, 16f,
                    t == _tab ? "ui/tab_button_sort_selected" : "ui/tab_button_sort", () =>
                    {
                        _tab = t;
                        Refresh();
                    });
                Text(tab, LM.Get("vd_policy_tab_" + t.ToString().ToLowerInvariant()), 0f, 0f, tw, 16f, 7, Color.white, TextAnchor.MiddleCenter);
            }

            // 政策列表
            var shown = Policies.All.FindAll(p => p.tab == _tab && p.shown(_kingdom));
            float contentH = 0f;
            foreach (Policy p in shown) contentH += (p.id == _open ? 42f : 22f) + 3f;
            RectTransform content = Scroll("scroll_policies", RightX, PolicyY, RightW, PolicyH, contentH);
            float y = 0f;
            foreach (Policy p in shown)
            {
                Policy policy = p;
                bool open = p.id == _open;
                float h = open ? 42f : 22f;
                RectTransform slot = Sliced(content, "ui/special/windowInnerSlicedRound", 0f, y, RightW - 8f, h);
                // 还没有效果的政策灰着，不能点开
                RectTransform head = p.active
                    ? Button(slot, 0f, 0f, RightW - 8f, 22f, null, () =>
                    {
                        _open = _open == policy.id ? null : policy.id;
                        Refresh();
                    })
                    : Box(slot, 0f, 0f, RightW - 8f, 22f);
                RectTransform icon = Icon(head, p.icon, 5f, 4f, 14f);
                if (!p.active) icon.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.4f);
                Text(head, LM.Get(p.id), 23f, 0f, 90f, 22f, 7, p.active ? Ink : Muted, TextAnchor.MiddleLeft);
                int current = p.Get(_kingdom);
                string currentName = LM.Get(p.OptionName(_kingdom, current));
                bool locked = Parliament.Locked(_kingdom, p);
                // 锁定的政策在现行选项里带一把锁
                float lockW = locked ? LockW + 2f : 0f;
                float pillW = Measure(currentName, 6) + 14f + lockW;
                RectTransform pill = Sliced(head, p.active ? "ui/special/special_buttonRed" : "ui/special/special_buttonGray",
                    RightW - 30f - pillW, 4f, pillW, 14f);
                Text(pill, currentName, 0f, 0f, pillW - lockW, 14f, 6, p.active ? Color.white : Muted, TextAnchor.MiddleCenter);
                if (locked) LockIconAt(pill, LockIcon, pillW - LockW - 5f, (14f - LockH) / 2f);
                if (p.active) Text(head, open ? "▲" : "▼", RightW - 24f, 0f, 12f, 22f, 6, Muted, TextAnchor.MiddleCenter);
                if (open)
                {
                    // 锁跟着玩家选中的那一档：这项政策上有玩家的议案就在议案那一档，否则在现行那一档
                    int lockOption = bill != null && bill.player && bill.policy == p ? bill.option : current;
                    float ow = (RightW - 8f - 23f - 6f - 4f * (p.count - 1)) / p.count;
                    for (int o = 0; o < p.count; o++)
                    {
                        int option = o;
                        bool isCurrent = o == current;
                        RectTransform ob = Button(slot, 23f + o * (ow + 4f), 23f, ow, 15f,
                            isCurrent ? "ui/special/special_buttonRed" : "ui/special/special_buttonGray", () => Pick(policy, option));
                        bool lockable = o == lockOption && p.CanPropose(_kingdom);
                        float textW = lockable ? ow - LockW - 7f : ow;
                        Text(ob, LM.Get(p.OptionName(_kingdom, o)), 0f, 0f, textW, 15f, 6, Color.white, TextAnchor.MiddleCenter);
                        if (bill != null && bill.policy == p && bill.option == o) Frame(ob, -1.5f);
                        OptionTip(ob, policy, option);
                        if (lockable) LockButton(ob, policy, option, ow, locked && isCurrent);
                    }
                }
                y += h + 3f;
            }
        }

        const string LockIcon = "ui/Icons/vd_lock";
        const string UnlockIcon = "ui/Icons/vd_unlock";
        const float LockW = 9f, LockH = 9f;

        static void LockIconAt(RectTransform pParent, string pSprite, float pX, float pY) =>
            UiKit.Picture(pParent, SpriteTextureLoader.getSprite(pSprite), pX, pY, LockW, LockH);

        // 选中的那一档右端的锁：锁上后议会不再提议改这项政策。点议案那一档上的锁，直接颁布再锁上
        static void LockButton(RectTransform pOption, Policy pPolicy, int pOptionIndex, float pW, bool pLocked)
        {
            bool enact = pPolicy.Get(_kingdom) != pOptionIndex;
            RectTransform lb = Button(pOption, pW - LockW - 9f, 0f, LockW + 9f, 15f, null, () =>
            {
                if (enact) Parliament.Enact(_kingdom, new Bill { policy = pPolicy, option = pOptionIndex, player = true });
                Parliament.SetLocked(_kingdom, pPolicy, !pLocked);
                Tooltip.hideTooltip();
                Refresh();
            });
            LockIconAt(lb, pLocked ? LockIcon : UnlockIcon, 2f, (15f - LockH) / 2f);
            var trigger = lb.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => Tooltip.show(lb.gameObject, "tip",
                new TooltipData { tip_name = pLocked ? "vd_policy_unlock" : "vd_policy_lock", tip_description = enact ? "vd_policy_lock_enact_info" : "vd_policy_lock_info" }));
            trigger.triggers.Add(enter);
            var exit = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => Tooltip.hideTooltip());
            trigger.triggers.Add(exit);
        }

        // 共和国的说明里没有王冠，换成国库
        static string DescriptionKey(Policy pPolicy, int pOption)
        {
            string key = pPolicy.OptionKey(pOption) + "_info";
            if (Government.IsRepublic(_kingdom) && LocalizedTextManager.stringExists(key + "_republic")) key += "_republic";
            return key;
        }

        // 点选项：提出议案，顶替原来的议案；点现行选项撤回这项政策上的议案
        static void Pick(Policy pPolicy, int pOption)
        {
            if (!pPolicy.CanPropose(_kingdom)) return;
            Bill old = Parliament.GetBill(_kingdom);
            if (pPolicy.Get(_kingdom) == pOption)
            {
                if (old != null && old.policy == pPolicy) Parliament.ClearBill(_kingdom);
                Refresh();
                return;
            }
            Parliament.SetLocked(_kingdom, pPolicy, false);
            Parliament.SetBill(_kingdom, new Bill { policy = pPolicy, option = pOption, player = true });
            Clan rival = old != null && !old.player ? ProposerClan(old) : null;
            if (rival != null && (old.policy != pPolicy || old.option != pOption))
                WorldTip.showNow(LM.Get("vd_parliament_replaced").Replace("$clan$", rival.name), false, "top");
            Refresh();
        }

        static Clan ProposerClan(Bill pBill)
        {
            if (pBill.proposer < 0) return null;
            Clan clan = World.world.clans.get(pBill.proposer);
            return clan != null && !clan.isRekt() ? clan : null;
        }

        // 标题右边：哪个氏族提的（玩家提的不写）、离表决还有几年
        static string BillWhen(Bill pBill)
        {
            int years = Mathf.Max(0, Parliament.NextSession(_kingdom) - Date.getCurrentYear());
            string when = LM.Get("vd_parliament_vote_in").Replace("$years$", years.ToString());
            Clan proposer = pBill.player ? null : ProposerClan(pBill);
            return proposer != null ? LM.Get("vd_parliament_proposed_by").Replace("$clan$", proposer.name) + " · " + when : when;
        }

        static void BuildBill(List<ParliamentClan> pClans, Bill pBill, VoteResult result)
        {
            float y = BillY + 23f;
            if (pBill == null)
            {
                RectTransform msg = Sliced(_parent, "ui/special/windowInnerSlicedRound", RightX, y, RightW, 18f);
                Text(msg, LM.Get("vd_parliament_no_bill"), 8f, 0f, RightW - 16f, 18f, 7, Color.white, TextAnchor.MiddleLeft);
                Text(_parent, LM.Get("vd_parliament_no_bill_hint"), RightX + 3f, y + 21f, RightW - 6f, 10f, 6, Muted, TextAnchor.MiddleLeft);
                return;
            }

            RectTransform title = Sliced(_parent, "ui/special/windowInnerSlicedRound", RightX, y, RightW, 18f);
            string titleText = LM.Get(pBill.policy.id) + LM.Get("vd_parliament_colon") + LM.Get(pBill.policy.OptionName(_kingdom, pBill.policy.Get(_kingdom)))
                               + " → " + LM.Get(pBill.policy.OptionName(_kingdom, pBill.option));
            Text(title, titleText, 7f, 0f, RightW - 14f, 18f, 7, Ink, TextAnchor.MiddleLeft);
            _billWhen = Text(title, BillWhen(pBill), 7f, 0f, RightW - 14f, 18f, 6, Muted, TextAnchor.MiddleRight).GetComponent<Text>();
            y += 20f;

            RectTransform bar = Sliced(_parent, "ui/special/windowInnerSlicedRound", RightX, y, RightW, 16f);
            float inner = RightW - 6f;
            float yesW = result.Total > 0 ? inner * result.yes / result.Total : 0f;
            Rect(bar, 3f, 3f, yesW, 10f, YesBar);
            Rect(bar, 3f + yesW, 3f, inner - yesW, 10f, NoBar);
            Rect(bar, 3f + inner * pBill.policy.threshold - 0.5f, 1f, 1f, 14f, Ink);
            Text(bar, LM.Get("vd_parliament_yes") + " " + result.yes, 7f, 0f, 80f, 16f, 6, Color.white, TextAnchor.MiddleLeft);
            Text(bar, LM.Get("vd_parliament_no") + " " + result.no, RightW - 87f, 0f, 80f, 16f, 6, Color.white, TextAnchor.MiddleRight);
            y += 18f;

            List<ParliamentClan> list = RulingFirst(pClans);
            const float rowH = 11f;
            // 按钮贴着窗口底部，名单占满中间
            float buttonsY = H - 30f;
            RectTransform votes = Scroll("scroll_votes", RightX, y, RightW, buttonsY - 3f - y, list.Count * rowH);
            float ry = 0f;
            foreach (ParliamentClan pc in list)
            {
                bool yes = result.votes.TryGetValue(pc.Key, out bool v) && v;
                Swatch(votes, 6f, ry + 3f, 5f, FactionColor(pc.faction), 1f);
                string suffix = pc.cadet ? " " + LM.Get("vd_parliament_cadet") : "";
                string name = UiKit.Fit(pc.clan.name, 6, RightW - 102f - Measure(suffix, 6)) + suffix;
                Text(votes, name, 16f, ry, RightW - 102f, rowH, 6, Ink, TextAnchor.MiddleLeft);
                Text(votes, pc.seats.ToString(), RightW - 82f, ry, 28f, rowH, 6, Gold, TextAnchor.MiddleRight);
                Icon(votes, yes ? "ui/Icons/iconMoodHappy" : "ui/Icons/iconMoodAngry", RightW - 50f, ry + 1.5f, 8f);
                Text(votes, LM.Get(yes ? "vd_parliament_yes" : "vd_parliament_no"), RightW - 40f, ry, 30f, rowH, 6, yes ? YesText : NoText, TextAnchor.MiddleRight);
                ry += rowH;
            }
            y = buttonsY;

            float bw = (RightW - 50f - 8f) / 2f;
            RectTransform vote = Button(_parent, RightX, y, bw, 18f, "ui/special/special_buttonRed", () =>
            {
                Bill b = Parliament.GetBill(_kingdom);
                if (b != null) Parliament.VoteNow(_kingdom, b);
                Refresh();
            });
            Text(vote, LM.Get("vd_parliament_submit"), 0f, 0f, bw, 18f, 7, Color.white, TextAnchor.MiddleCenter);
            RectTransform force = Button(_parent, RightX + bw + 4f, y, bw, 18f, "ui/special/special_buttonGray", () =>
            {
                Bill b = Parliament.GetBill(_kingdom);
                if (b != null) Parliament.Enact(_kingdom, b);
                Refresh();
            });
            Text(force, LM.Get("vd_parliament_force"), 0f, 0f, bw, 18f, 7, Color.white, TextAnchor.MiddleCenter);
            RectTransform withdraw = Button(_parent, RightX + 2f * bw + 8f, y, 50f, 18f, "ui/special/special_buttonGray", () =>
            {
                Parliament.ClearBill(_kingdom);
                Refresh();
            });
            Text(withdraw, LM.Get("vd_parliament_withdraw"), 0f, 0f, 50f, 18f, 7, Color.white, TextAnchor.MiddleCenter);
        }

        static void Header(float pX, float pY, float pW, string pText, string pIcon) => UiKit.Header(_parent, pX, pY, pW, pText, pIcon);

        static RectTransform Scroll(string pName, float pX, float pY, float pW, float pH, float pContentH) =>
            UiKit.Scroll(_parent, pName, pX, pY, pW, pH, pContentH);

        // 正在悬停的政策选项和它的数字行，由 Patch_PolicyTip 加进原版提示
        internal static string TipName;
        internal static List<(string key, string value)> TipEffects;
        // 提出这一档时议会的表决预测；现行选项没有
        internal static Bill TipBill;
        internal static VoteResult TipVote;

        // 政策选项的悬停提示：一句说明，下面是这一档的数字
        static void OptionTip(RectTransform pTarget, Policy pPolicy, int pOption)
        {
            var trigger = pTarget.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                TipName = pPolicy.OptionName(_kingdom, pOption);
                TipEffects = Policies.Effects(_kingdom, pPolicy, pOption);
                TipBill = pPolicy.Get(_kingdom) != pOption ? new Bill { policy = pPolicy, option = pOption } : null;
                TipVote = TipBill != null ? Parliament.Vote(_kingdom, TipBill, Parliament.Clans(_kingdom)) : null;
                Tooltip.show(pTarget.gameObject, "tip", new TooltipData { tip_name = TipName, tip_description = DescriptionKey(pPolicy, pOption) });
            });
            trigger.triggers.Add(enter);
            var exit = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => Tooltip.hideTooltip());
            trigger.triggers.Add(exit);
        }
    }

    // 政策选项的提示：在原版属性栏里列出这一档的数字
    [HarmonyLib.HarmonyPatch(typeof(TooltipLibrary), "showTip")]
    static class Patch_PolicyTip
    {
        static void Postfix(Tooltip pTooltip, TooltipData pData)
        {
            if (pData == null || pData.tip_name != ParliamentWindow.TipName || ParliamentWindow.TipEffects == null) return;
            foreach (var (key, value) in ParliamentWindow.TipEffects) pTooltip.addLineText(key, value);
            VoteResult vote = ParliamentWindow.TipVote;
            if (vote != null && vote.Total > 0)
            {
                bool passes = Parliament.Passes(ParliamentWindow.TipBill, vote);
                pTooltip.addLineText("vd_tip_votes", vote.yes + " : " + vote.no);
                pTooltip.addLineText("vd_tip_vote", LM.Get(passes ? "vd_parliament_will_pass" : "vd_parliament_will_fail"),
                    "#" + ColorUtility.ToHtmlStringRGB(passes ? ParliamentWindow.YesText : ParliamentWindow.NoText));
            }
            else if (ParliamentWindow.TipEffects.Count == 0) return;
            pTooltip.showStatValues();
        }
    }

    // 左下角的王国取消按钮显示或收起时，右下角的议会按钮跟着
    [HarmonyPatch(typeof(PowerButtonSelector), "updateSelectedUnitCancelButton")]
    static class Patch_SelectedCancelButton
    {
        static void Postfix() => ParliamentWindow.UpdateSide();
    }
}
