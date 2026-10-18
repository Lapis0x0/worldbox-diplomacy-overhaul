using System;
using System.Collections.Generic;
using HarmonyLib;

namespace DiplomacyOverhaul
{
    // 两条政治轴：保守(-1) → 激进(+1)，分权(-1) → 集权(+1)
    public enum PolicyAxis
    {
        Radical,
        Central
    }

    public enum PolicyTab
    {
        Domestic,
        Military,
        Diplomacy
    }

    // 一项政策的物质得失：每个选项给掌权家族（王室或执政家族）、地方家族各带来多少好处，距离单位，越大越好，
    // 0 是不偏不倚，负数是吃亏（地方家族吃亏时城市忠诚下降，见 Parliament.Grievance）。
    // 地方家族的那份按它的分量放大缩小，两边都按拿主意的人在不在意放大缩小，见 Parliament.Weight、Parliament.Care
    public class Stake
    {
        public float[] ruler, local;

        // 收归中央：越集权掌权家族越得利、地方家族越吃亏，按选项在轴上的位置算，
        // 档数不同的政策挪一档时和国家坐标的变化同比例
        public static Stake Crown(int pCount, float pPerUnit)
        {
            var stake = new Stake { ruler = new float[pCount], local = new float[pCount] };
            for (int i = 0; i < pCount; i++)
            {
                float position = -1f + 2f * i / (pCount - 1);
                stake.ruler[i] = pPerUnit * position;
                stake.local[i] = -pPerUnit * position;
            }
            return stake;
        }
    }

    // 一项国家政策：几个选项依次排在一条轴上。现行选项存在 kingdom.data 里，
    // 国家政体例外，直接读写政体大类
    public class Policy
    {
        public string id;
        public PolicyTab tab;
        public PolicyAxis axis;
        public int count = 3;
        public string icon;
        // 通过要多少议席赞成
        public float threshold = 0.5f;
        // 还没有效果的政策：界面里摆出来但不能点，不提案，不算进国家坐标
        public bool active = true;
        public Func<Kingdom, bool> shown = _ => true;
        public Func<Kingdom, bool> proposable;
        public Func<Kingdom, int> read;
        public Action<Kingdom, int> write;
        // 切身利益：立场之外的得失，加在议案通过后的距离上（正数是吃亏）。
        // 一般写 stake（每个选项的得失），算法特殊的写 interest，两个都没有就只看立场
        public Stake stake;
        public Func<Kingdom, ParliamentClan, int, float> interest;

        public int Get(Kingdom pKingdom)
        {
            if (read != null) return read(pKingdom);
            pKingdom.data.get(id, out int value, -1);
            return value < 0 || value >= count ? Policies.Default : value;
        }

        public void Set(Kingdom pKingdom, int pOption)
        {
            if (write != null) write(pKingdom, pOption);
            else pKingdom.data.set(id, pOption);
            // 海军方针改了陆上单位的伤害，让全国单位重算属性
            if (id == Policies.Navy)
                foreach (Actor a in pKingdom.units) a.setStatsDirty();
        }

        public float Interest(Kingdom pKingdom, ParliamentClan pClan, int pOption)
        {
            if (interest != null) return interest(pKingdom, pClan, pOption);
            if (stake == null) return 0f;
            float[] gain = pClan.ruling ? stake.ruler : stake.local;
            float change = gain[pOption] - gain[Get(pKingdom)];
            return -change * pClan.weight * Parliament.Care(Parliament.Voice(pKingdom, pClan));
        }

        public bool CanPropose(Kingdom pKingdom) => active && shown(pKingdom) && (proposable == null || proposable(pKingdom));

        // 选项在轴上的位置：三个选项是 -1、0、+1，两个选项是 -1、+1
        public float Position(int pOption) => count <= 1 ? 0f : -1f + 2f * pOption / (count - 1);

        public string OptionKey(int pOption) => id + "_" + pOption;

        // 共和国没有王室，有的选项换一个名字
        public string OptionName(Kingdom pKingdom, int pOption)
        {
            string key = OptionKey(pOption);
            if (Government.IsRepublic(pKingdom) && LocalizedTextManager.stringExists(key + "_republic")) key += "_republic";
            return key;
        }
    }

    public static class Policies
    {
        public const int Default = 1;

        public const string Tax = "vd_pol_tax";
        public const string Territory = "vd_pol_territory";
        public const string Form = "vd_pol_form";
        public const string Office = "vd_pol_office";
        public const string Customs = "vd_pol_customs";
        public const string Army = "vd_pol_army";
        public const string Navy = "vd_pol_navy";
        public const string Arms = "vd_pol_arms";
        public const string War = "vd_pol_war";
        public const string Vassals = "vd_pol_vassals";
        public const string Overlord = "vd_pol_overlord";
        public const string VassalTax = "vd_pol_vassal_tax";

        // 税收体制：包税制、分成制、国库集中制
        public const int TaxFarming = 0, TaxShared = 1, TaxCentral = 2;
        // 领地制度：分封制、有限分封、中央直辖
        public const int Feudal = 0, LimitedFeudal = 1, DirectRule = 2;
        // 选官制度：世袭、宗室优先（原版）、唯才是举
        public const int Hereditary = 0, RoyalFirst = 1, Merit = 2;
        // 教化政策：因俗而治、顺其自然、同文同教
        public const int LocalCustoms = 0, NaturalCourse = 1, Uniformity = 2;
        // 海军方针：陆权优先、海陆并重、海权优先
        public const int LandPower = 0, BalancedForces = 1, SeaPower = 2;
        // 外交路线：和平主义、均势政策、扩张主义
        public const int Pacifism = 0, Balance = 1, Expansion = 2;
        // 附庸政策：自治特许、封臣契约、强制同化
        public const int Autonomy = 0, Contract = 1, Assimilation = 2;
        // 对宗主方针：争取自治、履行契约、忠诚臣属
        public const int SeekAutonomy = 0, Obligation = 1, Loyal = 2;

        // 兵和钱收归中央时，掌权家族多得、地方家族交出的切身利益：选项在轴上每挪 1（三档政策的一档）算这么多
        const float CrownStake = 0.2f;
        // 改政体时各家族的切身利益：
        // 改君主制，执政家族会成为王室，其他家族要受人统治；改共和制，王室失去王位，其他家族有机会轮流执政
        const float FormInterest = 0.25f;

        // 原版的贡赋特质：上缴比例改由税收体制决定
        const string TributeLow = "tax_rate_tribute_low";
        const string TributeHigh = "tax_rate_tribute_high";
        const string TributeGroup = "tribute";
        static readonly float[] TributeRates = { 0.2f, 0.5f, 0.7f };

        public static readonly List<Policy> All = new List<Policy>();
        static readonly Dictionary<string, Policy> _byId = new Dictionary<string, Policy>();

        public static Policy Get(string pId) => _byId.TryGetValue(pId, out Policy p) ? p : null;

        public static int Value(Kingdom pKingdom, string pId) => Get(pId).Get(pKingdom);

        public static void Init()
        {
            All.Clear();
            _byId.Clear();
            // 内政按 政体 → 选官 → 领地 → 税收 → 教化 排，界面照这个顺序显示
            Add(new Policy
            {
                id = Form, tab = PolicyTab.Domestic, axis = PolicyAxis.Radical, count = 2, icon = "ui/Icons/iconKings",
                threshold = 0.75f,
                proposable = k => !Integration.IsProvince(k),
                read = k => (int)Government.Form(k),
                write = (k, v) => Regime.Reform(k, (GovForm)v),
                interest = (k, pc, v) => pc.ruling == (v == (int)GovForm.Monarchy) ? -FormInterest : FormInterest
            });
            Add(new Policy { id = Office, tab = PolicyTab.Domestic, axis = PolicyAxis.Radical, icon = "ui/Icons/iconLeaders" });
            Add(new Policy
            {
                id = Territory, tab = PolicyTab.Domestic, axis = PolicyAxis.Central, icon = "ui/Icons/iconVillages",
                // 附庸不分封，这一项对它没有意义
                shown = k => !DiplomacyOverhaul.Vassals.IsVassal(k)
            });
            Add(new Policy { id = Tax, tab = PolicyTab.Domestic, axis = PolicyAxis.Central, icon = "ui/Icons/iconMoney", stake = Stake.Crown(3, CrownStake) });
            Add(new Policy { id = Customs, tab = PolicyTab.Domestic, axis = PolicyAxis.Central, icon = "ui/Icons/iconCulture" });
            Add(new Policy { id = Army, tab = PolicyTab.Military, axis = PolicyAxis.Central, count = 4, icon = "ui/Icons/iconArmy", stake = Stake.Crown(4, CrownStake) });
            Add(new Policy { id = Navy, tab = PolicyTab.Military, axis = PolicyAxis.Radical, icon = "ui/Icons/iconBoat" });
            Add(new Policy { id = Arms, tab = PolicyTab.Military, axis = PolicyAxis.Radical, count = 4, icon = "ui/Icons/iconReforge" });
            Add(new Policy
            {
                id = War, tab = PolicyTab.Diplomacy, axis = PolicyAxis.Radical, icon = "ui/Icons/iconWar",
                // 附庸不自己开战
                shown = k => !DiplomacyOverhaul.Vassals.IsVassal(k)
            });
            Add(new Policy
            {
                id = Vassals, tab = PolicyTab.Diplomacy, axis = PolicyAxis.Central, icon = "ui/Icons/iconKingdomList",
                shown = k => DiplomacyOverhaul.Vassals.GetVassals(k).Count > 0
            });
            Add(new Policy
            {
                id = VassalTax, tab = PolicyTab.Diplomacy, axis = PolicyAxis.Central, count = 4, icon = "ui/Icons/iconMoney",
                shown = k => DiplomacyOverhaul.Vassals.GetVassals(k).Count > 0,
                interest = (k, pc, v) => Treasury.TributeInterest(k, v, Parliament.Voice(k, pc))
            });
            Add(new Policy
            {
                id = Overlord, tab = PolicyTab.Diplomacy, axis = PolicyAxis.Central, icon = "ui/Icons/iconKingdom",
                shown = k => DiplomacyOverhaul.Vassals.IsVassal(k)
            });
            HideTributeTraits();
        }

        static void Add(Policy pPolicy)
        {
            All.Add(pPolicy);
            _byId[pPolicy.id] = pPolicy;
        }

        // 从特质编辑器里拿掉原版的贡赋特质：编辑器列的是库的 list，dict 里留着，
        // 物种默认特质和旧存档里的特质照样能按 id 找到，再由 ConvertTribute 换成政策
        static void HideTributeTraits()
        {
            foreach (string id in new[] { TributeLow, TributeHigh })
            {
                KingdomTrait t = AssetManager.kingdoms_traits.get(id);
                if (t != null) AssetManager.kingdoms_traits.list.Remove(t);
            }
            KingdomTraitGroupAsset group = AssetManager.kingdoms_traits_groups.get(TributeGroup);
            if (group != null) AssetManager.kingdoms_traits_groups.list.Remove(group);
        }

        // 国家身上的原版贡赋特质（物种默认、旧存档）换成税收体制的初始值：
        // 原版精灵默认低贡物，羊族默认高贡物
        public static void ConvertTribute(Kingdom pKingdom)
        {
            foreach (KingdomTrait t in new List<KingdomTrait>(pKingdom.getTraits()))
            {
                if (t.id != TributeLow && t.id != TributeHigh) continue;
                pKingdom.data.get(Tax, out int current, -1);
                if (current < 0) pKingdom.data.set(Tax, t.id == TributeLow ? TaxFarming : TaxCentral);
                pKingdom.removeTrait(t);
            }
        }

        public static float TributeRate(Kingdom pKingdom)
        {
            pKingdom.data.get(Tax, out int value, -1);
            if (value < 0)
            {
                // 还没换掉特质的新国家
                if (pKingdom.hasTrait(TributeLow)) return TributeRates[TaxFarming];
                if (pKingdom.hasTrait(TributeHigh)) return TributeRates[TaxCentral];
                value = TaxShared;
            }
            return TributeRates[value];
        }

        // 国家坐标：各项现行政策的位置在两条轴上分别取平均
        public static (float x, float y) Coordinate(Kingdom pKingdom, Policy pChanged = null, int pOption = -1)
        {
            float sx = 0f, sy = 0f;
            int nx = 0, ny = 0;
            foreach (Policy p in All)
            {
                if (!p.active || !p.shown(pKingdom)) continue;
                int option = p == pChanged ? pOption : p.Get(pKingdom);
                float pos = p.Position(option);
                if (p.axis == PolicyAxis.Radical) { sx += pos; nx++; }
                else { sy += pos; ny++; }
            }
            return (nx > 0 ? sx / nx : 0f, ny > 0 ? sy / ny : 0f);
        }

        // ---------- 各项政策的数值 ----------
        // 三档选项的倍数按选项排：中间一档都是 1（照常）

        // 领地制度：叛乱、攻城改分封的概率，分封宗室、收归直辖的动机
        static readonly float[] GrantFactors = { 1.5f, 1f, 0.5f };
        static readonly float[] AppanageFactors = { 2f, 1f, 0.5f };
        static readonly float[] ReclaimFactors = { 0.5f, 1f, 2f };

        public static float FeudalChance(Kingdom pLord) => GrantFactors[Value(pLord, Territory)];
        public static float AppanageFactor(Kingdom pKingdom) => AppanageFactors[Value(pKingdom, Territory)];
        public static float ReclaimFactor(Kingdom pKingdom) => ReclaimFactors[Value(pKingdom, Territory)];

        // 教化政策：原版忠诚里文化、语言、宗教不同的扣分
        static readonly float[] ForeignLoyaltyFactors = { 0.5f, 1f, 1.5f };

        public static float ForeignLoyaltyFactor(Kingdom pKingdom) => ForeignLoyaltyFactors[Value(pKingdom, Customs)];

        // 海军方针：每个港口运输船的上限，陆上单位的伤害倍数
        public const string TransportBoat = "boat_type_transport";
        static readonly int[] TransportLimit = { 1, 2, 4 };
        public static readonly int MaxTransports = 4;
        static readonly float[] LandDamageFactors = { 1.15f, 1f, 0.85f };

        public static float LandDamageFactor(Kingdom pKingdom) => LandDamageFactors[Value(pKingdom, Navy)];

        public static int TransportCap(Docks pDocks)
        {
            Kingdom k = pDocks?.building?.city?.kingdom;
            if (k == null || !k.isAlive()) return 1;
            return TransportLimit[Value(k, Navy)];
        }

        // 外交路线：停战期和敢挑的对手的倍数
        static readonly float[] WarTimeoutFactors = { 2f, 1f, 0.5f };
        static readonly float[] WarStrengthFactors = { 0.5f, 1f, 1.25f };

        public static float WarTimeoutFactor(Kingdom pKingdom) =>
            DiplomacyOverhaul.Vassals.IsVassal(pKingdom) ? 1f : WarTimeoutFactors[Value(pKingdom, War)];

        public static float WarStrengthFactor(Kingdom pKingdom) =>
            DiplomacyOverhaul.Vassals.IsVassal(pKingdom) ? 1f : WarStrengthFactors[Value(pKingdom, War)];

        // 附庸政策（宗主的）、对宗主方针（附庸自己的）：整合度每年的变动
        static readonly float[] VassalsDrift = { -1f, 0f, 0f };
        static readonly float[] OverlordDrift = { -1f, 0f, 1f };
        // 对宗主方针：独立密谋动机的倍数
        static readonly float[] IndependenceFactors = { 1.5f, 1f, 0.5f };

        public static float IntegrationDrift(Kingdom pVassal, Kingdom pLord) =>
            VassalsDrift[Value(pLord, Vassals)] + OverlordDrift[Value(pVassal, Overlord)];

        public static float IndependenceFactor(Kingdom pVassal) => IndependenceFactors[Value(pVassal, Overlord)];

        // 附庸贡赋（宗主的）：附庸国库的进账交给宗主的比例，和附庸对宗主的好感
        static readonly float[] VassalTaxRates = { 0f, 0.25f, 0.5f, 0.75f };
        static readonly int[] VassalTaxOpinions = { 15, 0, -30, -50 };

        public static float VassalTaxRate(Kingdom pLord) => VassalTaxRates[Value(pLord, VassalTax)];

        public static int VassalTaxOpinion(Kingdom pLord) => VassalTaxOpinions[Value(pLord, VassalTax)];

        public static int VassalTaxOpinionOf(int pOption) => VassalTaxOpinions[pOption];

        // 选项悬停提示里的数字行：偏离照常的项才列，上缴比例和运输船上限每档都列
        public static List<(string key, string value)> Effects(Kingdom pKingdom, Policy pPolicy, int pOption)
        {
            var rows = new List<(string, string)>();
            bool republic = Government.IsRepublic(pKingdom);
            switch (pPolicy.id)
            {
                case Tax:
                    rows.Add((republic ? "vd_eff_tribute_republic" : "vd_eff_tribute", Percent(TributeRates[pOption])));
                    break;
                case Territory:
                    if (pOption == LimitedFeudal) break;
                    rows.Add(("vd_eff_grant", Change(GrantFactors[pOption])));
                    // 共和国没有王室，不分封宗室
                    if (!republic) rows.Add(("vd_eff_appanage", Change(AppanageFactors[pOption])));
                    rows.Add(("vd_eff_reclaim", Change(ReclaimFactors[pOption])));
                    break;
                case Customs:
                    if (pOption == NaturalCourse) break;
                    if (pOption == Uniformity) rows.Add(("vd_eff_uniformity", Percent(DiplomacyOverhaul.Assimilation.DomesticYearlyShare)));
                    rows.Add(("vd_eff_foreign_loyalty", Change(ForeignLoyaltyFactors[pOption])));
                    break;
                case Navy:
                    rows.Add(("vd_eff_transports", TransportLimit[pOption].ToString()));
                    if (LandDamageFactors[pOption] != 1f) rows.Add(("vd_eff_land_damage", Change(LandDamageFactors[pOption])));
                    break;
                case War:
                    if (pOption == Balance) break;
                    rows.Add(("vd_eff_war_timeout", Change(WarTimeoutFactors[pOption])));
                    rows.Add(("vd_eff_war_strength", Change(WarStrengthFactors[pOption])));
                    break;
                case Vassals:
                    if (pOption == Autonomy)
                    {
                        rows.Add(("vd_eff_vassal_opinion", Signed(DiplomacyOverhaul.Assimilation.AutonomyOpinion)));
                        rows.Add(("vd_eff_vassal_integration", Signed(VassalsDrift[pOption])));
                    }
                    else if (pOption == Assimilation)
                    {
                        rows.Add(("vd_eff_assimilation", Percent(DiplomacyOverhaul.Assimilation.VassalYearlyShare)));
                        rows.Add(("vd_eff_vassal_resent", Signed(DiplomacyOverhaul.Assimilation.ResentOpinion)));
                    }
                    break;
                case Army:
                    rows.Add(("vd_eff_guard_share", Percent(Guard.RateOf(pOption))));
                    break;
                case Arms:
                    rows.Add(("vd_eff_arms_budget", Percent(Armory.BudgetRate(pOption))));
                    break;
                case VassalTax:
                    rows.Add(("vd_eff_vassal_tax", Percent(VassalTaxRates[pOption])));
                    if (VassalTaxOpinions[pOption] != 0) rows.Add(("vd_eff_vassal_opinion", Signed(VassalTaxOpinions[pOption])));
                    break;
                case Overlord:
                    if (pOption == Obligation) break;
                    rows.Add(("vd_eff_integration", Signed(OverlordDrift[pOption])));
                    rows.Add(("vd_eff_independence", Change(IndependenceFactors[pOption])));
                    break;
            }
            return rows;
        }

        static string Percent(float pValue) => UnityEngine.Mathf.RoundToInt(pValue * 100f) + "%";

        static string Signed(float pValue)
        {
            int v = UnityEngine.Mathf.RoundToInt(pValue);
            return v > 0 ? "+" + v : v.ToString();
        }

        // 倍数写成增减的百分比：×1.5 是 +50%，×0.5 是 -50%
        static string Change(float pFactor) => Signed((pFactor - 1f) * 100f) + "%";
    }

    // 城主上缴国王的比例由税收体制决定，不再看原版的贡赋特质
    [HarmonyPatch(typeof(Kingdom), nameof(Kingdom.getTaxRateTribute))]
    static class Patch_TributeRate
    {
        static bool Prefix(Kingdom __instance, ref float __result)
        {
            __result = Policies.TributeRate(__instance);
            return false;
        }
    }

    // 单位伤害的倍数：海军方针只管陆上单位，天命（王朝周期）管全国单位，欠饷管士兵，几项相乘
    [HarmonyPatch(typeof(Actor), "updateStats")]
    static class Patch_LandDamage
    {
        static void Postfix(Actor __instance)
        {
            Kingdom k = __instance.kingdom;
            if (k == null || !k.isCiv()) return;
            float factor = __instance.asset.is_boat ? 1f : Policies.LandDamageFactor(k);
            factor *= 1f + Cycle.DamagePercent(k) / 100f;
            factor *= 1f - Treasury.Penalty(__instance);
            if (factor != 1f) __instance.stats["damage"] *= factor;
        }
    }

    // 海军方针：原版每个港口每种船最多 1 艘，写死在这几个方法里。运输船的上限换成政策给的数；
    // 「超额」只按最高的上限算，政策改小时已有的船不会被原版当成超额炸掉
    [HarmonyPatch(typeof(Docks), nameof(Docks.isFull))]
    static class Patch_DocksFull
    {
        static bool Prefix(Docks __instance, string pType, ref bool __result)
        {
            if (pType != Policies.TransportBoat) return true;
            __result = __instance.countBoatTypes(pType) >= Policies.TransportCap(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(Docks), nameof(Docks.isOverfilled))]
    static class Patch_DocksOverfilled
    {
        static bool Prefix(Docks __instance, string pType, ref bool __result)
        {
            if (pType != Policies.TransportBoat) return true;
            __result = __instance.countBoatTypes(pType) > Policies.MaxTransports;
            return false;
        }
    }

    // 照抄原版造船，只把「这种船已经有 1 艘就不造」换成按上限
    [HarmonyPatch(typeof(Docks), nameof(Docks.buildBoatFromHere))]
    static class Patch_DocksBuildBoat
    {
        static bool Prefix(Docks __instance, City pCity, ref Actor __result)
        {
            __result = null;
            ActorAsset asset = __instance.building.asset.getRandomBoatAssetToBuild(pCity);
            if (asset == null) return false;
            int limit = asset.boat_type == Policies.TransportBoat ? Policies.TransportCap(__instance) : 1;
            if (__instance.countBoatTypes(asset.boat_type) >= limit) return false;
            if (!pCity.hasEnoughResourcesFor(asset.cost)) return false;
            if (__instance.tiles_ocean.Count == 0)
            {
                __instance.recalculateOceanTiles();
                return false;
            }
            WorldTile tile = __instance.tiles_ocean.GetRandom();
            if (!tile.region.island.goodForDocks()) return false;
            Actor boat = World.world.units.createNewUnit(asset.id, tile);
            if (boat == null) return false;
            __instance.addBoatToDock(boat);
            pCity.spendResourcesForBuildingAsset(asset.cost);
            __result = boat;
            return false;
        }
    }
}
