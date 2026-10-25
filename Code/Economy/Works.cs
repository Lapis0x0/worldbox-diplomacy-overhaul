using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 营建：国家出钱放宽某城某类建筑的数量上限，当场按原版选址开工，之后由建造工照常施工。
    // 材料不能先拨给城市再等原版去盖：原版随机挑建筑来盖，拨下去的材料会先被别的建筑用掉。
    // 写法照禁军 AI：挑定一个项目就等到办成，钱不够就攒着，不每年重算。
    // 城里还没有的类型也能营建，只要原版的前置条件（人口、建筑数、领地、前置建筑）满足
    public static class Works
    {
        public const int MaxPerType = 2;      // 一座城每类最多多盖几座
        public const int ZonesPerGrant = 20;  // 一座城的营建名额合计：领地格子数 ÷ 这个数
        const int MaxPerYear = 3;             // 一个国家一年最多办成几个项目
        const int BlockedYears = 5;           // 城里找不到地方开工的那一类，这么多年里不再考虑
        const int GiveUpYears = 10;           // 定下项目这么多年还没攒够钱，放弃
        const float NeedMin = 0.2f;           // 需要程度不到这么多不营建

        // 需要程度
        const float MarketPayback = 5f;       // 市场：多盖一座的税收这么多年能回本就是 1
        const int TempleCalm = 30;            // 神庙：忠诚到这么高就不需要
        const float TempleSpan = 60f;         //       再往下这么多是 1
        const float TowerBorder = 0.3f, TowerThreat = 0.6f;
        const float BarracksThreat = 0.5f, BarracksPeace = 0.1f;   // 兵营：挨着强邻、太平年间（打仗时是 1）
        const float StatueRich = 10f;         // 雕像：存款超过储备的这么多倍、再加 500 是满的

        // 城里缺的材料按商队价买，钱流出本国（普通金属同军械）
        const int WoodPrice = 1, StonePrice = 1;

        static readonly string[] Types =
            { Buildings.Market, Buildings.Temple, Buildings.Barracks, Buildings.Tower, Buildings.Statue, Buildings.Windmill };

        // kingdom.data：定下的项目
        const string KeyCity = "vd_works_city", KeyType = "vd_works_type", KeySince = "vd_works_since";
        // kingdom.data：家族立场用的全国合计，每年营建打分时存
        const string KeyNeedTotal = "vd_works_need_total", KeySeatsTotal = "vd_works_seats_total", KeyFrontShare = "vd_works_front_share";
        // city.data：本城的营建需要程度之和、是不是前线；找不到地方开工的那一类记下年份，键是名额的键加这个后缀
        const string KeyNeed = "vd_works_need", KeyFront = "vd_works_front";
        const string BlockedSuffix = "_blocked";

        // 一年里一个国家打分要用的东西，每国算一次往下传
        class Context
        {
            public Kingdom kingdom;
            public int gold, reserve;
            public bool war;
            public Kingdom threat;
            public bool threatened;   // 最强的邻国比本国强得多（同储备的判断）
            public List<ParliamentClan> clans;   // 第一次营建时才建
        }

        struct Candidate
        {
            public City city;
            public string type;
            public float need, score;
            public int cost;
        }

        public static void UpdateAll()
        {
            if (!Buildings.On) return;
            foreach (Kingdom k in new List<Kingdom>(World.world.kingdoms))
            {
                if (!k.isAlive() || !k.isCiv()) continue;
                Update(k);
            }
        }

        static void Update(Kingdom pKingdom)
        {
            var ctx = new Context
            {
                kingdom = pKingdom,
                gold = Treasury.Get(pKingdom),
                war = World.world.wars.hasWars(pKingdom)
            };
            ctx.threat = GuardAI.StrongestNeighbor(pKingdom, out float power);
            ctx.threatened = ctx.threat != null && Treasury.Threatened(pKingdom, power);
            ctx.reserve = Treasury.Reserve(pKingdom, ctx.threatened);

            var candidates = new List<Candidate>();
            float needTotal = 0f;
            int seatsTotal = 0, fronts = 0, cities = 0;
            foreach (City city in pKingdom.getCities())
            {
                cities++;
                seatsTotal += Parliament.CitySeats(city);
                bool front = ctx.war && Front(city);
                if (front) fronts++;
                if (front) city.data.set(KeyFront, true);
                else city.data.removeBool(KeyFront);
                float cityNeed = 0f;
                if (Eligible(city))
                {
                    BuildOrder[] orders = OrdersOf(city);
                    for (int i = 0; i < Types.Length; i++)
                    {
                        string type = Types[i];
                        if (!Open(city, type, orders[i])) continue;
                        int cost = Cost(city, orders[i].getBuildingAsset(city));
                        float need = Need(ctx, city, type, cost);
                        cityNeed += need;
                        if (need < NeedMin || cost <= 0) continue;
                        int n = Buildings.Count(city, type);
                        float marginal = Buildings.Weight(n + 1) - Buildings.Weight(n);
                        // 价钱不进分数：贵的项目照样挑，钱不够就攒
                        candidates.Add(new Candidate { city = city, type = type, need = need, cost = cost, score = need * marginal });
                    }
                }
                city.data.set(KeyNeed, cityNeed);
                needTotal += cityNeed;
            }
            pKingdom.data.set(KeyNeedTotal, needTotal);
            pKingdom.data.set(KeySeatsTotal, seatsTotal);
            pKingdom.data.set(KeyFrontShare, cities > 0 ? (float)fronts / cities : 0f);

            int budget = Budget(pKingdom, ctx);
            for (int done = 0; done < MaxPerYear; done++)
            {
                Candidate? project = Project(pKingdom, candidates);
                if (project == null) break;
                Candidate c = project.Value;
                if (c.cost > budget) break;   // 攒着，钱留在国库
                ClearProject(pKingdom);
                if (Grant(ctx, c))
                {
                    budget -= c.cost;
                    candidates.RemoveAll(x => x.city == c.city);
                }
                else
                {
                    c.city.data.set(Buildings.WorksKey(c.type) + BlockedSuffix, Date.getCurrentYear());
                    candidates.RemoveAll(x => x.city == c.city && x.type == c.type);
                    done--;
                }
            }
        }

        // 营建能用的钱：储备以外的钱里，财政重心留给营建的那一份。军械今年已经先花了，两边合计不超过储备以外的钱
        static int Budget(Kingdom pKingdom, Context pCtx)
        {
            int surplus = pCtx.gold - pCtx.reserve;
            if (surplus <= 0) return 0;
            float rate = Military.On ? 1f - Armory.BudgetRate(Policies.Value(pKingdom, Policies.Arms)) : 1f;
            int arms = Mathf.Max(0, Ledger.ThisYear(pKingdom, Ledger.Flow.Arms));
            return Mathf.Min(surplus, Mathf.FloorToInt((surplus + arms) * rate));
        }

        // 定下的项目还成立就接着办，否则从今年的候选里挑分数最高的定下来
        static Candidate? Project(Kingdom pKingdom, List<Candidate> pCandidates)
        {
            pKingdom.data.get(KeyCity, out long cityId, -1L);
            pKingdom.data.get(KeyType, out string type, null);
            pKingdom.data.get(KeySince, out int since, 0);
            if (cityId >= 0)
            {
                if (Date.getCurrentYear() - since < GiveUpYears)
                    foreach (Candidate c in pCandidates)
                        if (c.city.id == cityId && c.type == type) return c;
                ClearProject(pKingdom);
            }
            if (pCandidates.Count == 0) return null;
            Candidate best = pCandidates[0];
            foreach (Candidate c in pCandidates)
                if (c.score > best.score) best = c;
            pKingdom.data.set(KeyCity, best.city.id);
            pKingdom.data.set(KeyType, best.type);
            pKingdom.data.set(KeySince, Date.getCurrentYear());
            return best;
        }

        static void ClearProject(Kingdom pKingdom)
        {
            pKingdom.data.removeLong(KeyCity);
            pKingdom.data.removeString(KeyType);
            pKingdom.data.removeInt(KeySince);
        }

        // ---------- 名额 ----------

        // 城里没在盖别的（原版一次只盖一座），有仓库，没在被攻城，营建名额没满
        static bool Eligible(City pCity) =>
            pCity.under_construction_building == null && pCity.hasStockpiles() && !pCity.isGettingCaptured()
            && TotalGranted(pCity) < pCity.zones.Count / ZonesPerGrant;

        // 这一类还能多给：名额没满，原版的前置条件满足（数量上限不算）
        static bool Open(City pCity, string pType, BuildOrder pOrder)
        {
            if (pOrder == null || Buildings.Granted(pCity, pType) >= MaxPerType) return false;
            pCity.data.get(Buildings.WorksKey(pType) + BlockedSuffix, out int blocked, -BlockedYears);
            return Date.getCurrentYear() - blocked >= BlockedYears && Prerequisites(pCity, pOrder);
        }

        // 照抄原版 CityBehBuild.canUseBuildAsset 里和数量上限无关的条件
        static bool Prerequisites(City pCity, BuildOrder pOrder)
        {
            if (pOrder.min_zones != 0 && pCity.zones.Count < pOrder.min_zones) return false;
            if (pCity.status.population < pOrder.required_pop) return false;
            if (pCity.buildings.Count < pOrder.required_buildings) return false;
            return ai.behaviours.CityBehBuild.haveRequiredBuildings(pOrder, pCity)
                   && ai.behaviours.CityBehBuild.haveRequiredBuildingTypes(pOrder.requirements_types, pCity);
        }

        static int TotalGranted(City pCity)
        {
            int n = 0;
            foreach (string type in Types) n += Buildings.Granted(pCity, type);
            return n;
        }

        // ---------- 需要程度 ----------

        static float Need(Context pCtx, City pCity, string pType, int pCost)
        {
            switch (pType)
            {
                case Buildings.Market:
                    return pCost <= 0 ? 0f : Mathf.Clamp01(MarketPayback * Ledger.LastYear(pCity) * Buildings.MarketShare / pCost);
                case Buildings.Temple:
                    return pCity.isCapitalCity() ? 0f : Mathf.Clamp01((TempleCalm - pCity.getLoyalty()) / TempleSpan);
                case Buildings.Barracks:
                {
                    if (!Military.On) return 0f;
                    float danger = pCtx.war ? 1f : pCtx.threatened ? BarracksThreat : BarracksPeace;
                    return danger * Mathf.Clamp01(pCity.getPopulationPeople() / 100f);
                }
                case Buildings.Tower:
                {
                    if (pCtx.war && Front(pCity)) return 1f;
                    float need = 0f;
                    foreach (City n in pCity.neighbours_cities)
                    {
                        Kingdom k = n.kingdom;
                        if (k == null || k == pCtx.kingdom || !k.isCiv() || Camp.Same(k, pCtx.kingdom)) continue;
                        need = Mathf.Max(need, k == pCtx.threat ? TowerThreat : TowerBorder);
                    }
                    return need;
                }
                case Buildings.Windmill:
                {
                    int pop = pCity.getPopulationPeople();
                    return pop <= 0 ? 0f : Mathf.Clamp01(1f - pCity.getTotalFood() / (float)pop);
                }
                case Buildings.Statue:
                    if (!pCity.isCapitalCity()) return 0f;
                    return 0.5f * Mathf.Clamp01((pCtx.gold - pCtx.reserve) / (pCtx.reserve * StatueRich + 500f));
                default:
                    return 0f;
            }
        }

        // 前线：挨着交战国的城
        static bool Front(City pCity)
        {
            Kingdom own = pCity.kingdom;
            foreach (City n in pCity.neighbours_cities)
                if (n.kingdom != null && n.kingdom != own && own.isEnemy(n.kingdom)) return true;
            return false;
        }

        // ---------- 造价和拨款 ----------

        // 这座城的种族盖各类建筑的原版建造项（不算升级），按 Types 的顺序；建造清单只翻一遍
        static BuildOrder[] OrdersOf(City pCity)
        {
            var result = new BuildOrder[Types.Length];
            ActorAsset race = pCity.getActorAsset();
            if (race == null || string.IsNullOrEmpty(race.build_order_template_id)) return result;
            CityBuildOrderAsset orders = AssetManager.city_build_orders.get(race.build_order_template_id);
            if (orders == null) return result;
            foreach (BuildOrder order in orders.list)
            {
                if (order.upgrade) continue;
                BuildingAsset asset = order.getBuildingAsset(pCity);
                if (asset == null) continue;
                int i = System.Array.IndexOf(Types, asset.type);
                if (i >= 0 && result[i] == null) result[i] = order;
            }
            return result;
        }

        static BuildingAsset AssetOf(City pCity, string pType)
        {
            int i = System.Array.IndexOf(Types, pType);
            return i < 0 ? null : OrdersOf(pCity)[i]?.getBuildingAsset(pCity);
        }

        // 原版造价里的金子，加上城里缺的材料按商队价买的钱
        static int Cost(City pCity, BuildingAsset asset)
        {
            if (asset == null) return -1;
            ConstructionCost c = asset.cost;
            return Mathf.Max(1, c.gold
                + Short(pCity, "wood", c.wood) * WoodPrice
                + Short(pCity, "stone", c.stone) * StonePrice
                + Short(pCity, "common_metals", c.common_metals) * Armory.MetalImport);
        }

        static int Short(City pCity, string pResource, int pNeed) => Mathf.Max(0, pNeed - pCity.getResourcesAmount(pResource));

        // 先放宽上限、把造价放进城市仓库，再用原版的 tryToBuild 选址开工（它会照原版扣掉造价）。
        // 找不到地方就撤回，不花钱
        static bool Grant(Context pCtx, Candidate pC)
        {
            Kingdom k = pCtx.kingdom;
            City city = pC.city;
            BuildingAsset asset = AssetOf(city, pC.type);
            if (asset == null) return false;
            ConstructionCost c = asset.cost;
            int wood = Short(city, "wood", c.wood), stone = Short(city, "stone", c.stone), metals = Short(city, "common_metals", c.common_metals);
            Give(city, "gold", c.gold);
            Give(city, "wood", wood);
            Give(city, "stone", stone);
            Give(city, "common_metals", metals);
            string key = Buildings.WorksKey(pC.type);
            int granted = Buildings.Granted(city, pC.type);
            city.data.set(key, granted + 1);
            if (ai.behaviours.CityBehBuild.tryToBuild(city, asset) == null)
            {
                city.data.set(key, granted);
                city.takeResource("gold", c.gold);
                city.takeResource("wood", wood);
                city.takeResource("stone", stone);
                city.takeResource("common_metals", metals);
                return false;
            }
            int paid = Treasury.Take(k, pC.cost);
            Ledger.Record(k, Ledger.Flow.Works, paid);
            pCtx.gold -= paid;

            string name = NameKey(pC.type);
            Ledger.Log(k, "vd_ledger_works", city.name + " · " + LM.Get(name), -paid);
            WorldLogs.Add(WorldLogs.Works, city.city_center, null, new[] { k.data.name, city.name, LM.Get(name) }, new[] { k, k, k }, false);
            Owe(pCtx, city);
            return true;
        }

        static void Give(City pCity, string pResource, int pAmount)
        {
            if (pAmount > 0) pCity.addResourcesToRandomStockpile(pResource, pAmount);
        }

        // 营建修在哪个家族的城里，那个家族欠掌权家族一份人情
        static void Owe(Context pCtx, City pCity)
        {
            if (!Settings.ParliamentEnabled || pCity.isCapitalCity()) return;
            if (pCtx.clans == null) pCtx.clans = Parliament.Clans(pCtx.kingdom);
            ParliamentClan owner = null, ruling = null;
            foreach (ParliamentClan pc in pCtx.clans)
            {
                if (pc.ruling) ruling = pc;
                if (pc.cities.Contains(pCity)) owner = pc;
            }
            Relations.Owe(owner, ruling, Relations.WorksFavor);
        }

        public static string NameKey(string pType) => Buildings.WorksKey(pType);

        // ---------- 家族立场 ----------

        // 自家各城的营建需要程度占全国的比例，减去议席比例：自家城缺得多的更想要营建
        public static float NeedGap(Kingdom pKingdom, ParliamentClan pClan)
        {
            pKingdom.data.get(KeyNeedTotal, out float total, 0f);
            pKingdom.data.get(KeySeatsTotal, out int seats, 0);
            if (total <= 0f || seats <= 0) return 0f;
            float need = 0f;
            foreach (City c in pClan.cities)
            {
                c.data.get(KeyNeed, out float v, 0f);
                need += v;
            }
            return need / total - (float)pClan.seats / seats;
        }

        // 打仗时自家前线城市的比例；掌权家族看全国
        public static float FrontShare(Kingdom pKingdom, ParliamentClan pClan)
        {
            if (!World.world.wars.hasWars(pKingdom)) return 0f;
            if (pClan.ruling)
            {
                pKingdom.data.get(KeyFrontShare, out float share, 0f);
                return share;
            }
            if (pClan.cities.Count == 0) return 0f;
            int n = 0;
            foreach (City c in pClan.cities)
            {
                c.data.get(KeyFront, out bool front, false);
                if (front) n++;
            }
            return (float)n / pClan.cities.Count;
        }

        // 城市窗口：营建过的建筑
        public static string GrantedText(City pCity)
        {
            var names = new List<string>();
            foreach (string type in Types)
            {
                int n = Buildings.Granted(pCity, type);
                if (n <= 0) continue;
                string name = LM.Get(NameKey(type));
                names.Add(n > 1 ? name + " ×" + n : name);
            }
            return names.Count > 0 ? string.Join("、", names) : null;
        }
    }
}
