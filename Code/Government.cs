using System.Collections.Generic;
using HarmonyLib;
using NeoModLoader.General;

namespace DiplomacyOverhaul
{
    public enum GovForm
    {
        Monarchy = 0,
        Republic = 1
    }

    // 政体：一个原版王国特质组，组内特质互斥。
    // 国家记一个政体大类（君主制 / 共和国），诞生时按文化掷出，之后只由历史事件改变；
    // 显示的特质由大类、城市数、附庸状态每年重新算
    public static class Government
    {
        public const string GroupId = "vd_government";
        public const string Kingdom = "vd_gov_kingdom";
        public const string Empire = "vd_gov_empire";
        public const string Republic = "vd_gov_republic";
        public const string MerchantRepublic = "vd_gov_merchant_republic";
        public const string MilitaryRepublic = "vd_gov_military_republic";
        public const string Duchy = "vd_gov_duchy";
        public const string Dominion = "vd_gov_dominion";
        public const string Province = "vd_gov_province";
        // 旧版的城邦，已取消；旧存档里的会被换掉
        const string LegacyCityState = "vd_gov_city_state";

        // 最近一次自动设置的政体；与当前政体不一致说明玩家手动改过
        const string KeyAuto = "vd_gov_auto";
        const string KeyLocked = "vd_gov_locked";
        const string KeyForm = "vd_gov_form";
        // 共和国由将领夺权建立
        const string KeyMilitary = "vd_gov_military";

        // 诞生时是共和国的概率，看文化的继承特质
        const float RepublicChanceStrong = 0.5f;  // 外交晋升、黄金法则
        const float RepublicChanceMild = 0.25f;   // 名望之冠
        const float RepublicChanceBase = 0.1f;

        static readonly string[] Ids = { Kingdom, Empire, Republic, MerchantRepublic, MilitaryRepublic, Duchy, Dominion, Province };
        static readonly HashSet<string> IdSet = new HashSet<string>(Ids);
        static readonly HashSet<string> RepublicIds = new HashSet<string> { Republic, MerchantRepublic, MilitaryRepublic };

        public static void Init()
        {
            // 热重载时库里还留着旧版的城邦特质
            KingdomTrait legacy = AssetManager.kingdoms_traits.dict.TryGetValue(LegacyCityState, out KingdomTrait l) ? l : null;
            if (legacy != null)
            {
                AssetManager.kingdoms_traits.list.Remove(legacy);
                AssetManager.kingdoms_traits.dict.Remove(LegacyCityState);
            }
            AddTraitGroup(GroupId, "#FFD27F", Ids,
                new[] { "ui/Icons/iconKingdom", "ui/Icons/iconKingdomList", "ui/Icons/vd_gov_republic", "ui/Icons/vd_gov_merchant_republic",
                        "ui/Icons/vd_gov_military_republic", "ui/Icons/vd_gov_duchy", "ui/Icons/vd_gov_dominion", "ui/Icons/vd_gov_province" });
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
                KingdomTrait trait = Core.Upsert(AssetManager.kingdoms_traits, new KingdomTrait
                {
                    id = pIds[i],
                    group_id = pGroupId,
                    path_icon = pIcons[i],
                    spawn_random_trait_allowed = false,
                    needs_to_be_explored = false,
                    has_description_2 = false,
                    rarity = Rarity.R0_Normal
                });
                // 热重载时图标可能换了，清掉缓存的旧图
                trait.cached_sprite = null;
            }

            // 特质库在模组加载前已经 link 过，互斥关系要自己补上
            var traits = new List<KingdomTrait>();
            foreach (string id in pIds) traits.Add(AssetManager.kingdoms_traits.get(id));
            foreach (KingdomTrait t in traits)
            {
                // 整组重建：热重载时组员可能变了
                t.opposite_list = new List<string>();
                t.opposite_traits = new HashSet<KingdomTrait>();
                foreach (KingdomTrait other in traits)
                {
                    if (other == t) continue;
                    t.addOpposite(other.id);
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
        // 名牌、悬停提示里显示的国名：后面接上政体（「科英布拉公国」），国名本身不改
        public static string DisplayName(Kingdom pKingdom)
        {
            KingdomTrait gov = Settings.GovernmentNameEnabled ? Current(pKingdom) : null;
            if (gov == null) return pKingdom.name;
            return LM.Get("vd_kingdom_display_name").Replace("$name$", pKingdom.name).Replace("$gov$", LM.Get(gov.getLocaleID()));
        }

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

        public static GovForm Form(Kingdom pKingdom)
        {
            EnsureForm(pKingdom);
            pKingdom.data.get(KeyForm, out int form, 0);
            return (GovForm)form;
        }

        // 不掷政体，只看已经记下的；国家刚建立、文化还没定时用（还没记下的算君主制）
        public static bool IsRepublic(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyForm, out int form, -1);
            return form == (int)GovForm.Republic;
        }

        public static bool IsMilitary(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyMilitary, out bool military, false);
            return military;
        }

        public static bool IsLocked(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyLocked, out bool locked, false);
            return locked;
        }

        // 共和国没有王室：家族轮流执政
        public static void SetForm(Kingdom pKingdom, GovForm pForm, bool pMilitary = false)
        {
            pKingdom.data.set(KeyForm, (int)pForm);
            if (pForm == GovForm.Republic && pMilitary) pKingdom.data.set(KeyMilitary, true);
            else pKingdom.data.removeBool(KeyMilitary);
            if (pForm == GovForm.Republic) pKingdom.data.royal_clan_id = -1L;
        }

        // 新国家（包括旧存档里还没有记录的）掷一次政体。
        // 原版一局里文化很少，文化只改变概率，同一文化下的国家也各不相同
        static void EnsureForm(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyForm, out int form, -1);
            if (form >= 0) return;
            SetForm(pKingdom, Randy.randomChance(RepublicChance(pKingdom.culture)) ? GovForm.Republic : GovForm.Monarchy);
        }

        static float RepublicChance(Culture pCulture)
        {
            if (pCulture == null) return RepublicChanceBase;
            if (pCulture.hasTrait("diplomatic_ascension") || pCulture.hasTrait("golden_rule")) return RepublicChanceStrong;
            if (pCulture.hasTrait("fames_crown")) return RepublicChanceMild;
            return RepublicChanceBase;
        }

        // 开战倾向：停战多少年后才会再开战、敢挑兵力是自己多少倍的对手。王国、帝国同原版（5 年、1 倍）
        public static int WarTimeout(Kingdom pKingdom)
        {
            switch (Current(pKingdom)?.id)
            {
                case Republic: return 10;
                case MerchantRepublic: return 15;
                case MilitaryRepublic: return 2;
                default: return SimGlobals.m.diplomacy_years_war_timeout;
            }
        }

        public static float WarStrengthRatio(Kingdom pKingdom)
        {
            switch (Current(pKingdom)?.id)
            {
                case MerchantRepublic: return 0.5f;
                case MilitaryRepublic: return 1.25f;
                default: return 1f;
            }
        }

        // 诸家族共治的共和国城市更安定；军事共和国没有这份加成
        public const int RepublicLoyalty = 5;

        public static int CityLoyalty(City pCity)
        {
            Kingdom k = pCity.kingdom;
            if (k == null || !IsRepublic(k) || IsMilitary(k)) return 0;
            return RepublicLoyalty;
        }

        static string Desired(Kingdom pKingdom)
        {
            int cities = pKingdom.countCities();
            if (cities <= 0) return null;
            bool republic = Form(pKingdom) == GovForm.Republic;
            if (Vassals.IsVassal(pKingdom))
            {
                if (Integration.IsProvince(pKingdom)) return Province;
                return republic ? Dominion : Duchy;
            }
            if (!republic) return cities >= Cycle.EmpireCities ? Empire : Kingdom;
            if (IsMilitary(pKingdom)) return MilitaryRepublic;
            if (pKingdom.culture != null && pKingdom.culture.hasTrait("golden_rule")) return MerchantRepublic;
            return Republic;
        }

        public static void Update(Kingdom pKingdom)
        {
            if (!pKingdom.isAlive() || !pKingdom.isCiv()) return;
            KingdomData data = pKingdom.data;
            EnsureForm(pKingdom);
            RemoveLegacy(pKingdom);
            string current = Current(pKingdom)?.id;
            data.get(KeyAuto, out string last, null);
            data.get(KeyLocked, out bool locked, false);

            if (locked)
            {
                // 玩家把政体删掉 = 交还给自动判定
                if (current != null) return;
                data.removeBool(KeyLocked);
            }
            else if (last != null && current != null && current != last && last != LegacyCityState)
            {
                // 玩家在特质编辑器里改过政体：锁定，政体大类跟着玩家的选择走
                data.set(KeyLocked, true);
                if (RepublicIds.Contains(current)) SetForm(pKingdom, GovForm.Republic, current == MilitaryRepublic);
                else if (current == Kingdom || current == Empire) SetForm(pKingdom, GovForm.Monarchy);
                return;
            }

            string desired = Desired(pKingdom);
            if (desired == null || desired == current) return;
            pKingdom.addTrait(desired, true);
            data.set(KeyAuto, desired);
        }

        // 旧版的城邦特质：资产已从库里移除，只能按 id 找到实例再删
        static void RemoveLegacy(Kingdom pKingdom)
        {
            foreach (KingdomTrait t in pKingdom.getTraits())
            {
                if (t.id != LegacyCityState) continue;
                pKingdom.removeTrait(t);
                return;
            }
        }

        public static void UpdateAll()
        {
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                if (Settings.GovernmentAutoEnabled) Update(k);
                else EnsureForm(k);
            }
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
            Regime.UpdateAll();
            Government.UpdateAll();
            Cycle.UpdateAll();
            Integration.UpdateAll();
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
            pTooltip.setTitle(Government.DisplayName(k), Government.TitleKey(k), k.getColor().color_text);
        }
    }

    // 地图上的国家名牌：showTextKingdom 里用 kingdom.name 拼名牌文字，这里换成带政体的显示名
    [HarmonyPatch(typeof(NameplateText), nameof(NameplateText.showTextKingdom))]
    static class Patch_KingdomNameplate
    {
        internal static Kingdom Current;

        static void Prefix(Kingdom pMetaObject) => Current = pMetaObject;

        static void Postfix() => Current = null;
    }

    [HarmonyPatch(typeof(NameplateText), "getStringForNameplate")]
    static class Patch_KingdomNameplateText
    {
        static void Prefix(ref string pName)
        {
            if (Patch_KingdomNameplate.Current != null) pName = Government.DisplayName(Patch_KingdomNameplate.Current);
        }
    }
}
