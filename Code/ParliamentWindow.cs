using System;
using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DiplomacyOverhaul
{
    // 议会窗口：从王国窗口左侧页签栏打开。左边是议席和氏族，右边是政策和议案。
    // 界面用原版的窗口贴图拼出来，分成几块，每块只在自己用到的数据变了时重建；选中氏族只改样式
    public static class ParliamentWindow
    {
        const string WindowId = "vd_parliament";
        const string ButtonName = "vd_parliament_button";
        const string TabsName = "vd_parliament_tabs";
        const string SideName = "vd_parliament_side";
        const string KingdomWindowId = "kingdom";
        public const string IconPath = "ui/Icons/vd_parliament";
        const float W = 560f, H = 330f;

        static readonly Color Ink = Hex("#f4f1e8");
        static readonly Color Muted = Hex("#cfc8b4");
        static readonly Color Gold = Hex("#ffb52e");
        static readonly Color HeaderGold = Hex("#ffa827");
        static readonly Color Line = Hex("#5d6253");
        static readonly Color Edge = Hex("#1a1a18");
        static readonly Color YesBar = Hex("#1e90d6");
        static readonly Color NoBar = Hex("#d5491b");
        static readonly Color YesText = Hex("#5fb8f0");
        static readonly Color NoText = Hex("#ff7a4a");

        static ScrollWindow _window;
        static RectTransform _root;
        static Kingdom _kingdom;
        static long _selected = -1;
        static PolicyTab _tab = PolicyTab.Domestic;
        static string _open;
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

        static Color Hex(string pHex)
        {
            ColorUtility.TryParseHtmlString(pHex, out Color c);
            return c;
        }

        // 热重载：旧版本建的窗口和按钮引用着旧代码，全部拆掉，用到时重建
        public static void Init()
        {
            if (ScrollWindow._all_windows.TryGetValue(WindowId, out ScrollWindow old) && old != null)
            {
                // 开着的窗口直接拆掉会让原版的窗口切换卡住，先关掉
                if (old.gameObject.activeSelf) ScrollWindow.hideAllEvent(false);
                Object.Destroy(old.gameObject);
            }
            ScrollWindow._all_windows.Remove(WindowId);
            if (AssetManager.window_library.dict.TryGetValue(WindowId, out WindowAsset asset))
            {
                AssetManager.window_library.dict.Remove(WindowId);
                AssetManager.window_library.list.Remove(asset);
            }
            foreach (Button b in Resources.FindObjectsOfTypeAll<Button>())
            {
                if (b != null && b.name == ButtonName) Object.DestroyImmediate(b.gameObject);
            }
            foreach (RectTransform t in Resources.FindObjectsOfTypeAll<RectTransform>())
            {
                if (t != null && t.name == SideName) Object.DestroyImmediate(t.gameObject);
            }
            _side = null;
            _window = null;
            _root = null;
            // 按钮平时在王国窗口显示概览页时加上，重开窗口可能直接停在别的页签，这里马上加回去
            if (ScrollWindow._all_windows.TryGetValue(KingdomWindowId, out ScrollWindow kingdomWindow) && kingdomWindow != null)
            {
                KingdomWindow tabs = kingdomWindow.GetComponent<KingdomWindow>();
                if (tabs != null) EnsureButton(tabs);
            }
        }

        // ---------- 王国窗口里的按钮 ----------

        // 王国窗口左侧页签栏，排在第一个页签（概览）后面。照原版的页签复制一个，
        // 去掉页签组件：别的页签在窗口里换内容，议会内容太宽，点了打开单独的窗口
        public static void EnsureButton(KingdomWindow pWindow)
        {
            Transform tabs = pWindow.transform.Find("Background/Tabs");
            if (tabs == null || tabs.Find(ButtonName) != null) return;
            Transform template = tabs.Find("Villages");
            if (template == null) return;
            GameObject go = Object.Instantiate(template.gameObject, tabs);
            go.name = ButtonName;
            go.transform.SetSiblingIndex(1);
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c is WindowMetaTab || c is DragOrderElement || c is ScrollableButton || c is UnityEngine.EventSystems.EventTrigger) Object.DestroyImmediate(c);
            }
            go.GetComponent<Image>().sprite = SpriteTextureLoader.getSprite("ui/tab_button_vertical");
            Image icon = go.transform.Find("Icon")?.GetComponent<Image>();
            if (icon != null) icon.sprite = SpriteTextureLoader.getSprite(IconPath);
            TipButton tip = go.GetComponent<TipButton>();
            if (tip != null)
            {
                tip.textOnClick = "vd_parliament";
                tip.textOnClickDescription = "vd_parliament_description";
                tip.text_description_2 = "";
            }
            Button button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => Open(SelectedMetas.selected_kingdom));
            // 原版这一栏有两个挨着的空位，收掉一个，页签栏保持原来的长度
            tabs.Find("space (2)")?.gameObject.SetActive(false);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)tabs);
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
            Create();
            BuildTabs();
            LM.AddToCurrentLocale("vd_parliament_window_title", _kingdom.data.name);
            _window.titleText.GetComponent<LocalizedText>().setKeyAndUpdate("vd_parliament_window_title");
            ScrollWindow.showWindow(WindowId);
            Refresh();
        }

        static void Create()
        {
            if (_window != null) return;
            _window = WindowCreator.CreateEmptyWindow(WindowId, WindowId);
            // 新建的窗口会先播一段收起动画，动画没完原版不接受打开窗口，直接放完
            ScrollWindow.finishAnimations();
            Transform background = _window.transform.Find("Background");
            background.Find("Scroll View")?.gameObject.SetActive(false);
            RectTransform bg = background.GetComponent<RectTransform>();
            bg.sizeDelta = new Vector2(W, H);
            Image bgImage = background.GetComponent<Image>();
            bgImage.type = Image.Type.Sliced;
            Transform close = _window.transform.Find("CloseBackground");
            if (close != null) close.localPosition = new Vector3(W / 2f - 18f, H / 2f + 4f);
            _window.titleText.transform.localPosition = new Vector3(0f, H / 2f - 2f);

            var root = new GameObject("vd_parliament_root", typeof(RectTransform));
            _root = root.GetComponent<RectTransform>();
            _root.SetParent(background, false);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = new Vector2(W, H);
            _root.anchoredPosition = Vector2.zero;
            _sections = new[] { _frame = NewSection("frame"), _seats = NewSection("seats"), _clans = NewSection("clans"), _compass = NewSection("compass"),
                _detail = NewSection("detail"), _policies = NewSection("policies"), _bill = NewSection("bill") };
            _window.gameObject.AddComponent<ParliamentWindowTicker>();
        }

        // 左侧页签栏：照王国窗口的页签栏复制一份，议会那格选中，点别的格回到王国窗口的那一页。
        // 每次打开都重新复制，跟着王国窗口当前显示哪些页签
        static void BuildTabs()
        {
            Transform background = _window.transform.Find("Background");
            Transform old = background.Find(TabsName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            if (!ScrollWindow._all_windows.TryGetValue(KingdomWindowId, out ScrollWindow kingdomWindow) || kingdomWindow == null) return;
            // 热重载后王国窗口还没重新打开过，议会按钮还没加回去
            KingdomWindow kingdomTabs = kingdomWindow.GetComponent<KingdomWindow>();
            if (kingdomTabs != null) EnsureButton(kingdomTabs);
            RectTransform source = kingdomWindow.transform.Find("Background/Tabs") as RectTransform;
            if (source == null) return;

            // 先复制到一个没激活的节点下，原版页签组件不会初始化，拆掉后再挂到窗口上
            var holder = new GameObject("vd_parliament_tabs_holder");
            holder.SetActive(false);
            GameObject go = Object.Instantiate(source.gameObject, holder.transform);
            go.name = TabsName;
            foreach (Component c in go.GetComponentsInChildren<Component>(true))
            {
                if (c is PlatformRemover) Object.DestroyImmediate(c);
            }
            foreach (Component c in go.GetComponentsInChildren<Component>(true))
            {
                if (c is WindowMetaTab || c is WindowMetaTabButtonsContainer || c is DragOrderElement || c is ScrollableButton || c is UnityEngine.EventSystems.EventTrigger) Object.DestroyImmediate(c);
            }
            foreach (Transform t in go.transform)
            {
                Button button = t.GetComponent<Button>();
                if (button == null) continue;
                bool self = t.name == ButtonName;
                t.GetComponent<Image>().sprite = SpriteTextureLoader.getSprite(self ? "ui/tab_button_vertical_selected" : "ui/tab_button_vertical");
                button.onClick = new Button.ButtonClickedEvent();
                string tab = t.name;
                if (!self) button.onClick.AddListener(() => OpenKingdomTab(tab));
            }

            // 和王国窗口里一样，贴着窗口左上角
            RectTransform from = (RectTransform)source.parent;
            RectTransform to = (RectTransform)background;
            go.transform.SetParent(background, false);
            Vector3 offset = source.localPosition - new Vector3(from.rect.xMin, from.rect.yMax);
            go.transform.localPosition = new Vector3(to.rect.xMin, to.rect.yMax) + offset;
            Object.Destroy(holder);
        }

        static void OpenKingdomTab(string pTab)
        {
            if (_kingdom == null || !_kingdom.isAlive()) return;
            SelectedMetas.selected_kingdom = _kingdom;
            ScrollWindow.showWindow(KingdomWindowId);
            if (ScrollWindow._all_windows.TryGetValue(KingdomWindowId, out ScrollWindow kingdomWindow) && kingdomWindow != null)
                kingdomWindow.tabs.showTab(pTab);
        }

        static Section NewSection(string pName)
        {
            RectTransform rt = Box(_root, 0f, 0f, W, H, "vd_section_" + pName);
            return new Section { root = rt };
        }

        internal static void Tick()
        {
            if (_window == null || !_window.gameObject.activeInHierarchy) return;
            if (_kingdom == null || !_kingdom.isAlive())
            {
                _window.clickHide();
                return;
            }
            Refresh();
        }

        // 每块算一个数据指纹，和上次不同才重建那一块
        static void Refresh()
        {
            if (_root == null || _kingdom == null) return;
            List<ParliamentClan> clans = Parliament.Clans(_kingdom);
            if (_selected >= 0 && clans.Find(c => c.clan.id == _selected) == null) _selected = -1;
            bool republic = Government.IsRepublic(_kingdom);
            string k = _kingdom.id + "|";

            var ck = new System.Text.StringBuilder(k);
            foreach (ParliamentClan pc in clans)
                ck.Append(pc.clan.id).Append(':').Append(pc.clan.name).Append(':').Append(pc.seats).Append(':').Append((int)pc.faction).Append(':').Append(pc.ruling).Append('|');
            string clanKey = ck.ToString();

            bool built = Build(_frame, "frame", BuildFrame);
            built |= Build(_seats, clanKey + (_kingdom.countCities() >= 3), () =>
            {
                if (_kingdom.countCities() >= 3 && clans.Count > 0) Hemicycle(clans, LeftX, Top + 24f);
                else Banner(clans, LeftX, Top + 24f);
            });
            built |= Build(_clans, clanKey + republic, () => ClanList(clans));

            var pk = new System.Text.StringBuilder(clanKey);
            foreach (ParliamentClan pc in clans) pk.Append(pc.x.ToString("F2")).Append(',').Append(pc.y.ToString("F2")).Append('|');
            built |= Build(_compass, pk.ToString(), () => Compass(clans, LeftX + ListW + 4f, ListY, LeftW - ListW - 4f, ListRows * (RowH + 2f) - 2f));

            string detail = DetailText(clans);
            Build(_detail, k + detail, () => Detail(detail, LeftX, ListY + ListRows * (RowH + 2f) + 2f));

            Bill bill = Parliament.GetBill(_kingdom);
            var ok = new System.Text.StringBuilder(k).Append((int)_tab).Append('|').Append(_open).Append('|').Append(republic).Append('|');
            foreach (Policy p in Policies.All) ok.Append(p.shown(_kingdom) ? p.Get(_kingdom) : -1).Append(',');
            if (bill != null) ok.Append('b').Append(bill.policy.id).Append(bill.option);
            Build(_policies, ok.ToString(), () => BuildPolicies(bill));

            VoteResult result = bill != null ? Parliament.Vote(_kingdom, bill, clans) : null;
            var bk = new System.Text.StringBuilder(clanKey);
            if (bill != null)
            {
                bk.Append('b').Append(bill.policy.id).Append(bill.option).Append('|').Append(bill.policy.Get(_kingdom)).Append('|').Append(result.yes).Append(':').Append(result.no);
                foreach (ParliamentClan pc in clans) bk.Append(result.votes.TryGetValue(pc.clan, out bool v) && v ? 'y' : 'n');
            }
            if (Build(_bill, bk.ToString(), () => BuildBill(clans, bill, result))) built = true;
            // 离表决还有几年：跨年只改这一行字
            if (bill != null && _billWhen != null)
            {
                string when = BillWhen(bill, result);
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
                long id = pc.clan.id;
                RectTransform row = Button(content, 0f, ry, ListW - 6f, RowH, "ui/special/windowInnerSlicedRound", () => Select(id));
                Swatch(row, 6f, (RowH - 6f) / 2f, 6f, FactionColor(pc.faction), 1f);
                Text(row, pc.clan.name, 16f, 0f, 90f, RowH, 7, Ink, TextAnchor.MiddleLeft);
                if (pc.ruling) Text(row, LM.Get(Government.IsRepublic(_kingdom) ? "vd_parliament_ruling" : "vd_parliament_royal"), 16f + Measure(pc.clan.name, 7) + 3f, 0f, 30f, RowH, 6, Gold, TextAnchor.MiddleLeft);
                Text(row, LM.Get(FactionKey(pc.faction)), 108f, 0f, 36f, RowH, 6, Muted, TextAnchor.MiddleLeft);
                Text(row, pc.seats.ToString(), ListW - 40f, 0f, 28f, RowH, 7, Gold, TextAnchor.MiddleRight);
                _section.frames[id] = Frame(row, -1f).gameObject;
                ry += RowH + 2f;
            }
        }

        static List<ParliamentClan> RulingFirst(List<ParliamentClan> pClans)
        {
            var list = new List<ParliamentClan>(pClans);
            int i = list.FindIndex(c => c.ruling);
            if (i > 0)
            {
                ParliamentClan r = list[i];
                list.RemoveAt(i);
                list.Insert(0, r);
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
                    owners[s] = pc.clan.id;
                }
            }
            SeatClicks(area, seats, owners, size);

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
            Text(area, LM.Get("vd_parliament_radical"), 0f, ah + 1f, 40f, 9f, 6, Muted, TextAnchor.MiddleLeft);
            Text(area, LM.Get("vd_parliament_conservative"), aw - 40f, ah + 1f, 40f, 9f, 6, Muted, TextAnchor.MiddleRight);
        }

        // 点半圆里的议席，选中它所属的氏族：找离点击位置最近的议席
        static void SeatClicks(RectTransform pArea, List<Vector3> pSeats, long[] pOwners, float pSize)
        {
            Image hit = pArea.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
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

        // 一、两座城的国家不画半圆：「某某家独掌朝政」「某某家主导朝政」
        static void Banner(List<ParliamentClan> pClans, float pX, float pY)
        {
            RectTransform area = Box(_parent, pX, pY, LeftW, 132f);
            Icon(area, "ui/Icons/iconKings", (LeftW - 28f) / 2f, 36f, 28f);
            ParliamentClan top = null;
            foreach (ParliamentClan pc in pClans)
            {
                if (top == null || pc.seats > top.seats) top = pc;
            }
            if (top == null) return;
            string key = pClans.Count == 1 ? "vd_parliament_sole" : "vd_parliament_dominant";
            string text = LM.Get(key).Replace("$clan$", $"<color=#{ColorUtility.ToHtmlStringRGB(FactionColor(top.faction))}>{top.clan.name}</color>");
            Text(area, text, 0f, 72f, LeftW, 20f, 12, Ink, TextAnchor.MiddleCenter);
        }

        // 半圆的排法：试 1–16 排，挑方块沿弧和沿半径的间距最接近的那种；每排的席位数和半径成正比
        static List<Vector3> SeatLayout(int pCount, float pW, float pH, out float pSize)
        {
            var result = new List<Vector3>();
            pSize = 6f;
            if (pCount <= 0) return result;
            float cx = pW / 2f, cy = pH - 3f, rOut = pW / 2f - 4f, rIn = rOut * 0.465f;
            float[] bestRadii = null;
            float bestScore = float.MaxValue;
            for (int rows = 1; rows <= 16; rows++)
            {
                var radii = new float[rows];
                float sum = 0f;
                for (int i = 0; i < rows; i++)
                {
                    radii[i] = rows == 1 ? rOut - 5f : rIn + (rOut - rIn) * i / (rows - 1);
                    sum += radii[i];
                }
                float along = Mathf.PI * sum / pCount;
                float radial = rows == 1 ? rOut - rIn : (rOut - rIn) / (rows - 1);
                float score = Mathf.Abs(Mathf.Log(radial / along));
                if (score < bestScore)
                {
                    bestScore = score;
                    bestRadii = radii;
                    pSize = Mathf.Clamp(Mathf.Min(along, radial) * 0.74f, 3.5f, 9f);
                }
            }
            float total = 0f;
            foreach (float r in bestRadii) total += r;
            var counts = new int[bestRadii.Length];
            var rest = new List<KeyValuePair<float, int>>();
            int left = pCount;
            for (int i = 0; i < bestRadii.Length; i++)
            {
                float raw = pCount * bestRadii[i] / total;
                counts[i] = Mathf.FloorToInt(raw);
                left -= counts[i];
                rest.Add(new KeyValuePair<float, int>(raw - counts[i], i));
            }
            rest.Sort((a, b) => b.Key.CompareTo(a.Key));
            for (int i = 0; i < rest.Count && left > 0; i++, left--) counts[rest[i].Value]++;
            var pos = new List<(float t, float r, Vector3 p)>();
            for (int ri = 0; ri < bestRadii.Length; ri++)
            {
                int k = counts[ri];
                float r = bestRadii[ri];
                for (int j = 0; j < k; j++)
                {
                    float t = k == 1 ? Mathf.PI / 2f : Mathf.PI * (1f - (float)j / (k - 1));
                    pos.Add((t, r, new Vector3(cx + r * Mathf.Cos(t), cy - r * Mathf.Sin(t))));
                }
            }
            pos.Sort((a, b) => a.t != b.t ? b.t.CompareTo(a.t) : a.r.CompareTo(b.r));
            foreach (var p in pos) result.Add(p.p);
            return result;
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
            float rx = pW / 2f - 8f, ry = pH / 2f - 12f;
            foreach (ParliamentClan pc in pClans)
            {
                float size = 3f + Mathf.Sqrt(pc.seats) * 0.45f;
                float x = cx - pc.x * rx, y = cy - pc.y * ry;
                Dimmable(pc, Swatch(slot, x - size / 2f, y - size / 2f, size, FactionColor(pc.faction), 1f), 0.3f);
                if (pc.ruling) Dimmable(pc, Icon(slot, "ui/Icons/iconCrown", x - 5f, y - size / 2f - 10f, 10f), 0.3f);
            }
        }

        static void Dimmable(ParliamentClan pClan, RectTransform pTarget, float pAlpha)
        {
            _section.dims.Add(new Dim { clan = pClan.clan.id, graphic = pTarget.GetComponent<Graphic>(), outline = pTarget.GetComponent<Outline>(), alpha = pAlpha });
        }

        // 选中的氏族：族长和占的城
        static string DetailText(List<ParliamentClan> pClans)
        {
            ParliamentClan pc = pClans.Find(c => c.clan.id == _selected);
            if (pc == null) return "";
            var parts = new List<string>();
            Actor chief = pc.clan.getChief();
            if (chief != null && chief.isAlive()) parts.Add($"{LM.Get("vd_parliament_chief")} <color=#8fb0ff>{chief.getName()}</color>");
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
                if (bill != null && bill.policy.tab == t) Swatch(tab, tw - 12f, 4f, 5f, Hex("#ffd23a"), 1f);
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
                string currentName = LM.Get(p.OptionKey(current));
                float pillW = Measure(currentName, 6) + 14f;
                RectTransform pill = Sliced(head, p.active ? "ui/special/special_buttonRed" : "ui/special/special_buttonGray",
                    RightW - 30f - pillW, 4f, pillW, 14f);
                Text(pill, currentName, 0f, 0f, pillW, 14f, 6, p.active ? Color.white : Muted, TextAnchor.MiddleCenter);
                if (bill != null && bill.policy == p) Frame(pill, -1.5f);
                if (p.active) Text(head, open ? "▲" : "▼", RightW - 24f, 0f, 12f, 22f, 6, Muted, TextAnchor.MiddleCenter);
                if (open)
                {
                    float ow = (RightW - 8f - 23f - 6f - 4f * (p.count - 1)) / p.count;
                    for (int o = 0; o < p.count; o++)
                    {
                        int option = o;
                        bool isCurrent = o == current;
                        RectTransform ob = Button(slot, 23f + o * (ow + 4f), 23f, ow, 15f,
                            isCurrent ? "ui/special/special_buttonRed" : "ui/special/special_buttonGray", () => Pick(policy, option));
                        Text(ob, LM.Get(p.OptionKey(o)), 0f, 0f, ow, 15f, 6, Color.white, TextAnchor.MiddleCenter);
                        if (bill != null && bill.policy == p && bill.option == o) Frame(ob, -1.5f);
                        Tip(ob, p.OptionKey(o), DescriptionKey(p, o));
                    }
                }
                y += h + 3f;
            }
        }

        // 共和国的说明里没有王冠，换成国库
        static string DescriptionKey(Policy pPolicy, int pOption)
        {
            string key = pPolicy.OptionKey(pOption) + "_info";
            if (Government.IsRepublic(_kingdom) && LocalizedTextManager.stringExists(key + "_republic")) key += "_republic";
            return key;
        }

        // 点选项：选了现行选项就撤回议案，否则提出议案
        static void Pick(Policy pPolicy, int pOption)
        {
            if (!pPolicy.CanPropose(_kingdom)) return;
            if (pPolicy.Get(_kingdom) == pOption) Parliament.ClearBill(_kingdom);
            else Parliament.SetBill(_kingdom, new Bill { policy = pPolicy, option = pOption, player = true });
            Refresh();
        }

        static string BillWhen(Bill pBill, VoteResult pResult)
        {
            bool passes = Parliament.Passes(pBill, pResult);
            int years = Mathf.Max(0, Parliament.NextSession(_kingdom) - Date.getCurrentYear());
            string verdict = $"<color=#{ColorUtility.ToHtmlStringRGB(passes ? YesText : NoText)}>{LM.Get(passes ? "vd_parliament_will_pass" : "vd_parliament_will_fail")}</color>";
            return LM.Get("vd_parliament_vote_in").Replace("$years$", years.ToString()) + " · " + verdict;
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
            string titleText = LM.Get(pBill.policy.id) + LM.Get("vd_parliament_colon") + LM.Get(pBill.policy.OptionKey(pBill.policy.Get(_kingdom)))
                               + " → " + LM.Get(pBill.policy.OptionKey(pBill.option));
            Text(title, titleText, 7f, 0f, RightW - 14f, 18f, 7, Ink, TextAnchor.MiddleLeft);
            _billWhen = Text(title, BillWhen(pBill, result), 7f, 0f, RightW - 14f, 18f, 6, Muted, TextAnchor.MiddleRight).GetComponent<Text>();
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
                bool yes = result.votes.TryGetValue(pc.clan, out bool v) && v;
                Swatch(votes, 6f, ry + 3f, 5f, FactionColor(pc.faction), 1f);
                Text(votes, pc.clan.name, 16f, ry, 120f, rowH, 6, Ink, TextAnchor.MiddleLeft);
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

        // ---------- 拼界面的小工具：坐标都从父节点左上角算，y 向下 ----------

        static RectTransform Box(Transform pParent, float pX, float pY, float pW, float pH, string pName = "box")
        {
            var go = new GameObject(pName, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(pParent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(pX, -pY);
            rt.sizeDelta = new Vector2(pW, pH);
            return rt;
        }

        static RectTransform Sliced(Transform pParent, string pSprite, float pX, float pY, float pW, float pH)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "slot");
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = SpriteTextureLoader.getSprite(pSprite);
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return rt;
        }

        static RectTransform Rect(Transform pParent, float pX, float pY, float pW, float pH, Color pColor)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "rect");
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = pColor;
            img.raycastTarget = false;
            return rt;
        }

        // 带深色描边的色块：议席、氏族的派系色
        static RectTransform Swatch(Transform pParent, float pX, float pY, float pSize, Color pColor, float pAlpha)
        {
            RectTransform rt = Rect(pParent, pX, pY, pSize, pSize, new Color(pColor.r, pColor.g, pColor.b, pAlpha));
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(Edge.r, Edge.g, Edge.b, pAlpha);
            outline.effectDistance = new Vector2(0.5f, -0.5f);
            return rt;
        }

        static RectTransform Icon(Transform pParent, string pSprite, float pX, float pY, float pSize)
        {
            RectTransform rt = Box(pParent, pX, pY, pSize, pSize, "icon");
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = SpriteTextureLoader.getSprite(pSprite);
            img.preserveAspect = true;
            img.raycastTarget = false;
            return rt;
        }

        // 选中的黄框
        static RectTransform Frame(RectTransform pTarget, float pInset)
        {
            RectTransform rt = Sliced(pTarget, "ui/special/buttonSelected", 0f, 0f, 0f, 0f);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pInset, pInset);
            rt.offsetMax = new Vector2(-pInset, -pInset);
            rt.GetComponent<Image>().fillCenter = false;
            return rt;
        }

        static RectTransform Button(Transform pParent, float pX, float pY, float pW, float pH, string pSprite, Action pClick)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "button");
            Image img = rt.gameObject.AddComponent<Image>();
            if (pSprite != null)
            {
                img.sprite = SpriteTextureLoader.getSprite(pSprite);
                img.type = Image.Type.Sliced;
            }
            else img.color = new Color(0f, 0f, 0f, 0f);
            var button = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => pClick());
            return rt;
        }

        static void Tip(RectTransform pTarget, string pTitle, string pDescription)
        {
            var trigger = pTarget.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => Tooltip.show(pTarget.gameObject, "tip", new TooltipData { tip_name = pTitle, tip_description = pDescription }));
            trigger.triggers.Add(enter);
            var exit = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => Tooltip.hideTooltip());
            trigger.triggers.Add(exit);
        }

        static RectTransform Text(Transform pParent, string pText, float pX, float pY, float pW, float pH, int pSize, Color pColor, TextAnchor pAlign)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "text");
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = LocalizedTextManager.current_font;
            t.fontSize = pSize;
            t.color = pColor;
            t.alignment = pAlign;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = pText;
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Hex("#151515");
            shadow.effectDistance = new Vector2(0.6f, -0.6f);
            return rt;
        }

        static float Measure(string pText, int pSize)
        {
            Font font = LocalizedTextManager.current_font;
            font.RequestCharactersInTexture(pText, pSize);
            float w = 0f;
            foreach (char c in pText)
            {
                if (font.GetCharacterInfo(c, out CharacterInfo info, pSize)) w += info.advance;
                else w += pSize;
            }
            return w;
        }

        // 分区标题条：原版的标签页底图，两端各一个图标
        static void Header(float pX, float pY, float pW, string pText, string pIcon)
        {
            RectTransform bar = Sliced(_parent, "ui/special/background_tab_content", pX, pY, pW, 20f);
            Icon(bar, pIcon, 6f, 3f, 14f);
            Icon(bar, pIcon, pW - 20f, 3f, 14f);
            Text(bar, pText, 0f, 0f, pW, 18f, 9, HeaderGold, TextAnchor.MiddleCenter);
        }

        // 能滚动的列表，返回内容节点
        static RectTransform Scroll(string pName, float pX, float pY, float pW, float pH, float pContentH)
        {
            RectTransform view = Box(_parent, pX, pY, pW, pH, pName);
            view.gameObject.AddComponent<RectMask2D>();
            Image hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            RectTransform content = Box(view, 0f, 0f, pW, Mathf.Max(pH, pContentH), "content");
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = pContentH > pH;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 8f;
            return content;
        }
    }

    // 每帧检查内容有没有变化
    public class ParliamentWindowTicker : MonoBehaviour
    {
        float _timer;

        void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.5f;
            ParliamentWindow.Tick();
        }
    }
}
