using System.Collections.Generic;
using ai.behaviours;
using HarmonyLib;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 军械：国库按「军械拨款」给禁军置办制式装备（一种近战武器或弓，加同一材质的头盔、胸甲、靴子）。
    // 每年发完军饷后结算：国库留出两年禁军军饷，余下的按议案的比例拿来置办。材料先按官价收购各城仓库的存货，
    // 钱进卖方城市的仓库；普通金属不够的向商队买，价钱更高，秘银、精金只能靠本国。
    // 装备最差的士兵先换：先从本国各城的仓库里挑比身上好的（不比制式差的直接用，差的先凑合），再花钱买。
    // 换下的旧装备交回户籍城的仓库。
    // 九成禁军达到现行制式后，第二年才升一档（新兵年年有，不等全军）；士兵自己有更好的装备就留着。
    // 禁军身上的官造装备由国家修：每年按损耗收修理费，原版的免费修理不管它们
    public static class Armory
    {
        const string KeyTier = "vd_arms_tier";            // kingdom.data：现行制式的档次
        internal const string KeyRole = "vd_arms_role";   // actor.data：兵种，第一次领制式时定下
        const string FlagOfficial = "vd_arms_official";   // item.data：官造装备
        const int RoleMelee = 1, RoleArcher = 2;

        const int ReserveYears = 2;          // 国库先留出几年的禁军军饷
        const int OfficialTries = 3;         // 官造装备抽几次词条（原版民间打造是 1 次）
        const int CityReserve = 10;          // 每座城留底的材料，原版打造装备要仓库里有 10 份以上
        const float RepairShare = 0.5f;      // 修满一件全坏的装备花它价钱的几成，同原版修理费的系数
        const float ArcherShare = 0.25f;     // 禁军里弓手的比例
        const float ArcherShareBowLovers = 0.5f;
        const float UpgradeShare = 0.9f;     // 多少禁军达到现行制式才升档

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

        public static float BudgetRate(int pOption) => BudgetRates[pOption];

        public static int TierOf(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyTier, out int v, 0);
            return Mathf.Clamp(v, 0, Tiers.Length - 1);
        }

        public static string TierKey(Kingdom pKingdom) => "vd_arms_tier_" + TierOf(pKingdom);

        // 一件装备的价钱：本国存货按官价，不够的按商队的价钱，加上工钱（按档次）。材料凑不齐返回 -1
        static int Cost(Kingdom pKingdom, Tier pTier)
        {
            int domestic = Mathf.Min(pTier.amount, Stock(pKingdom, pTier.resource));
            int rest = pTier.amount - domestic;
            if (rest > 0 && pTier.import == 0) return -1;
            return domestic * pTier.price + rest * pTier.import + Mathf.CeilToInt(pTier.value / 10f);
        }

        // 一件装备按商队价算的价钱（秘银、精金按官价），修理费照这个算
        static int ListPrice(Tier pTier) =>
            pTier.amount * (pTier.import > 0 ? pTier.import : pTier.price) + Mathf.CeilToInt(pTier.value / 10f);

        public static bool IsOfficial(Item pItem) => pItem?.data != null && pItem.data.hasFlag(FlagOfficial);

        // 升到这一档要材料够全军换装：商队卖的总买得到，秘银、精金看本国的存货
        static bool Available(Kingdom pKingdom, Tier pTier, int pMembers) =>
            pTier.import > 0 || Stock(pKingdom, pTier.resource) >= pMembers * Slots.Length * pTier.amount;

        public static void UpdateAll()
        {
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
                if (k.isAlive() && k.isCiv()) Update(k);
        }

        static void Update(Kingdom pKingdom)
        {
            int option = Policies.Value(pKingdom, Policies.Arms);
            if (option == 0) return;
            List<Actor> members = Members(pKingdom);
            if (members.Count == 0) return;

            string melee = MeleeWeapon(pKingdom);
            int tier = TierOf(pKingdom);
            if (option > Replacements && tier < Tiers.Length - 1
                && members.FindAll(a => Short(a, melee, tier)).Count <= members.Count * (1f - UpgradeShare)
                && Available(pKingdom, Tiers[tier + 1], members.Count))
            {
                tier++;
                pKingdom.data.set(KeyTier, tier);
            }

            int reserve = Mathf.CeilToInt(Guard.KingdomMembers(pKingdom) * Treasury.PayPerWarrior * ReserveYears);
            Repair(pKingdom, members, Treasury.Get(pKingdom) - reserve);

            float archers = ArcherShareOf(pKingdom);
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

            Reclaim(pKingdom, members, melee, tier);

            int budget = Mathf.FloorToInt((Treasury.Get(pKingdom) - reserve) * BudgetRates[option]);
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
                    broke = !Issue(pKingdom, a, slot, subtype, tier, budget - spent, ref spent);
                    if (broke) break;
                }
                if (broke) break;
            }
            Treasury.Take(pKingdom, spent);
        }

        // 修禁军身上的官造装备：先于置办新装备，用储备以外的钱，不按议案的比例。修理费按损耗的比例算，钱不够就停
        static void Repair(Kingdom pKingdom, List<Actor> pMembers, int pBudget)
        {
            float spent = 0f;
            bool broke = false;
            foreach (Actor a in pMembers)
            {
                bool repaired = false;
                foreach (ActorEquipmentSlot slot in a.equipment)
                {
                    Item item = slot.getItem();
                    if (!IsOfficial(item) || !item.needRepair()) continue;
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
            Treasury.Take(pKingdom, Mathf.CeilToInt(spent));
        }

        // 回收：本国各城仓库里的装备，挑比身上好的发给不到制式的禁军，每次拿最好的一件。
        // 武器要合兵种：弓手只拿弓，近战兵只拿近战武器
        static void Reclaim(Kingdom pKingdom, List<Actor> pMembers, string pMelee, int pTier)
        {
            foreach (Actor a in pMembers)
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
                    foreach (City city in pKingdom.getCities())
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
                    Item old = slot.getItem();
                    if (old != null)
                    {
                        slot.takeAwayItem();
                        a.city?.tryToPutItem(old);
                    }
                    slot.setItem(best, a);
                }
            }
        }

        internal static List<Actor> Members(Kingdom pKingdom)
        {
            var list = new List<Actor>();
            foreach (City city in pKingdom.getCities())
            {
                if (!city._professions_dict.TryGetValue(UnitProfession.Warrior, out List<Actor> warriors)) continue;
                foreach (Actor a in warriors)
                    if (a.isAlive() && Guard.IsMember(a) && a.understandsHowToUseItems()) list.Add(a);
            }
            return list;
        }

        // 文化偏好的近战武器，没有就用剑
        static string MeleeWeapon(Kingdom pKingdom)
        {
            Culture culture = pKingdom.getCulture();
            if (culture != null)
                foreach (string s in culture._preferred_weapons_craft_subtypes)
                    if (s != "bow" && AssetManager.items.get(s + "_" + Tiers[0].material) != null) return s;
            return "sword";
        }

        static float ArcherShareOf(Kingdom pKingdom) =>
            pKingdom.getCulture() != null && pKingdom.getCulture().hasTrait("bow_lovers") ? ArcherShareBowLovers : ArcherShare;

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
        static bool Issue(Kingdom pKingdom, Actor pActor, EquipmentType pSlot, string pSubtype, int pTier, int pBudget, ref int pSpent)
        {
            ActorEquipmentSlot slot = pActor.equipment.getSlot(pSlot);
            Item old = slot.getItem();
            int current = old?.getAsset().equipment_value ?? 0;
            for (int t = pTier; t >= 0; t--)
            {
                Tier tier = Tiers[t];
                if (tier.value < current) break;
                EquipmentAsset asset = AssetManager.items.get(pSubtype + "_" + tier.material) as EquipmentAsset;
                if (asset == null) continue;
                int cost = Cost(pKingdom, tier);
                if (cost < 0) continue;
                if (cost > pBudget) return false;
                Buy(pKingdom, tier.resource, tier.amount, tier.price);
                pSpent += cost;
                Item item = World.world.items.generateItem(asset, pKingdom, pKingdom.name, OfficialTries, pActor);
                item.data.addFlag(FlagOfficial);
                if (old != null)
                {
                    slot.takeAwayItem();
                    pActor.city?.tryToPutItem(old);
                }
                slot.setItem(item, pActor);
                pActor.setStatsDirty();
                return true;
            }
            return true;
        }

        // 各城仓库里能卖给国家的材料：每座城留底，多出的才卖
        internal static int Stock(Kingdom pKingdom, string pResource)
        {
            int sum = 0;
            foreach (City city in pKingdom.getCities())
                sum += Mathf.Max(0, city.getResourcesAmount(pResource) - CityReserve);
            return sum;
        }

        // 按官价收购本国的存货：存货多的城先卖，钱进卖方城市的仓库。不够的部分是向商队买的，钱流出本国
        static void Buy(Kingdom pKingdom, string pResource, int pAmount, int pPrice)
        {
            while (pAmount > 0)
            {
                City seller = null;
                int best = 0;
                foreach (City city in pKingdom.getCities())
                {
                    int surplus = city.getResourcesAmount(pResource) - CityReserve;
                    if (surplus <= best) continue;
                    seller = city;
                    best = surplus;
                }
                if (seller == null) return;
                int n = Mathf.Min(best, pAmount);
                seller.takeResource(pResource, n);
                seller.addResourcesToRandomStockpile("gold", n * pPrice);
                pAmount -= n;
            }
        }
    }

    // 原版士兵在有兵营的城里免费修装备。禁军身上的官造装备由国家修（见 Armory.Repair），这里跳过；
    // 其余照抄原版
    [HarmonyPatch(typeof(BehCheckCanRepairEquipment), nameof(BehCheckCanRepairEquipment.execute))]
    static class Patch_CheckRepair
    {
        static bool Prefix(Actor pActor, ref BehResult __result)
        {
            __result = BehResult.Stop;
            if (!pActor.hasEquipment()) return false;
            foreach (ActorEquipmentSlot slot in pActor.equipment)
            {
                Item item = slot.getItem();
                if (!item.needRepair() || Patch_Repair.StateRepaired(pActor, item)) continue;
                int cost = (int)(item.getAsset().cost_gold * SimGlobals.m.item_repair_cost_multiplier);
                if (pActor.money >= cost) __result = BehResult.Continue;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(BehRepairEquipment), nameof(BehRepairEquipment.execute))]
    static class Patch_Repair
    {
        internal static bool StateRepaired(Actor pActor, Item pItem) => Guard.IsMember(pActor) && Armory.IsOfficial(pItem);

        static bool Prefix(Actor pActor, ref BehResult __result)
        {
            __result = BehResult.Continue;
            bool repaired = false;
            foreach (ActorEquipmentSlot slot in pActor.equipment)
            {
                Item item = slot.getItem();
                if (!item.needRepair() || StateRepaired(pActor, item)) continue;
                int cost = (int)(item.getAsset().cost_gold * SimGlobals.m.item_repair_cost_multiplier);
                if (!pActor.hasEnoughMoney(cost)) continue;
                pActor.spendMoney(cost);
                item.fullRepair();
                repaired = true;
            }
            if (repaired) pActor.setStatsDirty();
            return false;
        }
    }
}
