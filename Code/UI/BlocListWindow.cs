using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DiplomacyOverhaul
{
    // 集团列表：底栏「集团图层」上面一格的按钮打开，和原版的氏族、王国列表并排。
    // 窗口和条目都复制原版的氏族列表：插画、排序页签、滚动区、空状态照旧，原版按氏族填内容的组件拆掉，换成按集团填。
    // 每个集团一条：魁首家族的族徽、集团名、议席、几家、几座城，右边是魁首家族拿主意的人（头像带着国家的旗）。
    // 悬停弹集团面板，点一下打开那个国家的议会窗口并选中这个集团，和集团图层里一样
    public static class BlocListWindow
    {
        public const string WindowId = "vd_bloc_list";
        public const string ButtonId = "vd_bloc_list";
        // 标题和原版的「氏族」一样只写「集团」；按钮的悬停面板照原版列表按钮，写现存几个
        const string Title = "vd_blocs", TipId = "vd_tooltip_meta_list_blocs";
        const string Prefab = "windows/list_clans";
        const string Art = "ui/illustrations/art_clans";

        static ScrollWindow _window;
        static View _view;

        // 热重载：旧版本建的窗口引用着旧代码，拆掉，用到时重建
        public static void Init()
        {
            if (ScrollWindow._all_windows.TryGetValue(WindowId, out ScrollWindow old) && old != null)
            {
                if (old.gameObject.activeSelf) ScrollWindow.hideAllEvent(false);
                WindowHistory.list.RemoveAll(h => h.window == old || h.window == null);
                Object.Destroy(old.gameObject);
            }
            ScrollWindow._all_windows.Remove(WindowId);
            _window = null;
            _view = null;
            Core.Upsert(AssetManager.window_library, new WindowAsset { id = WindowId, icon_path = "vd_parliament" });
            Core.Upsert(AssetManager.tooltips, new TooltipAsset { id = TipId, prefab_id = "tooltips/tooltip_meta_list", callback = ShowButtonTip });
        }

        // 和原版列表按钮的面板一样：标题、说明，图标写现存几个；集团散了不留记录，不写消亡数
        static void ShowButtonTip(global::Tooltip pTooltip, string pType, TooltipData pData)
        {
            if (!Safety.On(Sys.Interface)) return;
            try
            {
                AssetManager.tooltips.showNormal(pTooltip, pType, pData);
                int count = 0;
                foreach (BlocMapView.Entry _ in BlocMapView.All()) count++;
                Transform total = pTooltip.transform.FindRecursive("i_total");
                if (total != null && total.GetComponent<StatsIcon>() is StatsIcon icon)
                {
                    icon.enable_animation = false;
                    icon.setValue(count);
                    icon.getIcon().sprite = SpriteTextureLoader.getSprite(ParliamentWindow.IconPath);
                }
                pTooltip.transform.FindRecursive("i_destroyed")?.gameObject.SetActive(false);
            }
            catch (Exception e) { Safety.Fail(Sys.Interface, "集团按钮面板", e); }
        }

        // ---------- 按钮 ----------

        // 复制原版氏族列表的按钮，放在 pAt 这一格（集团图层的上格）
        public static void BuildButton(Transform pTab, int pAt)
        {
            PowerButton clans = null;
            foreach (Transform child in pTab)
            {
                PowerButton b = child.GetComponent<PowerButton>();
                if (b != null && b.type == PowerButtonType.Window && b.open_window_id == "list_clans") { clans = b; break; }
            }
            GameObject button;
            if (clans == null) button = new GameObject(ButtonId, typeof(RectTransform));
            else
            {
                // 先放进一个关着的节点里复制，改好再挂上去：按钮在启用时才初始化
                var holder = new GameObject("vd_holder");
                holder.SetActive(false);
                button = Object.Instantiate(clans.gameObject, holder.transform);
                button.name = ButtonId;
                PowerButton power = button.GetComponent<PowerButton>();
                power.open_window_id = WindowId;
                if (power.icon != null) power.icon.sprite = SpriteTextureLoader.getSprite(ParliamentWindow.IconPath);
                TipButton tip = button.GetComponent<TipButton>();
                if (tip != null)
                {
                    tip.type = TipId;
                    tip.textOnClick = Title;
                    tip.textOnClickDescription = "vd_blocs_list_description";
                }
                button.transform.SetParent(pTab, false);
                Object.Destroy(holder);
            }
            button.transform.SetParent(pTab, false);
            button.transform.SetSiblingIndex(pAt);
        }

        // ---------- 窗口 ----------

        class View : MonoBehaviour
        {
            public SortingTab sorting;
            public Transform list;
            public GameObject noItems, template, wrapper;
            public Text counter;
            public float height;
            public Comparison<BlocMapView.Entry> sort;
            public readonly List<Row> rows = new List<Row>();

            void OnEnable() => Show();

            public void Show()
            {
                if (!Safety.On(Sys.Interface)) return;
                try { Fill(this); }
                catch (Exception e) { Safety.Fail(Sys.Interface, "集团列表", e); }
            }
        }

        class Row
        {
            public GameObject root;
            public Text name;
            public ClanBanner banner;
            public UiUnitAvatarElement avatar;
            public Image species;
            public CountUpOnClick seats, members, cities;
            public Pointer pointer;
        }

        // 原版打开窗口时按 id 找预制件，找不到就打开「找不到窗口」；集团列表在这里先建好
        internal static void Create()
        {
            ScrollWindow prefab = Resources.Load<ScrollWindow>(Prefab);
            var holder = new GameObject("vd_holder");
            holder.SetActive(false);
            try
            {
                ScrollWindow w = Object.Instantiate(prefab, holder.transform);
                ListWindow list = w.GetComponent<ListWindow>();

                // 只留「列表」这一页：收藏、统计页和右侧的世界统计、对比按钮都和原版对象绑着
                if (list._tab_favorite != null) Object.DestroyImmediate(list._tab_favorite.gameObject);
                if (list._tab_dead != null) Object.DestroyImmediate(list._tab_dead.gameObject);
                Transform tabs = list._tabs_container.transform;
                Kill(tabs.Find("Statistics"));
                Kill(w.transform.Find("Tabs Right"));
                Transform content = list._content_list;
                Kill(content.Find("title_favorites"));
                Kill(content.Find("title_dead"));
                Transform scrolled = content.parent;
                Kill(scrolled.Find("content_statistics"));
                Kill(scrolled.Find("window_content_total_breakdown_rows"));

                Sprite icon = SpriteTextureLoader.getSprite(ParliamentWindow.IconPath);
                list._art.sprite = SpriteTextureLoader.getSprite(Art);
                list._tab_list_icon.sprite = list._title_icon_left.sprite = list._title_icon_right.sprite = icon;
                list._no_items_icon_left.sprite = list._no_items_icon_right.sprite = icon;
                list._no_items_description.setKeyAndUpdate("vd_bloc_list_empty");
                content.Find("title_list/title_tab")?.GetComponent<LocalizedText>()?.setKeyAndUpdate("vd_bloc_list_all");
                w.titleText.GetComponent<LocalizedText>()?.setKeyAndUpdate(Title);
                WindowMetaTab tab = list._tab_list;
                tab.GetComponent<TipButton>().textOnClick = "vd_bloc_list_all";
                tab.tab_action.AddListener(_ => list._tabs_container.showTab(tab));

                View view = w.gameObject.AddComponent<View>();
                view.sorting = list._sorting_tab;
                view.list = list._list_transform;
                view.noItems = list._no_items;
                view.counter = list._title_counter;
                // 条目模板收在窗口里一个关着的节点下
                var store = new GameObject("vd_templates");
                store.transform.SetParent(w.transform, false);
                store.SetActive(false);
                view.template = Template(list._list_element_prefab, store.transform);
                view.height = view.template.GetComponent<RectTransform>().sizeDelta.y;
                view.wrapper = Resources.Load<GameObject>("ui/list_element_empty");

                // 列表组件由 ListWindow 在启用时按氏族建，拆掉它就不会建
                Object.DestroyImmediate(list);

                view.sorting.addButton(ParliamentWindow.IconPath, "vd_sort_by_seats", view.Show, () => view.sort = BySeats);
                view.sorting.addButton("ui/Icons/iconClan", "sort_by_members", view.Show, () => view.sort = ByMembers);
                view.sorting.addButton("ui/Icons/iconKingdom", "sort_by_kingdom", view.Show, () => view.sort = ByKingdom);
                view.sort = BySeats;

                w.name = WindowId;
                w.screen_id = WindowId;
                w.transform.SetParent(CanvasMain.instance.transformWindows, false);
                ScrollWindow._all_windows[WindowId] = w;
                w.create();
                view.sorting.enableFirstIfNone();
                _window = w;
                _view = view;
            }
            finally
            {
                Object.Destroy(holder);
            }
        }

        static void Kill(Transform pNode)
        {
            if (pNode != null) Object.DestroyImmediate(pNode.gameObject);
        }

        // 原版氏族条目的外观，按氏族填内容的组件拆掉；点击换成我们的
        static GameObject Template(GameObject pPrefab, Transform pHolder)
        {
            GameObject t = Object.Instantiate(pPrefab, pHolder);
            Object.DestroyImmediate(t.GetComponent<ClanListElement>());
            t.GetComponent<Button>().onClick = new Button.ButtonClickedEvent();
            t.AddComponent<Pointer>();
            Transform icons = t.transform.Find("Icons");
            Counter(icons, "Age", ParliamentWindow.IconPath, "vd_bloc_tip_seats");
            Counter(icons, "Renown", "ui/Icons/iconClan", "vd_bloc_tip_members");
            Counter(icons, "Members", "ui/Icons/iconCity", "vd_bloc_tip_cities");
            icons.Find("Dead")?.gameObject.SetActive(false);
            t.transform.FindRecursive("Favorited")?.gameObject.SetActive(false);
            return t;
        }

        static void Counter(Transform pIcons, string pName, string pIcon, string pTip)
        {
            Transform c = pIcons.Find(pName);
            if (c == null) return;
            Image image = c.Find("Container/Icon")?.GetComponent<Image>();
            if (image != null) image.sprite = SpriteTextureLoader.getSprite(pIcon);
            TipButton tip = c.GetComponent<TipButton>();
            if (tip != null)
            {
                tip.textOnClick = pTip;
                tip.textOnClickDescription = null;
            }
        }

        static Row NewRow(View pView)
        {
            GameObject wrapper = Object.Instantiate(pView.wrapper, pView.list);
            LayoutElement layout = wrapper.GetComponent<LayoutElement>();
            if (layout != null) layout.minHeight = pView.height;
            GameObject go = Object.Instantiate(pView.template, wrapper.transform);
            go.transform.localPosition = Vector3.zero;
            Transform t = go.transform;
            var row = new Row
            {
                root = wrapper,
                name = t.FindRecursive("Name")?.GetComponent<Text>(),
                banner = t.Find("Clan Banner")?.GetComponent<ClanBanner>(),
                avatar = go.GetComponentInChildren<UiUnitAvatarElement>(true),
                species = t.FindRecursive("Species")?.GetComponent<Image>(),
                seats = t.Find("Icons/Age")?.GetComponent<CountUpOnClick>(),
                members = t.Find("Icons/Renown")?.GetComponent<CountUpOnClick>(),
                cities = t.Find("Icons/Members")?.GetComponent<CountUpOnClick>(),
                pointer = go.GetComponent<Pointer>()
            };
            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(() =>
            {
                BlocMapView.Entry e = row.pointer.entry;
                if (e != null && e.kingdom.isAlive()) ParliamentWindow.OpenBloc(e.kingdom, e.bloc.Key);
            });
            return row;
        }

        static readonly List<BlocMapView.Entry> _entries = new List<BlocMapView.Entry>();

        static void Fill(View pView)
        {
            if (!Config.game_loaded) return;
            _entries.Clear();
            foreach (BlocMapView.Entry e in BlocMapView.All())
                if (e.kingdom.isAlive() && e.bloc.leader.voice != null) _entries.Add(e);
            _entries.Sort(pView.sort ?? BySeats);
            SortButton current = pView.sorting.getCurrentButton();
            if (current != null && current.getState() == SortButtonState.Down) _entries.Reverse();

            pView.noItems.SetActive(_entries.Count == 0);
            if (pView.counter != null) pView.counter.text = _entries.Count.ToString();
            while (pView.rows.Count < _entries.Count) pView.rows.Add(NewRow(pView));
            for (int i = 0; i < pView.rows.Count; i++)
            {
                Row row = pView.rows[i];
                bool on = i < _entries.Count;
                row.root.SetActive(on);
                if (on) Show(row, _entries[i]);
            }
            ScrollWindow.checkElements();
        }

        static void Show(Row pRow, BlocMapView.Entry pEntry)
        {
            Bloc b = pEntry.bloc;
            pRow.pointer.entry = pEntry;
            if (pRow.name != null)
            {
                pRow.name.text = Blocs.Name(b);
                pRow.name.color = pEntry.clan.getColor().getColorText();
            }
            pRow.banner?.load(pEntry.clan);
            if (pRow.species != null) pRow.species.sprite = pEntry.clan.getActorAsset().getSpriteIcon();
            Actor voice = b.leader.voice;
            if (pRow.avatar != null)
            {
                if (voice != null && voice.isAlive()) pRow.avatar.show(voice);
                else pRow.avatar.gameObject.SetActive(false);
            }
            pRow.seats?.setValue(b.seats);
            pRow.members?.setValue(b.members.Count);
            pRow.cities?.setValue(pEntry.cities.Count);
        }

        static int BySeats(BlocMapView.Entry a, BlocMapView.Entry b) => b.bloc.seats.CompareTo(a.bloc.seats);

        static int ByMembers(BlocMapView.Entry a, BlocMapView.Entry b)
        {
            int c = b.bloc.members.Count.CompareTo(a.bloc.members.Count);
            return c != 0 ? c : BySeats(a, b);
        }

        // 同一国的排在一起，国家按集团合计议席从大到小
        static int ByKingdom(BlocMapView.Entry a, BlocMapView.Entry b)
        {
            if (a.kingdom == b.kingdom) return BySeats(a, b);
            int c = b.kingdom.getPopulationPeople().CompareTo(a.kingdom.getPopulationPeople());
            return c != 0 ? c : a.kingdom.id.CompareTo(b.kingdom.id);
        }

        // 悬停弹集团面板；只接鼠标移动和移出，拖动和滚轮照样传给滚动列表
        class Pointer : MonoBehaviour, IPointerMoveHandler, IPointerExitHandler
        {
            public BlocMapView.Entry entry;

            public void OnPointerMove(PointerEventData pData)
            {
                if (!InputHelpers.mouseSupported || global::Tooltip.anyActive() || entry == null || !Safety.On(Sys.Interface)) return;
                try { BlocMapView.ShowTip(entry); }
                catch (Exception e) { Safety.Fail(Sys.Interface, "集团列表面板", e); }
            }

            public void OnPointerExit(PointerEventData pData) => global::Tooltip.hideTooltip();
        }
    }

    // 打开集团列表：原版按 id 去 windows/ 下找预制件，没有这一项，先替它建好
    [HarmonyPatch(typeof(ScrollWindow), nameof(ScrollWindow.checkWindowExist))]
    static class Patch_BlocListWindow
    {
        static bool Prefix(string pWindowID, ref bool __result)
        {
            if (pWindowID != BlocListWindow.WindowId || ScrollWindow._all_windows.ContainsKey(pWindowID) || !Safety.On(Sys.Interface)) return true;
            try
            {
                BlocListWindow.Create();
                __result = true;
                return false;
            }
            catch (Exception e)
            {
                Safety.Fail(Sys.Interface, "集团列表窗口", e);
                return true;
            }
        }
    }
}
