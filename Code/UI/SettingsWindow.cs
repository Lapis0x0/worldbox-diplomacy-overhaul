using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using static DiplomacyOverhaul.UiKit;
using Object = UnityEngine.Object;

namespace DiplomacyOverhaul
{
    // 模组设置窗口：各系统的开关按外交、内政、军事、显示、性能分成几块，两列排开，一屏放下
    public static class SettingsWindow
    {
        const string WindowId = "vd_settings_window";

        class Card
        {
            public string title, icon;
            public (string group, string id)[] rows;
        }

        // 左右两列
        static readonly Card[][] Columns =
        {
            new[]
            {
                new Card { title = "vd_settings_diplomacy", icon = "ui/Icons/iconDiplomacy", rows = new[]
                {
                    (Settings.GroupVassal, "vd_feudal_enabled"),
                    (Settings.GroupPeace, "vd_peace_enabled")
                } },
                new Card { title = "vd_settings_domestic", icon = "ui/Icons/iconCrown", rows = new[]
                {
                    (Settings.GroupParliament, Settings.KeyParliament),
                    (Settings.GroupCycle, "vd_cycle_enabled"),
                    (Settings.GroupGovernment, "vd_gov_auto_enabled"),
                    (Settings.GroupGovernment, "vd_gov_events_enabled")
                } }
            },
            new[]
            {
                new Card { title = "vd_settings_military", icon = "ui/Icons/iconWar", rows = new[]
                {
                    (Settings.GroupMilitary, Settings.KeyMilitary)
                } },
                new Card { title = "vd_settings_display", icon = "ui/Icons/iconKingdom", rows = new[]
                {
                    (Settings.GroupGovernment, "vd_gov_name_enabled")
                } },
                new Card { title = "vd_settings_perf", icon = "ui/Icons/iconClock", rows = new[]
                {
                    (Settings.GroupPerf, "vd_perf_enabled")
                } }
            }
        };

        const float Pad = 8f, Gap = 6f, Top = 22f;
        const float W = 340f;
        const float ColW = (W - 2f * Pad - Gap) / 2f;
        const float HeadH = 20f, RowH = 21f, CardPad = 4f;
        const float FooterH = 18f;

        static ScrollWindow _window;
        static RectTransform _root;

        // 热重载：旧版本建的窗口引用着旧代码，拆掉，用到时重建
        public static void Init()
        {
            if (ScrollWindow._all_windows.TryGetValue(WindowId, out ScrollWindow old) && old != null)
            {
                if (old.gameObject.activeSelf) ScrollWindow.hideAllEvent(false);
                Object.Destroy(old.gameObject);
            }
            ScrollWindow._all_windows.Remove(WindowId);
            if (AssetManager.window_library.dict.TryGetValue(WindowId, out WindowAsset asset))
            {
                AssetManager.window_library.dict.Remove(WindowId);
                AssetManager.window_library.list.Remove(asset);
            }
            _window = null;
            _root = null;
        }

        static float CardHeight(Card pCard) => HeadH + pCard.rows.Length * RowH + CardPad;

        static float Height()
        {
            float h = 0f;
            foreach (Card[] col in Columns)
            {
                float ch = 0f;
                foreach (Card c in col) ch += CardHeight(c) + Gap;
                h = Mathf.Max(h, ch);
            }
            return Top + h + FooterH + Pad;
        }

        public static void Open()
        {
            if (_window == null) Create();
            ScrollWindow.showWindow(WindowId);
            Build();
        }

        static void Create()
        {
            float h = Height();
            _window = WindowCreator.CreateEmptyWindow(WindowId, "vd_settings");
            // 新建的窗口会先播一段收起动画，动画没完原版不接受打开窗口，直接放完
            ScrollWindow.finishAnimations();
            Transform background = _window.transform.Find("Background");
            background.Find("Scroll View")?.gameObject.SetActive(false);
            background.GetComponent<RectTransform>().sizeDelta = new Vector2(W, h);
            background.GetComponent<Image>().type = Image.Type.Sliced;
            Transform close = _window.transform.Find("CloseBackground");
            if (close != null) close.localPosition = new Vector3(W / 2f - 18f, h / 2f + 4f);
            _window.titleText.transform.localPosition = new Vector3(0f, h / 2f - 2f);

            var go = new GameObject(WindowId + "_root", typeof(RectTransform));
            _root = go.GetComponent<RectTransform>();
            _root.SetParent(background, false);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = new Vector2(W, h);
            _root.anchoredPosition = Vector2.zero;
        }

        // 开关不多，点一下整个重建
        static void Build()
        {
            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                GameObject child = _root.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
            for (int c = 0; c < Columns.Length; c++)
            {
                float x = Pad + c * (ColW + Gap), y = Top;
                foreach (Card card in Columns[c])
                {
                    BuildCard(card, x, y);
                    y += CardHeight(card) + Gap;
                }
            }
            float footY = Height() - Pad - FooterH + 2f;
            RectTransform reset = Button(_root, W - Pad - 54f, footY, 54f, 16f, "ui/special/special_buttonGray", Reset);
            Text(reset, LM.Get("vd_settings_reset"), 0f, 0f, 54f, 16f, 7, Color.white, TextAnchor.MiddleCenter);
        }

        static void BuildCard(Card pCard, float pX, float pY)
        {
            RectTransform card = Sliced(_root, Slot, pX, pY, ColW, CardHeight(pCard));
            Icon(card, pCard.icon, 6f, 4f, 12f);
            Text(card, LM.Get(pCard.title), 22f, 2f, ColW - 28f, 16f, 8, HeaderGold, TextAnchor.MiddleLeft);
            Rect(card, 5f, HeadH - 2f, ColW - 10f, 1f, Hex("#2c2f29"));
            float y = HeadH;
            foreach (var (group, id) in pCard.rows)
            {
                string g = group, i = id;
                bool on = Settings.Get(g, i);
                Text(card, LM.Get(i), 7f, y + 1f, ColW - 50f, 10f, 7, Ink, TextAnchor.MiddleLeft);
                Text(card, LM.Get(i + " Description"), 7f, y + 10f, ColW - 50f, 9f, 5, Muted, TextAnchor.MiddleLeft);
                RectTransform sw = Button(card, ColW - 38f, y + 3f, 32f, 14f,
                    on ? "ui/special/special_buttonRed" : "ui/special/special_buttonGray", () =>
                    {
                        Settings.Set(g, i, !Settings.Get(g, i));
                        Build();
                    });
                Text(sw, LM.Get(on ? "vd_settings_on" : "vd_settings_off"), 0f, 0f, 32f, 14f, 7, Color.white, TextAnchor.MiddleCenter);
                y += RowH;
            }
        }

        static void Reset()
        {
            foreach (Card[] col in Columns)
                foreach (Card card in col)
                    foreach (var (group, id) in card.rows)
                        if (!Settings.Get(group, id)) Settings.Set(group, id, true);
            Build();
        }
    }
}
