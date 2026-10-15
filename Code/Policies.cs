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
        }

        public bool CanPropose(Kingdom pKingdom) => active && shown(pKingdom) && (proposable == null || proposable(pKingdom));

        // 选项在轴上的位置：三个选项是 -1、0、+1，两个选项是 -1、+1
        public float Position(int pOption) => count <= 1 ? 0f : -1f + 2f * pOption / (count - 1);

        public string OptionKey(int pOption) => id + "_" + pOption;
    }

    public static class Policies
    {
        public const int Default = 1;

        public const string Tax = "vd_pol_tax";
        public const string Territory = "vd_pol_territory";
        public const string Form = "vd_pol_form";
        public const string Army = "vd_pol_army";
        public const string Navy = "vd_pol_navy";
        public const string War = "vd_pol_war";
        public const string Vassals = "vd_pol_vassals";
        public const string Overlord = "vd_pol_overlord";

        // 税收体制：包税制、分成制、国库集中制
        public const int TaxFarming = 0, TaxShared = 1, TaxCentral = 2;
        // 领地制度：分封制、有限分封、中央直辖
        public const int Feudal = 0, LimitedFeudal = 1, DirectRule = 2;
        // 海军方针：陆权优先、海陆并重、海权优先
        public const int LandPower = 0, BalancedForces = 1, SeaPower = 2;
        // 外交路线：和平主义、均势政策、扩张主义
        public const int Pacifism = 0, Balance = 1, Expansion = 2;
        // 附庸政策：自治特许、封臣契约、强制同化
        public const int Autonomy = 0, Contract = 1, Assimilation = 2;
        // 对宗主方针：争取自治、履行契约、忠诚臣属
        public const int SeekAutonomy = 0, Obligation = 1, Loyal = 2;

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
            Add(new Policy { id = Tax, tab = PolicyTab.Domestic, axis = PolicyAxis.Central, icon = "ui/Icons/iconMoney" });
            Add(new Policy
            {
                id = Territory, tab = PolicyTab.Domestic, axis = PolicyAxis.Central, icon = "ui/Icons/iconVillages",
                // 附庸不分封，这一项对它没有意义
                shown = k => !DiplomacyOverhaul.Vassals.IsVassal(k)
            });
            Add(new Policy
            {
                id = Form, tab = PolicyTab.Domestic, axis = PolicyAxis.Radical, count = 2, icon = "ui/Icons/iconKings",
                threshold = 0.75f,
                proposable = k => !Government.IsLocked(k) && !Integration.IsProvince(k),
                read = k => (int)Government.Form(k),
                write = (k, v) => Regime.Reform(k, (GovForm)v)
            });
            // 兵制要等中央军（WORLD-26），先摆出来
            Add(new Policy { id = Army, tab = PolicyTab.Military, axis = PolicyAxis.Central, icon = "ui/Icons/iconArmy", active = false });
            Add(new Policy { id = Navy, tab = PolicyTab.Military, axis = PolicyAxis.Radical, icon = "ui/Icons/iconBoat" });
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

        // 领地制度对分封概率的倍数
        public static float FeudalChance(Kingdom pLord)
        {
            switch (Value(pLord, Territory))
            {
                case Feudal: return 1.5f;
                case DirectRule: return 0f;
                default: return 1f;
            }
        }

        // 海军方针：每个港口运输船的上限。中间一档是原版的 1 艘
        public const string TransportBoat = "boat_type_transport";
        static readonly int[] TransportLimit = { 0, 1, 3 };
        public static readonly int MaxTransports = 3;

        public static int TransportCap(Docks pDocks)
        {
            Kingdom k = pDocks?.building?.city?.kingdom;
            if (k == null || !k.isAlive()) return 1;
            return TransportLimit[Value(k, Navy)];
        }

        // 外交路线：停战期和敢挑的对手的倍数
        public static float WarTimeoutFactor(Kingdom pKingdom)
        {
            if (DiplomacyOverhaul.Vassals.IsVassal(pKingdom)) return 1f;
            switch (Value(pKingdom, War))
            {
                case Pacifism: return 2f;
                case Expansion: return 0.5f;
                default: return 1f;
            }
        }

        public static float WarStrengthFactor(Kingdom pKingdom)
        {
            if (DiplomacyOverhaul.Vassals.IsVassal(pKingdom)) return 1f;
            switch (Value(pKingdom, War))
            {
                case Pacifism: return 0.5f;
                case Expansion: return 1.25f;
                default: return 1f;
            }
        }
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
