using System;
using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DiplomacyOverhaul
{
    // 从王国窗口左侧页签栏打开的单独窗口（议会、选举与任命）：页签栏里的按钮、窗口外框、
    // 复制过来的页签栏、按屏幕缩放。内容由各窗口自己拼在 root 下
    public class KingdomTabWindow
    {
        const string KingdomWindowId = "kingdom";
        const float Margin = 6f, TitleSpace = 20f;

        static readonly List<KingdomTabWindow> All = new List<KingdomTabWindow>();

        public readonly string id;
        readonly string _button, _icon, _tabs;
        readonly int _index;
        readonly Action<Kingdom> _open;
        readonly Action _tick;
        public readonly float W, H;
        // 系统关掉时页签栏里不显示这个窗口的按钮
        public Func<bool> enabled = () => true;
        // 按钮插在原版哪个页签前面（不设时按 pIndex 排），显示时藏起原版的哪个空位，让页签栏保持原来的长度
        public string before, spacer;

        public ScrollWindow window;
        public RectTransform root;
        float _shift;

        // pIndex：在页签栏里排第几个（概览是 0）
        public KingdomTabWindow(string pId, string pIcon, int pIndex, float pW, float pH, Action<Kingdom> pOpen, Action pTick)
        {
            id = pId;
            _button = pId + "_button";
            _tabs = pId + "_tabs";
            _icon = pIcon;
            _index = pIndex;
            W = pW;
            H = pH;
            _open = pOpen;
            _tick = pTick;
            All.RemoveAll(w => w.id == pId);
            All.Add(this);
            All.Sort((a, b) => a._index.CompareTo(b._index));
        }

        // 热重载：旧版本建的窗口和按钮引用着旧代码，全部拆掉，用到时重建
        public void Init()
        {
            if (ScrollWindow._all_windows.TryGetValue(id, out ScrollWindow old) && old != null)
            {
                // 开着的窗口直接拆掉会让原版的窗口切换卡住，先关掉
                if (old.gameObject.activeSelf) ScrollWindow.hideAllEvent(false);
                // 原版的窗口历史还记着旧窗口，拆掉后再切页签会在历史里碰到已销毁的窗口而报错
                WindowHistory.list.RemoveAll(h => h.window == old || h.window == null);
                Object.Destroy(old.gameObject);
            }
            ScrollWindow._all_windows.Remove(id);
            if (AssetManager.window_library.dict.TryGetValue(id, out WindowAsset asset))
            {
                AssetManager.window_library.dict.Remove(id);
                AssetManager.window_library.list.Remove(asset);
            }
            foreach (UnityEngine.UI.Button b in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>())
            {
                if (b != null && b.name == _button) Object.DestroyImmediate(b.gameObject);
            }
            window = null;
            root = null;
            // 按钮平时在王国窗口显示概览页时加上，重开窗口可能直接停在别的页签，这里马上加回去
            KingdomWindow tabs = KingdomTabs();
            if (tabs != null) EnsureButtons(tabs);
        }

        static KingdomWindow KingdomTabs() =>
            ScrollWindow._all_windows.TryGetValue(KingdomWindowId, out ScrollWindow w) && w != null ? w.GetComponent<KingdomWindow>() : null;

        public bool IsOpen => window != null && window.gameObject.activeInHierarchy;

        // ---------- 王国窗口里的按钮 ----------

        public static void EnsureButtons(KingdomWindow pWindow)
        {
            foreach (KingdomTabWindow w in All) w.EnsureButton(pWindow);
        }

        // 照原版的页签复制一个，去掉页签组件：别的页签在窗口里换内容，这里点了打开单独的窗口
        void EnsureButton(KingdomWindow pWindow)
        {
            Transform tabs = pWindow.transform.Find("Background/Tabs");
            if (tabs == null) return;
            Transform existing = tabs.Find(_button);
            if (existing != null)
            {
                if (existing.gameObject.activeSelf != enabled()) SetButtonActive(tabs, existing.gameObject, enabled());
                return;
            }
            Transform template = tabs.Find("Villages");
            if (template == null) return;
            GameObject go = Object.Instantiate(template.gameObject, tabs);
            go.name = _button;
            Transform anchor = before != null ? tabs.Find(before) : null;
            go.transform.SetSiblingIndex(anchor != null ? anchor.GetSiblingIndex() : _index);
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c is WindowMetaTab || c is DragOrderElement || c is ScrollableButton || c is UnityEngine.EventSystems.EventTrigger) Object.DestroyImmediate(c);
            }
            go.GetComponent<Image>().sprite = SpriteTextureLoader.getSprite("ui/tab_button_vertical");
            Image icon = go.transform.Find("Icon")?.GetComponent<Image>();
            if (icon != null) icon.sprite = SpriteTextureLoader.getSprite(_icon);
            TipButton tip = go.GetComponent<TipButton>();
            if (tip != null)
            {
                tip.textOnClick = id;
                tip.textOnClickDescription = id + "_description";
                tip.text_description_2 = "";
            }
            UnityEngine.UI.Button button = go.GetComponent<UnityEngine.UI.Button>();
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            button.onClick.AddListener(() => _open(SelectedMetas.selected_kingdom));
            SetButtonActive(tabs, go, enabled());
        }

        void SetButtonActive(Transform pTabs, GameObject pButton, bool pActive)
        {
            pButton.SetActive(pActive);
            if (spacer != null) pTabs.Find(spacer)?.gameObject.SetActive(!pActive);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)pTabs);
        }

        // 开关改了：按钮马上跟着显示或隐藏，关掉时窗口开着就关上
        public void Refresh()
        {
            KingdomWindow tabs = KingdomTabs();
            if (tabs != null) EnsureButton(tabs);
            if (!enabled() && IsOpen) ScrollWindow.hideAllEvent(false);
        }

        // ---------- 窗口 ----------

        // 第一次打开时建窗口外框；返回 true 表示刚建好
        public bool Create()
        {
            if (window != null) return false;
            _shift = 0f;
            window = WindowCreator.CreateEmptyWindow(id, id);
            // 新建的窗口会先播一段收起动画，动画没完原版不接受打开窗口，直接放完
            ScrollWindow.finishAnimations();
            Transform background = window.transform.Find("Background");
            background.Find("Scroll View")?.gameObject.SetActive(false);
            RectTransform bg = background.GetComponent<RectTransform>();
            bg.sizeDelta = new Vector2(W, H);
            Image bgImage = background.GetComponent<Image>();
            bgImage.type = Image.Type.Sliced;
            Transform close = window.transform.Find("CloseBackground");
            if (close != null) close.localPosition = new Vector3(W / 2f - 18f, H / 2f + 4f);
            window.titleText.transform.localPosition = new Vector3(0f, H / 2f - 2f);

            var go = new GameObject(id + "_root", typeof(RectTransform));
            root = go.GetComponent<RectTransform>();
            root.SetParent(background, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(W, H);
            root.anchoredPosition = Vector2.zero;
            window.gameObject.AddComponent<KingdomTabWindowTicker>().tick = _tick;
            return true;
        }

        public void Show(Kingdom pKingdom)
        {
            BuildTabs();
            LM.AddToCurrentLocale(id + "_window_title", pKingdom.data.name);
            window.titleText.GetComponent<LocalizedText>().setKeyAndUpdate(id + "_window_title");
            ScrollWindow.showWindow(id);
            Fit();
        }

        // 左侧页签栏：照王国窗口的页签栏复制一份，自己那格选中，点别的格回到王国窗口的那一页或别的单独窗口。
        // 每次打开都重新复制，跟着王国窗口当前显示哪些页签
        void BuildTabs()
        {
            Transform background = window.transform.Find("Background");
            Transform old = background.Find(_tabs);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            KingdomWindow kingdomTabs = KingdomTabs();
            if (kingdomTabs == null) return;
            // 热重载后王国窗口还没重新打开过，按钮还没加回去
            EnsureButtons(kingdomTabs);
            RectTransform source = kingdomTabs.transform.Find("Background/Tabs") as RectTransform;
            if (source == null) return;

            // 先复制到一个没激活的节点下，原版页签组件不会初始化，拆掉后再挂到窗口上
            var holder = new GameObject(id + "_tabs_holder");
            holder.SetActive(false);
            GameObject go = Object.Instantiate(source.gameObject, holder.transform);
            go.name = _tabs;
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
                UnityEngine.UI.Button button = t.GetComponent<UnityEngine.UI.Button>();
                if (button == null) continue;
                bool self = t.name == _button;
                t.GetComponent<Image>().sprite = SpriteTextureLoader.getSprite(self ? "ui/tab_button_vertical_selected" : "ui/tab_button_vertical");
                button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                if (self) continue;
                string tab = t.name;
                KingdomTabWindow other = All.Find(w => w._button == tab);
                if (other != null) button.onClick.AddListener(() => other._open(Current));
                else button.onClick.AddListener(() => OpenKingdomTab(tab));
            }

            // 和王国窗口里一样，贴着窗口左上角
            RectTransform from = (RectTransform)source.parent;
            RectTransform to = (RectTransform)background;
            go.transform.SetParent(background, false);
            Vector3 offset = source.localPosition - new Vector3(from.rect.xMin, from.rect.yMax);
            go.transform.localPosition = new Vector3(to.rect.xMin, to.rect.yMax) + offset;
            Object.Destroy(holder);
        }

        // 各个单独窗口当前显示的王国（同一时间只开着一个）
        public static Kingdom Current;

        static void OpenKingdomTab(string pTab)
        {
            if (Current == null || !Current.isAlive()) return;
            SelectedMetas.selected_kingdom = Current;
            ScrollWindow.showWindow(KingdomWindowId);
            if (ScrollWindow._all_windows.TryGetValue(KingdomWindowId, out ScrollWindow kingdomWindow) && kingdomWindow != null)
                kingdomWindow.tabs.showTab(pTab);
        }

        // 窗口比原版的宽，屏幕窄时放不下：在左边原版工具栏和屏幕右边之间，整体缩小到能完整显示（连同左侧页签栏），
        // 还挤的话往右挪。窗口自身的位置归原版的滑入动画管，挪的是窗口里的各个部件
        public void Fit()
        {
            Canvas canvas = window.GetComponentInParent<Canvas>();
            Transform parent = window.transform.parent;
            if (canvas == null || parent == null) return;
            var corners = new Vector3[4];
            ((RectTransform)canvas.transform).GetWorldCorners(corners);
            float left = parent.InverseTransformPoint(corners[0]).x;
            float right = parent.InverseTransformPoint(corners[2]).x;
            float height = parent.InverseTransformPoint(corners[2]).y - parent.InverseTransformPoint(corners[0]).y;
            // 工具栏随窗口打开从屏幕外滑进来，按它滑到的位置算，不看动画走到哪了
            RectTransform toolbar = canvas.transform.Find("WindowToolbarContainer/WindowToolbar") as RectTransform;
            UiMover mover = toolbar != null && toolbar.gameObject.activeInHierarchy ? toolbar.GetComponent<UiMover>() : null;
            if (mover != null && mover.visible)
            {
                Vector3 edge = toolbar.parent.TransformPoint(mover.initPos + new Vector3(toolbar.rect.xMax * toolbar.localScale.x, 0f));
                left = Mathf.Max(left, parent.InverseTransformPoint(edge).x);
            }
            // 窗口居中，按伸得最远的一边算（页签栏伸在背景左边外面）
            float side = W / 2f;
            foreach (Transform c in window.transform.Find("Background"))
            {
                if (!c.gameObject.activeSelf) continue;
                UnityEngine.Rect r = ((RectTransform)c).rect;
                side = Mathf.Max(side, -(c.localPosition.x + r.xMin), c.localPosition.x + r.xMax);
            }
            side += Margin;
            float s = Mathf.Min(1f, (right - left) / (2f * side), height / (H + 2f * (TitleSpace + Margin)));
            float shift = Mathf.Max(0f, left - (-s * side)) / s;
            window.transform.localScale = new Vector3(s, s, 1f);
            foreach (Transform c in window.transform) c.localPosition += new Vector3(shift - _shift, 0f, 0f);
            _shift = shift;
        }
    }

    // 每半秒检查一次内容有没有变化
    public class KingdomTabWindowTicker : MonoBehaviour
    {
        public Action tick;
        float _timer;

        void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.5f;
            tick?.Invoke();
        }
    }
}
