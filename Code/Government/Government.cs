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
        public const string HereditaryProvince = "vd_gov_hereditary_province";
        // 旧版的城邦，已取消；旧存档里的会被换掉
        const string LegacyCityState = "vd_gov_city_state";

        // 最近一次设置或生效的政体；与当前政体不一致说明玩家手动改过
        const string KeyAuto = "vd_gov_auto";
        const string KeyLocked = "vd_gov_locked";
        const string KeyForm = "vd_gov_form";
        // 共和国由将领夺权建立
        const string KeyMilitary = "vd_gov_military";

        // 诞生时是共和国的概率，看文化的继承特质
        const float RepublicChanceStrong = 0.5f;  // 外交晋升、黄金法则
        const float RepublicChanceMild = 0.25f;   // 名望之冠
        const float RepublicChanceBase = 0.1f;

        static readonly string[] Ids = { Kingdom, Empire, Republic, MerchantRepublic, MilitaryRepublic, Duchy, Dominion, Province, HereditaryProvince };
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
                        "ui/Icons/vd_gov_military_republic", "ui/Icons/vd_gov_duchy", "ui/Icons/vd_gov_dominion", "ui/Icons/vd_gov_province",
                        "ui/Icons/vd_gov_province" });
            Core.Upsert(AssetManager.loyalty_library, new LoyaltyAsset
            {
                id = "loyalty_vd_republic",
                translation_key = "loyalty_vd_republic",
                translation_key_negative = "loyalty_vd_republic",
                calc = CityLoyalty
            });
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

        // 领袖的称呼按显示的政体取，还没判定政体的按大类；国王、皇帝、执政再按政制换成更贴切的叫法（见 RegimeNames.RulerKey），
        // 护国公和附庸的称呼不换
        public static string RulerKey(Kingdom pKingdom) => RegimeNames.RulerKey(pKingdom, BaseRulerKey(pKingdom));

        static string BaseRulerKey(Kingdom pKingdom)
        {
            switch (Current(pKingdom)?.id)
            {
                case Kingdom: return "vd_ruler_king";
                case Empire: return "vd_ruler_emperor";
                case MilitaryRepublic: return "vd_ruler_protector";
                case Duchy: return "vd_ruler_duke";
                case HereditaryProvince: return "vd_ruler_prince";
                case Province: return "vd_ruler_governor";
                case Republic:
                case MerchantRepublic:
                case Dominion: return "vd_ruler_consul";
                default: return IsRepublic(pKingdom) ? "vd_ruler_consul" : "vd_ruler_king";
            }
        }

        public static string RulerTitle(Kingdom pKingdom) => LM.Get(RulerKey(pKingdom));

        // 含 $title$ 的文案模板按国家的领袖称呼注册成一个 key（「$title$在位(年)」→「皇帝在位(年)」）。
        // 原版界面按 key 显示文字，拼出来的文字要先注册才不会被冲掉
        public static string TitledKey(Kingdom pKingdom, string pTemplate)
        {
            string ruler = RulerKey(pKingdom);
            string key = pTemplate + "_" + ruler;
            LM.AddToCurrentLocale(key, LM.Get(pTemplate).Replace("$title$", LM.Get(ruler)));
            return key;
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
        // 已经有政体特质的（旧存档）按特质定，没有才掷
        static void EnsureForm(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyForm, out int form, -1);
            if (form >= 0) return;
            string current = Current(pKingdom)?.id;
            GovForm? known = FormOf(current);
            if (known != null) SetForm(pKingdom, known.Value, current == MilitaryRepublic);
            else SetForm(pKingdom, Randy.randomChance(RepublicChance(pKingdom.culture)) ? GovForm.Republic : GovForm.Monarchy);
        }

        // 政体特质对应的大类，和自动判定一致：自治领、行省是共和制，公国、世袭行省是君主制
        static GovForm? FormOf(string pGov)
        {
            if (pGov == null) return null;
            if (RepublicIds.Contains(pGov) || pGov == Dominion || pGov == Province) return GovForm.Republic;
            if (pGov == Kingdom || pGov == Empire || pGov == Duchy || pGov == HereditaryProvince) return GovForm.Monarchy;
            return null;
        }

        static float RepublicChance(Culture pCulture)
        {
            if (pCulture == null) return RepublicChanceBase;
            if (pCulture.hasTrait("diplomatic_ascension") || pCulture.hasTrait("golden_rule")) return RepublicChanceStrong;
            if (pCulture.hasTrait("fames_crown")) return RepublicChanceMild;
            return RepublicChanceBase;
        }

        // 开战倾向：停战多少年后才会再开战、敢挑兵力是自己多少倍的对手。王国、帝国同原版（5 年、1 倍）
        // 再乘上外交路线的倍数
        public static int WarTimeout(Kingdom pKingdom)
        {
            int years;
            switch (Current(pKingdom)?.id)
            {
                case Republic: years = 10; break;
                case MerchantRepublic: years = 15; break;
                case MilitaryRepublic: years = 2; break;
                default: years = SimGlobals.m.diplomacy_years_war_timeout; break;
            }
            return UnityEngine.Mathf.RoundToInt(years * Policies.WarTimeoutFactor(pKingdom));
        }

        public static float WarStrengthRatio(Kingdom pKingdom)
        {
            float ratio;
            switch (Current(pKingdom)?.id)
            {
                case MerchantRepublic: ratio = 0.5f; break;
                case MilitaryRepublic: ratio = 1.25f; break;
                default: ratio = 1f; break;
            }
            return ratio * Policies.WarStrengthFactor(pKingdom);
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
                if (Integration.IsProvince(pKingdom)) return Integration.IsHereditary(pKingdom) ? HereditaryProvince : Province;
                return republic ? Dominion : Duchy;
            }
            // 帝国按整个集团的城市数算，分封出去的城也算
            if (!republic) return Vassals.BlocCities(pKingdom) >= Cycle.EmpireCities ? Empire : Kingdom;
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
            // 清掉旧存档里的锁定标记
            data.removeBool(KeyLocked);

            // 玩家在特质编辑器里改过政体：改的是背后的大类和行省状态，称号仍按规则判定
            if (last != null && current != null && current != last && last != LegacyCityState)
                ApplyManual(pKingdom, current);

            string desired = Desired(pKingdom);
            if (desired == null || desired == current) return;
            pKingdom.addTrait(desired, true);
            data.set(KeyAuto, desired);
        }

        // 政体大类和附庸的行省状态跟着玩家的选择走
        static void ApplyManual(Kingdom pKingdom, string pGov)
        {
            pKingdom.data.set(KeyAuto, pGov);
            GovForm? form = FormOf(pGov);
            if (form != null) SetForm(pKingdom, form.Value, pGov == MilitaryRepublic);
            Integration.ApplyTitle(pKingdom, pGov);
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
            foreach (Kingdom k in Clock.Settling())
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                if (Settings.GovernmentAutoEnabled) Update(k);
                else EnsureForm(k);
            }
        }
    }
}
