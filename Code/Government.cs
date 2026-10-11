using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.General;

namespace DiplomacyOverhaul
{
    // 政体：一个原版王国特质组，组内特质互斥；按城市数和附庸状态自动判定
    public static class Government
    {
        public const string GroupId = "vd_government";
        public const string CityState = "vd_gov_city_state";
        public const string Kingdom = "vd_gov_kingdom";
        public const string Empire = "vd_gov_empire";
        public const string Duchy = "vd_gov_duchy";

        // 最近一次自动设置的政体；与当前政体不一致说明玩家手动改过
        const string KeyAuto = "vd_gov_auto";
        const string KeyLocked = "vd_gov_locked";

        static readonly string[] Ids = { CityState, Kingdom, Empire, Duchy };
        static readonly HashSet<string> IdSet = new HashSet<string>(Ids);

        public static void Init()
        {
            AddTraitGroup(GroupId, "#FFD27F", Ids,
                new[] { "ui/Icons/iconCity", "ui/Icons/iconKingdom", "ui/Icons/iconCrown", "ui/Icons/iconKings" });
        }

        // 一组互斥的王国特质（政体、王朝周期）；组名的 key 是 trait_group_<组 id>
        public static void AddTraitGroup(string pGroupId, string pColor, string[] pIds, string[] pIcons)
        {
            Core.Upsert(AssetManager.kingdoms_traits_groups, new KingdomTraitGroupAsset
            {
                id = pGroupId,
                name = "trait_group_" + pGroupId,
                color = pColor
            });

            for (int i = 0; i < pIds.Length; i++)
            {
                Core.Upsert(AssetManager.kingdoms_traits, new KingdomTrait
                {
                    id = pIds[i],
                    group_id = pGroupId,
                    path_icon = pIcons[i],
                    spawn_random_trait_allowed = false,
                    needs_to_be_explored = false,
                    has_description_2 = false,
                    rarity = Rarity.R0_Normal
                });
            }

            // 特质库在模组加载前已经 link 过，互斥关系要自己补上
            var traits = new List<KingdomTrait>();
            foreach (string id in pIds) traits.Add(AssetManager.kingdoms_traits.get(id));
            foreach (KingdomTrait t in traits)
            {
                t.opposite_traits = new HashSet<KingdomTrait>();
                foreach (KingdomTrait other in traits)
                {
                    if (other == t) continue;
                    if (t.opposite_list == null || !t.opposite_list.Contains(other.id)) t.addOpposite(other.id);
                    t.opposite_traits.Add(other);
                }
            }
        }

        public static KingdomTrait Current(Kingdom pKingdom)
        {
            foreach (KingdomTrait t in pKingdom.getTraits())
            {
                if (IdSet.Contains(t.id)) return t;
            }
            return null;
        }

        public const string VanillaTitleKey = "kingdom";
        public const string CustomTitlePrefix = "vd_kingdom_title_";

        // 王国窗口和悬停提示里政体名用的 key。附庸是"政体 · 宗主"，帝国是"政体 · 周期阶段"，
        // 没有现成的 key，按国家写进当前语言表：原版会按 key 刷新文字，直接改文字会被冲掉
        public static string TitleKey(Kingdom pKingdom)
        {
            string key = Current(pKingdom)?.getLocaleID() ?? VanillaTitleKey;
            string suffix = Cycle.Label(pKingdom);
            Kingdom lord = Vassals.GetOverlord(pKingdom);
            if (lord != null)
            {
                string color = UnityEngine.ColorUtility.ToHtmlStringRGB(lord.getColor().getColorText());
                suffix = $"<color=#{color}>{lord.data.name}</color>";
            }
            if (suffix == null) return key;
            string customKey = CustomTitlePrefix + pKingdom.id;
            LM.AddToCurrentLocale(customKey, $"{LM.Get(key)} · {suffix}");
            return customKey;
        }

        static string Desired(Kingdom pKingdom)
        {
            if (Vassals.IsVassal(pKingdom)) return Duchy;
            int cities = pKingdom.countCities();
            if (cities <= 0) return null;
            if (cities == 1) return CityState;
            if (cities <= 5) return Kingdom;
            return Empire;
        }

        public static void Update(Kingdom pKingdom)
        {
            if (!pKingdom.isAlive() || !pKingdom.isCiv()) return;
            KingdomData data = pKingdom.data;
            string current = Current(pKingdom)?.id;
            data.get(KeyAuto, out string last, null);
            data.get(KeyLocked, out bool locked, false);

            if (locked)
            {
                // 玩家把政体删掉 = 交还给自动判定
                if (current != null) return;
                data.removeBool(KeyLocked);
            }
            else if (last != null && current != null && current != last)
            {
                // 玩家在特质编辑器里改过政体：锁定
                data.set(KeyLocked, true);
                return;
            }

            string desired = Desired(pKingdom);
            if (desired == null || desired == current) return;
            pKingdom.addTrait(desired, true);
            data.set(KeyAuto, desired);
        }

        public static void UpdateAll()
        {
            if (!Settings.GovernmentAutoEnabled) return;
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms)) Update(k);
        }
    }

    // 和投降检查一样，每个游戏年判定一次
    [HarmonyPatch(typeof(KingdomManager), nameof(KingdomManager.update))]
    static class Patch_YearlyTick
    {
        static double _lastCheck = -1;

        static void Postfix()
        {
            double now = World.world.getCurWorldTime();
            // 读档或换世界后时间可能倒退
            if (_lastCheck >= 0 && now >= _lastCheck && now - _lastCheck < Capitulation.YearLength) return;
            _lastCheck = now;
            Government.UpdateAll();
            Cycle.UpdateAll();
        }
    }
}

namespace DiplomacyOverhaul
{
    // 鼠标悬停在疆域上的王国提示：副标题"王国"换成政体名，附庸再接上宗主名
    [HarmonyPatch(typeof(TooltipLibrary), "showKingdom")]
    static class Patch_KingdomTooltip
    {
        static void Postfix(Tooltip pTooltip, TooltipData pData)
        {
            Kingdom k = pData?.kingdom;
            if (k == null) return;
            if (Government.Current(k) == null && !Vassals.IsVassal(k) && Cycle.Get(k) == CyclePhase.None) return;
            pTooltip.setTitle(k.name, Government.TitleKey(k), k.getColor().color_text);
        }
    }
}
