using System;
using System.Linq;
using System.Collections.Generic;
using ai.behaviours;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 军械：国家给禁军、各城给本城的地方兵置办制式装备（一种近战武器或弓，加同一材质的头盔、胸甲、靴子）。
    // 每年发完军饷后结算，先国家后城市。国家留出两年禁军军饷，余下的按「军械拨款」的比例置办；
    // 城市留出两年地方兵军饷和一栋建筑的钱，余下的拿一半置办。
    // 材料：国家先按官价收购各城仓库的存货，钱进卖方城市的仓库；城市只用自己仓库的。普通金属不够的向商队买，
    // 秘银、精金只能靠本国（本城）。
    // 装备最差的士兵先换：先从仓库里挑比身上好的（不比制式差的直接用，差的先凑合），再花钱买。
    // 换下的自家造的装备回炉，按材料官价退一半；士兵自己的旧装备还给户籍城的仓库。
    // 九成士兵达到现行制式后，第二年才升一档（新兵年年有，不等全军）；士兵自己有更好的装备就留着。
    // 自家造的装备由出钱的一方修：每年按损耗收修理费，原版的免费修理不管它们
    public static class Armory
    {
        const string KeyTier = "vd_arms_tier";            // kingdom.data / city.data：现行制式的档次
        internal const string KeyRole = "vd_arms_role";   // actor.data：兵种，第一次领制式时定下
        const string FlagOfficial = "vd_arms_official";   // item.data：国家造的装备
        const string FlagLocal = "vd_arms_local";         // item.data：城市造的装备
        const int RoleMelee = 1, RoleArcher = 2;

        const int ReserveYears = 2;          // 先留出几年的军饷
        const int CityBuildReserve = 100;    // 城市再留一栋最贵的建筑的钱（原版要 100 金子）
        const float CityBudgetRate = 0.5f;   // 城市拿储备以外的几成置办
        const int OfficialTries = 3;         // 国家造的装备抽几次词条（原版民间打造是 1 次）
        const int LocalTries = 2;            // 城市造的
        const int CityReserve = 10;          // 每座城留底的材料，原版打造装备要仓库里有 10 份以上
        const float RepairShare = 0.5f;      // 修满一件全坏的装备花它价钱的几成，同原版修理费的系数
        const float MeltShare = 0.5f;        // 回炉退回材料官价的几成
        const float ArcherShare = 0.25f;     // 弓手的比例
        const float ArcherShareBowLovers = 0.5f;
        const float UpgradeShare = 0.9f;     // 多少士兵达到现行制式才升档

        static readonly float[] BudgetRates = { 0f, 0.25f, 0.5f, 1f };
        const int Replacements = 1;          // 补缺换损：只补发，不升级制式

        // 制式的档次：材质、要的材料和份数、每份的官价和商队的价钱（0 是商队不卖）
        struct Tier
        {
            public string material, resource;
            public int amount, price, import, value;
        }

        const int MetalPrice = 2, MetalImport = 5;

        static readonly Tier[] Tiers =
        {
            new Tier { material = "copper", resource = "common_metals", amount = 1, price = MetalPrice, import = MetalImport, value = 10 },
            new Tier { material = "bronze", resource = "common_metals", amount = 2, price = MetalPrice, import = MetalImport, value = 15 },
            new Tier { material = "iron", resource = "common_metals", amount = 3, price = MetalPrice, import = MetalImport, value = 30 },
            new Tier { material = "steel", resource = "common_metals", amount = 4, price = MetalPrice, import = MetalImport, value = 40 },
            new Tier { material = "mythril", resource = "mythril", amount = 1, price = 20, value = 50 },
            new Tier { material = "adamantine", resource = "adamantine", amount = 1, price = 40, value = 70 },
        };

        static readonly EquipmentType[] Slots = { EquipmentType.Weapon, EquipmentType.Helmet, EquipmentType.Armor, EquipmentType.Boots };

        // 出钱置办军械的一方：国家（city 为空）给禁军，城市给本城的地方兵
        class Arsenal
        {
            public Kingdom kingdom;
            public City city;
            public List<Actor> members;
            public int reserve;
            public int capacity;   // 编制人数：升档看达标的人数占编制的比例，打空了不算达标
            public float rate;
            public bool upgrades;

            public BaseSystemData Data => city != null ? (BaseSystemData)city.data : kingdom.data;
            public string Flag => city != null ? FlagLocal : FlagOfficial;
            public int Tries => city != null ? LocalTries : OfficialTries;
            public Culture Culture => city != null ? city.getCulture() : kingdom.getCulture();
            public int Gold => city != null ? city.getResourcesAmount("gold") : Treasury.Get(kingdom);

            // 材料和回收的来源：国家是本国各城，城市只有自己
            public IEnumerable<City> Stores => city != null ? new[] { city } : kingdom.getCities();

            // 本国（本城）的存货每份多少钱：城市用自己的不花钱
            public int DomesticPrice(Tier pTier) => city != null ? 0 : pTier.price;

            public void Spend(int pAmount)
            {
                if (pAmount <= 0) return;
                if (city != null) city.takeResource("gold", Mathf.Min(pAmount, Gold));
                else Ledger.Record(kingdom, Ledger.Flow.Arms, Treasury.Take(kingdom, pAmount));
            }

            public void Refund(int pAmount)
            {
                if (pAmount <= 0) return;
                if (city != null) city.addResourcesToRandomStockpile("gold", pAmount);
                else
                {
                    Treasury.Add(kingdom, pAmount);
                    Ledger.Record(kingdom, Ledger.Flow.Arms, -pAmount);
                }
            }
        }

        public static float BudgetRate(int pOption) => BudgetRates[pOption];

        static int TierOf(BaseSystemData pData)
        {
            pData.get(KeyTier, out int v, 0);
            return Mathf.Clamp(v, 0, Tiers.Length - 1);
        }

        public static int TierOf(Kingdom pKingdom) => TierOf(pKingdom.data);

        public static int TierOf(City pCity) => TierOf(pCity.data);

        public static string TierKey(Kingdom pKingdom) => "vd_arms_tier_" + TierOf(pKingdom);

        public static string TierKey(City pCity) => "vd_arms_tier_" + TierOf(pCity);

        // 一件装备的价钱：本国（本城）存货按官价，不够的按商队的价钱，加上工钱（按档次）。材料凑不齐返回 -1
        static int Cost(Arsenal pArsenal, Tier pTier)
        {
            int domestic = Mathf.Min(pTier.amount, Stock(pArsenal.Stores, pTier.resource));
            int rest = pTier.amount - domestic;
            if (rest > 0 && pTier.import == 0) return -1;
            return domestic * pArsenal.DomesticPrice(pTier) + rest * pTier.import + Mathf.CeilToInt(pTier.value / 10f);
        }

        // 一件装备按商队价算的价钱（秘银、精金按官价），修理费照这个算
        static int ListPrice(Tier pTier) =>
            pTier.amount * (pTier.import > 0 ? pTier.import : pTier.price) + Mathf.CeilToInt(pTier.value / 10f);

        public static bool IsOfficial(Item pItem) => pItem?.data != null && pItem.data.hasFlag(FlagOfficial);

        public static bool IsLocal(Item pItem) => pItem?.data != null && pItem.data.hasFlag(FlagLocal);

        // 升到这一档要材料够全军换装：商队卖的总买得到，秘银、精金看本国（本城）的存货
        static bool Available(Arsenal pArsenal, Tier pTier) =>
            pTier.import > 0 || Stock(pArsenal.Stores, pTier.resource) >= pArsenal.members.Count * Slots.Length * pTier.amount;

        public static void UpdateAll()
        {
            if (!Military.On) return;
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                int option = Policies.Value(k, Policies.Arms);
                if (option > 0)
                    Update(new Arsenal
                    {
                        kingdom = k,
                        members = Members(k),
                        reserve = Mathf.CeilToInt(Guard.KingdomMembers(k) * Treasury.PayPerWarrior * ReserveYears),
                        capacity = k.getCities().Sum(c => Guard.Quota(c)),
                        rate = BudgetRates[option],
                        upgrades = option > Replacements
                    });
                foreach (City city in new List<City>(k.getCities()))
                    Update(new Arsenal
                    {
                        kingdom = k,
                        city = city,
                        members = LocalMembers(city),
                        capacity = city.getMaxWarriors() - Guard.Quota(city),
                        reserve = Mathf.CeilToInt(Guard.CountLocal(city) * Treasury.PayPerWarrior * ReserveYears) + CityBuildReserve,
                        rate = CityBudgetRate,
                        upgrades = true
                    });
            }
        }

        static void Update(Arsenal pArsenal)
        {
            List<Actor> members = pArsenal.members;
            if (members.Count == 0) return;

            string melee = MeleeWeapon(pArsenal.Culture);
            int tier = TierOf(pArsenal.Data);
            if (pArsenal.upgrades && tier < Tiers.Length - 1
                && members.FindAll(a => !Short(a, melee, tier)).Count >= Mathf.Max(pArsenal.capacity, members.Count) * UpgradeShare
                && Available(pArsenal, Tiers[tier + 1]))
            {
                tier++;
                pArsenal.Data.set(KeyTier, tier);
            }

            Repair(pArsenal, pArsenal.Gold - pArsenal.reserve);

            float archers = ArcherShareOf(pArsenal.Culture);
            int archerCount = 0, assigned = 0;
            foreach (Actor a in members)
            {
                a.data.get(KeyRole, out int role, 0);
                if (role == 0) continue;
                assigned++;
                if (role == RoleArcher) archerCount++;
            }
            members.Sort((x, y) => Score(x).CompareTo(Score(y)));
            foreach (Actor a in members) Role(a, archers, ref archerCount, ref assigned);

            Reclaim(pArsenal, melee, tier);

            int budget = Mathf.FloorToInt((pArsenal.Gold - pArsenal.reserve) * pArsenal.rate);
            if (budget <= 0) return;
            int spent = 0;
            bool broke = false;
            foreach (Actor a in members)
            {
                a.data.get(KeyRole, out int role, RoleMelee);
                foreach (EquipmentType slot in Slots)
                {
                    string subtype = slot == EquipmentType.Weapon ? (role == RoleArcher ? "bow" : melee) : SlotName(slot);
                    if (!Needs(a, slot, subtype, tier)) continue;
                    broke = !Issue(pArsenal, a, slot, subtype, tier, budget - spent, ref spent);
                    if (broke) break;
                }
                if (broke) break;
            }
            pArsenal.Spend(spent);
        }

        // 修自家造的装备：先于置办新装备，用储备以外的钱，不按比例。修理费按损耗的比例算，钱不够就停
        static void Repair(Arsenal pArsenal, int pBudget)
        {
            float spent = 0f;
            bool broke = false;
            foreach (Actor a in pArsenal.members)
            {
                bool repaired = false;
                foreach (ActorEquipmentSlot slot in a.equipment)
                {
                    Item item = slot.getItem();
                    if (!item.data.hasFlag(pArsenal.Flag) || !item.needRepair()) continue;
                    int t = System.Array.FindIndex(Tiers, x => x.material == item.getAsset().material);
                    if (t < 0) continue;
                    float fee = ListPrice(Tiers[t]) * RepairShare * (1f - item.getDurabilityCurrent() / (float)item.getDurabilityMax());
                    broke = spent + fee > pBudget;
                    if (broke) break;
                    spent += fee;
                    item.data.durability = item.getDurabilityMax();
                    repaired = true;
                }
                if (repaired) a.setStatsDirty();
                if (broke) break;
            }
            pArsenal.Spend(Mathf.CeilToInt(spent));
        }

        // 回收：仓库里的装备，挑比身上好的发给不到制式的士兵，每次拿最好的一件。
        // 武器要合兵种：弓手只拿弓，近战兵只拿近战武器
        static void Reclaim(Arsenal pArsenal, string pMelee, int pTier)
        {
            foreach (Actor a in pArsenal.members)
            {
                a.data.get(KeyRole, out int role, RoleMelee);
                foreach (EquipmentType type in Slots)
                {
                    string subtype = type == EquipmentType.Weapon ? (role == RoleArcher ? "bow" : pMelee) : SlotName(type);
                    if (!Needs(a, type, subtype, pTier)) continue;
                    ActorEquipmentSlot slot = a.equipment.getSlot(type);
                    int current = slot.getItem()?.getAsset().equipment_value ?? 0;
                    City from = null;
                    Item best = null;
                    foreach (City city in pArsenal.Stores)
                        foreach (long id in city.data.equipment.getEquipmentList(type))
                        {
                            Item item = World.world.items.get(id);
                            if (item == null || item.isCursed()) continue;
                            EquipmentAsset asset = item.getAsset();
                            if (asset.equipment_value <= current || (best != null && asset.equipment_value <= best.getAsset().equipment_value)) continue;
                            if (type == EquipmentType.Weapon && (role == RoleArcher ? asset.group_id != "bow" : asset.attack_type != WeaponType.Melee)) continue;
                            best = item;
                            from = city;
                        }
                    if (best == null) continue;
                    from.data.equipment.getEquipmentList(type).Remove(best.id);
                    from._storage_version++;
                    Retire(pArsenal, a, slot);
                    slot.setItem(best, a);
                }
            }
        }

        static List<Actor> Warriors(City pCity) =>
            pCity._professions_dict.TryGetValue(UnitProfession.Warrior, out List<Actor> list) ? list : null;

        // 禁军
        internal static List<Actor> Members(Kingdom pKingdom)
        {
            var list = new List<Actor>();
            foreach (City city in pKingdom.getCities())
            {
                List<Actor> warriors = Warriors(city);
                if (warriors == null) continue;
                foreach (Actor a in warriors)
                    if (a.isAlive() && Guard.IsMember(a) && a.understandsHowToUseItems()) list.Add(a);
            }
            return list;
        }

        // 本城的地方兵
        internal static List<Actor> LocalMembers(City pCity)
        {
            var list = new List<Actor>();
            List<Actor> warriors = Warriors(pCity);
            if (warriors == null) return list;
            foreach (Actor a in warriors)
                if (a.isAlive() && !Guard.IsMember(a) && a.understandsHowToUseItems()) list.Add(a);
            return list;
        }

        // 文化偏好的近战武器，没有就用剑
        static string MeleeWeapon(Culture pCulture)
        {
            if (pCulture != null)
                foreach (string s in pCulture._preferred_weapons_craft_subtypes)
                    if (s != "bow" && AssetManager.items.get(s + "_" + Tiers[0].material) != null) return s;
            return "sword";
        }

        static float ArcherShareOf(Culture pCulture) =>
            pCulture != null && pCulture.hasTrait("bow_lovers") ? ArcherShareBowLovers : ArcherShare;

        // 兵种第一次领制式时定下：手里拿着弓的当弓手，其余按比例补足弓手
        static int Role(Actor pActor, float pShare, ref int pArchers, ref int pAssigned)
        {
            pActor.data.get(KeyRole, out int role, 0);
            if (role != 0) return role;
            bool bow = pActor.equipment.weapon.getItem()?.getAsset().group_id == "bow";
            role = bow || pArchers < pShare * (pAssigned + 1) ? RoleArcher : RoleMelee;
            pActor.data.set(KeyRole, role);
            pAssigned++;
            if (role == RoleArcher) pArchers++;
            return role;
        }

        static string SlotName(EquipmentType pSlot) =>
            pSlot == EquipmentType.Helmet ? "helmet" : pSlot == EquipmentType.Armor ? "armor" : "boots";

        static int Score(Actor pActor)
        {
            int sum = 0;
            foreach (EquipmentType slot in Slots)
                sum += pActor.equipment.getSlot(slot).getItem()?.getAsset().equipment_value ?? 0;
            return sum;
        }

        // 这个兵的这件装备不到制式：空着、档次不到，或同一档次但材质、武器种类和制式不一样（比制式好的不算）
        static bool Needs(Actor pActor, EquipmentType pSlot, string pSubtype, int pTier)
        {
            Item item = pActor.equipment.getSlot(pSlot).getItem();
            if (item == null) return true;
            if (item.isCursed()) return false;
            EquipmentAsset asset = item.getAsset();
            int value = asset.equipment_value;
            int standard = Tiers[pTier].value;
            if (value > standard) return false;
            if (value < standard) return true;
            return asset.material != Tiers[pTier].material || asset.group_id != pSubtype;
        }

        // 这个兵还有装备不到这一档的制式
        static bool Short(Actor pActor, string pMelee, int pTier)
        {
            pActor.data.get(KeyRole, out int role, RoleMelee);
            foreach (EquipmentType slot in Slots)
            {
                string subtype = slot == EquipmentType.Weapon ? (role == RoleArcher ? "bow" : pMelee) : SlotName(slot);
                if (Needs(pActor, slot, subtype, pTier)) return true;
            }
            return false;
        }

        // 发一件：从制式往下挑材料买得到的一档（不比手上的差）。钱不够返回 false，今年不再置办。
        // 普通金属商队总有，凑不齐材料的只会是秘银、精金，这时退到钢
        static bool Issue(Arsenal pArsenal, Actor pActor, EquipmentType pSlot, string pSubtype, int pTier, int pBudget, ref int pSpent)
        {
            ActorEquipmentSlot slot = pActor.equipment.getSlot(pSlot);
            int current = slot.getItem()?.getAsset().equipment_value ?? 0;
            for (int t = pTier; t >= 0; t--)
            {
                Tier tier = Tiers[t];
                if (tier.value < current) break;
                EquipmentAsset asset = AssetManager.items.get(pSubtype + "_" + tier.material) as EquipmentAsset;
                if (asset == null) continue;
                int cost = Cost(pArsenal, tier);
                if (cost < 0) continue;
                if (cost > pBudget) return false;
                Buy(pArsenal, tier.resource, tier.amount, pArsenal.DomesticPrice(tier));
                pSpent += cost;
                Item item = World.world.items.generateItem(asset, pActor.kingdom, pArsenal.city?.name ?? pArsenal.kingdom.name, pArsenal.Tries, pActor);
                item.data.addFlag(pArsenal.Flag);
                Retire(pArsenal, pActor, slot);
                slot.setItem(item, pActor);
                pActor.setStatsDirty();
                return true;
            }
            return true;
        }

        // 换下这件旧装备：自家造的回炉，退钱（玩家收藏的、永恒的不能销毁，照私物处理）；
        // 别处来的、士兵自己的还给户籍城的仓库
        static void Retire(Arsenal pArsenal, Actor pActor, ActorEquipmentSlot pSlot)
        {
            Item old = pSlot.getItem();
            if (old == null) return;
            pSlot.takeAwayItem();
            int t = System.Array.FindIndex(Tiers, x => x.material == old.getAsset().material);
            if (old.data.hasFlag(pArsenal.Flag) && old.isDestroyable() && t >= 0)
            {
                pArsenal.Refund(Mathf.FloorToInt(Tiers[t].amount * Tiers[t].price * MeltShare));
                return;
            }
            pActor.city?.tryToPutItem(old);
        }

        // 仓库里能拿来打装备的材料：每座城留底，多出的才用
        static int Stock(IEnumerable<City> pCities, string pResource)
        {
            int sum = 0;
            foreach (City city in pCities)
                sum += Mathf.Max(0, city.getResourcesAmount(pResource) - CityReserve);
            return sum;
        }

        internal static int Stock(Kingdom pKingdom, string pResource) => Stock(pKingdom.getCities(), pResource);

        // 用仓库里的存货：存货多的城先出，按官价付钱给它（城市用自己的不付钱）。不够的部分是向商队买的，钱流出本国
        static void Buy(Arsenal pArsenal, string pResource, int pAmount, int pPrice)
        {
            while (pAmount > 0)
            {
                City seller = null;
                int best = 0;
                foreach (City city in pArsenal.Stores)
                {
                    int surplus = city.getResourcesAmount(pResource) - CityReserve;
                    if (surplus <= best) continue;
                    seller = city;
                    best = surplus;
                }
                if (seller == null) return;
                int n = Mathf.Min(best, pAmount);
                seller.takeResource(pResource, n);
                if (pPrice > 0) seller.addResourcesToRandomStockpile("gold", n * pPrice);
                pAmount -= n;
            }
        }
    }

    // 原版士兵在有兵营的城里免费修装备。禁军身上的国家造装备、地方兵身上的城市造装备由出钱的一方修
    // （见 Armory.Repair），这里跳过；其余照抄原版
    [HarmonyPatch(typeof(BehCheckCanRepairEquipment), nameof(BehCheckCanRepairEquipment.execute))]
    static class Patch_CheckRepair
    {
        static bool Prefix(Actor pActor, ref BehResult __result)
        {
            if (!Military.On || !Safety.On(Sys.Military)) return true;
            try
            {
                BehResult result = BehResult.Stop;
                if (pActor.hasEquipment())
                {
                    foreach (ActorEquipmentSlot slot in pActor.equipment)
                    {
                        Item item = slot.getItem();
                        if (!item.needRepair() || Patch_Repair.Maintained(pActor, item)) continue;
                        int cost = (int)(item.getAsset().cost_gold * SimGlobals.m.item_repair_cost_multiplier);
                        if (pActor.money >= cost) result = BehResult.Continue;
                    }
                }
                __result = result;
                return false;
            }
            catch (Exception e)
            {
                Safety.Fail(Sys.Military, "检查要不要修装备", e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(BehRepairEquipment), nameof(BehRepairEquipment.execute))]
    static class Patch_Repair
    {
        internal static bool Maintained(Actor pActor, Item pItem) =>
            Guard.IsMember(pActor) ? Armory.IsOfficial(pItem) : Armory.IsLocal(pItem);

        static bool Prefix(Actor pActor, ref BehResult __result)
        {
            if (!Military.On || !Safety.On(Sys.Military)) return true;
            bool repaired = false;
            try
            {
                foreach (ActorEquipmentSlot slot in pActor.equipment)
                {
                    Item item = slot.getItem();
                    if (!item.needRepair() || Maintained(pActor, item)) continue;
                    int cost = (int)(item.getAsset().cost_gold * SimGlobals.m.item_repair_cost_multiplier);
                    if (!pActor.hasEnoughMoney(cost)) continue;
                    pActor.spendMoney(cost);
                    item.fullRepair();
                    repaired = true;
                }
                if (repaired) pActor.setStatsDirty();
                __result = BehResult.Continue;
                return false;
            }
            catch (Exception e)
            {
                Safety.Fail(Sys.Military, "修装备", e);
                // 已经修了一部分就不再交给原版，免得再扣一遍钱
                if (!repaired) return true;
                pActor.setStatsDirty();
                __result = BehResult.Continue;
                return false;
            }
        }
    }
}
