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
    public static partial class ParliamentWindow
    {
        const string WindowId = "vd_parliament";
        const string SideName = "vd_parliament_side";
        public const string IconPath = "ui/Icons/vd_parliament";
        public const float W = 560f, H = 330f;

        static readonly Color YesBar = Hex("#1e90d6");
        static readonly Color NoBar = Hex("#d5491b");
        internal static readonly Color YesText = Hex("#5fb8f0");
        internal static readonly Color NoText = Hex("#ff7a4a");
        static readonly Color RegimeText = Hex("#f5d08a");

        static KingdomTabWindow _shell;
        static Kingdom _kingdom;
        static long _selected = -1;
        static PolicyTab _tab = PolicyTab.Domestic;
        internal static string _open;
        static Section _frame, _regime, _seats, _clans, _compass, _detail, _policies, _bill;
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
            _shell = new KingdomTabWindow(WindowId, IconPath, 1, W, H, Open, Tick) { enabled = () => Settings.ParliamentEnabled, spacer = "space (2)" };
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

        internal static void OnToggle(bool pEnabled)
        {
            _shell?.Refresh();
            UpdateSide();
        }

        internal static void UpdateSide()
        {
            bool show = Settings.ParliamentEnabled && PowerButtonSelector.instance.cancelUnitSelectedMover.visible && SelectedKingdom() != null;
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
            if (pKingdom == null || !pKingdom.isAlive() || !Settings.ParliamentEnabled) return;
            if (_kingdom != pKingdom) _selected = -1;
            _kingdom = pKingdom;
            KingdomTabWindow.Current = pKingdom;
            if (_shell.Create())
            {
                _sections = new[] { _frame = NewSection("frame"), _regime = NewSection("regime"), _seats = NewSection("seats"), _clans = NewSection("clans"), _compass = NewSection("compass"),
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

            RegimeNames.Rule regime = RegimeNames.Of(_kingdom);
            string assembly = RegimeNames.AssemblyKey(regime, republic);
            bool built = Build(_frame, "frame" + assembly, () => BuildFrame(assembly));
            string regimeKey = regime == null ? "" : regime.id + republic + string.Join(",", RegimeNames.Reasons(_kingdom, regime));
            Build(_regime, k + regimeKey, () => Regime(regime));
            built |= Build(_seats, clanKey, () =>
            {
                if (clans.Count > 0) Hemicycle(clans, LeftX, Top + 24f);
            });
            built |= Build(_clans, clanKey + republic, () => ClanList(clans));

            var pk = new System.Text.StringBuilder(clanKey).Append(republic).Append('|');
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

        // 三个分区标题；议会那一栏按政制换叫法
        static void BuildFrame(string pAssembly)
        {
            Header(LeftX, Top, LeftW, LM.Get(pAssembly), IconPath);
            Header(RightX, Top, RightW, LM.Get("vd_parliament_policies"), "ui/Icons/iconKings");
            Header(RightX, BillY, RightW, LM.Get("vd_parliament_bill"), "ui/Icons/iconDiplomacy");
        }

        // 国名下面一行小字：政制名称，悬停列出是哪几项政策凑成的
        static void Regime(RegimeNames.Rule pRule)
        {
            if (pRule == null) return;
            string name = LM.Get(RegimeNames.Key(pRule, Government.IsRepublic(_kingdom)));
            float w = Measure(name, 9) + 12f;
            RectTransform hover = UiKit.Rect(_parent, (W - w) / 2f, RegimeY, w, 14f, new Color(0f, 0f, 0f, 0f));
            Text(hover, name, 0f, 0f, w, 14f, 9, RegimeText, TextAnchor.MiddleCenter);
            List<string> reasons = RegimeNames.Reasons(_kingdom, pRule);
            string info = "vd_regime_default_info";
            if (reasons.Count > 0)
            {
                info = "vd_regime_info_" + _kingdom.id;
                LM.AddToCurrentLocale(info, string.Join(LM.Get("vd_parliament_separator"), reasons));
            }
            HoverTip(hover, () => Tooltip.show(hover.gameObject, "tip", new TooltipData { tip_name = RegimeNames.Key(pRule, Government.IsRepublic(_kingdom)), tip_description = info }));
        }

        const float LeftX = 18f, LeftW = 266f;
        const float RightX = 298f, RightW = 244f;
        const float Top = 26f, RegimeY = 7f;
        const int ListRows = 5;
        const float PolicyY = Top + 43f, PolicyH = 112f, BillY = PolicyY + PolicyH + 4f;

        static void Header(float pX, float pY, float pW, string pText, string pIcon) => UiKit.Header(_parent, pX, pY, pW, pText, pIcon);

        static RectTransform Scroll(string pName, float pX, float pY, float pW, float pH, float pContentH) =>
            UiKit.Scroll(_parent, pName, pX, pY, pW, pH, pContentH);

        // 正在悬停的政策选项和它的数字行，由 Patch_PolicyTip 加进原版提示
        internal static string TipName;
        internal static List<(string key, string value)> TipEffects;
        // 带颜色的数字行：议案名单里一股势力的投票明细
        internal static List<(string key, string value, string color)> TipLines;
        // 提出这一档时议会的表决预测；现行选项没有
        internal static Bill TipBill;
        internal static VoteResult TipVote;

        // 政策选项的悬停提示：一句说明，下面是这一档的数字
        static void OptionTip(RectTransform pTarget, Policy pPolicy, int pOption)
        {
            HoverTip(pTarget, () =>
            {
                TipName = pPolicy.OptionName(_kingdom, pOption);
                TipEffects = Policies.Effects(_kingdom, pPolicy, pOption);
                TipLines = null;
                TipBill = pPolicy.Get(_kingdom) != pOption ? new Bill { policy = pPolicy, option = pOption } : null;
                TipVote = TipBill != null ? Parliament.Vote(_kingdom, TipBill, Parliament.Clans(_kingdom)) : null;
                Tooltip.show(pTarget.gameObject, "tip", new TooltipData { tip_name = TipName, tip_description = DescriptionKey(pPolicy, pOption) });
            });
        }
    }

    // 政策选项的提示：在原版属性栏里列出这一档的数字
    [HarmonyLib.HarmonyPatch(typeof(TooltipLibrary), "showTip")]
    static class Patch_PolicyTip
    {
        static void Postfix(Tooltip pTooltip, TooltipData pData)
        {
            if (pData == null || pData.tip_name != ParliamentWindow.TipName || ParliamentWindow.TipEffects == null) return;
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                foreach (var (key, value) in ParliamentWindow.TipEffects) pTooltip.addLineText(key, value);
                if (ParliamentWindow.TipLines != null)
                {
                    foreach (var (key, value, color) in ParliamentWindow.TipLines) pTooltip.addLineText(key, value, color);
                    pTooltip.showStatValues();
                    return;
                }
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
            catch (Exception e) { Safety.Fail(Sys.Interface, "政策选项提示框", e); }
        }
    }

    // 只接鼠标进出：拖动和滚轮照样传给外面的滚动列表
    public class HoverPointer : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public Action show;

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => show?.Invoke();

        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => Tooltip.hideTooltip();
    }

    // 左下角的王国取消按钮显示或收起时，右下角的议会按钮跟着
    [HarmonyPatch(typeof(PowerButtonSelector), "updateSelectedUnitCancelButton")]
    static class Patch_SelectedCancelButton
    {
        static void Postfix()
        {
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                ParliamentWindow.UpdateSide();
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "议会按钮", e); }
        }
    }
}
